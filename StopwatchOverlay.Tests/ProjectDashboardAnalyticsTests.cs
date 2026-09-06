using System;
using System.Collections.Generic;
using StopwatchOverlay;
using Xunit;

namespace StopwatchOverlay.Tests;

public sealed class ProjectDashboardAnalyticsTests
{
    private static readonly TimeZoneInfo TestTimeZone = CreateTestTimeZone();

    [Fact]
    public void CreateRange_DayUsesExactHalfOpenLocalBoundaries()
    {
        DateTime asOfUtc = Utc(2026, 3, 12, 12);

        DashboardUtcRange range = ProjectDashboardAnalytics.CreateRange(
            DashboardRange.Day,
            new DateTime(2026, 3, 8),
            asOfUtc,
            TestTimeZone);

        Assert.Equal(Utc(2026, 3, 8, 5), range.StartUtc);
        Assert.Equal(Utc(2026, 3, 9, 4), range.EndUtc);
        Assert.Equal(TimeSpan.FromHours(23), range.EndUtc - range.StartUtc);
    }

    [Fact]
    public void CreateRange_RepeatedHourDayIncludesBothOccurrences()
    {
        DashboardUtcRange range = ProjectDashboardAnalytics.CreateRange(
            DashboardRange.Day,
            new DateTime(2026, 11, 1),
            Utc(2026, 11, 3),
            TestTimeZone);

        Assert.Equal(Utc(2026, 11, 1, 4), range.StartUtc);
        Assert.Equal(Utc(2026, 11, 2, 5), range.EndUtc);
        Assert.Equal(TimeSpan.FromHours(25), range.EndUtc - range.StartUtc);
    }

    [Theory]
    [InlineData((int)DashboardRange.SevenDays, 7)]
    [InlineData((int)DashboardRange.ThirtyDays, 30)]
    public void CreateRange_MultiDayRangesIncludeSelectedDay(
        int dashboardRangeValue,
        int expectedDays)
    {
        DashboardRange dashboardRange = (DashboardRange)dashboardRangeValue;
        DateTime selectedDate = new(2026, 2, 20);

        DashboardUtcRange range = ProjectDashboardAnalytics.CreateRange(
            dashboardRange,
            selectedDate,
            Utc(2026, 2, 22),
            TimeZoneInfo.Utc);

        Assert.Equal(
            DateTime.SpecifyKind(selectedDate.AddDays(1 - expectedDays), DateTimeKind.Utc),
            range.StartUtc);
        Assert.Equal(Utc(2026, 2, 21), range.EndUtc);
    }

    [Fact]
    public void CreateRange_FutureDayClampsToCurrentLocalDayAndAsOf()
    {
        DateTime asOfUtc = Utc(2026, 6, 10, 16, 30);

        DashboardUtcRange range = ProjectDashboardAnalytics.CreateRange(
            DashboardRange.Day,
            new DateTime(2030, 1, 1),
            asOfUtc,
            TestTimeZone);

        Assert.Equal(Utc(2026, 6, 10, 4), range.StartUtc);
        Assert.Equal(asOfUtc, range.EndUtc);
    }

    [Fact]
    public void CreateRange_AllTimeHasNoStartAndEndsAtAsOf()
    {
        DateTime asOfUtc = Utc(2026, 6, 10, 16, 30);

        DashboardUtcRange range = ProjectDashboardAnalytics.CreateRange(
            DashboardRange.AllTime,
            new DateTime(2026, 6, 10),
            asOfUtc,
            TestTimeZone);

        Assert.Null(range.StartUtc);
        Assert.Equal(asOfUtc, range.EndUtc);
    }

    [Fact]
    public void LocalBoundaryToUtc_HandlesInvalidAndAmbiguousWallTimes()
    {
        DateTime invalid = ProjectDashboardAnalytics.LocalBoundaryToUtc(
            new DateTime(2026, 3, 8, 2, 30, 0),
            TestTimeZone);
        DateTime ambiguous = ProjectDashboardAnalytics.LocalBoundaryToUtc(
            new DateTime(2026, 11, 1, 1, 30, 0),
            TestTimeZone);

        Assert.Equal(Utc(2026, 3, 8, 7), invalid);
        Assert.Equal(Utc(2026, 11, 1, 5, 30), ambiguous);
    }

