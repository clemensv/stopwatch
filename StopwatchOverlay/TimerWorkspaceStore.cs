using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StopwatchOverlay
{
    public enum TimerWorkspaceReadStatus
    {
        None,
        Success,
        NotFound,
        Corrupt,
        UnsupportedVersion,
        Unavailable
    }

    /// <summary>
    /// Versioned, serializable state for the complete logical timer workspace.
    /// Appearance and shortcut preferences intentionally remain in settings.json.
    /// </summary>
    public sealed class TimerWorkspaceSnapshot
    {
        public int Version { get; set; } = TimerWorkspaceStore.CurrentVersion;
        public DateTime SavedAtUtc { get; set; }
        public Guid? ActiveTimerId { get; set; }
        public int NextNumber { get; set; } = 1;
        public bool SkipProjectHistoryReconciliation { get; set; }
        public bool CombinedOverlayMode { get; set; }
        public bool CombinedOverlayVisible { get; set; } = true;
        public bool CombinedHasCustomPosition { get; set; }
        public Dictionary<string, TimerScreenPositionSnapshot> CombinedPositionsByScreen { get; set; } = new();
        public List<TimerSessionSnapshot> Timers { get; set; } = new();
    }

    public sealed class TimerSessionSnapshot
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; } = "";
        public bool IsRunning { get; set; }
        public TimeSpan Elapsed { get; set; }

        public int Mode { get; set; }
        public int LastNonClockMode { get; set; }
        public TimeSpan CountdownDuration { get; set; }
        public TimeSpan CountdownRemaining { get; set; }
        public DateTime ClockTarget { get; set; }
        public DateTime LastCountdownUpdateUtc { get; set; }
        public bool CountdownInitialized { get; set; }
        public bool UseClockTarget { get; set; }

        public string CountdownMinutesText { get; set; } = "5";
        public string CountdownSecondsText { get; set; } = "00";
        public string ClockTargetHoursText { get; set; } = "00";
        public string ClockTargetMinutesText { get; set; } = "00";
        public string ClockTargetSecondsText { get; set; } = "00";
        public string SmartInputText { get; set; } = "";

        public List<string> LapTimes { get; set; } = new();
        public int LapCount { get; set; }
        public bool ColonVisible { get; set; } = true;
        public bool RecBlinkVisible { get; set; }

        public bool OverlayVisible { get; set; }
        public bool HasCustomPosition { get; set; }
        public double CustomLeft { get; set; }
        public double CustomTop { get; set; }
        public Dictionary<string, TimerScreenPositionSnapshot> CustomPositionsByScreen { get; set; } = new();
        public string LastPresetPosition { get; set; } = "Top Center";
        public int CascadeIndex { get; set; }
    }

    public sealed class TimerScreenPositionSnapshot
    {
        public double Left { get; set; }
        public double Top { get; set; }
    }

    /// <summary>
    /// Crash-tolerant persistence for logical timers. Writes are made to a file
    /// in the destination directory and atomically replace the previous file.
    /// The previous valid generation remains as workspace.json.bak and is used
    /// automatically if the primary file becomes unreadable.
    /// </summary>
    public sealed class TimerWorkspaceStore
    {
        public const int CurrentVersion = 1;
        private bool _loadedFromBackup;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public TimerWorkspaceStore(string? filePath = null)
        {
            FilePath = filePath ?? WorkspacePath;
        }

        public static string WorkspacePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StopwatchOverlay",
            "workspace.json");

        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public TimerWorkspaceReadStatus LastReadStatus { get; private set; }
        public TimerWorkspaceReadStatus LastPrimaryReadStatus { get; private set; }
        public TimerWorkspaceReadStatus LastBackupReadStatus { get; private set; }

        /// <summary>
        /// Save timestamp carried by the exact snapshot most recently restored
        /// through <see cref="TryLoad(TimerSessionManager, DateTime, DateTime)"/>.
        /// A read or save attempt clears this value so callers cannot mistake
        /// stale startup metadata for the result of a later operation.
        /// </summary>
        public DateTime? LastLoadedSavedAtUtc { get; private set; }

        /// <summary>
        /// True when the snapshot represented by <see cref="LastLoadedSavedAtUtc"/>
        /// was restored from <see cref="BackupPath"/>. This is false whenever
        /// there is no successful-load timestamp.
        /// </summary>
        public bool LastLoadUsedBackup { get; private set; }

        /// <summary>
        /// Carries a safety marker from the restored workspace when its timer
        /// generation is known to be older than project history. The controller
        /// preserves this marker until explicit timer actions make both agree.
        /// </summary>
        public bool LastLoadedSkipProjectHistoryReconciliation { get; private set; }

        public bool LastLoadedCombinedOverlayMode { get; private set; }
        public bool LastLoadedCombinedOverlayVisible { get; private set; } = true;
        public bool LastLoadedCombinedHasCustomPosition { get; private set; }
        public IReadOnlyDictionary<string, TimerScreenPositionSnapshot> LastLoadedCombinedPositionsByScreen
            { get; private set; } = new Dictionary<string, TimerScreenPositionSnapshot>();

        public static TimerWorkspaceSnapshot Capture(
            TimerSessionManager manager,
            DateTime savedAtUtc)
        {
            ArgumentNullException.ThrowIfNull(manager);
            savedAtUtc = NormalizeUtc(savedAtUtc);

            return new TimerWorkspaceSnapshot
            {
                Version = CurrentVersion,
                SavedAtUtc = savedAtUtc,
                ActiveTimerId = manager.Active?.Id,
                NextNumber = manager.NextNumber,
                Timers = manager.Sessions.Select(CaptureTimer).ToList()
            };
        }

        public bool Save(TimerSessionManager manager)
            => Save(manager, DateTime.UtcNow);

        public bool Save(TimerSessionManager manager, DateTime savedAtUtc)
            => Save(Capture(manager, savedAtUtc));

        public bool Save(TimerWorkspaceSnapshot snapshot)
        {
            ClearLastLoadMetadata();
            string? temporaryPath = null;
            string? replacementBackupPath = null;

            try
            {
                ArgumentNullException.ThrowIfNull(snapshot);
                Validate(snapshot);
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                temporaryPath = FilePath + ".tmp." + Guid.NewGuid().ToString("N");
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
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
                    _loadedFromBackup = false;
                    return true;
                }

                if (_loadedFromBackup)
                {
                    // The current primary is known to be corrupt. Replacing it
                    // through the normal generation rotation would promote that
                    // corrupt file to .bak and destroy our last known-good copy.
                    // Repair only the primary on this first save after recovery.
                    ReplacePrimaryWithoutRotatingBackup(temporaryPath);
                    temporaryPath = null;
                    _loadedFromBackup = false;
                    return true;
                }

                replacementBackupPath = BackupPath + ".tmp." + Guid.NewGuid().ToString("N");
                try
                {
                    File.Replace(temporaryPath, FilePath, replacementBackupPath, ignoreMetadataErrors: true);
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

                _loadedFromBackup = false;
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

        public bool TryLoad(TimerSessionManager manager)
            => TryLoad(manager, DateTime.UtcNow, DateTime.Now);

        public bool TryLoad(
            TimerSessionManager manager,
            DateTime utcNow,
            DateTime localNow)
        {
            ArgumentNullException.ThrowIfNull(manager);

            ClearLastLoadMetadata();

            if (!TryReadSnapshot(out var snapshot))
                return false;

            bool loadedFromBackup = _loadedFromBackup;
            try
            {
                Restore(snapshot!, manager, utcNow, localNow);
                LastLoadedSavedAtUtc = NormalizeUtc(snapshot!.SavedAtUtc);
                LastLoadUsedBackup = loadedFromBackup;
                LastLoadedSkipProjectHistoryReconciliation =
                    snapshot.SkipProjectHistoryReconciliation;
                LastLoadedCombinedOverlayMode = snapshot.CombinedOverlayMode;
                LastLoadedCombinedOverlayVisible = snapshot.CombinedOverlayVisible;
                LastLoadedCombinedHasCustomPosition = snapshot.CombinedHasCustomPosition;
                LastLoadedCombinedPositionsByScreen = snapshot.CombinedPositionsByScreen.ToDictionary(
                    pair => pair.Key,
                    pair => new TimerScreenPositionSnapshot
                    {
                        Left = pair.Value.Left,
                        Top = pair.Value.Top
                    });
                return true;
            }
            catch
            {
                // A semantically corrupt snapshot must not replace the current
                // in-memory workspace.
                ClearLastLoadMetadata();
                _loadedFromBackup = false;
                LastReadStatus = TimerWorkspaceReadStatus.Corrupt;
                return false;
            }
        }

        public bool TryReadSnapshot(out TimerWorkspaceSnapshot? snapshot)
        {
            ClearLastLoadMetadata();
            var primaryStatus = TryReadSnapshotFile(FilePath, out snapshot);
            LastPrimaryReadStatus = primaryStatus;
            LastBackupReadStatus = TimerWorkspaceReadStatus.None;
            if (primaryStatus == TimerWorkspaceReadStatus.Success)
            {
                _loadedFromBackup = false;
                LastReadStatus = primaryStatus;
                return true;
            }

            // A newer application may have intentionally upgraded this file.
            // Never silently downgrade it by loading an older backup.
            if (primaryStatus == TimerWorkspaceReadStatus.UnsupportedVersion)
            {
                _loadedFromBackup = false;
                LastReadStatus = primaryStatus;
                return false;
            }

            // An access or I/O failure does not prove that the primary is bad.
            // Loading an older backup here could later overwrite a valid newer
            // primary, so preserve both files and let the caller retry later.
            if (primaryStatus == TimerWorkspaceReadStatus.Unavailable)
            {
                _loadedFromBackup = false;
                LastReadStatus = primaryStatus;
                return false;
            }

            var backupStatus = TryReadSnapshotFile(BackupPath, out snapshot);
            LastBackupReadStatus = backupStatus;
            if (backupStatus == TimerWorkspaceReadStatus.Success)
            {
                _loadedFromBackup = true;
                LastReadStatus = backupStatus;
                return true;
            }

            _loadedFromBackup = false;
            LastReadStatus = backupStatus is TimerWorkspaceReadStatus.UnsupportedVersion
                or TimerWorkspaceReadStatus.Unavailable
                    ? backupStatus
                    : primaryStatus == TimerWorkspaceReadStatus.Corrupt
                        || backupStatus == TimerWorkspaceReadStatus.Corrupt
                            ? TimerWorkspaceReadStatus.Corrupt
                            : TimerWorkspaceReadStatus.NotFound;
            return false;
        }

        public static void Restore(
            TimerWorkspaceSnapshot snapshot,
            TimerSessionManager manager,
            DateTime utcNow,
            DateTime localNow)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(manager);
            Validate(snapshot);

            utcNow = NormalizeUtc(utcNow);
            DateTime savedAtUtc = NormalizeUtc(snapshot.SavedAtUtc);
            TimeSpan offlineTime = utcNow > savedAtUtc
                ? utcNow - savedAtUtc
                : TimeSpan.Zero;

            // Construct every session before mutating the manager. If any item
            // is invalid, the caller's existing workspace remains untouched.
            var restored = snapshot.Timers
                .Select(timer => RestoreTimer(timer, offlineTime, utcNow, localNow))
                .ToList();

            manager.Restore(restored, snapshot.ActiveTimerId, snapshot.NextNumber);
        }

        private void FallbackReplace(string temporaryPath, string replacementBackupPath)
        {
            // The copy happens before replacement, so there is always either the
            // old primary or an intact backup if the process stops mid-save.
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

        private TimerWorkspaceReadStatus TryReadSnapshotFile(
            string path,
            out TimerWorkspaceSnapshot? snapshot)
        {
            snapshot = null;
            try
            {
                using var stream = File.OpenRead(path);
                snapshot = JsonSerializer.Deserialize<TimerWorkspaceSnapshot>(stream, JsonOptions);
                if (snapshot == null)
                    return TimerWorkspaceReadStatus.Corrupt;
                if (snapshot.Version != CurrentVersion)
                {
                    snapshot = null;
                    return TimerWorkspaceReadStatus.UnsupportedVersion;
                }
                Validate(snapshot);
                return TimerWorkspaceReadStatus.Success;
            }
            catch (FileNotFoundException)
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.NotFound;
            }
            catch (DirectoryNotFoundException)
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.NotFound;
            }
            catch (JsonException)
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.Corrupt;
            }
            catch (InvalidDataException)
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.Corrupt;
            }
            catch (NotSupportedException)
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.Corrupt;
            }
            catch (UnauthorizedAccessException)
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.Unavailable;
            }
            catch (IOException)
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.Unavailable;
            }
            catch
            {
                snapshot = null;
                return TimerWorkspaceReadStatus.Unavailable;
            }
        }

        private static TimerSessionSnapshot CaptureTimer(TimerSession timer)
        {
            return new TimerSessionSnapshot
            {
                Id = timer.Id,
                Number = timer.Number,
                Name = timer.Name,
                IsRunning = timer.IsRunning,
                Elapsed = timer.Elapsed,
                Mode = timer.Mode,
                LastNonClockMode = timer.LastNonClockMode,
                CountdownDuration = timer.CountdownDuration,
                CountdownRemaining = timer.CountdownRemaining,
                ClockTarget = timer.ClockTarget,
                LastCountdownUpdateUtc = timer.LastCountdownUpdateUtc,
                CountdownInitialized = timer.CountdownInitialized,
                UseClockTarget = timer.UseClockTarget,
                CountdownMinutesText = timer.CountdownMinutesText,
                CountdownSecondsText = timer.CountdownSecondsText,
                ClockTargetHoursText = timer.ClockTargetHoursText,
                ClockTargetMinutesText = timer.ClockTargetMinutesText,
                ClockTargetSecondsText = timer.ClockTargetSecondsText,
                SmartInputText = timer.SmartInputText,
                LapTimes = timer.LapTimes.ToList(),
                LapCount = timer.LapCount,
                ColonVisible = timer.ColonVisible,
                RecBlinkVisible = timer.RecBlinkVisible,
                OverlayVisible = timer.OverlayVisible,
                HasCustomPosition = timer.HasCustomPosition,
                CustomLeft = timer.CustomLeft,
                CustomTop = timer.CustomTop,
                CustomPositionsByScreen = timer.CustomPositionsByScreen.ToDictionary(
                    pair => pair.Key,
                    pair => new TimerScreenPositionSnapshot
                    {
                        Left = pair.Value.Left,
                        Top = pair.Value.Top
                    }),
                LastPresetPosition = timer.LastPresetPosition,
                CascadeIndex = timer.CascadeIndex
            };
        }

        private static TimerSession RestoreTimer(
            TimerSessionSnapshot saved,
            TimeSpan offlineTime,
            DateTime utcNow,
            DateTime localNow)
        {
            int runningMode = saved.Mode == 1 ? saved.LastNonClockMode : saved.Mode;
            TimeSpan elapsed = saved.Elapsed;
            TimeSpan countdownRemaining = saved.CountdownRemaining;

            if (saved.IsRunning)
            {
                elapsed += offlineTime;
                if (runningMode == 2)
                {
                    countdownRemaining = saved.UseClockTarget
                        ? LocalClockTarget(saved.ClockTarget) - localNow
                        : saved.CountdownRemaining - offlineTime;
                }
            }

            var timer = new TimerSession(saved.Id, saved.Number)
            {
                Name = saved.Name ?? "",
                IsRunning = saved.IsRunning,
                Mode = saved.Mode,
                LastNonClockMode = saved.LastNonClockMode,
                CountdownDuration = saved.CountdownDuration,
                CountdownRemaining = countdownRemaining,
                ClockTarget = LocalClockTarget(saved.ClockTarget),
                LastCountdownUpdateUtc = saved.IsRunning && runningMode == 2
                    ? utcNow
                    : saved.LastCountdownUpdateUtc,
                CountdownInitialized = saved.CountdownInitialized,
                UseClockTarget = saved.UseClockTarget,
                CountdownMinutesText = saved.CountdownMinutesText ?? "5",
                CountdownSecondsText = saved.CountdownSecondsText ?? "00",
                ClockTargetHoursText = saved.ClockTargetHoursText ?? "00",
                ClockTargetMinutesText = saved.ClockTargetMinutesText ?? "00",
                ClockTargetSecondsText = saved.ClockTargetSecondsText ?? "00",
                SmartInputText = saved.SmartInputText ?? "",
                LapCount = saved.LapCount,
                ColonVisible = saved.ColonVisible,
                RecBlinkVisible = saved.RecBlinkVisible,
                OverlayVisible = saved.OverlayVisible,
                HasCustomPosition = saved.HasCustomPosition,
                CustomLeft = saved.CustomLeft,
                CustomTop = saved.CustomTop,
                LastPresetPosition = saved.LastPresetPosition ?? "Top Center",
                CascadeIndex = saved.CascadeIndex
            };

            timer.RestoreElapsed(elapsed, saved.IsRunning);
            foreach (string lap in saved.LapTimes)
                timer.LapTimes.Add(lap);
            foreach (var pair in saved.CustomPositionsByScreen)
                timer.CustomPositionsByScreen[pair.Key] = (pair.Value.Left, pair.Value.Top);

            return timer;
        }

        private static DateTime LocalClockTarget(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };

        private static void Validate(TimerWorkspaceSnapshot snapshot)
        {
            if (snapshot.Version != CurrentVersion)
                throw new InvalidDataException($"Unsupported timer workspace version {snapshot.Version}.");
            if (snapshot.SavedAtUtc == default)
                throw new InvalidDataException("The timer workspace has no save timestamp.");
            if (snapshot.Timers == null)
                throw new InvalidDataException("The timer workspace has no timer list.");
            if (snapshot.Timers.Any(timer => timer == null))
                throw new InvalidDataException("The timer workspace contains an empty timer.");
            if (snapshot.Timers.Any(timer => timer.Id == Guid.Empty || timer.Number < 1))
                throw new InvalidDataException("The timer workspace contains an invalid timer identity.");
            if (snapshot.Timers.Select(timer => timer.Id).Distinct().Count() != snapshot.Timers.Count)
                throw new InvalidDataException("The timer workspace contains duplicate timer ids.");
            if (snapshot.Timers.Select(timer => timer.Number).Distinct().Count() != snapshot.Timers.Count)
                throw new InvalidDataException("The timer workspace contains duplicate timer numbers.");
            if (snapshot.Timers.Any(timer => timer.Elapsed < TimeSpan.Zero))
                throw new InvalidDataException("The timer workspace contains negative elapsed time.");
            if (snapshot.Timers.Any(timer => timer.Mode is < 0 or > 3
                || timer.LastNonClockMode is < 0 or > 3))
                throw new InvalidDataException("The timer workspace contains an invalid timer mode.");
            if (snapshot.Timers.Any(timer => timer.LapCount < 0))
                throw new InvalidDataException("The timer workspace contains a negative lap count.");
            if (snapshot.ActiveTimerId.HasValue
                && snapshot.Timers.All(timer => timer.Id != snapshot.ActiveTimerId.Value))
                throw new InvalidDataException("The active timer id is not present in the workspace.");

            snapshot.CombinedPositionsByScreen ??= new Dictionary<string, TimerScreenPositionSnapshot>();
            if (snapshot.CombinedPositionsByScreen.Any(pair =>
                string.IsNullOrEmpty(pair.Key) || pair.Value == null))
                throw new InvalidDataException("The timer workspace contains an invalid combined-overlay position.");
            if (snapshot.CombinedPositionsByScreen.Values.Any(position =>
                !IsFinite(position.Left) || !IsFinite(position.Top)))
                throw new InvalidDataException("The timer workspace contains non-finite combined-overlay coordinates.");

            foreach (var timer in snapshot.Timers)
            {
                timer.LapTimes ??= new List<string>();
                timer.CustomPositionsByScreen ??= new Dictionary<string, TimerScreenPositionSnapshot>();
                if (timer.CustomPositionsByScreen.Any(pair =>
                    string.IsNullOrEmpty(pair.Key) || pair.Value == null))
                    throw new InvalidDataException("The timer workspace contains an invalid screen position.");
                if (!IsFinite(timer.CustomLeft) || !IsFinite(timer.CustomTop)
                    || timer.CustomPositionsByScreen.Values.Any(position =>
                        !IsFinite(position.Left) || !IsFinite(position.Top)))
                    throw new InvalidDataException("The timer workspace contains non-finite coordinates.");
            }
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private void ClearLastLoadMetadata()
        {
            LastLoadedSavedAtUtc = null;
            LastLoadUsedBackup = false;
            LastLoadedSkipProjectHistoryReconciliation = false;
            LastLoadedCombinedOverlayMode = false;
            LastLoadedCombinedOverlayVisible = true;
            LastLoadedCombinedHasCustomPosition = false;
            LastLoadedCombinedPositionsByScreen =
                new Dictionary<string, TimerScreenPositionSnapshot>();
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try { File.Delete(path); }
            catch { }
        }
    }
}
