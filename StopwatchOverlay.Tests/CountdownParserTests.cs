using System;
using Xunit;
using StopwatchOverlay;

namespace StopwatchOverlay.Tests
{
    public class CountdownParserTests
    {
        // Fixed reference instant for deterministic parsing.
        // 2026-06-20 is a Saturday, 10:00:00.
        private static readonly DateTime Now = new(2026, 6, 20, 10, 0, 0);

        [Theory]
        [InlineData("1", 1)]
        [InlineData("5", 5)]
        [InlineData("10", 10)]
        public void BareNumber_IsMinutes(string input, int minutes)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(TimeSpan.FromMinutes(minutes), r.Duration);
        }

        [Theory]
        [InlineData("30 seconds", 0, 0, 30)]
        [InlineData("30s", 0, 0, 30)]
        [InlineData("1 second", 0, 0, 1)]
        [InlineData("5 minutes", 0, 5, 0)]
        [InlineData("5m", 0, 5, 0)]
        [InlineData("1 minute", 0, 1, 0)]
        [InlineData("7 hours", 7, 0, 0)]
        [InlineData("7h", 7, 0, 0)]
        [InlineData("1 hour", 1, 0, 0)]
        public void SingleUnit_Durations(string input, int h, int m, int s)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(new TimeSpan(h, m, s), r.Duration);
        }

        [Theory]
        [InlineData("3 days", 3)]
        [InlineData("3d", 3)]
        [InlineData("25 weeks", 175)]
        [InlineData("25w", 175)]
        public void DayAndWeek_Durations(string input, int totalDays)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(TimeSpan.FromDays(totalDays), r.Duration);
        }

        [Theory]
        [InlineData("5 minutes 30 seconds", 0, 5, 30)]
        [InlineData("5m30s", 0, 5, 30)]
        [InlineData("7 hours 15 minutes", 7, 15, 0)]
        [InlineData("7h15m", 7, 15, 0)]
        [InlineData("1h30m", 1, 30, 0)]
        public void CombinedUnit_Durations(string input, int h, int m, int s)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(new TimeSpan(h, m, s), r.Duration);
        }

        [Theory]
        [InlineData("5.5 minutes", 0, 5, 30)]
        [InlineData("1.5 hours", 1, 30, 0)]
        public void DecimalWithUnit_Durations(string input, int h, int m, int s)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(new TimeSpan(h, m, s), r.Duration);
        }

        [Theory]
        [InlineData("5:30", 0, 5, 30)]
        [InlineData("7:15:00", 7, 15, 0)]
        [InlineData("14:30", 0, 14, 30)]   // bare colon is ALWAYS a duration
        [InlineData("5.30", 0, 5, 30)]     // dot = separator (no unit present)
        [InlineData("7.15.00", 7, 15, 0)]
        public void ColonAndDot_AreDurations(string input, int h, int m, int s)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(new TimeSpan(h, m, s), r.Duration);
        }

        [Fact]
        public void Months_ResolveToCalendarTarget()
        {
            var r = CountdownParser.Parse("6 months", Now);
            Assert.True(r.Success);
            Assert.Equal(Now.AddMonths(6), r.Target);
        }

        [Fact]
        public void Years_ResolveToCalendarTarget()
        {
            var r = CountdownParser.Parse("2 years", Now);
            Assert.True(r.Success);
            Assert.Equal(Now.AddYears(2), r.Target);
        }

        [Fact]
        public void HalfYear_IsSixMonths()
        {
            var r = CountdownParser.Parse("0.5 years", Now);
            Assert.True(r.Success);
            Assert.Equal(Now.AddMonths(6), r.Target);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("potato")]
        [InlineData("5 potatoes")]
        public void Garbage_Fails(string input)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.False(r.Success);
            Assert.NotNull(r.Error);
        }

        [Fact]
        public void Pm_Time_Today_WhenStillAhead()
        {
            // Now = 10:00 -> 2 pm today.
            var r = CountdownParser.Parse("2 pm", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 20, 14, 0, 0), r.Target);
        }

        [Fact]
        public void Am_Time_RollsToTomorrow_WhenPast()
        {
            // Now = 10:00 -> 9 am already passed -> tomorrow 9 am.
            var r = CountdownParser.Parse("9 am", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 21, 9, 0, 0), r.Target);
        }

        [Theory]
        [InlineData("2:30 pm", 14, 30, 0)]
        [InlineData("2:30:15 pm", 14, 30, 15)]
        [InlineData("12 pm", 12, 0, 0)]
        public void Meridiem_Times(string input, int h, int m, int s)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 20, h, m, s), r.Target);
        }

        [Fact]
        public void Midnight_12am_RollsToNextDay()
        {
            // 12 am = midnight; already past at 10:00 -> next midnight (tomorrow).
            var r = CountdownParser.Parse("12 am", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 21, 0, 0, 0), r.Target);
        }

        [Fact]
        public void Until_24Hour_Time()
        {
            var r = CountdownParser.Parse("until 14:30", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 20, 14, 30, 0), r.Target);
        }

        [Fact]
        public void Until_Past_Time_RollsToTomorrow()
        {
            var r = CountdownParser.Parse("till 9:00", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 21, 9, 0, 0), r.Target);
        }

        [Theory]
        [InlineData("13 pm")]
        [InlineData("0 am")]
        [InlineData("until 25:00")]
        [InlineData("until 9:99")]
        public void InvalidClockTimes_Fail(string input)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.False(r.Success);
        }

        [Fact]
        public void Tomorrow_IsMidnightNextDay()
        {
            var r = CountdownParser.Parse("tomorrow", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 21, 0, 0, 0), r.Target);
        }

        [Theory]
        [InlineData("monday", 22)]    // Now is Saturday 6/20 -> next Mon 6/22
        [InlineData("mon", 22)]
        [InlineData("wednesday", 24)]
        [InlineData("wed", 24)]
        [InlineData("saturday", 27)]  // today is Saturday -> strictly future -> +7
        public void Weekday_NextOccurrence_AtMidnight(string input, int day)
        {
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, day, 0, 0, 0), r.Target);
        }

        [Theory]
        [InlineData("january 1")]
        [InlineData("jan 1")]
        [InlineData("1 january")]
        [InlineData("1 jan")]
        [InlineData("1/1")]
        [InlineData("01/01")]
        public void PastDateThisYear_RollsToNextYear(string input)
        {
            // Jan 1 already passed in 2026 -> 2027-01-01 00:00.
            var r = CountdownParser.Parse(input, Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0), r.Target);
        }

        [Fact]
        public void FutureDateThisYear_StaysThisYear()
        {
            var r = CountdownParser.Parse("dec 25", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 12, 25, 0, 0, 0), r.Target);
        }

        [Fact]
        public void Today_IsMidnightToday_NotNextYear()
        {
            var r = CountdownParser.Parse("today", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 20, 0, 0, 0), r.Target);
        }

        [Fact]
        public void Weekday_NotBumpedToNextYear()
        {
            // Sanity: a weekday stays the next occurrence this year, never +1 year.
            var r = CountdownParser.Parse("monday", Now);
            Assert.True(r.Success);
            Assert.Equal(new DateTime(2026, 6, 22, 0, 0, 0), r.Target);
        }
    }
}