    [Fact]
    public void Clip_ClipsBothEndsOfInterval()
    {
        ProjectWorkIntervalView source = Interval(
            "Alpha",
            Utc(2026, 1, 1, 8),
            Utc(2026, 1, 1, 14));
        var range = new DashboardUtcRange(
            Utc(2026, 1, 1, 10),
            Utc(2026, 1, 1, 12));

        ClippedProjectInterval clipped = Assert.IsType<ClippedProjectInterval>(
            ProjectDashboardAnalytics.Clip(source, range, Utc(2026, 1, 1, 13)));

        Assert.Equal(Utc(2026, 1, 1, 10), clipped.StartUtc);
        Assert.Equal(Utc(2026, 1, 1, 12), clipped.EndUtc);
        Assert.Equal(TimeSpan.FromHours(2), clipped.Duration);
        Assert.False(clipped.IsLive);
    }

    [Fact]
    public void Clip_HistoricalSliceOfOpenIntervalIsNotLive()
    {
        ProjectWorkIntervalView source = Interval(
            "Alpha",
            Utc(2026, 1, 1, 8),
            endUtc: null);
        var historicalRange = new DashboardUtcRange(
            Utc(2026, 1, 1),
            Utc(2026, 1, 2));

        ClippedProjectInterval clipped = Assert.IsType<ClippedProjectInterval>(
            ProjectDashboardAnalytics.Clip(
                source,
                historicalRange,
                Utc(2026, 1, 3, 12)));

        Assert.Equal(Utc(2026, 1, 2), clipped.EndUtc);
        Assert.False(clipped.IsLive);
    }

    [Fact]
    public void Clip_CurrentSliceOfOpenIntervalIsLive()
    {
        DateTime asOfUtc = Utc(2026, 1, 1, 12);
        ProjectWorkIntervalView source = Interval(
            "Alpha",
            Utc(2026, 1, 1, 8),
            endUtc: null);

        ClippedProjectInterval clipped = Assert.IsType<ClippedProjectInterval>(
            ProjectDashboardAnalytics.Clip(
                source,
                new DashboardUtcRange(Utc(2026, 1, 1), asOfUtc),
                asOfUtc));

        Assert.True(clipped.IsLive);
    }

    [Fact]
    public void BuildHeatmap_IncludesZeroDaysAndSplitsCrossMidnightRecord()
    {
        ProjectWorkIntervalView record = Interval(
            "Alpha",
            Utc(2026, 1, 1, 23),
            Utc(2026, 1, 2, 1));
        ProjectHistoryView history = History(Utc(2026, 1, 4), record);

        IReadOnlyList<HeatmapDayValue> values = ProjectDashboardAnalytics.BuildHeatmap(
            history,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 3),
            projectKey: null,
            TimeZoneInfo.Utc);

