using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StopwatchOverlay
{
    // Outcome of parsing one countdown expression. Exactly one of Duration/Target
    // is set on success; Error is set on failure.
    public readonly struct ParseResult
    {
        public bool Success { get; }
        public TimeSpan? Duration { get; }
        public DateTime? Target { get; }
        public string? Error { get; }

        private ParseResult(bool success, TimeSpan? duration, DateTime? target, string? error)
        {
            Success = success;
            Duration = duration;
            Target = target;
            Error = error;
        }

        public static ParseResult FromDuration(TimeSpan d) => new(true, d, null, null);
        public static ParseResult FromTarget(DateTime t) => new(true, null, t, null);
        public static ParseResult Fail(string error) => new(false, null, null, error);
    }

    // Parses Hourglass-style countdown expressions. Pure and deterministic:
    // the caller supplies the reference instant `now`.
    public static class CountdownParser
    {
        private enum Unit { None, Second, Minute, Hour, Day, Week, Month, Year }

        public static ParseResult Parse(string input, DateTime now)
        {
            var s = (input ?? string.Empty).Trim();
            if (s.Length == 0) return ParseResult.Fail("Enter a duration or time.");

            s = Regex.Replace(s.ToLowerInvariant(), @"\s+", " ");

            // "until 14:30" / "till 9:00" forces clock-time interpretation.
            bool forceClock = false;
            var prefix = Regex.Match(s, @"^(?:until|till|til)\s+(.+)$");
            if (prefix.Success) { forceClock = true; s = prefix.Groups[1].Value; }

            bool clockBranch = forceClock
                || Regex.IsMatch(s, @"\b(am|pm)\b")
                || Regex.IsMatch(s, @"\b(today|tomorrow|sunday|sun|monday|mon|tuesday|tue|wednesday|wed|thursday|thu|friday|fri|saturday|sat)\b")
                || Regex.IsMatch(s, @"\d{1,2}/\d{1,2}")
                || ContainsMonth(s);

            return clockBranch
                ? ParseDateTime(s, now, forceClock)
                : ParseDuration(s, now);
        }

        private static ParseResult ParseDuration(string s, DateTime now)
        {
            // Pure separator form: 5:30, 7:15:00, 5.30, 7.15.00 (digits + : or . only).
            if (Regex.IsMatch(s, @"^\d{1,3}([:.]\d{1,2}){1,2}$"))
            {
                var parts = Regex.Split(s, @"[:.]").Select(int.Parse).ToArray();
                TimeSpan ts = parts.Length == 2
                    ? new TimeSpan(0, parts[0], parts[1])          // m:s
                    : new TimeSpan(parts[0], parts[1], parts[2]);  // h:m:s
                return ParseResult.FromDuration(ts);
            }

            // Bare number (optionally decimal), no unit -> minutes.
            if (Regex.IsMatch(s, @"^\d+(\.\d+)?$"))
            {
                double mins = double.Parse(s, CultureInfo.InvariantCulture);
                return ParseResult.FromDuration(TimeSpan.FromMinutes(mins));
            }

            // Value+unit pairs: "1h30m", "5 minutes 30 seconds", "0.5 years".
            var matches = Regex.Matches(s, @"(\d+(?:\.\d+)?)\s*([a-z]+)");
            if (matches.Count == 0)
                return ParseResult.Fail($"Could not understand \"{s}\".");

            // Reject leftover text we did not consume (e.g. "5 potatoes 3m").
            var consumed = string.Concat(matches.Select(x => x.Value));
            if (Strip(s) != Strip(consumed))
                return ParseResult.Fail($"Could not understand \"{s}\".");

            TimeSpan span = TimeSpan.Zero;
            double totalMonths = 0;
            foreach (Match mt in matches)
            {
                double val = double.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (UnitKind(mt.Groups[2].Value))
                {
                    case Unit.Second: span += TimeSpan.FromSeconds(val); break;
                    case Unit.Minute: span += TimeSpan.FromMinutes(val); break;
                    case Unit.Hour:   span += TimeSpan.FromHours(val); break;
                    case Unit.Day:    span += TimeSpan.FromDays(val); break;
                    case Unit.Week:   span += TimeSpan.FromDays(val * 7); break;
                    case Unit.Month:  totalMonths += val; break;
                    case Unit.Year:   totalMonths += val * 12; break;
                    default: return ParseResult.Fail($"Could not understand \"{s}\".");
                }
            }

            if (totalMonths > 0)
            {
                int whole = (int)Math.Floor(totalMonths);
                double fracDays = (totalMonths - whole) * 30.436875; // mean month length
                var target = now.AddMonths(whole).AddDays(fracDays) + span;
                return ParseResult.FromTarget(target);
            }

            return ParseResult.FromDuration(span);
        }

        private static string Strip(string s) => Regex.Replace(s, @"\s+", "");

        private static Unit UnitKind(string u) => u switch
        {
            "s" or "sec" or "secs" or "second" or "seconds" => Unit.Second,
            "m" or "min" or "mins" or "minute" or "minutes" => Unit.Minute,
            "h" or "hr" or "hrs" or "hour" or "hours" => Unit.Hour,
            "d" or "day" or "days" => Unit.Day,
            "w" or "wk" or "wks" or "week" or "weeks" => Unit.Week,
            "mo" or "month" or "months" => Unit.Month,
            "y" or "yr" or "yrs" or "year" or "years" => Unit.Year,
            _ => Unit.None,
        };

        // Stub: implemented in Task 3. Keeps the clock branch compiling.
        private static ParseResult ParseDateTime(string s, DateTime now, bool forceClock)
            => ParseResult.Fail($"Could not understand \"{s}\".");

        // Stub: replaced in Task 4.
        private static bool ContainsMonth(string s) => false;
    }
}
