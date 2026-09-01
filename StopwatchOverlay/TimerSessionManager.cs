using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StopwatchOverlay
{
    /// <summary>
    /// Owns the ordered collection of logical timers and the single logical
    /// active timer. This class deliberately has no WPF dependencies; windows
    /// remain the controller's responsibility.
    /// </summary>
    public sealed class TimerSessionManager
    {
        private readonly List<TimerSession> _sessions = new();
        private readonly ReadOnlyCollection<TimerSession> _readOnlySessions;
        private int _nextNumber = 1;

        public TimerSessionManager()
        {
            _readOnlySessions = _sessions.AsReadOnly();
        }

        public IReadOnlyList<TimerSession> Sessions => _readOnlySessions;
        public TimerSession? Active { get; private set; }
        public int Count => _sessions.Count;
        public int NextNumber => _nextNumber;

        /// <summary>
        /// Creates a timer with a never-reused, monotonically increasing number
        /// and makes it active.
        /// </summary>
        public TimerSession Create()
        {
            var session = new TimerSession(_nextNumber++);
            _sessions.Add(session);
            Active = session;
            return session;
        }

        /// <summary>
        /// Makes an owned timer active. Foreign or null sessions are rejected.
        /// </summary>
        public bool Activate(TimerSession? session)
        {
            if (session == null || !_sessions.Contains(session))
                return false;

            Active = session;
            return true;
        }

        /// <summary>
        /// Selects the next timer in creation order, wrapping at the end.
        /// Hidden timers remain part of the cycle so they can be selected and
        /// shown again. Returns null when no timers exist.
        /// </summary>
        public TimerSession? CycleNext()
        {
            if (_sessions.Count == 0)
            {
                Active = null;
                return null;
            }

            int activeIndex = Active == null ? -1 : _sessions.IndexOf(Active);
            int nextIndex = activeIndex < 0 ? 0 : (activeIndex + 1) % _sessions.Count;
            Active = _sessions[nextIndex];
            return Active;
        }

        /// <summary>
        /// Removes an owned timer. Closing the active timer selects its next
        /// neighbor, wrapping to the first timer; closing the final timer leaves
        /// the manager empty with no active timer.
        /// </summary>
        public bool Close(TimerSession? session)
        {
            if (session == null)
                return false;

            int removedIndex = _sessions.IndexOf(session);
            if (removedIndex < 0)
                return false;

            bool wasActive = ReferenceEquals(Active, session);
            _sessions.RemoveAt(removedIndex);
            session.Stopwatch.Stop();
            session.IsRunning = false;

            if (wasActive)
                Active = _sessions.Count == 0
                    ? null
                    : _sessions[removedIndex % _sessions.Count];

            return true;
        }

        public bool CloseActive() => Close(Active);

        /// <summary>
        /// Replaces the workspace with restored sessions while preserving their
        /// saved order and stable ids. The next timer number is always moved
        /// beyond every restored number, even if an older snapshot contains a
        /// stale value.
        /// </summary>
        public void Restore(
            IEnumerable<TimerSession> sessions,
            Guid? activeTimerId,
            int nextNumber)
        {
            ArgumentNullException.ThrowIfNull(sessions);

            var restored = sessions.ToList();
            if (restored.Select(session => session.Id).Distinct().Count() != restored.Count)
                throw new ArgumentException("Restored timer ids must be unique.", nameof(sessions));

            foreach (var existing in _sessions)
                existing.Stopwatch.Stop();

            _sessions.Clear();
            _sessions.AddRange(restored);

            int firstUnusedNumber = restored.Count == 0
                ? 1
                : restored.Max(session => session.Number) + 1;
            _nextNumber = Math.Max(Math.Max(1, nextNumber), firstUnusedNumber);

            Active = activeTimerId.HasValue
                ? restored.FirstOrDefault(session => session.Id == activeTimerId.Value)
                : null;
            Active ??= restored.FirstOrDefault();
        }
    }
}
