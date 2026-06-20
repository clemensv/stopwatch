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

        private static ParseResult ParseDateTime(string s, DateTime now, bool forceClock)
        {
            s = (" " + s + " ").Replace(" at ", " ").Replace(" on ", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();

            int hour = 0, minute = 0, second = 0;
            bool hasTime = false;

            // 12-hour with meridiem.
            var tm = Regex.Match(s, @"\b(\d{1,2})(?::(\d{1,2}))?(?::(\d{1,2}))?\s*(am|pm)\b");
            if (tm.Success)
            {
                hour = int.Parse(tm.Groups[1].Value);
                minute = tm.Groups[2].Success ? int.Parse(tm.Groups[2].Value) : 0;
                second = tm.Groups[3].Success ? int.Parse(tm.Groups[3].Value) : 0;
                if (hour < 1 || hour > 12)
                    return ParseResult.Fail("Invalid time.");
                string mer = tm.Groups[4].Value;
                if (mer == "pm" && hour < 12) hour += 12;
                if (mer == "am" && hour == 12) hour = 0;
                hasTime = true;
                s = s.Remove(tm.Index, tm.Length).Trim();
            }
            else if (forceClock)
            {
                // 24-hour clock, only when "until/till" forced this branch.
                var tc = Regex.Match(s, @"\b(\d{1,2})(?::(\d{1,2}))?(?::(\d{1,2}))?\b");
                if (tc.Success)
                {
                    hour = int.Parse(tc.Groups[1].Value);
                    minute = tc.Groups[2].Success ? int.Parse(tc.Groups[2].Value) : 0;
                    second = tc.Groups[3].Success ? int.Parse(tc.Groups[3].Value) : 0;
                    hasTime = true;
                    s = s.Remove(tc.Index, tc.Length).Trim();
                }
            }

            if (hour > 23 || minute > 59 || second > 59)
                return ParseResult.Fail("Invalid time.");

            s = Regex.Replace(s, @"\s+", " ").Trim();

            // Date component from whatever text remains.
            DateTime date;
            bool hasDate = true;
            if (s.Length == 0) { hasDate = false; date = now.Date; }
            else if (!TryParseDate(s, now, out date))
                return ParseResult.Fail($"Could not understand \"{s}\".");

            if (!hasTime && !hasDate)
                return ParseResult.Fail("Enter a duration or time.");

            var result = date.AddHours(hour).AddMinutes(minute).AddSeconds(second);

            // Roll a bare time-of-day forward to tomorrow if it already passed today.
            if (!hasDate && result <= now) result = result.AddDays(1);
            // An explicit calendar date in the past bumps to next year.
            else if (hasDate && result <= now) result = result.AddYears(1);

            return ParseResult.FromTarget(result);
        }

        private static readonly string[] MonthNames =
        {
            "january", "february", "march", "april", "may", "june",
            "july", "august", "september", "october", "november", "december"
        };
        private static readonly string[] MonthAbbr =
        {
            "jan", "feb", "mar", "apr", "may", "jun",
            "jul", "aug", "sep", "oct", "nov", "dec"
        };
        private static readonly string[] WeekdayNames =
        {
            "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"
        };
        private static readonly string[] WeekdayAbbr =
        {
            "sun", "mon", "tue", "wed", "thu", "fri", "sat"
        };

        private static bool ContainsMonth(string s) =>
            MonthNames.Any(n => Regex.IsMatch(s, $@"\b{n}\b")) ||
            MonthAbbr.Any(n => Regex.IsMatch(s, $@"\b{n}\b"));

        private static int MonthIndex(string t)
        {
            int i = Array.IndexOf(MonthNames, t);
            if (i < 0) i = Array.IndexOf(MonthAbbr, t);
            return i < 0 ? -1 : i + 1;
        }

        private static bool TryParseDate(string s, DateTime now, out DateTime date)
        {
            date = now.Date;

            if (s == "today") { date = now.Date; return true; }
            if (s == "tomorrow") { date = now.Date.AddDays(1); return true; }

            // Weekday -> strictly-future occurrence.
            int wd = Array.IndexOf(WeekdayNames, s);
            if (wd < 0) wd = Array.IndexOf(WeekdayAbbr, s);
            if (wd >= 0)
            {
                int delta = (wd - (int)now.DayOfWeek + 7) % 7;
                if (delta == 0) delta = 7;
                date = now.Date.AddDays(delta);
                return true;
            }

            // Numeric M/d.
            var num = Regex.Match(s, @"^(\d{1,2})/(\d{1,2})$");
            if (num.Success)
            {
                int month = int.Parse(num.Groups[1].Value);
                int day = int.Parse(num.Groups[2].Value);
                return TryBuildDate(now.Year, month, day, out date);
            }

            // Month + day in either order ("jan 1" / "1 jan").
            var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 2)
            {
                int month = MonthIndex(tokens[0]);
                string dayTok = tokens[1];
                if (month < 0) { month = MonthIndex(tokens[1]); dayTok = tokens[0]; }
                if (month > 0 && int.TryParse(dayTok, out int d))
                    return TryBuildDate(now.Year, month, d, out date);
            }

            return false;
        }

        private static bool TryBuildDate(int year, int month, int day, out DateTime date)
        {
            date = default;
            if (month < 1 || month > 12) return false;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
            date = new DateTime(year, month, day);
            return true;
        }
    }
}