        Assert.Collection(
            values,
            day => AssertDay(day, new DateTime(2026, 1, 1), TimeSpan.FromHours(1), 1),
            day => AssertDay(day, new DateTime(2026, 1, 2), TimeSpan.FromHours(1), 1),
            day => AssertDay(day, new DateTime(2026, 1, 3), TimeSpan.Zero, 0));
    }

    [Fact]
    public void BuildHeatmap_ProjectFilterIsCaseInsensitive()
    {
        ProjectWorkIntervalView alpha = Interval(
            "Alpha",
            Utc(2026, 1, 1, 8),
            Utc(2026, 1, 1, 9));
        ProjectWorkIntervalView beta = Interval(
            "Beta",
            Utc(2026, 1, 1, 10),
            Utc(2026, 1, 1, 12));
        ProjectHistoryView history = History(Utc(2026, 1, 2), alpha, beta);

        HeatmapDayValue value = Assert.Single(ProjectDashboardAnalytics.BuildHeatmap(
            history,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 1),
            "alpha",
            TimeZoneInfo.Utc));

        Assert.Equal(TimeSpan.FromHours(1), value.Duration);
        Assert.Equal(1, value.RecordCount);
    }

    [Fact]
    public void BuildHeatmap_SumsSimultaneousRecordsAndClampsOpenRecordToAsOf()
    {
        ProjectWorkIntervalView first = Interval(
            "Alpha",
            Utc(2026, 1, 1, 8),
            Utc(2026, 1, 1, 10));
        ProjectWorkIntervalView second = Interval(
            "Beta",
            Utc(2026, 1, 1, 9),
            endUtc: null);
        ProjectHistoryView history = History(Utc(2026, 1, 1, 11), first, second);

        HeatmapDayValue value = Assert.Single(ProjectDashboardAnalytics.BuildHeatmap(
            history,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 1),
            projectKey: null,
            TimeZoneInfo.Utc));

        Assert.Equal(TimeSpan.FromHours(4), value.Duration);
        Assert.Equal(2, value.RecordCount);
    }

    [Fact]
    public void BuildHeatmap_UsesHalfOpenEdgesAndSkipsOutsideRecords()
    {
        ProjectWorkIntervalView endingAtStart = Interval(
            "Before",
            Utc(2026, 1, 9, 22),
            Utc(2026, 1, 10));
        ProjectWorkIntervalView inside = Interval(
            "Inside",
            Utc(2026, 1, 10, 1),
            Utc(2026, 1, 10, 2));
        ProjectWorkIntervalView startingAtEnd = Interval(
            "After",
            Utc(2026, 1, 11),
            Utc(2026, 1, 11, 1));
        ProjectHistoryView history = History(
            Utc(2026, 1, 12),
            endingAtStart,
            inside,
            startingAtEnd);

        HeatmapDayValue value = Assert.Single(ProjectDashboardAnalytics.BuildHeatmap(
            history,
            new DateTime(2026, 1, 10),
            new DateTime(2026, 1, 10),
            projectKey: null,
            TimeZoneInfo.Utc));

        Assert.Equal(TimeSpan.FromHours(1), value.Duration);
        Assert.Equal(1, value.RecordCount);
    }

    private static void AssertDay(
        HeatmapDayValue actual,
        DateTime expectedDate,
        TimeSpan expectedDuration,
        int expectedRecordCount)
    {
        Assert.Equal(expectedDate, actual.Date);
        Assert.Equal(expectedDuration, actual.Duration);
        Assert.Equal(expectedRecordCount, actual.RecordCount);
    }

    private static ProjectHistoryView History(
        DateTime asOfUtc,
        params ProjectWorkIntervalView[] intervals)
    {
        var projects = new List<ProjectInfoView>();
        foreach (ProjectWorkIntervalView interval in intervals)
        {
            if (!projects.Exists(project => string.Equals(
                    project.Key,
                    interval.ProjectKey,
                    StringComparison.OrdinalIgnoreCase)))
            {
                projects.Add(new ProjectInfoView(interval.ProjectKey, interval.ProjectName));
            }
        }

        return new ProjectHistoryView(asOfUtc, projects, intervals);
    }

    private static ProjectWorkIntervalView Interval(
        string projectName,
        DateTime startUtc,
        DateTime? endUtc)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            projectName.ToUpperInvariant(),
            projectName,
            startUtc,
            endUtc);

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour = 0,
        int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static TimeZoneInfo CreateTestTimeZone()
    {
        TimeZoneInfo.TransitionTime daylightStart =
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                month: 3,
                week: 2,
                dayOfWeek: DayOfWeek.Sunday);
        TimeZoneInfo.TransitionTime daylightEnd =
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                month: 11,
                week: 1,
                dayOfWeek: DayOfWeek.Sunday);
        TimeZoneInfo.AdjustmentRule adjustment = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            "ProjectDashboardAnalyticsTests",
            TimeSpan.FromHours(-5),
            "Project dashboard test zone",
            "Test standard time",
            "Test daylight time",
            [adjustment]);
    }
}
