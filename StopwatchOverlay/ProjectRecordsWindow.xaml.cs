using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace StopwatchOverlay;

public partial class ProjectRecordsWindow : Window
{
    private const int RecordsPerPage = 100;
    private readonly Func<ProjectHistoryView> _historyProvider;
    private readonly Func<string, DateTime, DateTime, ProjectRecordMutationResult> _addRecord;
    private readonly Func<Guid, string, DateTime, DateTime, ProjectRecordMutationResult> _updateRecord;
    private readonly Func<Guid, ProjectRecordMutationResult> _deleteRecord;
    private readonly Func<bool> _canMutate;
    private readonly Func<string?> _persistenceWarning;
    private readonly DispatcherTimer _liveRefreshTimer;

    private ProjectHistoryView? _history;
    private string? _selectedProjectKey;
    private bool _updatingProjectFilter;
    private int _pageIndex;

    public ProjectRecordsWindow(
        Func<ProjectHistoryView> historyProvider,
        Func<string, DateTime, DateTime, ProjectRecordMutationResult> addRecord,
        Func<Guid, string, DateTime, DateTime, ProjectRecordMutationResult> updateRecord,
        Func<Guid, ProjectRecordMutationResult> deleteRecord,
        Func<bool> canMutate,
        Func<string?> persistenceWarning)
    {
        ArgumentNullException.ThrowIfNull(historyProvider);
        ArgumentNullException.ThrowIfNull(addRecord);
        ArgumentNullException.ThrowIfNull(updateRecord);
        ArgumentNullException.ThrowIfNull(deleteRecord);
        ArgumentNullException.ThrowIfNull(canMutate);
        ArgumentNullException.ThrowIfNull(persistenceWarning);

        _historyProvider = historyProvider;
        _addRecord = addRecord;
        _updateRecord = updateRecord;
        _deleteRecord = deleteRecord;
        _canMutate = canMutate;
        _persistenceWarning = persistenceWarning;
        InitializeComponent();

        _liveRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _liveRefreshTimer.Tick += (_, _) =>
        {
            if (_history?.Intervals.Any(interval => interval.IsOpen) == true)
                RefreshFromHistory();
        };
    }

    public void SelectProject(string? projectKey)
    {
        string? nextProjectKey = string.IsNullOrWhiteSpace(projectKey)
            ? null
            : projectKey.Trim();
        if (!string.Equals(_selectedProjectKey, nextProjectKey, StringComparison.OrdinalIgnoreCase))
            _pageIndex = 0;
        _selectedProjectKey = nextProjectKey;
        if (IsLoaded)
            RefreshFromHistory();
    }

    public void RefreshFromHistory()
    {
        ProjectHistoryView history;
        try
        {
            history = _historyProvider();
        }
        catch
        {
            ShowWarning("Project records could not be loaded. Your existing data has not been changed.");
            return;
        }

        _history = history;
        UpdateProjectFilter(history.Projects);
        UpdateMutationAvailability();

        List<ProjectWorkIntervalView> records = history.Intervals
            .Where(record => _selectedProjectKey == null
                             || string.Equals(record.ProjectKey, _selectedProjectKey,
                                 StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => EnsureUtc(record.StartUtc))
            .ThenByDescending(record => record.Id)
            .ToList();

        RenderSummary(history, records);
        RenderRecords(history, records);
        UpdatedText.Text = $"Updated {EnsureUtc(history.AsOfUtc).ToLocalTime():t}";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshFromHistory();
        _liveRefreshTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e) => _liveRefreshTimer.Stop();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshFromHistory();

    private void ProjectFilterSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProjectFilter
            || ProjectFilterSelector.SelectedItem is not ProjectFilterOption option)
        {
            return;
        }

