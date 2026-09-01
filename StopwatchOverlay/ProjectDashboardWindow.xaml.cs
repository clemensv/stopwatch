using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace StopwatchOverlay;

/// <summary>
/// Presents project work intervals without taking a dependency on a charting package.
/// Pass a provider when open intervals should continue updating while this window is open.
/// </summary>
public partial class ProjectDashboardWindow : Window
{
    private static readonly Color[] MidnightProjectPalette =
    [
        Color.FromRgb(66, 185, 232),
        Color.FromRgb(126, 204, 139),
        Color.FromRgb(244, 178, 77),
        Color.FromRgb(180, 139, 236),
        Color.FromRgb(235, 111, 146),
        Color.FromRgb(82, 201, 184),
        Color.FromRgb(239, 130, 84),
        Color.FromRgb(116, 151, 232)
    ];

    private static readonly Color[] PixelDeckProjectPalette =
    [
        Color.FromRgb(255, 212, 108),
        Color.FromRgb(55, 174, 176),
        Color.FromRgb(182, 138, 225),
        Color.FromRgb(105, 185, 242),
        Color.FromRgb(239, 100, 113),
        Color.FromRgb(112, 214, 162),
        Color.FromRgb(244, 198, 77),
        Color.FromRgb(166, 107, 56)
    ];

    // The light canvases need darker series colors so white labels remain
    // readable while the charts keep the same lively, project-specific feel.
    private static readonly Color[] DaylightProjectPalette =
    [
        Color.FromRgb(17, 122, 156),
        Color.FromRgb(35, 122, 71),
        Color.FromRgb(167, 97, 0),
        Color.FromRgb(116, 71, 163),
        Color.FromRgb(179, 38, 30),
        Color.FromRgb(15, 117, 108),
        Color.FromRgb(163, 77, 32),
        Color.FromRgb(72, 105, 178)
    ];

    private static readonly Color[] PixelDeckDayProjectPalette =
    [
        Color.FromRgb(8, 122, 141),
        Color.FromRgb(23, 107, 67),
        Color.FromRgb(123, 74, 168),
        Color.FromRgb(23, 108, 176),
        Color.FromRgb(169, 29, 47),
        Color.FromRgb(10, 111, 120),
        Color.FromRgb(154, 92, 0),
        Color.FromRgb(112, 64, 34)
    ];

    private readonly Func<ProjectHistoryView> _historyProvider;
    private readonly Action<string?> _openRecords;
    private readonly DispatcherTimer _liveRefreshTimer;
    private DashboardRange _selectedRange = DashboardRange.Today;
    private string? _selectedProjectKey;
    private bool _updatingProjectFilter;

    public ProjectDashboardWindow(ProjectHistoryView history)
        : this(() => history, _ => { })
    {
    }

    public ProjectDashboardWindow(Func<ProjectHistoryView> historyProvider)
        : this(historyProvider, _ => { })
    {
    }

    public ProjectDashboardWindow(
        Func<ProjectHistoryView> historyProvider,
        Action<string?> openRecords)
    {
        ArgumentNullException.ThrowIfNull(historyProvider);
        ArgumentNullException.ThrowIfNull(openRecords);

        _historyProvider = historyProvider;
        _openRecords = openRecords;
        InitializeComponent();

        _liveRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _liveRefreshTimer.Tick += (_, _) => RefreshFromHistory();

        SelectRange(DashboardRange.Today);
        _liveRefreshTimer.Start();
    }

