using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StopwatchOverlay
{
    public enum ProjectTimeReadStatus
    {
        None,
        Success,
        NotFound,
        Corrupt,
        UnsupportedVersion,
        Unavailable
    }

    /// <summary>
    /// Public persistence DTOs keep the on-disk schema explicit and versioned.
    /// Dashboard code should use ProjectHistoryView instead of these mutable
    /// serialization types.
    /// </summary>
    public sealed class ProjectHistoryDocument
    {
        public int Version { get; set; } = ProjectTimeStore.CurrentVersion;
        public DateTime SavedAtUtc { get; set; }
        public List<ProjectDocumentEntry> Projects { get; set; } = new();
        public List<WorkIntervalDocumentEntry> Intervals { get; set; } = new();
    }

    public sealed class ProjectDocumentEntry
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public sealed class WorkIntervalDocumentEntry
    {
        public Guid Id { get; set; }
        public Guid TimerSessionId { get; set; }
        public string ProjectKey { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public DateTime StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }
    }

    /// <summary>
    /// Crash-tolerant store for the permanent project registry and UTC work
    /// intervals. The previous valid generation is retained as .bak and is used
    /// automatically if the primary file is corrupt or incomplete.
    /// </summary>
    public sealed class ProjectTimeStore
    {
        public const int CurrentVersion = 1;
        private readonly object _gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public ProjectTimeStore(string? filePath = null)
        {
            FilePath = filePath ?? ProjectHistoryPath;
        }

        public static string ProjectHistoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StopwatchOverlay",
            "project-history.json");

        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public ProjectTimeReadStatus LastReadStatus { get; private set; }
        public ProjectTimeReadStatus LastPrimaryReadStatus { get; private set; }
        public ProjectTimeReadStatus LastBackupReadStatus { get; private set; }
        public bool LoadedFromBackup { get; private set; }
        public bool NeedsPrimaryRepair { get; private set; }
        public DateTime? LastLoadedSavedAtUtc { get; private set; }

        public bool Save(ProjectTimeHistory history)
            => Save(history, DateTime.UtcNow);

        public bool Save(ProjectTimeHistory history, DateTime savedAtUtc)
        {
            ArgumentNullException.ThrowIfNull(history);
            ProjectHistoryDocument document;
            try
            {
                document = history.CreateDocument(savedAtUtc);
            }
            catch
            {
                return false;
            }

            return Save(document);
        }

        public bool Save(ProjectHistoryDocument document)
        {
            lock (_gate)
            {
                string? temporaryPath = null;
                string? replacementBackupPath = null;

                try
                {
                    ArgumentNullException.ThrowIfNull(document);
                    Validate(document);

                    string? directory = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    temporaryPath = FilePath + ".tmp." + Guid.NewGuid().ToString("N");
                    byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
                    using (var stream = new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.WriteThrough))
                    {
                        stream.Write(json);
                        stream.Flush(flushToDisk: true);
                    }

                    if (!File.Exists(FilePath))
                    {
                        File.Move(temporaryPath, FilePath);
                        temporaryPath = null;
                        CompleteSuccessfulSave();
                        return true;
                    }

                    if (NeedsPrimaryRepair)
                    {
                        // Do not rotate the known-corrupt primary over the last
                        // good backup on the first save after recovery.
                        ReplacePrimaryWithoutRotatingBackup(temporaryPath);
                        temporaryPath = null;
                        CompleteSuccessfulSave();
                        return true;
                    }

                    replacementBackupPath = BackupPath + ".tmp." + Guid.NewGuid().ToString("N");
                    try
                    {
                        File.Replace(
                            temporaryPath,
                            FilePath,
                            replacementBackupPath,
                            ignoreMetadataErrors: true);
                        temporaryPath = null;
                        File.Move(replacementBackupPath, BackupPath, overwrite: true);
                        replacementBackupPath = null;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        FallbackReplace(temporaryPath!, replacementBackupPath!);
                        temporaryPath = null;
                        replacementBackupPath = null;
                    }

                    CompleteSuccessfulSave();
                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    TryDelete(temporaryPath);
                    TryDelete(replacementBackupPath);
                }
            }
        }

        public bool TryLoad(out ProjectTimeHistory? history)
        {
            lock (_gate)
            {
                history = null;
                if (!TryReadDocumentCore(out ProjectHistoryDocument? document))
                    return false;

                try
                {
                    history = ProjectTimeHistory.FromDocument(document!);
                    return true;
                }
                catch
                {
                    history = null;
                    LastReadStatus = ProjectTimeReadStatus.Corrupt;
                    ClearLoadedDocumentState();
                    return false;
                }
            }
        }

        public bool TryReadDocument(out ProjectHistoryDocument? document)
        {
            lock (_gate)
            {
                return TryReadDocumentCore(out document);
            }
        }

        private bool TryReadDocumentCore(out ProjectHistoryDocument? document)
        {
            ProjectTimeReadStatus primaryStatus = TryReadDocumentFile(FilePath, out document);
            LastPrimaryReadStatus = primaryStatus;
            LastBackupReadStatus = ProjectTimeReadStatus.None;
            if (primaryStatus == ProjectTimeReadStatus.Success)
            {
                SetSuccessfulLoad(document!, loadedFromBackup: false);
                return true;
            }

            // A newer application may intentionally own this schema. Loading an
            // older backup could silently discard its data, so never downgrade.
            if (primaryStatus == ProjectTimeReadStatus.UnsupportedVersion)
            {
                LastReadStatus = primaryStatus;
                ClearLoadedDocumentState();
                return false;
            }

            // A transient I/O or access failure does not prove the primary is
            // bad. Falling back to an older generation could later overwrite a
            // valid primary with stale data, so leave the history untouched and
            // let the caller retry when the file becomes available.
            if (primaryStatus == ProjectTimeReadStatus.Unavailable)
            {
                LastReadStatus = primaryStatus;
                ClearLoadedDocumentState();
                return false;
            }

            ProjectTimeReadStatus backupStatus = TryReadDocumentFile(BackupPath, out document);
            LastBackupReadStatus = backupStatus;
            if (backupStatus == ProjectTimeReadStatus.Success)
            {
                SetSuccessfulLoad(document!, loadedFromBackup: true);
                return true;
            }

            ClearLoadedDocumentState();
            if (backupStatus is ProjectTimeReadStatus.UnsupportedVersion
                or ProjectTimeReadStatus.Unavailable)
            {
                LastReadStatus = backupStatus;
            }
            else if (primaryStatus == ProjectTimeReadStatus.Corrupt
                || backupStatus == ProjectTimeReadStatus.Corrupt)
            {
                LastReadStatus = ProjectTimeReadStatus.Corrupt;
            }
            else
            {
                LastReadStatus = ProjectTimeReadStatus.NotFound;
            }
            document = null;
            return false;
        }

        private static ProjectTimeReadStatus TryReadDocumentFile(
            string path,
            out ProjectHistoryDocument? document)
        {
            document = null;
            try
            {
                using var stream = File.OpenRead(path);
                document = JsonSerializer.Deserialize<ProjectHistoryDocument>(stream, JsonOptions);
                if (document == null)
                    return ProjectTimeReadStatus.Corrupt;
                if (document.Version != CurrentVersion)
                {
                    document = null;
                    return ProjectTimeReadStatus.UnsupportedVersion;
                }

                Validate(document);
                return ProjectTimeReadStatus.Success;
            }
            catch (FileNotFoundException)
            {
                document = null;
                return ProjectTimeReadStatus.NotFound;
            }
            catch (DirectoryNotFoundException)
            {
                document = null;
                return ProjectTimeReadStatus.NotFound;
            }
            catch (JsonException)
            {
                document = null;
                return ProjectTimeReadStatus.Corrupt;
            }
            catch (InvalidDataException)
            {
                document = null;
                return ProjectTimeReadStatus.Corrupt;
            }
            catch (NotSupportedException)
            {
                document = null;
                return ProjectTimeReadStatus.Corrupt;
            }
            catch (UnauthorizedAccessException)
            {
                document = null;
                return ProjectTimeReadStatus.Unavailable;
            }
            catch (IOException)
            {
                document = null;
                return ProjectTimeReadStatus.Unavailable;
            }
            catch
            {
                document = null;
                return ProjectTimeReadStatus.Unavailable;
            }
        }

        private void SetSuccessfulLoad(
            ProjectHistoryDocument document,
            bool loadedFromBackup)
        {
            LoadedFromBackup = loadedFromBackup;
            NeedsPrimaryRepair = loadedFromBackup;
            LastLoadedSavedAtUtc = ProjectTimeHistory.NormalizeUtc(document.SavedAtUtc);
            LastReadStatus = ProjectTimeReadStatus.Success;
        }

        private void ClearLoadedDocumentState()
        {
            LoadedFromBackup = false;
            NeedsPrimaryRepair = false;
            LastLoadedSavedAtUtc = null;
        }

        private void CompleteSuccessfulSave()
        {
            LoadedFromBackup = false;
            NeedsPrimaryRepair = false;
        }

        internal static void Validate(ProjectHistoryDocument document)
        {
            if (document.Version != CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported project history version {document.Version}.");
            if (document.SavedAtUtc == default)
                throw new InvalidDataException("Project history has no save timestamp.");
            if (document.Projects == null)
                throw new InvalidDataException("Project history has no project registry.");
            if (document.Intervals == null)
                throw new InvalidDataException("Project history has no interval list.");
            if (document.Projects.Any(project => project == null))
                throw new InvalidDataException("Project history contains an empty project.");
            if (document.Intervals.Any(interval => interval == null))
                throw new InvalidDataException("Project history contains an empty interval.");

            var projectsByKey = new Dictionary<string, ProjectDocumentEntry>(
                StringComparer.OrdinalIgnoreCase);
            foreach (ProjectDocumentEntry project in document.Projects)
            {
                string name;
                try
                {
                    name = ProjectTimeHistory.NormalizeProjectName(project.Name);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException("Project history contains an invalid project name.", exception);
                }

                string expectedKey = ProjectTimeHistory.CreateProjectKey(name);
                if (!StringComparer.Ordinal.Equals(project.Name, name)
                    || !StringComparer.Ordinal.Equals(project.Key, expectedKey))
                {
                    throw new InvalidDataException("Project history contains an invalid project identity.");
                }
                if (!projectsByKey.TryAdd(project.Key, project))
                    throw new InvalidDataException("Project history contains duplicate projects.");
            }

            var intervalIds = new HashSet<Guid>();
            foreach (WorkIntervalDocumentEntry interval in document.Intervals)
            {
                if (interval.Id == Guid.Empty || interval.TimerSessionId == Guid.Empty)
                    throw new InvalidDataException("Project history contains an invalid interval identity.");
                if (!intervalIds.Add(interval.Id))
                    throw new InvalidDataException("Project history contains duplicate interval ids.");
                if (!projectsByKey.TryGetValue(interval.ProjectKey, out ProjectDocumentEntry? project)
                    || !StringComparer.Ordinal.Equals(interval.ProjectKey, project.Key)
                    || !StringComparer.Ordinal.Equals(interval.ProjectName, project.Name))
                {
                    throw new InvalidDataException("A work interval references an unknown project.");
                }
                if (interval.StartUtc == default)
                    throw new InvalidDataException("A work interval has no start timestamp.");
                if (interval.EndUtc.HasValue && interval.EndUtc.Value == default)
                    throw new InvalidDataException("A work interval has an invalid end timestamp.");

                DateTime startUtc = ProjectTimeHistory.NormalizeUtc(interval.StartUtc);
                DateTime? endUtc = interval.EndUtc.HasValue
                    ? ProjectTimeHistory.NormalizeUtc(interval.EndUtc.Value)
                    : null;
                if (endUtc.HasValue && endUtc.Value < startUtc)
                    throw new InvalidDataException("A work interval ends before it starts.");
            }

            foreach (IGrouping<Guid, WorkIntervalDocumentEntry> timerIntervals in
                document.Intervals.GroupBy(interval => interval.TimerSessionId))
            {
                if (timerIntervals.Count(interval => !interval.EndUtc.HasValue) > 1)
                    throw new InvalidDataException("A timer has more than one open work interval.");

                WorkIntervalDocumentEntry[] ordered = timerIntervals
                    .OrderBy(interval => ProjectTimeHistory.NormalizeUtc(interval.StartUtc))
                    .ThenBy(interval => interval.EndUtc.HasValue ? 0 : 1)
                    .ToArray();

                for (int index = 1; index < ordered.Length; index++)
                {
                    WorkIntervalDocumentEntry previous = ordered[index - 1];
                    DateTime previousEnd = previous.EndUtc.HasValue
                        ? ProjectTimeHistory.NormalizeUtc(previous.EndUtc.Value)
                        : DateTime.MaxValue;
                    DateTime currentStart = ProjectTimeHistory.NormalizeUtc(ordered[index].StartUtc);
                    if (currentStart < previousEnd)
                        throw new InvalidDataException("A timer has overlapping work intervals.");
                }
            }
        }

        private void FallbackReplace(string temporaryPath, string replacementBackupPath)
        {
            File.Copy(FilePath, replacementBackupPath, overwrite: true);
            File.Move(replacementBackupPath, BackupPath, overwrite: true);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }

        private void ReplacePrimaryWithoutRotatingBackup(string temporaryPath)
        {
            try
            {
                File.Replace(
                    temporaryPath,
                    FilePath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(temporaryPath, FilePath, overwrite: true);
            }
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup; a uniquely named temp file is harmless.
            }
        }
    }
}
