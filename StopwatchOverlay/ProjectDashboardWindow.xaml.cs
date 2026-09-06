using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
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
    private const int RecordsPerPage = 20;
    private const int HeatmapWeekCount = 53;
    private const double HeatmapCellSize = 13;
    private const double HeatmapCellStep = 17;

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

    private static readonly Color[] AcanthusProjectPalette =
    [
        Color.FromRgb(79, 117, 90),
        Color.FromRgb(176, 138, 77),
        Color.FromRgb(113, 128, 106),
        Color.FromRgb(138, 62, 69),
        Color.FromRgb(68, 81, 64),
        Color.FromRgb(155, 109, 53),
        Color.FromRgb(93, 126, 105),
        Color.FromRgb(133, 100, 76)
    ];

    private readonly Func<ProjectHistoryView> _historyProvider;
    private readonly Func<string, DateTime, DateTime, ProjectRecordMutationResult> _addRecord;
    private readonly Func<Guid, string, DateTime, DateTime, ProjectRecordMutationResult> _updateRecord;
    private readonly Func<Guid, ProjectRecordMutationResult> _deleteRecord;
    private readonly Func<bool> _canMutateRecords;
    private readonly Func<string?> _recordsPersistenceWarning;
    private readonly DispatcherTimer _liveRefreshTimer;
    private readonly Dictionary<DateTime, Button> _heatmapCells = [];
    private readonly Dictionary<RecordActionFocus, Button> _recordActionButtons = [];
    private ProjectHistoryView? _history;
    private IReadOnlyList<DisplayInterval> _records = [];
    private DashboardRange _selectedRange = DashboardRange.Day;
    private DateTime? _selectedDayLocal;
    private DateTime _latestLocalDate = DateTime.Today;
    private bool _followToday = true;
    private string? _selectedProjectKey;
    private bool _updatingProjectFilter;
    private int _recordsPageIndex;
    private DateTime? _heatmapFirstDate;
    private DateTime? _heatmapRenderedToday;
    private Button? _heatmapTabStop;
    private bool _recordDialogOpen;
    private bool _refreshFailed;

    public ProjectDashboardWindow(ProjectHistoryView history)
        : this(
            () => history,
            (_, _, _) => new ProjectRecordMutationResult(ProjectRecordMutationStatus.NotFound),
            (_, _, _, _) => new ProjectRecordMutationResult(ProjectRecordMutationStatus.NotFound),
            _ => new ProjectRecordMutationResult(ProjectRecordMutationStatus.NotFound),
            () => false,
            () => "Record editing is unavailable in this read-only dashboard.")
    {
    }

    public ProjectDashboardWindow(Func<ProjectHistoryView> historyProvider)
        : this(
            historyProvider,
            (_, _, _) => new ProjectRecordMutationResult(ProjectRecordMutationStatus.NotFound),
            (_, _, _, _) => new ProjectRecordMutationResult(ProjectRecordMutationStatus.NotFound),
            _ => new ProjectRecordMutationResult(ProjectRecordMutationStatus.NotFound),
            () => false,
            () => "Record editing is unavailable in this read-only dashboard.")
    {
    }

    public ProjectDashboardWindow(
        Func<ProjectHistoryView> historyProvider,
        Func<string, DateTime, DateTime, ProjectRecordMutationResult> addRecord,
        Func<Guid, string, DateTime, DateTime, ProjectRecordMutationResult> updateRecord,
        Func<Guid, ProjectRecordMutationResult> deleteRecord,
        Func<bool> canMutateRecords,
        Func<string?> recordsPersistenceWarning)
    {
        ArgumentNullException.ThrowIfNull(historyProvider);
        ArgumentNullException.ThrowIfNull(addRecord);
        ArgumentNullException.ThrowIfNull(updateRecord);
        ArgumentNullException.ThrowIfNull(deleteRecord);
        ArgumentNullException.ThrowIfNull(canMutateRecords);
        ArgumentNullException.ThrowIfNull(recordsPersistenceWarning);

        _historyProvider = historyProvider;
        _addRecord = addRecord;
        _updateRecord = updateRecord;
        _deleteRecord = deleteRecord;
        _canMutateRecords = canMutateRecords;
        _recordsPersistenceWarning = recordsPersistenceWarning;
        InitializeComponent();

        _liveRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _liveRefreshTimer.Tick += (_, _) =>
        {
            if (_refreshFailed
                || _history?.Intervals.Any(interval => interval.IsOpen) == true
                || DateTime.Now.Date != _latestLocalDate)
            {
                RefreshFromHistory();
            }
        };

        SelectRange(DashboardRange.Day);
        _liveRefreshTimer.Start();
    }

    private void RangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string rangeName }
            && Enum.TryParse(rangeName, out DashboardRange range))
        {
            _followToday = true;
            _selectedDayLocal = null;
            _recordsPageIndex = 0;
            SelectRange(range);
        }
    }

    private void PreviousDayButton_Click(object sender, RoutedEventArgs e)
    {
        DateTime anchor = _selectedRange == DashboardRange.Day
            ? _selectedDayLocal ?? _latestLocalDate
            : _latestLocalDate;
        _selectedDayLocal = anchor.AddDays(-1);
        _followToday = false;
        _recordsPageIndex = 0;
        SelectRange(DashboardRange.Day);
    }

    private void NextDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRange != DashboardRange.Day)
            return;

        DateTime current = _selectedDayLocal ?? _latestLocalDate;
        if (current >= _latestLocalDate)
            return;

        DateTime next = current.AddDays(1);
        _selectedDayLocal = next > _latestLocalDate ? _latestLocalDate : next;
        _followToday = _selectedDayLocal.Value >= _latestLocalDate;
        _recordsPageIndex = 0;
        RefreshFromHistory();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshFromHistory();

    private void ProjectFilterSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProjectFilter || ProjectFilterSelector.SelectedItem is not ProjectFilterOption option)
        {
            return;
        }

        _selectedProjectKey = option.Key;
        _recordsPageIndex = 0;
        RefreshFromHistory();
    }

    private void ProjectRecordsExpander_Expanded(object sender, RoutedEventArgs e) =>
        RenderRecords();

    private void RecordsButton_Click(object sender, RoutedEventArgs e)
    {
        ProjectRecordsExpander.IsExpanded = true;
        RenderRecords();
        ProjectRecordsExpander.BringIntoView();
    }

    private void PreviousRecordsPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recordsPageIndex <= 0)
            return;

        _recordsPageIndex--;
        RenderRecords();
    }

    private void NextRecordsPageButton_Click(object sender, RoutedEventArgs e)
    {
        int pageCount = Math.Max(1, (_records.Count + RecordsPerPage - 1) / RecordsPerPage);
        if (_recordsPageIndex + 1 >= pageCount)
            return;

        _recordsPageIndex++;
        RenderRecords();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // The calendar is arranged oldest-to-newest, so open on the dates users
        // are most likely to need while leaving subsequent manual scrolling alone.
        HeatmapScrollViewer.ScrollToRightEnd();
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
        RefreshDashboard();
    }

    private void UpdateRangeButtons(DateTime asOfLocal)
    {
        var selectedBackground = (Brush)FindResource("PrimaryActionBrush");
        var selectedText = (Brush)FindResource("OnActionTextBrush");

        SetRangeButtonState(DayButton, _selectedRange == DashboardRange.Day);
        SetRangeButtonState(SevenDaysButton, _selectedRange == DashboardRange.SevenDays);
        SetRangeButtonState(ThirtyDaysButton, _selectedRange == DashboardRange.ThirtyDays);
        SetRangeButtonState(AllTimeButton, _selectedRange == DashboardRange.AllTime);

        DateTime selectedDay = _selectedDayLocal ?? asOfLocal.Date;
        bool viewingToday = selectedDay >= asOfLocal.Date;
        DayButton.ToolTip = viewingToday
            ? "Showing today"
            : "Return to today";
        NextDayButton.IsEnabled = _selectedRange == DashboardRange.Day && !viewingToday;

        void SetRangeButtonState(Button button, bool selected)
        {
            if (selected)
            {
                button.Background = selectedBackground;
                button.BorderBrush = selectedBackground;
                button.Foreground = selectedText;
            }
            else
            {
                // Let ModernButton own normal/hover/pressed resources. Local
                // brush values would outrank its Pixel Deck Night hover states.
                button.ClearValue(Control.BackgroundProperty);
                button.ClearValue(Control.BorderBrushProperty);
                button.ClearValue(Control.ForegroundProperty);
            }
        }
    }

    private void RefreshDashboard()
    {
        ProjectHistoryView history;
        try
        {
            history = _historyProvider();
        }
        catch (Exception exception) when (exception is
            InvalidOperationException
            or ArgumentException)
        {
            // A dashboard refresh should never take down the controller. The next
            // automatic refresh will try the provider again.
            CrashLogger.LogRecoverable(exception, "ProjectDashboardHistoryProvider");
            UpdatedText.Text = "Refresh failed — showing earlier data";
            UpdatedText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            ShowRecordsWarning("Project records could not be loaded. Your existing data has not been changed.");
            _refreshFailed = true;
            return;
        }

        _refreshFailed = false;
        _history = history;
        DateTime asOfUtc = EnsureUtc(history.AsOfUtc);
        DateTime asOfLocal = asOfUtc.ToLocalTime();
        _latestLocalDate = asOfLocal.Date;
        if (_followToday || !_selectedDayLocal.HasValue)
        {
            _selectedDayLocal = _latestLocalDate;
        }
        else if (_selectedDayLocal.Value > _latestLocalDate)
        {
            _selectedDayLocal = _latestLocalDate;
            _followToday = true;
        }

        UpdateRangeButtons(asOfLocal);
        DashboardUtcRange range = ProjectDashboardAnalytics.CreateRange(
            _selectedRange,
            _selectedDayLocal.Value,
            asOfUtc,
            TimeZoneInfo.Local);

        UpdateProjectFilter(history.Projects);

        List<DisplayInterval> intervals = history.Intervals
            .Select(item => CreateDisplayInterval(item, range, asOfUtc))
            .Where(item => item is not null)
            .Cast<DisplayInterval>()
            .Where(item => _selectedProjectKey is null
                           || string.Equals(item.ProjectKey, _selectedProjectKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.StartUtc)
            .ToList();

        TimeSpan total = TimeSpan.FromTicks(intervals.Sum(item => item.Duration.Ticks));
        int activeCount = history.Intervals
            .Where(item => item.IsOpen)
            .Where(item => _selectedProjectKey is null
                           || string.Equals(
                               item.ProjectKey,
                               _selectedProjectKey,
                               StringComparison.OrdinalIgnoreCase))
            .Select(item => item.TimerSessionId)
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
        UpdatedText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");

        RenderProjectBars(intervals);
        List<DayTotal> dayTotals = BuildDayTotals(intervals);
        RenderDailyBars(dayTotals);
        ProjectFilterOption? selectedProject =
            ProjectFilterSelector.SelectedItem as ProjectFilterOption;
        RenderHeatmap(history, asOfLocal, selectedProject);
        RenderTimeline(intervals);

        UpdateRecordsSection(intervals, selectedProject);
        UpdateRecordsMutationAvailability();

        bool hasData = intervals.Count > 0;
        ChartsGrid.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        TimelineCard.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;

        EmptyStateHeadingText.Text = selectedProject?.Key is null
            ? "No tracked time in this period"
            : $"No time tracked for {selectedProject.Name}";
        EmptyStateDetailText.Text = selectedProject?.Key is null
            ? "Name a timer and start it. Its work sessions will appear here."
            : "This project has no sessions in the selected date range.";
    }

    private void UpdateRecordsSection(
        IReadOnlyList<DisplayInterval> intervals,
        ProjectFilterOption? selectedProject)
    {
        _records = intervals
            .OrderByDescending(interval => interval.StartUtc)
            .ThenByDescending(interval => interval.Source.Id)
            .ToList();

        int recordCount = _records.Count;
        string count = recordCount == 1
            ? "1 record"
            : $"{recordCount.ToString(CultureInfo.CurrentCulture)} records";
        string scope = selectedProject?.Key is null
            ? "All projects"
            : selectedProject.Name;
        DisplayInterval? latest = _records.FirstOrDefault();
        string latestText = latest == null
            ? "No records in the selected period"
            : $"Most recent activity {latest.StartLocal.ToString("g", CultureInfo.CurrentCulture)}";
        ProjectRecordsSummaryText.Text = $"{scope} · {count} · {latestText}";
        ProjectRecordsSummaryText.ToolTip = ProjectRecordsSummaryText.Text;
        RecordsHeadingText.Text = selectedProject?.Key is null
            ? $"All records · {GetRangeHeading(EnsureUtc(_history!.AsOfUtc).ToLocalTime())}"
            : $"{selectedProject.Name} records · {GetRangeHeading(EnsureUtc(_history!.AsOfUtc).ToLocalTime())}";

        int pageCount = Math.Max(1, (recordCount + RecordsPerPage - 1) / RecordsPerPage);
        _recordsPageIndex = Math.Clamp(_recordsPageIndex, 0, pageCount - 1);
        if (ProjectRecordsExpander.IsExpanded && !_recordDialogOpen)
            RenderRecords();
    }

    private void AddRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history == null || !CheckRecordsMutationAvailable())
            return;

        var editor = new ProjectRecordEditorWindow(
            _history.Projects,
            _selectedProjectKey,
            commit: SaveAddedRecord,
            initialLocalDate: _selectedRange == DashboardRange.Day
                ? _selectedDayLocal
                : null)
        {
            Owner = this
        };

        bool saved = ShowRecordDialog(editor) == true;
        if (saved)
        {
            if (_selectedProjectKey != null && editor.SavedRecord != null)
                _selectedProjectKey = editor.SavedRecord.ProjectKey;
            _recordsPageIndex = 0;
        }

        RefreshFromHistory();
    }

    private void EditRecord(ProjectWorkIntervalView record)
    {
        if (record.IsOpen || _history == null || !CheckRecordsMutationAvailable())
            return;

        var editor = new ProjectRecordEditorWindow(
            _history.Projects,
            record.ProjectKey,
            record,
            (project, startUtc, endUtc) =>
                SaveUpdatedRecord(record.Id, project, startUtc, endUtc))
        {
            Owner = this
        };

        bool saved = ShowRecordDialog(editor) == true;
        if (saved)
        {
            if (_selectedProjectKey != null && editor.SavedRecord != null)
                _selectedProjectKey = editor.SavedRecord.ProjectKey;
            _recordsPageIndex = 0;
        }

        RefreshFromHistory();
    }

    private ProjectRecordMutationResult SaveAddedRecord(
        string project,
        DateTime startUtc,
        DateTime endUtc)
    {
        EnsureRecordsMutationAvailable();
        return _addRecord(project, startUtc, endUtc);
    }

    private ProjectRecordMutationResult SaveUpdatedRecord(
        Guid id,
        string project,
        DateTime startUtc,
        DateTime endUtc)
    {
        EnsureRecordsMutationAvailable();
        return _updateRecord(id, project, startUtc, endUtc);
    }

    private void DeleteRecord(ProjectWorkIntervalView record)
    {
        if (record.IsOpen || !CheckRecordsMutationAvailable())
            return;

        var confirmation = new ProjectRecordDeleteWindow(record)
        {
            Owner = this
        };
        if (ShowRecordDialog(confirmation) != true)
        {
            RefreshFromHistory();
            return;
        }

        ProjectRecordMutationResult result;
        try
        {
            EnsureRecordsMutationAvailable();
            result = _deleteRecord(record.Id);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException
            or ArgumentException)
        {
            CrashLogger.LogRecoverable(exception, "ProjectDashboardDeleteRecord");
            UpdateRecordsMutationAvailability();
            ShowRecordsWarning(exception is InvalidOperationException
                ? "Project records are temporarily read-only. Refresh the dashboard and try again."
                : "The record could not be deleted because its identifier is invalid. Refresh the dashboard and try again.");
            return;
        }

        RefreshFromHistory();
        switch (result.Status)
        {
            case ProjectRecordMutationStatus.Success:
                return;
            case ProjectRecordMutationStatus.NotFound:
                ShowRecordsWarning("That record no longer exists. The dashboard has been refreshed.");
                return;
            case ProjectRecordMutationStatus.OpenInterval:
                ShowRecordsWarning("An active record cannot be deleted. Pause its timer first.");
                return;
            default:
                ShowRecordsWarning("The record could not be deleted.");
                return;
        }
    }

    private bool? ShowRecordDialog(Window dialog)
    {
        _recordDialogOpen = true;
        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            _recordDialogOpen = false;
        }
    }

    private void RenderRecords()
    {
        RecordActionFocus? focusedAction = Keyboard.FocusedElement is Button
            { Tag: RecordActionFocus action }
            ? action
            : null;
        _recordActionButtons.Clear();
        RecordsPanel.Children.Clear();
        if (_history == null)
        {
            RecordsPageStatusText.Text = "0 records";
            PreviousRecordsPageButton.IsEnabled = false;
            NextRecordsPageButton.IsEnabled = false;
            RecordsEmptyState.Visibility = Visibility.Visible;
            RecordsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        bool canEdit = SafeCanMutateRecords();
        DateTime asOfUtc = EnsureUtc(_history.AsOfUtc);
        int pageCount = Math.Max(1, (_records.Count + RecordsPerPage - 1) / RecordsPerPage);
        _recordsPageIndex = Math.Clamp(_recordsPageIndex, 0, pageCount - 1);
        List<DisplayInterval> visibleRecords = _records
            .Skip(_recordsPageIndex * RecordsPerPage)
            .Take(RecordsPerPage)
            .ToList();

        int first = _records.Count == 0 ? 0 : (_recordsPageIndex * RecordsPerPage) + 1;
        int last = Math.Min(_records.Count, (_recordsPageIndex + 1) * RecordsPerPage);
        RecordsPageStatusText.Text = _records.Count == 0
            ? "0 records"
            : $"{first}–{last} of {_records.Count.ToString(CultureInfo.CurrentCulture)}";
        PreviousRecordsPageButton.IsEnabled = _recordsPageIndex > 0;
        NextRecordsPageButton.IsEnabled = _recordsPageIndex + 1 < pageCount;

        foreach (IGrouping<DateTime, DisplayInterval> group in visibleRecords
                     .GroupBy(interval => interval.StartLocal.Date)
                     .OrderByDescending(group => group.Key))
        {
            RecordsPanel.Children.Add(new TextBlock
            {
                Text = group.Key.ToString("dddd, MMMM d", CultureInfo.CurrentCulture),
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, RecordsPanel.Children.Count == 0 ? 0 : 14, 0, 7)
            });

            foreach (DisplayInterval interval in group.OrderByDescending(item => item.StartUtc))
                RecordsPanel.Children.Add(CreateRecordCard(interval, asOfUtc, canEdit));
        }

        bool empty = _records.Count == 0;
        RecordsEmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        RecordsPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ProjectFilterOption? selected = ProjectFilterSelector.SelectedItem as ProjectFilterOption;
        RecordsEmptyHeadingText.Text = selected?.Key == null
            ? "No project records in this period"
            : $"No {selected.Name} records in this period";
        RecordsEmptyDetailText.Text =
            "Add a record manually, choose another date, or change the project filter.";

        if (focusedAction is { } restoreAction
            && _recordActionButtons.TryGetValue(restoreAction, out Button? focusedButton)
            && focusedButton.IsEnabled)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => focusedButton.Focus()));
        }
    }

    private Border CreateRecordCard(
        DisplayInterval interval,
        DateTime asOfUtc,
        bool canEdit)
    {
        ProjectWorkIntervalView record = interval.Source;
        DateTime startLocal = interval.StartLocal;
        DateTime endLocal = interval.EndLocal;
        DateTime sourceEndUtc = record.EndUtc.HasValue
            ? EnsureUtc(record.EndUtc.Value)
            : asOfUtc;
        bool isClipped = interval.StartUtc != EnsureUtc(record.StartUtc)
                         || interval.EndUtc != sourceEndUtc;

        var card = new Border
        {
            Style = (Style)FindResource("DashboardRecordRow")
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 105
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new Border
        {
            Width = 4,
            Background = (Brush)FindResource("AccentBrush"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 0, 1)
        });

        var project = new TextBlock
        {
            Text = record.ProjectName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 10, 0),
            ToolTip = record.ProjectName
        };
        Grid.SetColumn(project, 1);
        grid.Children.Add(project);

        var date = CreateRecordsSecondaryCell(
            startLocal.ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
        Grid.SetColumn(date, 2);
        grid.Children.Add(date);

        string startTimeLabel = FormatLocalTime(startLocal, interval.StartUtc);
        string endLabel = interval.IsLive
            ? "Now"
            : endLocal.Date == startLocal.Date
                ? FormatLocalTime(endLocal, interval.EndUtc)
                : $"{endLocal.ToString("MMM d", CultureInfo.CurrentCulture)}, {FormatLocalTime(endLocal, interval.EndUtc)}";
        string timeLabel = $"{startTimeLabel} – {endLabel}";
        var times = CreateRecordsSecondaryCell(timeLabel);
        times.ToolTip = isClipped
            ? $"{timeLabel}. Showing only the part inside the selected period; Edit changes the full record."
            : record.IsOpen
                ? "This record is still being tracked by a running timer."
                : timeLabel;
        Grid.SetColumn(times, 3);
        grid.Children.Add(times);

        var duration = new TextBlock
        {
            Text = FormatDetailedDuration(interval.Duration),
            FontFamily = (FontFamily)FindResource("ThemeMonoFontFamily"),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(duration, 4);
        grid.Children.Add(duration);

        FrameworkElement action;
        if (record.IsOpen)
        {
            action = new Border
            {
                Background = (Brush)FindResource("SelectionBrush"),
                CornerRadius = (CornerRadius)FindResource("ThemeCardCornerRadius"),
                Padding = new Thickness(7, 4, 7, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Pause this timer before editing its active record.",
                Child = new TextBlock
                {
                    Text = "ACTIVE",
                    Foreground = (Brush)FindResource("AccentBrush"),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                }
            };
        }
        else
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            var edit = new Button
            {
                Content = "Edit",
                Style = (Style)FindResource("ModernButton"),
                Height = 28,
                MinWidth = 58,
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11,
                IsEnabled = canEdit,
                ToolTip = canEdit
                    ? "Edit this record"
                    : "Record editing is unavailable while project history cannot be saved."
            };
            var editFocus = new RecordActionFocus(record.Id, IsDelete: false);
            edit.Tag = editFocus;
            AutomationProperties.SetName(
                edit,
                $"Edit {record.ProjectName} record from {EnsureUtc(record.StartUtc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}");
            _recordActionButtons[editFocus] = edit;
            edit.Click += (_, _) => EditRecord(record);
            actions.Children.Add(edit);

            var delete = new Button
            {
                Content = "Delete",
                Style = (Style)FindResource("StopButton"),
                Height = 28,
                MinWidth = 58,
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11,
                Margin = new Thickness(6, 0, 0, 0),
                IsEnabled = canEdit,
                ToolTip = canEdit
                    ? "Delete this saved record"
                    : "Record deletion is unavailable while project history cannot be saved."
            };
            var deleteFocus = new RecordActionFocus(record.Id, IsDelete: true);
            delete.Tag = deleteFocus;
            AutomationProperties.SetName(
                delete,
                $"Delete {record.ProjectName} record from {EnsureUtc(record.StartUtc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}");
            _recordActionButtons[deleteFocus] = delete;
            delete.Click += (_, _) => DeleteRecord(record);
            actions.Children.Add(delete);
            action = actions;
        }

        Grid.SetColumn(action, 5);
        grid.Children.Add(action);
        card.Child = grid;
        return card;
    }

    private TextBlock CreateRecordsSecondaryCell(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("SecondaryTextBrush"),
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(7, 0, 7, 0)
    };

    private void UpdateRecordsMutationAvailability()
    {
        bool canMutate = SafeCanMutateRecords();
        AddRecordButton.IsEnabled = canMutate;

        string? warning;
        try
        {
            warning = _recordsPersistenceWarning();
        }
        catch (InvalidOperationException exception)
        {
            CrashLogger.LogRecoverable(
                exception,
                "ProjectDashboardPersistenceWarningProvider");
            warning = "Project history storage is unavailable.";
        }

        if (!canMutate && string.IsNullOrWhiteSpace(warning))
            warning = "Records are read-only because project history cannot currently be saved.";

        AddRecordButton.ToolTip = canMutate
            ? "Add a past project record manually"
            : warning;
        if (string.IsNullOrWhiteSpace(warning))
        {
            RecordsPersistenceWarningPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowRecordsWarning(warning.Trim());
        }
    }

    private bool CheckRecordsMutationAvailable()
    {
        if (SafeCanMutateRecords())
            return true;

        UpdateRecordsMutationAvailability();
        ProjectRecordsExpander.IsExpanded = true;
        return false;
    }

    private void EnsureRecordsMutationAvailable()
    {
        if (!SafeCanMutateRecords())
        {
            throw new InvalidOperationException(
                "Project records are read-only because project history cannot be saved.");
        }
    }

    private bool SafeCanMutateRecords()
    {
        try
        {
            return _canMutateRecords();
        }
        catch (InvalidOperationException exception)
        {
            CrashLogger.LogRecoverable(
                exception,
                "ProjectDashboardMutationAvailabilityProvider");
            return false;
        }
    }

    private void ShowRecordsWarning(string message)
    {
        RecordsPersistenceWarningText.Text = message;
        RecordsPersistenceWarningPanel.Visibility = Visibility.Visible;
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

    private string GetRangeHeading(DateTime asOfLocal) => _selectedRange switch
    {
        DashboardRange.Day => (_selectedDayLocal ?? asOfLocal.Date)
            .ToString("dddd, MMMM d", CultureInfo.CurrentCulture),
        DashboardRange.SevenDays => "Last 7 days",
        DashboardRange.ThirtyDays => "Last 30 days",
        _ => "All tracked time"
    };

    private static DisplayInterval? CreateDisplayInterval(
        ProjectWorkIntervalView source,
        DashboardUtcRange range,
        DateTime asOfUtc)
    {
        ClippedProjectInterval? clipped = ProjectDashboardAnalytics.Clip(
            source,
            range,
            asOfUtc);
        if (clipped is not { } value)
            return null;

        string projectKey = string.IsNullOrWhiteSpace(source.ProjectKey)
            ? source.ProjectName.Trim()
            : source.ProjectKey;
        string projectName = string.IsNullOrWhiteSpace(source.ProjectName)
            ? "Unnamed project"
            : source.ProjectName.Trim();

        return new DisplayInterval(
            source,
            projectKey,
            projectName,
            value.StartUtc,
            value.EndUtc,
            value.IsLive);
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
                Background = (Brush)FindResource("ChartSeries1Brush"),
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

    private void RenderHeatmap(
        ProjectHistoryView history,
        DateTime asOfLocal,
        ProjectFilterOption? selectedProject)
    {
        DateTime today = asOfLocal.Date;
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        DateTime currentWeekStart = today.AddDays(-daysSinceMonday);
        DateTime firstDate = currentWeekStart.AddDays(-(HeatmapWeekCount - 1) * 7);
        DateTime lastDate = currentWeekStart.AddDays(6);

        IReadOnlyList<HeatmapDayValue> values = ProjectDashboardAnalytics.BuildHeatmap(
            history,
            firstDate,
            lastDate,
            selectedProject?.Key,
            TimeZoneInfo.Local);
        long maximumTicks = values
            .Where(item => item.Date <= today)
            .Select(item => item.Duration.Ticks)
            .DefaultIfEmpty(0)
            .Max();

        string scope = selectedProject?.Key is null
            ? "All projects"
            : selectedProject.Name;
        HeatmapScopeText.Text = $"{scope} · last 12 months";
        HeatmapScopeText.ToolTip = HeatmapScopeText.Text;
        HeatmapEmptyText.Text = maximumTicks == 0
            ? selectedProject?.Key is null
                ? "No tracked time for any project in the last 12 months."
                : $"No tracked time for {scope} in the last 12 months."
            : "";

        SolidColorBrush[] levelBrushes = CreateHeatmapLevelBrushes();
        SolidColorBrush borderBrush = GetSolidResourceBrush(
            "BorderBrush",
            Color.FromRgb(76, 88, 98));
        SolidColorBrush accentBrush = GetSolidResourceBrush(
            "AccentBrush",
            Color.FromRgb(66, 185, 232));

        DateTime? previouslyRenderedToday = _heatmapRenderedToday;
        EnsureHeatmapGrid(firstDate, today);
        foreach (HeatmapDayValue value in values)
        {
            Button cell = _heatmapCells[value.Date];
            bool isFuture = value.Date > today;
            bool isSelected = _selectedRange == DashboardRange.Day
                              && _selectedDayLocal == value.Date;
            int level = GetHeatmapLevel(value.Duration.Ticks, maximumTicks);
            string toolTip = CreateHeatmapToolTip(value, scope, isFuture);

            cell.Background = isFuture ? levelBrushes[0] : levelBrushes[level];
            cell.BorderBrush = isSelected ? accentBrush : borderBrush;
            cell.BorderThickness = new Thickness(isSelected ? 2 : 1);
            cell.Opacity = isFuture ? 0.3 : 1;
            cell.Cursor = isFuture ? Cursors.Arrow : Cursors.Hand;
            cell.ToolTip = toolTip;
            cell.IsEnabled = !isFuture;
            cell.Focusable = !isFuture;
            cell.IsTabStop = !isFuture && ReferenceEquals(cell, _heatmapTabStop);
            AutomationProperties.SetName(
                cell,
                isFuture
                    ? toolTip.Replace('\n', ' ')
                    : $"{toolTip.Replace('\n', ' ')}. Open daily statistics");
        }

        if (_heatmapTabStop?.Focusable != true
            && _heatmapCells.TryGetValue(today, out Button? todayCell))
        {
            SetHeatmapTabStop(todayCell);
        }

        if (previouslyRenderedToday.HasValue
            && previouslyRenderedToday.Value != today
            && !HeatmapHost.IsKeyboardFocusWithin)
        {
            DateTime preferredTabDate = _selectedRange == DashboardRange.Day
                                        && _selectedDayLocal is { } selectedDate
                                        && selectedDate >= firstDate
                                        && selectedDate <= today
                ? selectedDate
                : today;
            if (_heatmapCells.TryGetValue(preferredTabDate, out Button? preferredCell)
                && preferredCell.IsEnabled)
            {
                SetHeatmapTabStop(preferredCell);
            }
        }

        _heatmapRenderedToday = today;

        RenderHeatmapLegend(levelBrushes, borderBrush);
    }

    private void EnsureHeatmapGrid(DateTime firstDate, DateTime today)
    {
        if (_heatmapFirstDate == firstDate && _heatmapCells.Count == HeatmapWeekCount * 7)
            return;

        DateTime? focusedDate = Keyboard.FocusedElement is Button { Tag: DateTime focusedTagDate }
            ? focusedTagDate.Date
            : null;
        _heatmapFirstDate = firstDate;
        _heatmapCells.Clear();
        _heatmapTabStop = null;
        HeatmapHost.Children.Clear();
        HeatmapHost.ColumnDefinitions.Clear();
        HeatmapHost.RowDefinitions.Clear();
        HeatmapHost.Width = 42 + (HeatmapWeekCount * HeatmapCellStep);
        HeatmapHost.Height = 25 + (7 * HeatmapCellStep);
        HeatmapHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        for (int week = 0; week < HeatmapWeekCount; week++)
        {
            HeatmapHost.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(HeatmapCellStep)
            });
        }

        HeatmapHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(25) });
        for (int day = 0; day < 7; day++)
        {
            HeatmapHost.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(HeatmapCellStep)
            });
        }

        AddHeatmapLabels(firstDate);
        DateTime preferredTabDate = focusedDate
                                    ?? (_selectedRange == DashboardRange.Day
                                        && _selectedDayLocal is { } selectedDate
                                        && selectedDate >= firstDate
                                        && selectedDate <= today
                                            ? selectedDate
                                            : today);
        if (preferredTabDate < firstDate || preferredTabDate > today)
            preferredTabDate = today;

        for (int week = 0; week < HeatmapWeekCount; week++)
        {
            for (int dayIndex = 0; dayIndex < 7; dayIndex++)
            {
                DateTime date = firstDate.AddDays((week * 7) + dayIndex);
                bool isFuture = date > today;
                var cell = new Button
                {
                    Style = (Style)FindResource("HeatmapCellButton"),
                    Width = HeatmapCellSize,
                    Height = HeatmapCellSize,
                    Margin = new Thickness(2),
                    Tag = date,
                    IsEnabled = !isFuture,
                    Focusable = !isFuture,
                    IsTabStop = !isFuture && date == preferredTabDate
                };
                cell.Click += HeatmapCell_Click;
                cell.PreviewKeyDown += HeatmapCell_PreviewKeyDown;
                cell.GotKeyboardFocus += HeatmapCell_GotKeyboardFocus;
                Grid.SetColumn(cell, week + 1);
                Grid.SetRow(cell, dayIndex + 1);
                HeatmapHost.Children.Add(cell);
                _heatmapCells.Add(date, cell);
                if (cell.IsTabStop)
                    _heatmapTabStop = cell;
            }
        }

        if (focusedDate is { } restoreDate
            && _heatmapCells.TryGetValue(restoreDate, out Button? focusedCell)
            && focusedCell.Focusable)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => focusedCell.Focus()));
        }
    }

    private void AddHeatmapLabels(DateTime firstDate)
    {
        DateTime? previousMonth = null;
        for (int week = 0; week < HeatmapWeekCount; week++)
        {
            DateTime labelDate = firstDate.AddDays((week * 7) + 3);
            if (previousMonth?.Month == labelDate.Month
                && previousMonth?.Year == labelDate.Year)
            {
                continue;
            }

            var monthLabel = new TextBlock
            {
                Text = labelDate.ToString("MMM", CultureInfo.CurrentCulture),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            monthLabel.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            Grid.SetColumn(monthLabel, week + 1);
            Grid.SetColumnSpan(monthLabel, Math.Min(4, HeatmapWeekCount - week));
            HeatmapHost.Children.Add(monthLabel);
            previousMonth = labelDate;
        }

        for (int dayIndex = 0; dayIndex < 7; dayIndex++)
        {
            DateTime labelDate = firstDate.AddDays(dayIndex);
            var dayLabel = new TextBlock
            {
                Text = labelDate.ToString("ddd", CultureInfo.CurrentCulture),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            dayLabel.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            Grid.SetRow(dayLabel, dayIndex + 1);
            HeatmapHost.Children.Add(dayLabel);
        }
    }

    private void HeatmapCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date } || date > _latestLocalDate)
            return;

        e.Handled = true;
        SelectHeatmapDay(date);
    }

    private void HeatmapCell_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Button cell)
            SetHeatmapTabStop(cell);
    }

    private void HeatmapCell_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date })
            return;

        int dayOffset = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            Key.Left => -7,
            Key.Right => 7,
            _ => 0
        };
        if (dayOffset == 0)
            return;

        DateTime targetDate = date.AddDays(dayOffset);
        if (_heatmapCells.TryGetValue(targetDate, out Button? target)
            && target.Focusable)
        {
            e.Handled = true;
            target.Focus();
        }
    }

    private void SetHeatmapTabStop(Button cell)
    {
        if (!ReferenceEquals(_heatmapTabStop, cell) && _heatmapTabStop != null)
            _heatmapTabStop.IsTabStop = false;

        _heatmapTabStop = cell;
        cell.IsTabStop = true;
    }

    private void SelectHeatmapDay(DateTime date)
    {
        _selectedDayLocal = date.Date > _latestLocalDate
            ? _latestLocalDate
            : date.Date;
        _followToday = _selectedDayLocal.Value >= _latestLocalDate;
        _recordsPageIndex = 0;
        SelectRange(DashboardRange.Day);
    }

    private static int GetHeatmapLevel(long ticks, long maximumTicks)
    {
        if (ticks <= 0 || maximumTicks <= 0)
            return 0;

        double ratio = ticks / (double)maximumTicks;
        return Math.Clamp((int)Math.Ceiling(ratio * 4), 1, 4);
    }

    private static string CreateHeatmapToolTip(
        HeatmapDayValue value,
        string scope,
        bool isFuture)
    {
        string date = value.Date.ToString("D", CultureInfo.CurrentCulture);
        if (isFuture)
            return $"{date}\nFuture date";

        string records = value.RecordCount == 1
            ? "1 record"
            : $"{value.RecordCount.ToString(CultureInfo.CurrentCulture)} records";
        return $"{date}\n{scope}\n{FormatCompactDuration(value.Duration)} · {records}";
    }

    private SolidColorBrush[] CreateHeatmapLevelBrushes()
    {
        Color surface = GetSolidResourceBrush(
            "SurfaceRaisedBrush",
            Color.FromRgb(31, 38, 44)).Color;
        Color accent = GetSolidResourceBrush(
            "AccentBrush",
            Color.FromRgb(66, 185, 232)).Color;
        double[] strengths = [0, 0.18, 0.38, 0.62, 0.88];
        return strengths
            .Select(strength =>
            {
                var brush = new SolidColorBrush(BlendColors(surface, accent, strength));
                brush.Freeze();
                return brush;
            })
            .ToArray();
    }

    private void RenderHeatmapLegend(
        IReadOnlyList<SolidColorBrush> levelBrushes,
        Brush borderBrush)
    {
        HeatmapLegendPanel.Children.Clear();
        HeatmapLegendPanel.Children.Add(new TextBlock
        {
            Text = "Less",
            FontSize = 10,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });

        foreach (SolidColorBrush brush in levelBrushes)
        {
            HeatmapLegendPanel.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                Background = brush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(2, 0, 0, 0)
            });
        }

        HeatmapLegendPanel.Children.Add(new TextBlock
        {
            Text = "More",
            FontSize = 10,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });
    }

    private SolidColorBrush GetSolidResourceBrush(string key, Color fallback)
    {
        if (FindResource(key) is SolidColorBrush brush)
            return brush;

        var fallbackBrush = new SolidColorBrush(fallback);
        fallbackBrush.Freeze();
        return fallbackBrush;
    }

    private static Color BlendColors(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Blend(byte start, byte end) =>
            (byte)Math.Round(start + ((end - start) * amount));
        return Color.FromRgb(
            Blend(from.R, to.R),
            Blend(from.G, to.G),
            Blend(from.B, to.B));
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
        string end = fragment.IsLive && fragment.EndsAtIntervalEnd
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
            AppThemeCatalog.Acanthus => AcanthusProjectPalette,
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
                interval.IsLive,
                endUtc == interval.EndUtc);
        }
    }

    private static DateTime LocalBoundaryToUtc(DateTime local) =>
        ProjectDashboardAnalytics.LocalBoundaryToUtc(local, TimeZoneInfo.Local);

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

    private sealed record DisplayInterval(
        ProjectWorkIntervalView Source,
        string ProjectKey,
        string ProjectName,
        DateTime StartUtc,
        DateTime EndUtc,
        bool IsLive)
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
        bool IsLive,
        bool EndsAtIntervalEnd);
    private sealed record TimelineItem(DayFragment Fragment, int Lane);
    private readonly record struct RecordActionFocus(Guid RecordId, bool IsDelete);

    private sealed record ProjectFilterOption(string? Key, string Name)
    {
        public override string ToString() => Name;
    }
}
