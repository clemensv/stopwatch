using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace StopwatchOverlay
{
    public enum ProjectTrackingChange
    {
        NoChange,
        Started,
        Switched
    }

    public enum ProjectRecordMutationStatus
    {
        Success,
        NotFound,
        OpenInterval,
        Overlap
    }

    public sealed record ProjectRecordMutationResult(
        ProjectRecordMutationStatus Status,
        ProjectWorkIntervalView? Record = null);

    /// <summary>
    /// The part of a timer session that determines whether project time should
    /// remain open when persisted timer state and project history are reconciled.
    /// </summary>
    public sealed record ProjectTimerState(
        Guid TimerSessionId,
        string? ProjectName,
        bool IsRunning);

    public sealed record ProjectInfoView(string Key, string Name);

    /// <summary>
    /// An immutable, UTC work interval suitable for dashboard queries.
    /// EndUtc is null while its timer is actively tracking the project.
    /// </summary>
    public sealed record ProjectWorkIntervalView(
        Guid Id,
        Guid TimerSessionId,
        string ProjectKey,
        string ProjectName,
        DateTime StartUtc,
        DateTime? EndUtc)
    {
        public bool IsOpen => !EndUtc.HasValue;

        public TimeSpan Duration(DateTime asOfUtc)
        {
            asOfUtc = ProjectTimeHistory.NormalizeUtc(asOfUtc);
            DateTime effectiveEnd = EndUtc ?? asOfUtc;
            return effectiveEnd > StartUtc
                ? effectiveEnd - StartUtc
                : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// A detached, immutable point-in-time view of all projects and intervals.
    /// The backing collections cannot change when the live tracker is updated.
    /// </summary>
    public sealed class ProjectHistoryView
    {
        internal ProjectHistoryView(
            DateTime asOfUtc,
            IEnumerable<ProjectInfoView> projects,
            IEnumerable<ProjectWorkIntervalView> intervals)
        {
            AsOfUtc = ProjectTimeHistory.NormalizeUtc(asOfUtc);
            Projects = new ReadOnlyCollection<ProjectInfoView>(projects.ToArray());
            Intervals = new ReadOnlyCollection<ProjectWorkIntervalView>(intervals.ToArray());
        }

        public DateTime AsOfUtc { get; }
        public IReadOnlyList<ProjectInfoView> Projects { get; }
        public IReadOnlyList<ProjectWorkIntervalView> Intervals { get; }
    }

    /// <summary>
    /// Thread-safe project registry and work-interval model. Project identity is
    /// case-insensitive; the spelling from the first registration is retained
    /// permanently for display and historical reports.
    /// </summary>
    public sealed class ProjectTimeHistory
    {
        private const int MaximumProjectNameLength = 200;
        private readonly object _gate = new();
        private readonly List<ProjectEntry> _projects = new();
        private readonly List<WorkIntervalEntry> _intervals = new();

        public IReadOnlyList<string> ProjectNames
        {
            get
            {
                lock (_gate)
                {
                    return new ReadOnlyCollection<string>(
                        _projects.Select(project => project.Name).ToArray());
                }
            }
        }

        public string RegisterProject(string projectName)
        {
            string displayName = NormalizeProjectName(projectName);
            string key = CreateProjectKey(displayName);

            lock (_gate)
            {
                return RegisterProjectCore(key, displayName).Name;
            }
        }

        /// <summary>
        /// Adds a closed work record that is independent from every live timer.
        /// A fresh timer identity lets manually entered records overlap records
        /// produced by other independent timers without weakening the per-timer
        /// overlap invariant used for automatic tracking.
        /// </summary>
        public ProjectWorkIntervalView AddManualInterval(
            string projectName,
            DateTime startUtc,
            DateTime endUtc)
        {
            string displayName = NormalizeProjectName(projectName);
            string key = CreateProjectKey(displayName);
            startUtc = NormalizeUtc(startUtc);
            endUtc = NormalizeUtc(endUtc);
            ValidateClosedIntervalRange(startUtc, endUtc, nameof(endUtc));

            lock (_gate)
            {
                Guid intervalId;
                do
                {
                    intervalId = Guid.NewGuid();
                }
                while (intervalId == Guid.Empty
                       || _intervals.Any(interval => interval.Id == intervalId));

                Guid timerSessionId;
                do
                {
                    timerSessionId = Guid.NewGuid();
                }
                while (timerSessionId == Guid.Empty
                       || _intervals.Any(interval =>
                           interval.TimerSessionId == timerSessionId));

                ProjectEntry project = RegisterProjectCore(key, displayName);
                var interval = new WorkIntervalEntry(
                    intervalId,
                    timerSessionId,
                    project.Key,
                    project.Name,
                    startUtc,
                    endUtc);
                _intervals.Add(interval);
                return ToView(interval);
            }
        }

        /// <summary>
        /// Replaces a closed record while preserving both its record and timer
        /// identities. Live/open records remain owned by timer reconciliation and
        /// therefore cannot be changed here. Failed edits have no side effects.
        /// </summary>
        public ProjectRecordMutationResult UpdateClosedInterval(
            Guid id,
            string projectName,
            DateTime startUtc,
            DateTime endUtc)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("A record id must be non-empty.", nameof(id));

            string displayName = NormalizeProjectName(projectName);
            string key = CreateProjectKey(displayName);
            startUtc = NormalizeUtc(startUtc);
            endUtc = NormalizeUtc(endUtc);
            ValidateClosedIntervalRange(startUtc, endUtc, nameof(endUtc));

            lock (_gate)
            {
                int index = _intervals.FindIndex(interval => interval.Id == id);
                if (index < 0)
                {
                    return new ProjectRecordMutationResult(
                        ProjectRecordMutationStatus.NotFound);
                }

                WorkIntervalEntry existing = _intervals[index];
                if (!existing.EndUtc.HasValue)
                {
                    return new ProjectRecordMutationResult(
                        ProjectRecordMutationStatus.OpenInterval);
                }

                bool overlaps = _intervals.Any(interval =>
                    interval.Id != id
                    && interval.TimerSessionId == existing.TimerSessionId
                    && interval.StartUtc < endUtc
                    && startUtc < (interval.EndUtc ?? DateTime.MaxValue));
                if (overlaps)
                {
                    return new ProjectRecordMutationResult(
                        ProjectRecordMutationStatus.Overlap);
                }

                ProjectEntry project = RegisterProjectCore(key, displayName);
                var replacement = new WorkIntervalEntry(
                    existing.Id,
                    existing.TimerSessionId,
                    project.Key,
                    project.Name,
                    startUtc,
                    endUtc);
                _intervals[index] = replacement;
                return new ProjectRecordMutationResult(
                    ProjectRecordMutationStatus.Success,
                    ToView(replacement));
            }
        }

        /// <summary>
        /// Permanently removes one closed record. Live/open records remain owned by
        /// timer reconciliation and cannot be deleted from the records editor.
        /// The project registry is intentionally retained for future timers and
        /// manual entries, even when its final record is removed.
        /// </summary>
        public ProjectRecordMutationResult DeleteClosedInterval(Guid id)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("A record id must be non-empty.", nameof(id));

            lock (_gate)
            {
                int index = _intervals.FindIndex(interval => interval.Id == id);
                if (index < 0)
                {
                    return new ProjectRecordMutationResult(
                        ProjectRecordMutationStatus.NotFound);
                }

                WorkIntervalEntry existing = _intervals[index];
                if (!existing.EndUtc.HasValue)
                {
                    return new ProjectRecordMutationResult(
                        ProjectRecordMutationStatus.OpenInterval);
                }

                ProjectWorkIntervalView deleted = ToView(existing);
                _intervals.RemoveAt(index);
                return new ProjectRecordMutationResult(
                    ProjectRecordMutationStatus.Success,
                    deleted);
            }
        }

        /// <summary>
        /// Starts tracking a project for a timer. Repeating the same project is
        /// idempotent. Switching projects closes the previous interval and opens
        /// the replacement at the exact same timestamp.
        /// </summary>
        public ProjectTrackingChange StartTracking(
            Guid timerSessionId,
            string projectName,
            DateTime utcNow)
        {
            ValidateTimerId(timerSessionId);
            string displayName = NormalizeProjectName(projectName);
            string key = CreateProjectKey(displayName);
            utcNow = NormalizeUtc(utcNow);

            lock (_gate)
            {
                WorkIntervalEntry? current = FindOpenIntervalCore(timerSessionId);
                if (current != null && ProjectKeysEqual(current.ProjectKey, key))
                    return ProjectTrackingChange.NoChange;

                ProjectEntry project = RegisterProjectCore(key, displayName);
                DateTime transitionUtc;
                if (current != null)
                {
                    transitionUtc = CloseIntervalCore(current, utcNow);
                }
                else
                {
                    transitionUtc = ClampNewIntervalStartCore(timerSessionId, utcNow);
                }

                _intervals.Add(new WorkIntervalEntry(
                    Guid.NewGuid(),
                    timerSessionId,
                    project.Key,
                    project.Name,
                    transitionUtc,
                    endUtc: null));

                return current == null
                    ? ProjectTrackingChange.Started
                    : ProjectTrackingChange.Switched;
            }
        }

        public bool StopTracking(Guid timerSessionId, DateTime utcNow)
        {
            ValidateTimerId(timerSessionId);
            utcNow = NormalizeUtc(utcNow);

            lock (_gate)
            {
                WorkIntervalEntry? current = FindOpenIntervalCore(timerSessionId);
                if (current == null)
                    return false;

                CloseIntervalCore(current, utcNow);
                return true;
            }
        }

        /// <summary>
        /// Makes open history agree with restored timer state. Stale intervals
        /// are closed, missing intervals for named running timers are started,
        /// and a changed timer name is treated as an exact-timestamp switch.
        /// </summary>
        public bool Reconcile(
            IEnumerable<ProjectTimerState> timerStates,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(timerStates);
            utcNow = NormalizeUtc(utcNow);

            var states = timerStates.ToArray();
            if (states.Any(state => state == null))
                throw new ArgumentException("Timer state cannot be null.", nameof(timerStates));
            if (states.Any(state => state.TimerSessionId == Guid.Empty))
                throw new ArgumentException("Timer state ids must be non-empty.", nameof(timerStates));
            if (states.Select(state => state.TimerSessionId).Distinct().Count() != states.Length)
                throw new ArgumentException("Timer state ids must be unique.", nameof(timerStates));

            lock (_gate)
            {
                bool changed = false;
                var byId = states.ToDictionary(state => state.TimerSessionId);

                WorkIntervalEntry[] intervalsToClose = _intervals
                    .Where(item => item.EndUtc == null)
                    .Where(interval =>
                    {
                        return !byId.TryGetValue(interval.TimerSessionId, out var state)
                            || !state.IsRunning
                            || !TryNormalizeProjectName(state.ProjectName, out string? displayName)
                            || !ProjectKeysEqual(interval.ProjectKey, CreateProjectKey(displayName!));
                    })
                    .ToArray();

                // Close stale intervals first. A running timer whose project was
                // renamed will be reopened below at precisely the same instant.
                var transitionTimes = new Dictionary<Guid, DateTime>();
                foreach (WorkIntervalEntry interval in intervalsToClose)
                {
                    transitionTimes[interval.TimerSessionId] =
                        CloseIntervalCore(interval, utcNow);
                    changed = true;
                }

                foreach (ProjectTimerState state in states)
                {
                    if (!state.IsRunning
                        || !TryNormalizeProjectName(state.ProjectName, out string? displayName))
                    {
                        continue;
                    }

                    string key = CreateProjectKey(displayName!);
                    ProjectEntry project = RegisterProjectCore(key, displayName!);
                    WorkIntervalEntry? current = FindOpenIntervalCore(state.TimerSessionId);
                    if (current != null)
                        continue;

                    DateTime startUtc = transitionTimes.TryGetValue(
                        state.TimerSessionId,
                        out DateTime transitionUtc)
                            ? transitionUtc
                            : ClampNewIntervalStartCore(state.TimerSessionId, utcNow);

                    _intervals.Add(new WorkIntervalEntry(
                        Guid.NewGuid(),
                        state.TimerSessionId,
                        project.Key,
                        project.Name,
                        startUtc,
                        endUtc: null));
                    changed = true;
                }

                return changed;
            }
        }

        public ProjectWorkIntervalView? GetOpenInterval(Guid timerSessionId)
        {
            ValidateTimerId(timerSessionId);
            lock (_gate)
            {
                WorkIntervalEntry? interval = FindOpenIntervalCore(timerSessionId);
                return interval == null ? null : ToView(interval);
            }
        }

        public ProjectHistoryView CreateView(DateTime asOfUtc)
        {
            asOfUtc = NormalizeUtc(asOfUtc);
            lock (_gate)
            {
                return new ProjectHistoryView(
                    asOfUtc,
                    _projects.Select(project => new ProjectInfoView(project.Key, project.Name)),
                    _intervals
                        .OrderBy(interval => interval.StartUtc)
                        .ThenBy(interval => interval.Id)
                        .Select(ToView));
            }
        }

        internal ProjectHistoryDocument CreateDocument(DateTime savedAtUtc)
        {
            savedAtUtc = NormalizeUtc(savedAtUtc);
            lock (_gate)
            {
                return new ProjectHistoryDocument
                {
                    Version = ProjectTimeStore.CurrentVersion,
                    SavedAtUtc = savedAtUtc,
                    Projects = _projects.Select(project => new ProjectDocumentEntry
                    {
                        Key = project.Key,
                        Name = project.Name
                    }).ToList(),
                    Intervals = _intervals.Select(interval => new WorkIntervalDocumentEntry
                    {
                        Id = interval.Id,
                        TimerSessionId = interval.TimerSessionId,
                        ProjectKey = interval.ProjectKey,
                        ProjectName = interval.ProjectName,
                        StartUtc = interval.StartUtc,
                        EndUtc = interval.EndUtc
                    }).ToList()
                };
            }
        }

        internal static ProjectTimeHistory FromDocument(ProjectHistoryDocument document)
        {
            ProjectTimeStore.Validate(document);
            var result = new ProjectTimeHistory();

            foreach (ProjectDocumentEntry project in document.Projects)
            {
                result._projects.Add(new ProjectEntry(
                    project.Key,
                    project.Name));
            }

            foreach (WorkIntervalDocumentEntry interval in document.Intervals)
            {
                result._intervals.Add(new WorkIntervalEntry(
                    interval.Id,
                    interval.TimerSessionId,
                    interval.ProjectKey,
                    interval.ProjectName,
                    NormalizeUtc(interval.StartUtc),
                    interval.EndUtc.HasValue
                        ? NormalizeUtc(interval.EndUtc.Value)
                        : null));
            }

            return result;
        }

        internal static string NormalizeProjectName(string projectName)
        {
            if (projectName == null)
                throw new ArgumentNullException(nameof(projectName));

            string normalized = projectName.Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("A project name cannot be empty.", nameof(projectName));
            if (normalized.Length > MaximumProjectNameLength)
                throw new ArgumentException(
                    $"A project name cannot exceed {MaximumProjectNameLength} characters.",
                    nameof(projectName));
            if (normalized.Any(char.IsControl))
                throw new ArgumentException("A project name cannot contain control characters.", nameof(projectName));

            return normalized;
        }

        internal static bool TryNormalizeProjectName(
            string? projectName,
            out string? normalized)
        {
            normalized = null;
            if (projectName == null)
                return false;

            try
            {
                normalized = NormalizeProjectName(projectName);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static string CreateProjectKey(string normalizedProjectName)
            => normalizedProjectName.ToUpperInvariant();

        internal static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default)
                throw new ArgumentOutOfRangeException(nameof(value), "A timestamp is required.");

            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static bool ProjectKeysEqual(string left, string right)
            => StringComparer.OrdinalIgnoreCase.Equals(left, right);

        private ProjectEntry RegisterProjectCore(string key, string displayName)
        {
            ProjectEntry? existing = _projects.FirstOrDefault(
                project => ProjectKeysEqual(project.Key, key));
            if (existing != null)
                return existing;

            var project = new ProjectEntry(key, displayName);
            _projects.Add(project);
            return project;
        }

        private WorkIntervalEntry? FindOpenIntervalCore(Guid timerSessionId)
            => _intervals.FirstOrDefault(interval =>
                interval.TimerSessionId == timerSessionId && interval.EndUtc == null);

        private DateTime ClampNewIntervalStartCore(Guid timerSessionId, DateTime requestedUtc)
        {
            DateTime latestEnd = _intervals
                .Where(interval => interval.TimerSessionId == timerSessionId)
                .Where(interval => interval.EndUtc.HasValue)
                .Select(interval => interval.EndUtc!.Value)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            return requestedUtc < latestEnd ? latestEnd : requestedUtc;
        }

        private static DateTime CloseIntervalCore(WorkIntervalEntry interval, DateTime utcNow)
        {
            DateTime effectiveEnd = utcNow < interval.StartUtc
                ? interval.StartUtc
                : utcNow;
            interval.EndUtc = effectiveEnd;
            return effectiveEnd;
        }

        private static void ValidateClosedIntervalRange(
            DateTime startUtc,
            DateTime endUtc,
            string endParameterName)
        {
            if (endUtc <= startUtc)
            {
                throw new ArgumentException(
                    "A closed work interval must end after it starts.",
                    endParameterName);
            }
        }

        private static ProjectWorkIntervalView ToView(WorkIntervalEntry interval)
            => new(
                interval.Id,
                interval.TimerSessionId,
                interval.ProjectKey,
                interval.ProjectName,
                interval.StartUtc,
                interval.EndUtc);

        private static void ValidateTimerId(Guid timerSessionId)
        {
            if (timerSessionId == Guid.Empty)
                throw new ArgumentException("A timer id must be non-empty.", nameof(timerSessionId));
        }

        private sealed record ProjectEntry(string Key, string Name);

        private sealed class WorkIntervalEntry
        {
            public WorkIntervalEntry(
                Guid id,
                Guid timerSessionId,
                string projectKey,
                string projectName,
                DateTime startUtc,
                DateTime? endUtc)
            {
                Id = id;
                TimerSessionId = timerSessionId;
                ProjectKey = projectKey;
                ProjectName = projectName;
                StartUtc = startUtc;
                EndUtc = endUtc;
            }

            public Guid Id { get; }
            public Guid TimerSessionId { get; }
            public string ProjectKey { get; }
            public string ProjectName { get; }
            public DateTime StartUtc { get; }
            public DateTime? EndUtc { get; set; }
        }
    }
}