    private void RangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string rangeName }
            && Enum.TryParse(rangeName, out DashboardRange range))
        {
            SelectRange(range);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshFromHistory();

    private void OpenRecordsButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _openRecords(_selectedProjectKey);
    }

    private void ProjectRecordsCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _openRecords(_selectedProjectKey);
    }

    private void ProjectFilterSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProjectFilter || ProjectFilterSelector.SelectedItem is not ProjectFilterOption option)
        {
            return;
        }

        _selectedProjectKey = option.Key;
        RefreshFromHistory();
    }

    private void Window_Closed(object? sender, EventArgs e) => _liveRefreshTimer.Stop();

    private void SelectRange(DashboardRange range)
    {
        _selectedRange = range;
        RefreshFromHistory();
    }

    /// <summary>
    /// Reloads the immutable history view immediately while preserving the current
    /// date range and project selection whenever that project still exists.
    /// </summary>
    internal void RefreshFromHistory()
    {
        UpdateRangeButtons();
        RefreshDashboard();
    }

    private void UpdateRangeButtons()
    {
        var normalBackground = (Brush)FindResource("SurfaceRaisedBrush");
        var normalBorder = (Brush)FindResource("BorderBrush");
        var selectedBackground = (Brush)FindResource("PrimaryActionBrush");
        var selectedText = (Brush)FindResource("OnActionTextBrush");
        var normalText = (Brush)FindResource("PrimaryTextBrush");

        SetRangeButtonState(TodayButton, _selectedRange == DashboardRange.Today);
        SetRangeButtonState(SevenDaysButton, _selectedRange == DashboardRange.SevenDays);
        SetRangeButtonState(ThirtyDaysButton, _selectedRange == DashboardRange.ThirtyDays);
        SetRangeButtonState(AllTimeButton, _selectedRange == DashboardRange.AllTime);

        void SetRangeButtonState(Button button, bool selected)
        {
            button.Background = selected ? selectedBackground : normalBackground;
            button.BorderBrush = selected ? selectedBackground : normalBorder;
            button.Foreground = selected ? selectedText : normalText;
        }
    }

    private void RefreshDashboard()
    {
        ProjectHistoryView history;
        try
        {
            history = _historyProvider();
        }
        catch
        {
            // A dashboard refresh should never take down the controller. The next
            // automatic refresh will try the provider again.
            return;
        }

        DateTime asOfUtc = EnsureUtc(history.AsOfUtc);
        DateTime asOfLocal = asOfUtc.ToLocalTime();
        DateTime? rangeStartUtc = GetRangeStartUtc(asOfLocal);

        UpdateProjectFilter(history.Projects);

        List<DisplayInterval> intervals = history.Intervals
            .Select(item => CreateDisplayInterval(item, rangeStartUtc, asOfUtc))
            .Where(item => item is not null)
            .Cast<DisplayInterval>()
            .Where(item => _selectedProjectKey is null
                           || string.Equals(item.ProjectKey, _selectedProjectKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.StartUtc)
            .ToList();

        TimeSpan total = TimeSpan.FromTicks(intervals.Sum(item => item.Duration.Ticks));
        int activeCount = intervals
            .Where(item => item.Source.IsOpen)
            .Select(item => item.Source.TimerSessionId)
            .Distinct()
            .Count();
        int projectCount = intervals
            .Select(item => item.ProjectKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        TotalTrackedText.Text = FormatCompactDuration(total);
        SessionCountText.Text = intervals.Count.ToString(CultureInfo.CurrentCulture);
        ProjectCountText.Text = projectCount.ToString(CultureInfo.CurrentCulture);
        ActiveCountText.Text = activeCount.ToString(CultureInfo.CurrentCulture);
        ActiveStatusDot.Opacity = activeCount > 0 ? 1 : 0.25;
        RangeHeadingText.Text = GetRangeHeading(asOfLocal);
        UpdatedText.Text = $"Updated {asOfLocal:t}";

        RenderProjectBars(intervals);
        List<DayTotal> dayTotals = BuildDayTotals(intervals);
        RenderDailyBars(dayTotals);
        RenderTimeline(intervals);
        RenderSessions(intervals);

        ProjectFilterOption? selectedProject =
            ProjectFilterSelector.SelectedItem as ProjectFilterOption;
        UpdateRecordsPreview(intervals, selectedProject);

        bool hasData = intervals.Count > 0;
        ChartsGrid.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        TimelineCard.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        SessionsCard.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;

        EmptyStateHeadingText.Text = selectedProject?.Key is null
            ? "No tracked time in this period"
            : $"No time tracked for {selectedProject.Name}";
        EmptyStateDetailText.Text = selectedProject?.Key is null
            ? "Name a timer and start it. Its work sessions will appear here."
            : "This project has no sessions in the selected date range.";
    }

    private void UpdateRecordsPreview(
        IReadOnlyList<DisplayInterval> intervals,
        ProjectFilterOption? selectedProject)
    {
        int recordCount = intervals.Count;
        ProjectRecordsCountText.Text = recordCount == 1
            ? "1 record"
            : $"{recordCount.ToString(CultureInfo.CurrentCulture)} records";
        ProjectRecordsScopeText.Text = selectedProject?.Key is null
            ? "All projects in the selected period"
            : $"{selectedProject.Name} in the selected period";

        DisplayInterval? latest = intervals
            .OrderByDescending(item => item.Source.StartUtc)
            .FirstOrDefault();
        if (latest == null)
        {
            ProjectRecordsLatestText.Text =
                "No records here yet — open the records page to add one.";
            return;
        }

        DateTime latestLocal = latest.Source.StartUtc.ToLocalTime();
        string projectPrefix = selectedProject?.Key is null
            ? $"{latest.ProjectName} · "
            : "";
        ProjectRecordsLatestText.Text =
            $"Latest: {projectPrefix}{latestLocal.ToString("g", CultureInfo.CurrentCulture)}";
    }

    private void UpdateProjectFilter(IReadOnlyList<ProjectInfoView> projects)
    {
        var options = new List<ProjectFilterOption>(projects.Count + 1)
        {
            new(null, "All projects")
        };
        options.AddRange(projects
            .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(project => new ProjectFilterOption(project.Key, project.Name)));

        if (_selectedProjectKey is not null
            && !options.Any(option => string.Equals(
                option.Key,
                _selectedProjectKey,
                StringComparison.OrdinalIgnoreCase)))
        {
            _selectedProjectKey = null;
        }

        ProjectFilterOption selected = options.First(option =>
            string.Equals(option.Key, _selectedProjectKey, StringComparison.OrdinalIgnoreCase));

        _updatingProjectFilter = true;
        try
        {
            ProjectFilterSelector.ItemsSource = options;
            ProjectFilterSelector.SelectedItem = selected;
        }
        finally
        {
            _updatingProjectFilter = false;
        }
    }

    private DateTime? GetRangeStartUtc(DateTime asOfLocal)
    {
        DateTime? localStart = _selectedRange switch
        {
            DashboardRange.Today => asOfLocal.Date,
            DashboardRange.SevenDays => asOfLocal.Date.AddDays(-6),
            DashboardRange.ThirtyDays => asOfLocal.Date.AddDays(-29),
            _ => null
        };

        return localStart is null ? null : LocalBoundaryToUtc(localStart.Value);
    }

    private string GetRangeHeading(DateTime asOfLocal) => _selectedRange switch
    {
        DashboardRange.Today => asOfLocal.ToString("dddd, MMMM d", CultureInfo.CurrentCulture),
        DashboardRange.SevenDays => "Last 7 days",
        DashboardRange.ThirtyDays => "Last 30 days",
        _ => "All tracked time"
    };

    private static DisplayInterval? CreateDisplayInterval(
        ProjectWorkIntervalView source,
        DateTime? rangeStartUtc,
        DateTime asOfUtc)
    {
        DateTime sourceStart = EnsureUtc(source.StartUtc);
        DateTime sourceEnd = EnsureUtc(source.EndUtc ?? asOfUtc);
        DateTime start = rangeStartUtc is { } rangeStart && sourceStart < rangeStart
            ? rangeStart
            : sourceStart;
        DateTime end = sourceEnd < asOfUtc ? sourceEnd : asOfUtc;

        if (end <= start)
        {
            return null;
        }

        string projectKey = string.IsNullOrWhiteSpace(source.ProjectKey)
            ? source.ProjectName.Trim()
            : source.ProjectKey;
        string projectName = string.IsNullOrWhiteSpace(source.ProjectName)
            ? "Unnamed project"
            : source.ProjectName.Trim();

        return new DisplayInterval(source, projectKey, projectName, start, end);
    }

    private void RenderProjectBars(IReadOnlyList<DisplayInterval> intervals)
    {
        ProjectBarsPanel.Children.Clear();
        var totals = intervals
            .GroupBy(item => item.ProjectKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProjectTotal(
                group.Key,
                group.First().ProjectName,
                TimeSpan.FromTicks(group.Sum(item => item.Duration.Ticks))))
            .OrderByDescending(item => item.Duration)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (totals.Count == 0)
        {
            ProjectBarsPanel.Children.Add(CreateMutedMessage("No project time yet."));
            return;
        }

        double maximumTicks = Math.Max(1, totals[0].Duration.Ticks);
        foreach (ProjectTotal project in totals)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 13) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = GetProjectBrush(project.Key),
                Margin = new Thickness(0, 0, 8, 0)
            });
            label.Children.Add(new TextBlock
            {
                Text = project.Name,
                Foreground = (Brush)FindResource("PrimaryTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 118,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(label);

            var track = new Border
            {
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = (Brush)FindResource("SurfaceRaisedBrush"),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(track, 1);
            var proportion = new Grid();
            double value = project.Duration.Ticks / maximumTicks;
            proportion.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(0.004, value), GridUnitType.Star)
            });
            proportion.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(0, 1 - value), GridUnitType.Star)
            });
            proportion.Children.Add(new Border
            {
                Background = GetProjectBrush(project.Key),
                CornerRadius = new CornerRadius(5)
            });
            track.Child = proportion;
            row.Children.Add(track);

            var duration = new TextBlock
            {
                Text = FormatCompactDuration(project.Duration),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 12,
                MinWidth = 62,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(duration, 2);
            row.Children.Add(duration);
            ProjectBarsPanel.Children.Add(row);
        }
    }

    private static List<DayTotal> BuildDayTotals(IReadOnlyList<DisplayInterval> intervals)
    {
        var totals = new Dictionary<DateTime, long>();
        foreach (DisplayInterval interval in intervals)
        {
            foreach (DayFragment fragment in SplitByLocalDay(interval))
            {
                totals.TryGetValue(fragment.Date, out long ticks);
                totals[fragment.Date] = ticks + fragment.Duration.Ticks;
            }
        }

        return totals
            .Select(item => new DayTotal(item.Key, TimeSpan.FromTicks(item.Value)))
            .OrderByDescending(item => item.Date)
            .ToList();
    }

    private void RenderDailyBars(IReadOnlyList<DayTotal> totals)
    {
        DailyBarsPanel.Children.Clear();
        if (totals.Count == 0)
        {
            DailyBarsPanel.Children.Add(CreateMutedMessage("No daily totals yet."));
            return;
        }

        double maximumTicks = Math.Max(1, totals.Max(item => item.Duration.Ticks));
        foreach (DayTotal day in totals)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = day.Date.ToString("MMM d", CultureInfo.CurrentCulture),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            var track = new Border
            {
                Height = 8,
                Background = (Brush)FindResource("SurfaceRaisedBrush"),
                CornerRadius = (CornerRadius)FindResource("ThemeItemCornerRadius"),
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(track, 1);
            var proportion = new Grid();
            double value = day.Duration.Ticks / maximumTicks;
            proportion.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(0.004, value), GridUnitType.Star)
            });
            proportion.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(0, 1 - value), GridUnitType.Star)
            });
            proportion.Children.Add(new Border
            {
                Background = (Brush)FindResource("AccentBrush"),
                CornerRadius = new CornerRadius(4)
            });
            track.Child = proportion;
            row.Children.Add(track);

            var valueText = new TextBlock
            {
                Text = FormatCompactDuration(day.Duration),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 12,
                MinWidth = 52,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);
            DailyBarsPanel.Children.Add(row);
        }
    }

    private void RenderTimeline(IReadOnlyList<DisplayInterval> intervals)
    {
        TimelineDaysPanel.Children.Clear();
        List<DayFragment> fragments = intervals
            .SelectMany(SplitByLocalDay)
            .OrderByDescending(item => item.Date)
            .ThenBy(item => item.StartLocal)
            .ToList();

        if (fragments.Count == 0)
        {
            TimelineDaysPanel.Children.Add(CreateMutedMessage("No sessions to place on the timeline."));
            return;
        }

        foreach (IGrouping<DateTime, DayFragment> dayGroup in fragments.GroupBy(item => item.Date))
        {
            List<TimelineItem> items = AssignTimelineLanes(dayGroup.ToList());
            int laneCount = Math.Max(1, items.Max(item => item.Lane) + 1);
            DayFragment dayBounds = items[0].Fragment;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = dayGroup.Key.ToString("ddd, MMM d", CultureInfo.CurrentCulture),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 4, 12, 0)
            });

            var track = new Grid
            {
                Height = Math.Max(42, laneCount * 25 + 18),
                Background = (Brush)FindResource("SurfaceRaisedBrush"),
                ClipToBounds = true
            };
            Grid.SetColumn(track, 1);
            var guides = new Canvas { IsHitTestVisible = false };
            var segments = new Canvas();
            track.Children.Add(guides);
            track.Children.Add(segments);
            row.Children.Add(track);

            void DrawTrack()
            {
                DrawTimelineGuides(guides, track.ActualWidth, track.ActualHeight, dayBounds);
                DrawTimelineSegments(segments, items, track.ActualWidth);
            }

            track.Loaded += (_, _) => DrawTrack();
            track.SizeChanged += (_, _) => DrawTrack();
            TimelineDaysPanel.Children.Add(row);
        }
    }

    private void DrawTimelineGuides(
        Canvas canvas,
        double width,
        double height,
        DayFragment day)
    {
        canvas.Children.Clear();
        TimeSpan dayDuration = day.DayEndUtc - day.DayStartUtc;
        if (width <= 0 || dayDuration <= TimeSpan.Zero)
        {
            return;
        }

        for (int hour = 0; hour <= 24; hour += 6)
        {
            DateTime boundaryUtc = LocalBoundaryToUtc(day.Date.AddHours(hour));
            double fraction = Math.Clamp(
                (boundaryUtc - day.DayStartUtc).TotalSeconds / dayDuration.TotalSeconds,
                0,
                1);
            double x = width * fraction;

            var label = new TextBlock
            {
                Text = hour == 24 ? "24:00" : $"{hour:00}:00",
                FontSize = 9,
                Foreground = (Brush)FindResource("SecondaryTextBrush")
            };
            Canvas.SetLeft(label, Math.Clamp(x - (hour == 0 ? 0 : hour == 24 ? 30 : 14), 0, Math.Max(0, width - 30)));
            Canvas.SetTop(label, 1);
            canvas.Children.Add(label);

            if (hour is > 0 and < 24)
            {
                var guide = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 15,
                    Y2 = height,
                    Stroke = (Brush)FindResource("BorderBrush"),
                    StrokeThickness = 1
                };
                canvas.Children.Add(guide);
            }
        }
    }

    private void DrawTimelineSegments(Canvas canvas, IReadOnlyList<TimelineItem> items, double width)
    {
        canvas.Children.Clear();
        if (width <= 0)
        {
            return;
        }

        foreach (TimelineItem item in items)
        {
            TimeSpan dayDuration = item.Fragment.DayEndUtc - item.Fragment.DayStartUtc;
            if (dayDuration <= TimeSpan.Zero)
                continue;

            double startFraction = Math.Clamp(
                (item.Fragment.StartUtc - item.Fragment.DayStartUtc).TotalSeconds
                    / dayDuration.TotalSeconds,
                0,
                1);
            double endFraction = Math.Clamp(
                (item.Fragment.EndUtc - item.Fragment.DayStartUtc).TotalSeconds
                    / dayDuration.TotalSeconds,
                0,
                1);
            double left = width * startFraction;
            double segmentWidth = Math.Max(3, width * (endFraction - startFraction));

            SolidColorBrush segmentBrush = GetProjectBrush(item.Fragment.ProjectKey);
            var segment = new Border
            {
                Width = segmentWidth,
                Height = 20,
                Background = segmentBrush,
                CornerRadius = new CornerRadius(4),
                Opacity = 1,
                ToolTip = CreateTimelineToolTip(item.Fragment)
            };
            if (segmentWidth >= 72)
            {
                segment.Child = new TextBlock
                {
                    Text = item.Fragment.ProjectName,
                    Foreground = GetContrastingTextBrush(segmentBrush.Color),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            Canvas.SetLeft(segment, left);
            Canvas.SetTop(segment, item.Lane * 25 + 17);
            canvas.Children.Add(segment);
        }
    }

    private static string CreateTimelineToolTip(DayFragment fragment)
    {
        string end = fragment.IsOpen && fragment.EndsAtIntervalEnd
            ? "Now"
            : FormatLocalTime(fragment.EndLocal, fragment.EndUtc);
        return $"{fragment.ProjectName}\n{FormatLocalTime(fragment.StartLocal, fragment.StartUtc)} – {end}\n{FormatDetailedDuration(fragment.Duration)}";
    }

    private static List<TimelineItem> AssignTimelineLanes(IReadOnlyList<DayFragment> fragments)
    {
        var laneEnds = new List<DateTime>();
        var result = new List<TimelineItem>(fragments.Count);
        foreach (DayFragment fragment in fragments.OrderBy(item => item.StartUtc))
        {
            int lane = laneEnds.FindIndex(end => end <= fragment.StartUtc);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(fragment.EndUtc);
            }
            else
            {
                laneEnds[lane] = fragment.EndUtc;
            }

            result.Add(new TimelineItem(fragment, lane));
        }

        return result;
    }

    private void RenderSessions(IReadOnlyList<DisplayInterval> intervals)
    {
        SessionsPanel.Children.Clear();
        if (intervals.Count == 0)
        {
            SessionsPanel.Children.Add(CreateMutedMessage("No sessions in this period."));
            return;
        }

        foreach (IGrouping<DateTime, DisplayInterval> group in intervals
                     .GroupBy(item => item.StartLocal.Date)
                     .OrderByDescending(group => group.Key))
        {
            SessionsPanel.Children.Add(new TextBlock
            {
                Text = group.Key.ToString("dddd, MMMM d", CultureInfo.CurrentCulture),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, SessionsPanel.Children.Count == 0 ? 0 : 16, 0, 7)
            });

            foreach (DisplayInterval interval in group.OrderByDescending(item => item.StartUtc))
            {
                var row = new Border
                {
                    Background = (Brush)FindResource("SurfaceRaisedBrush"),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var project = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                project.Children.Add(new Border
                {
                    Width = 4,
                    Height = 24,
                    Background = GetProjectBrush(interval.ProjectKey),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 10, 0)
                });
                project.Children.Add(new TextBlock
                {
                    Text = interval.ProjectName,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                grid.Children.Add(project);

                string endLabel = interval.Source.IsOpen
                    ? "Now"
                    : FormatLocalTime(interval.EndLocal, interval.EndUtc);
                string timesLabel =
                    $"{FormatLocalTime(interval.StartLocal, interval.StartUtc)} – {endLabel}";
                var times = new TextBlock
                {
                    Text = timesLabel,
                    ToolTip = timesLabel,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)FindResource("SecondaryTextBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(times, 1);
                grid.Children.Add(times);

                var duration = new TextBlock
                {
                    Text = FormatDetailedDuration(interval.Duration),
                    Foreground = (Brush)FindResource("PrimaryTextBrush"),
                    FontFamily = (FontFamily)FindResource("ThemeMonoFontFamily"),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(duration, 2);
                grid.Children.Add(duration);

                if (interval.Source.IsOpen)
                {
                    var accentColor = ((SolidColorBrush)FindResource("AccentBrush")).Color;
                    var active = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(
                            45, accentColor.R, accentColor.G, accentColor.B)),
                        CornerRadius = (CornerRadius)FindResource("ThemeCardCornerRadius"),
                        Margin = new Thickness(12, 0, 0, 0),
                        Padding = new Thickness(7, 3, 7, 3),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "ACTIVE",
                            Foreground = (Brush)FindResource("AccentBrush"),
                            FontSize = 9,
                            FontWeight = FontWeights.Bold
                        }
                    };
                    Grid.SetColumn(active, 3);
                    grid.Children.Add(active);
                }

                row.Child = grid;
                SessionsPanel.Children.Add(row);
            }
        }
    }

    private TextBlock CreateMutedMessage(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("SecondaryTextBrush"),
        Margin = new Thickness(0, 5, 0, 5)
    };

    private SolidColorBrush GetProjectBrush(string key)
    {
        uint hash = 2166136261;
        foreach (char character in key.ToUpperInvariant())
        {
            hash ^= character;
            hash *= 16777619;
        }

        Color[] palette = AppThemeManager.CurrentTheme switch
        {
            AppThemeCatalog.PixelDeckDay => PixelDeckDayProjectPalette,
            AppThemeCatalog.Daylight => DaylightProjectPalette,
            AppThemeCatalog.PixelDeckNight => PixelDeckProjectPalette,
            _ => MidnightProjectPalette
        };
        var brush = new SolidColorBrush(palette[hash % (uint)palette.Length]);
        brush.Freeze();
        return brush;
    }

    private static Brush GetContrastingTextBrush(Color background)
    {
        static double Linearize(byte component)
        {
            double channel = component / 255d;
            return channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        double luminance =
            0.2126 * Linearize(background.R)
            + 0.7152 * Linearize(background.G)
            + 0.0722 * Linearize(background.B);
        return luminance > 0.179 ? Brushes.Black : Brushes.White;
    }

    private static IEnumerable<DayFragment> SplitByLocalDay(DisplayInterval interval)
    {
        DateTime firstDate = interval.StartLocal.Date;
        DateTime lastDate = interval.EndLocal.Date;
        for (DateTime date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            DateTime dayStartUtc = LocalBoundaryToUtc(date);
            DateTime nextDayUtc = LocalBoundaryToUtc(date.AddDays(1));
            DateTime startUtc = interval.StartUtc > dayStartUtc ? interval.StartUtc : dayStartUtc;
            DateTime endUtc = interval.EndUtc < nextDayUtc ? interval.EndUtc : nextDayUtc;
            if (endUtc <= startUtc)
            {
                continue;
            }

            DateTime startLocal = startUtc.ToLocalTime();
            DateTime endLocal = endUtc == nextDayUtc ? date.AddDays(1) : endUtc.ToLocalTime();
            yield return new DayFragment(
                interval.ProjectKey,
                interval.ProjectName,
                date,
                dayStartUtc,
                nextDayUtc,
                startUtc,
                endUtc,
                startLocal,
                endLocal,
                endUtc - startUtc,
                interval.Source.IsOpen,
                endUtc == interval.EndUtc);
        }
    }

    private static DateTime LocalBoundaryToUtc(DateTime local)
    {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeZoneInfo zone = TimeZoneInfo.Local;
        while (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(30);
        }

        if (zone.IsAmbiguousTime(unspecified))
        {
            // A few time zones move their clocks at midnight. Choose the earlier
            // UTC occurrence so the dashboard does not discard the repeated part
            // at the beginning of that local date.
            TimeSpan earlierOffset = zone.GetAmbiguousTimeOffsets(unspecified).Max();
            return new DateTimeOffset(unspecified, earlierOffset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string FormatLocalTime(DateTime local, DateTime utc)
    {
        string time = local.ToString("t", CultureInfo.CurrentCulture);
        TimeZoneInfo zone = TimeZoneInfo.Local;
        DateTime wallTime = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (!zone.IsAmbiguousTime(wallTime))
            return time;

        TimeSpan offset = zone.GetUtcOffset(EnsureUtc(utc));
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();
        return $"{time} (UTC{sign}{offset.Hours:00}:{offset.Minutes:00})";
    }

    private static string FormatCompactDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            int hours = (int)duration.TotalHours;
            return duration.Minutes == 0 ? $"{hours}h" : $"{hours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return duration.TotalSeconds >= 1 ? $"{(int)duration.TotalSeconds}s" : "0m";
    }

    private static string FormatDetailedDuration(TimeSpan duration)
    {
        int hours = (int)duration.TotalHours;
        return $"{hours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private enum DashboardRange
    {
        Today,
        SevenDays,
        ThirtyDays,
        AllTime
    }

    private sealed record DisplayInterval(
        ProjectWorkIntervalView Source,
        string ProjectKey,
        string ProjectName,
        DateTime StartUtc,
        DateTime EndUtc)
    {
        public TimeSpan Duration => EndUtc - StartUtc;
        public DateTime StartLocal => StartUtc.ToLocalTime();
        public DateTime EndLocal => EndUtc.ToLocalTime();
    }

    private sealed record ProjectTotal(string Key, string Name, TimeSpan Duration);
    private sealed record DayTotal(DateTime Date, TimeSpan Duration);
    private sealed record DayFragment(
        string ProjectKey,
        string ProjectName,
        DateTime Date,
        DateTime DayStartUtc,
        DateTime DayEndUtc,
        DateTime StartUtc,
        DateTime EndUtc,
        DateTime StartLocal,
        DateTime EndLocal,
        TimeSpan Duration,
        bool IsOpen,
        bool EndsAtIntervalEnd);
    private sealed record TimelineItem(DayFragment Fragment, int Lane);

    private sealed record ProjectFilterOption(string? Key, string Name)
    {
        public override string ToString() => Name;
    }
}
