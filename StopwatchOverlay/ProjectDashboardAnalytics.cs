using System;
using System.Collections.Generic;

namespace StopwatchOverlay;

internal enum DashboardRange
{
    Day,
    SevenDays,
    ThirtyDays,
    AllTime
}

/// <summary>
/// A half-open UTC range: StartUtc is inclusive and EndUtc is exclusive.
/// A null start represents all recorded history before EndUtc.
/// </summary>
internal readonly record struct DashboardUtcRange(DateTime? StartUtc, DateTime EndUtc);

internal readonly record struct ClippedProjectInterval(
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsLive)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

internal readonly record struct HeatmapDayValue(
    DateTime Date,
    TimeSpan Duration,
    int RecordCount);

/// <summary>
/// Time-zone-aware calculations shared by the project dashboard views.
/// </summary>
internal static class ProjectDashboardAnalytics
{
    /// <summary>
    /// Creates a half-open range ending on the selected local calendar day.
    /// A future selected day is clamped to the local date containing asOfUtc.
    /// The current day ends at asOfUtc; a completed historical day ends at its
    /// following local midnight.
    /// </summary>
    internal static DashboardUtcRange CreateRange(
        DashboardRange range,
        DateTime selectedDayLocal,
        DateTime asOfUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (selectedDayLocal == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedDayLocal),
                "A selected local day is required.");
        }

        asOfUtc = NormalizeUtc(asOfUtc, nameof(asOfUtc));
        if (range == DashboardRange.AllTime)
        {
            return new DashboardUtcRange(null, asOfUtc);
        }

        DateTime asOfLocalDate = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, timeZone).Date;
        DateTime selectedDate = DateTime.SpecifyKind(
            selectedDayLocal.Date,
            DateTimeKind.Unspecified);
        if (selectedDate > asOfLocalDate)
        {
            selectedDate = DateTime.SpecifyKind(asOfLocalDate, DateTimeKind.Unspecified);
        }

        int precedingDays = range switch
        {
            DashboardRange.Day => 0,
            DashboardRange.SevenDays => 6,
            DashboardRange.ThirtyDays => 29,
            _ => throw new ArgumentOutOfRangeException(nameof(range))
        };

        DateTime startLocal = selectedDate.AddDays(-precedingDays);
        if (selectedDate >= DateTime.MaxValue.Date)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedDayLocal),
                "The selected day must allow an exclusive end boundary.");
        }

        DateTime naturalEndUtc = LocalBoundaryToUtc(selectedDate.AddDays(1), timeZone);
        DateTime endUtc = naturalEndUtc < asOfUtc ? naturalEndUtc : asOfUtc;
        return new DashboardUtcRange(
            LocalBoundaryToUtc(startLocal, timeZone),
            endUtc);
    }

    /// <summary>
    /// Clips an interval at both report boundaries and at asOfUtc. An open
    /// interval is live only when the resulting interval actually ends at the
    /// supplied as-of instant.
    /// </summary>
    internal static ClippedProjectInterval? Clip(
        ProjectWorkIntervalView source,
        DashboardUtcRange range,
        DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        asOfUtc = NormalizeUtc(asOfUtc, nameof(asOfUtc));
        DateTime rangeEndUtc = NormalizeUtc(range.EndUtc, nameof(range));
        DateTime sourceStartUtc = NormalizeUtc(source.StartUtc, nameof(source));
        DateTime sourceEndUtc = source.EndUtc is { } closedEnd
            ? NormalizeUtc(closedEnd, nameof(source))
            : asOfUtc;

        DateTime startUtc = sourceStartUtc;
        if (range.StartUtc is { } rangeStart)
        {
            DateTime rangeStartUtc = NormalizeUtc(rangeStart, nameof(range));
            if (rangeStartUtc > startUtc)
            {
                startUtc = rangeStartUtc;
            }
        }

        DateTime endUtc = sourceEndUtc;
        if (asOfUtc < endUtc)
        {
            endUtc = asOfUtc;
        }
        if (rangeEndUtc < endUtc)
        {
            endUtc = rangeEndUtc;
        }

        if (endUtc <= startUtc)
        {
            return null;
        }

        bool isLive = source.IsOpen && endUtc == asOfUtc;
        return new ClippedProjectInterval(startUtc, endUtc, isLive);
    }

    /// <summary>
    /// Builds one value for every requested local calendar day, including days
    /// without records. Overlapping records are summed independently, and a
    /// record crossing midnight contributes once to each day it intersects.
    /// </summary>
    internal static IReadOnlyList<HeatmapDayValue> BuildHeatmap(
        ProjectHistoryView history,
        DateTime startLocalDate,
        DateTime endLocalDateInclusive,
        string? projectKey,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeZone);

        DateTime firstDate = DateTime.SpecifyKind(
            startLocalDate.Date,
            DateTimeKind.Unspecified);
        DateTime lastDate = DateTime.SpecifyKind(
            endLocalDateInclusive.Date,
            DateTimeKind.Unspecified);
        if (lastDate < firstDate)
        {
            throw new ArgumentException(
                "The heatmap end date cannot be earlier than its start date.",
                nameof(endLocalDateInclusive));
        }
        if (lastDate >= DateTime.MaxValue.Date)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endLocalDateInclusive),
                "The final day must allow an exclusive end boundary.");
        }

        var days = new List<DayAccumulator>();
        for (DateTime date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            days.Add(new DayAccumulator(
                date,
                LocalBoundaryToUtc(date, timeZone),
                LocalBoundaryToUtc(date.AddDays(1), timeZone)));
        }

        DateTime asOfUtc = NormalizeUtc(history.AsOfUtc, nameof(history));
        bool filterByProject = !string.IsNullOrWhiteSpace(projectKey);

        foreach (ProjectWorkIntervalView source in history.Intervals)
        {
            if (filterByProject
                && !string.Equals(
                    source.ProjectKey,
                    projectKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTime sourceStartUtc = NormalizeUtc(source.StartUtc, nameof(history));
            DateTime sourceEndUtc = source.EndUtc is { } closedEnd
                ? NormalizeUtc(closedEnd, nameof(history))
                : asOfUtc;
            if (sourceEndUtc > asOfUtc)
            {
                sourceEndUtc = asOfUtc;
            }
            if (sourceEndUtc <= sourceStartUtc)
            {
                continue;
            }

            int firstOverlappingDay = FindFirstOverlappingDay(days, sourceStartUtc);
            for (int dayIndex = firstOverlappingDay;
                 dayIndex < days.Count && days[dayIndex].StartUtc < sourceEndUtc;
                 dayIndex++)
            {
                DayAccumulator day = days[dayIndex];
                DateTime clippedStart = sourceStartUtc > day.StartUtc
                    ? sourceStartUtc
                    : day.StartUtc;
                DateTime clippedEnd = sourceEndUtc < day.EndUtc
                    ? sourceEndUtc
                    : day.EndUtc;
                if (clippedEnd <= clippedStart)
                {
                    continue;
                }

                day.DurationTicks = checked(
                    day.DurationTicks + (clippedEnd - clippedStart).Ticks);
                day.RecordIds.Add(source.Id);
            }
        }

        var result = new List<HeatmapDayValue>(days.Count);
        foreach (DayAccumulator day in days)
        {
            result.Add(new HeatmapDayValue(
                day.Date,
                TimeSpan.FromTicks(day.DurationTicks),
                day.RecordIds.Count));
        }

        return result;
    }

    private static int FindFirstOverlappingDay(
        IReadOnlyList<DayAccumulator> days,
        DateTime intervalStartUtc)
    {
        int low = 0;
        int high = days.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (days[middle].EndUtc <= intervalStartUtc)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// Maps a wall-clock boundary in timeZone to UTC. A boundary inside a DST
    /// gap advances to the first valid minute; a repeated boundary uses the
    /// earlier UTC occurrence so no repeated time at the start is discarded.
    /// </summary>
    internal static DateTime LocalBoundaryToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        DateTime boundary = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        const int maximumGapMinutes = 2 * 24 * 60;
        int advancedMinutes = 0;
        while (timeZone.IsInvalidTime(boundary))
        {
            if (advancedMinutes >= maximumGapMinutes || boundary >= DateTime.MaxValue.AddMinutes(-1))
            {
                throw new InvalidTimeZoneException(
                    "The local boundary does not have a valid UTC representation.");
            }

            boundary = boundary.AddMinutes(1);
            advancedMinutes++;
        }

        if (timeZone.IsAmbiguousTime(boundary))
        {
            TimeSpan[] offsets = timeZone.GetAmbiguousTimeOffsets(boundary);
            TimeSpan earlierOccurrenceOffset = offsets[0] > offsets[1]
                ? offsets[0]
                : offsets[1];
            return new DateTimeOffset(boundary, earlierOccurrenceOffset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(boundary, timeZone);
    }

    private static DateTime NormalizeUtc(DateTime value, string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A UTC timestamp is required.");
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private sealed class DayAccumulator(
        DateTime date,
        DateTime startUtc,
        DateTime endUtc)
    {
        internal DateTime Date { get; } = date;
        internal DateTime StartUtc { get; } = startUtc;
        internal DateTime EndUtc { get; } = endUtc;
        internal long DurationTicks { get; set; }
        internal HashSet<Guid> RecordIds { get; } = [];
    }
}