        _selectedProjectKey = option.Key;
        _pageIndex = 0;
        RefreshFromHistory();
    }

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex <= 0)
            return;
        _pageIndex--;
        RefreshFromHistory();
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        _pageIndex++;
        RefreshFromHistory();
    }

    private void AddRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history == null || !CheckMutationAvailable())
            return;

        var editor = new ProjectRecordEditorWindow(
            _history.Projects,
            _selectedProjectKey,
            commit: SaveAddedRecord)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            if (_selectedProjectKey != null && editor.SavedRecord != null)
                _selectedProjectKey = editor.SavedRecord.ProjectKey;
            _pageIndex = 0;
            RefreshFromHistory();
        }
    }

    private void EditRecord(ProjectWorkIntervalView record)
    {
        if (record.IsOpen || _history == null || !CheckMutationAvailable())
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

        if (editor.ShowDialog() == true)
        {
            if (_selectedProjectKey != null && editor.SavedRecord != null)
                _selectedProjectKey = editor.SavedRecord.ProjectKey;
            _pageIndex = 0;
            RefreshFromHistory();
        }
    }

    private ProjectRecordMutationResult SaveAddedRecord(string project, DateTime startUtc, DateTime endUtc)
    {
        EnsureMutationAvailable();
        return _addRecord(project, startUtc, endUtc);
    }

    private ProjectRecordMutationResult SaveUpdatedRecord(
        Guid id,
        string project,
        DateTime startUtc,
        DateTime endUtc)
    {
        EnsureMutationAvailable();
        return _updateRecord(id, project, startUtc, endUtc);
    }

    private void DeleteRecord(ProjectWorkIntervalView record)
    {
        if (record.IsOpen || !CheckMutationAvailable())
            return;

        var confirmation = new ProjectRecordDeleteWindow(record)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true)
            return;

        ProjectRecordMutationResult result;
        try
        {
            EnsureMutationAvailable();
            result = _deleteRecord(record.Id);
        }
        catch (InvalidOperationException exception)
        {
            UpdateMutationAvailability();
            ShowWarning(exception.Message);
            return;
        }
        catch
        {
            ShowWarning("The record could not be deleted. Refresh the list and try again.");
            return;
        }

        RefreshFromHistory();
        switch (result.Status)
        {
            case ProjectRecordMutationStatus.Success:
                return;
            case ProjectRecordMutationStatus.NotFound:
                ShowWarning("That record no longer exists. The list has been refreshed.");
                return;
            case ProjectRecordMutationStatus.OpenInterval:
                ShowWarning("An active record cannot be deleted. Pause its timer first.");
                return;
            default:
                ShowWarning("The record could not be deleted.");
                return;
        }
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

        if (_selectedProjectKey != null
            && !options.Any(option => string.Equals(
                option.Key, _selectedProjectKey, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedProjectKey = null;
        }

        ProjectFilterOption selected = options.First(option => string.Equals(
            option.Key, _selectedProjectKey, StringComparison.OrdinalIgnoreCase));

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

        RecordsHeadingText.Text = selected.Key == null
            ? "All records"
            : $"{selected.Name} records";
    }

    private void RenderSummary(
        ProjectHistoryView history,
        IReadOnlyList<ProjectWorkIntervalView> records)
    {
        DateTime asOfUtc = EnsureUtc(history.AsOfUtc);
        long totalTicks = 0;
        foreach (ProjectWorkIntervalView record in records)
        {
            TimeSpan duration = record.Duration(asOfUtc);
            totalTicks = duration.Ticks > long.MaxValue - totalTicks
                ? long.MaxValue
                : totalTicks + duration.Ticks;
        }

        TotalTimeText.Text = FormatCompactDuration(TimeSpan.FromTicks(totalTicks));
        RecordCountText.Text = records.Count.ToString(CultureInfo.CurrentCulture);
        LatestActivityText.Text = records.Count == 0
            ? "—"
            : EnsureUtc(records[0].StartUtc).ToLocalTime().ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    private void RenderRecords(
        ProjectHistoryView history,
        IReadOnlyList<ProjectWorkIntervalView> records)
    {
        RecordsPanel.Children.Clear();
        bool canEdit = SafeCanMutate();
        DateTime asOfUtc = EnsureUtc(history.AsOfUtc);
        int pageCount = Math.Max(1, (records.Count + RecordsPerPage - 1) / RecordsPerPage);
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);
        List<ProjectWorkIntervalView> visibleRecords = records
            .Skip(_pageIndex * RecordsPerPage)
            .Take(RecordsPerPage)
            .ToList();

        int first = records.Count == 0 ? 0 : (_pageIndex * RecordsPerPage) + 1;
        int last = Math.Min(records.Count, (_pageIndex + 1) * RecordsPerPage);
        PageStatusText.Text = records.Count == 0
            ? "0 records"
            : $"{first}–{last} of {records.Count.ToString(CultureInfo.CurrentCulture)}";
        PreviousPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = _pageIndex + 1 < pageCount;

        foreach (IGrouping<DateTime, ProjectWorkIntervalView> group in visibleRecords
                     .GroupBy(record => EnsureUtc(record.StartUtc).ToLocalTime().Date)
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

            foreach (ProjectWorkIntervalView record in group.OrderByDescending(item => item.StartUtc))
                RecordsPanel.Children.Add(CreateRecordCard(record, asOfUtc, canEdit));
        }

        bool empty = records.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        RecordsPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        ProjectFilterOption? selected = ProjectFilterSelector.SelectedItem as ProjectFilterOption;
        EmptyHeadingText.Text = selected?.Key == null
            ? "No project records yet"
            : $"No records for {selected.Name}";
        EmptyDetailText.Text = selected?.Key == null
            ? "Start a named timer or add a record manually."
            : "Add a record manually or choose another project.";
    }

    private Border CreateRecordCard(
        ProjectWorkIntervalView record,
        DateTime asOfUtc,
        bool canEdit)
    {
        DateTime startLocal = EnsureUtc(record.StartUtc).ToLocalTime();
        DateTime effectiveEndUtc = record.EndUtc.HasValue
            ? EnsureUtc(record.EndUtc.Value)
            : asOfUtc;
        DateTime endLocal = effectiveEndUtc.ToLocalTime();

        var card = new Border
        {
            Background = (Brush)FindResource("SurfaceRaisedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = (Thickness)FindResource("ThemeControlBorderThickness"),
            CornerRadius = (CornerRadius)FindResource("ThemeCardCornerRadius"),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 7)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });

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

        var date = CreateSecondaryCell(startLocal.ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
        Grid.SetColumn(date, 2);
        grid.Children.Add(date);

        string endLabel = record.IsOpen
            ? "Now"
            : endLocal.Date == startLocal.Date
                ? endLocal.ToString("HH:mm", CultureInfo.CurrentCulture)
                : endLocal.ToString("MMM d, HH:mm", CultureInfo.CurrentCulture);
        string timeLabel = $"{startLocal:HH:mm} – {endLabel}";
        var times = CreateSecondaryCell(timeLabel);
        times.ToolTip = record.IsOpen
            ? "This record is still being tracked by a running timer."
            : timeLabel;
        Grid.SetColumn(times, 3);
        grid.Children.Add(times);

        var duration = new TextBlock
        {
            Text = FormatDetailedDuration(record.Duration(asOfUtc)),
            FontFamily = (FontFamily)FindResource("ThemeMonoFontFamily"),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
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
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            var edit = new Button
            {
                Content = "Edit",
                Style = (Style)FindResource("ModernButton"),
                Height = 28,
                MinWidth = 72,
                Padding = new Thickness(9, 3, 9, 3),
                FontSize = 11,
                IsEnabled = canEdit,
                ToolTip = canEdit ? "Edit this record" : "Record editing is unavailable while project history cannot be saved."
            };
            edit.Click += (_, _) => EditRecord(record);
            actions.Children.Add(edit);

            var delete = new Button
            {
                Content = "Delete",
                Style = (Style)FindResource("StopButton"),
                Height = 28,
                MinWidth = 72,
                Padding = new Thickness(9, 3, 9, 3),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                IsEnabled = canEdit,
                ToolTip = canEdit ? "Delete this saved record" : "Record deletion is unavailable while project history cannot be saved."
            };
            delete.Click += (_, _) => DeleteRecord(record);
            actions.Children.Add(delete);
            action = actions;
        }

        Grid.SetColumn(action, 5);
        grid.Children.Add(action);
        card.Child = grid;
        return card;
    }

    private TextBlock CreateSecondaryCell(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("SecondaryTextBrush"),
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 8, 0)
    };

    private void UpdateMutationAvailability()
    {
        bool canMutate = SafeCanMutate();
        AddRecordButton.IsEnabled = canMutate;

        string? warning;
        try
        {
            warning = _persistenceWarning();
        }
        catch
        {
            warning = "Project history storage is unavailable.";
        }

        if (!canMutate && string.IsNullOrWhiteSpace(warning))
            warning = "Records are read-only because project history cannot currently be saved.";

        if (string.IsNullOrWhiteSpace(warning))
        {
            PersistenceWarningPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowWarning(warning.Trim());
        }
    }

    private bool CheckMutationAvailable()
    {
        if (SafeCanMutate())
            return true;

        UpdateMutationAvailability();
        return false;
    }

    private void EnsureMutationAvailable()
    {
        if (!SafeCanMutate())
            throw new InvalidOperationException("Project records are read-only because project history cannot be saved.");
    }

    private bool SafeCanMutate()
    {
        try
        {
            return _canMutate();
        }
        catch
        {
            return false;
        }
    }

    private void ShowWarning(string message)
    {
        PersistenceWarningText.Text = message;
        PersistenceWarningPanel.Visibility = Visibility.Visible;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string FormatCompactDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 24)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{Math.Max(0, (int)duration.TotalMinutes)}m";
    }

    private static string FormatDetailedDuration(TimeSpan duration)
    {
        int hours = Math.Max(0, (int)duration.TotalHours);
        return hours > 0 ? $"{hours}:{duration.Minutes:00}" : $"{duration.Minutes}m";
    }

    private sealed record ProjectFilterOption(string? Key, string Name)
    {
        public override string ToString() => Name;
    }
}
