using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;

namespace StopwatchOverlay
{
    /// <summary>
    /// A Stopwatch-compatible clock whose accumulated time can be restored from
    /// disk. The regular <see cref="System.Diagnostics.Stopwatch"/> cannot be
    /// assigned an initial elapsed value, so the restored portion is held as an
    /// offset and combined with the live monotonic stopwatch.
    /// </summary>
    public sealed class ResumableStopwatch
    {
        private readonly Stopwatch _live = new();
        private TimeSpan _elapsedOffset;

        public TimeSpan Elapsed => _elapsedOffset + _live.Elapsed;
        public TimeSpan ElapsedOffset => _elapsedOffset;
        public bool IsRunning => _live.IsRunning;

        public void Start() => _live.Start();
        public void Stop() => _live.Stop();

        public void Reset()
        {
            _live.Reset();
            _elapsedOffset = TimeSpan.Zero;
        }

        public void Restart()
        {
            _elapsedOffset = TimeSpan.Zero;
            _live.Restart();
        }

        /// <summary>
        /// Replaces the accumulated time and optionally resumes the monotonic
        /// clock. Negative elapsed values are rejected because Stopwatch itself
        /// can never produce one.
        /// </summary>
        public void Restore(TimeSpan elapsed, bool start)
        {
            if (elapsed < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(elapsed));

            _live.Reset();
            _elapsedOffset = elapsed;
            if (start)
                _live.Start();
        }
    }

    /// <summary>
    /// Independent runtime state for one logical timer. Overlay windows are kept
    /// separately because one logical timer can be mirrored to several screens.
    /// </summary>
    public sealed class TimerSession
    {
        public TimerSession(int number)
            : this(Guid.NewGuid(), number)
        {
        }

        public TimerSession(Guid id, int number)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("A timer must have a stable, non-empty id.", nameof(id));
            // Number zero is reserved for the controller's non-persisted empty
            // placeholder. Real timers created by the manager start at one.
            if (number < 0)
                throw new ArgumentOutOfRangeException(nameof(number));

            Id = id;
            Number = number;
        }

        public Guid Id { get; }
        public int Number { get; }
        public string Name { get; set; } = "";
        public ResumableStopwatch Stopwatch { get; } = new();
        public TimeSpan Elapsed => Stopwatch.Elapsed;
        public TimeSpan ElapsedOffset => Stopwatch.ElapsedOffset;
        public bool IsRunning { get; set; }

        public void RestoreElapsed(TimeSpan elapsed, bool start)
            => Stopwatch.Restore(elapsed, start);

        public void ResetElapsed() => Stopwatch.Reset();

        // 0=Stopwatch, 1=Clock, 2=Countdown, 3=Timecode
        public int Mode { get; set; }
        public int LastNonClockMode { get; set; }

        public TimeSpan CountdownDuration { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan CountdownRemaining { get; set; }
        public DateTime ClockTarget { get; set; }
        public DateTime LastCountdownUpdateUtc { get; set; }
        public bool CountdownInitialized { get; set; }
        public bool UseClockTarget { get; set; }

        // Per-timer countdown editor contents survive F3 activation changes.
        public string CountdownMinutesText { get; set; } = "5";
        public string CountdownSecondsText { get; set; } = "00";
        public string ClockTargetHoursText { get; set; } = "00";
        public string ClockTargetMinutesText { get; set; } = "00";
        public string ClockTargetSecondsText { get; set; } = "00";
        public string SmartInputText { get; set; } = "";

        public ObservableCollection<string> LapTimes { get; } = new();
        public int LapCount { get; set; }
        public bool ColonVisible { get; set; } = true;
        public bool RecBlinkVisible { get; set; }

        public bool OverlayVisible { get; set; }
        public bool HasCustomPosition { get; set; }
        public double CustomLeft { get; set; }
        public double CustomTop { get; set; }
        public Dictionary<string, (double Left, double Top)> CustomPositionsByScreen { get; } = new();
        public string LastPresetPosition { get; set; } = "Top Center";
        public int CascadeIndex { get; set; }
    }
}
