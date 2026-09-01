using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StopwatchOverlay;

/// <summary>
/// Collects one closed historical project record. The editor works in local
/// wall-clock time and converts to UTC only after validating both endpoints.
/// </summary>
public partial class ProjectRecordEditorWindow : Window
{
    private const string NewProjectKey = "__new_project__";

    private readonly ProjectWorkIntervalView? _record;
    private readonly Func<string, DateTime, DateTime, ProjectRecordMutationResult>? _commit;
    private bool _loaded;

    public ProjectRecordEditorWindow(
        IReadOnlyList<ProjectInfoView> projects,
        string? initialProjectKey,
        ProjectWorkIntervalView? record = null,
        Func<string, DateTime, DateTime, ProjectRecordMutationResult>? commit = null)
    {
        ArgumentNullException.ThrowIfNull(projects);

        _record = record;
        _commit = commit;
        InitializeComponent();

        var choices = projects
            .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(project => new ProjectChoice(project.Key, project.Name, false))
            .ToList();

        if (record != null
            && !choices.Any(choice => string.Equals(
                choice.Key,
                record.ProjectKey,
                StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new ProjectChoice(record.ProjectKey, record.ProjectName, false));
        }

        choices.Add(new ProjectChoice(NewProjectKey, "＋ New project…", true));
        ProjectSelector.ItemsSource = choices;
        ProjectSelector.DisplayMemberPath = nameof(ProjectChoice.Name);

        string? preferredKey = record?.ProjectKey ?? initialProjectKey;
        ProjectChoice? selected = choices.FirstOrDefault(choice =>
            !choice.IsNew
            && string.Equals(choice.Key, preferredKey, StringComparison.OrdinalIgnoreCase));
        ProjectSelector.SelectedItem = selected ?? choices.Last();

        DateTime endLocal;
        DateTime startLocal;
        if (record?.EndUtc is DateTime recordEndUtc)
        {
            startLocal = EnsureUtc(record.StartUtc).ToLocalTime();
            endLocal = EnsureUtc(recordEndUtc).ToLocalTime();
            HeadingText.Text = "Edit project record";
            Title = "Edit project record";
            SaveButton.Content = "Save changes";
            IntroText.Text = "Correct the project or time range for this completed record. Times use your computer's local time zone.";
        }
        else
        {
            endLocal = DateTime.Now;
            endLocal = new DateTime(
                endLocal.Year,
                endLocal.Month,
                endLocal.Day,
                endLocal.Hour,
                endLocal.Minute,
                0,
                DateTimeKind.Local);
            startLocal = endLocal.AddHours(-1);
        }

        StartDatePicker.SelectedDate = startLocal.Date;
        EndDatePicker.SelectedDate = endLocal.Date;
        StartTimeBox.Text = startLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        EndTimeBox.Text = endLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        UpdateDurationPreview();
    }

    public string ProjectName { get; private set; } = "";
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public ProjectWorkIntervalView? SavedRecord { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        UpdateNewProjectPanel();
        UpdateDurationPreview();

        if (ProjectSelector.SelectedItem is ProjectChoice { IsNew: true })
            NewProjectBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        DialogResult = false;
    }

    private void ProjectSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateNewProjectPanel();
        UpdateDurationPreview();
    }

    private void UpdateNewProjectPanel()
    {
        bool isNew = ProjectSelector.SelectedItem is ProjectChoice { IsNew: true };
        NewProjectPanel.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
        if (_loaded && isNew)
            NewProjectBox.Focus();
    }

    private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        => UpdateDurationPreview();

    private void Input_Changed(object sender, TextChangedEventArgs e)
        => UpdateDurationPreview();

    private void UpdateDurationPreview()
    {
        HideValidation();
        if (!TryReadUtcRange(out _, out DateTime startUtc, out DateTime endUtc, out _))
        {
            DurationPreviewText.Text = "—";
            return;
        }

        DurationPreviewText.Text = FormatDuration(endUtc - startUtc);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadUtcRange(
                out string projectName,
                out DateTime startUtc,
                out DateTime endUtc,
                out string error))
        {
            ShowValidation(error);
            return;
        }

        ProjectName = projectName;
        StartUtc = startUtc;
        EndUtc = endUtc;

        if (_commit != null)
        {
            ProjectRecordMutationResult result;
            try
            {
                result = _commit(projectName, startUtc, endUtc);
            }
            catch (Exception exception)
            {
                ShowValidation(exception.Message);
                return;
            }

            if (result.Status != ProjectRecordMutationStatus.Success)
            {
                ShowValidation(result.Status switch
                {
                    ProjectRecordMutationStatus.NotFound =>
                        "This record no longer exists. Close this editor and refresh the records page.",
                    ProjectRecordMutationStatus.OpenInterval =>
                        "This timer is currently running. Pause it before editing the record.",
                    ProjectRecordMutationStatus.Overlap =>
                        "That range overlaps another record from the same timer. Adjust the start or end time.",
                    _ => "The record could not be saved."
                });
                return;
            }

            SavedRecord = result.Record;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool TryReadUtcRange(
        out string projectName,
        out DateTime startUtc,
        out DateTime endUtc,
        out string error)
    {
        projectName = "";
        startUtc = default;
        endUtc = default;
        error = "";

        if (ProjectSelector.SelectedItem is not ProjectChoice projectChoice)
        {
            error = "Choose a project.";
            return false;
        }

        projectName = projectChoice.IsNew ? NewProjectBox.Text.Trim() : projectChoice.Name;
        try
        {
            projectName = ProjectTimeHistory.NormalizeProjectName(projectName);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        if (!TryReadLocalDateTime(StartDatePicker, StartTimeBox, "start", out DateTime startLocal, out error)
            || !TryReadLocalDateTime(EndDatePicker, EndTimeBox, "end", out DateTime endLocal, out error))
        {
            return false;
        }

        bool preserveStart = _record != null
            && EndpointMatchesOriginal(StartDatePicker, StartTimeBox, _record.StartUtc);
        bool preserveEnd = _record?.EndUtc is DateTime originalEnd
            && EndpointMatchesOriginal(EndDatePicker, EndTimeBox, originalEnd);
        if (TimeZoneInfo.Local.IsAmbiguousTime(startLocal) && !preserveStart)
        {
            error = "The start time occurs twice locally because the daylight-saving clock moves backward. Choose an unambiguous time.";
            return false;
        }
        if (TimeZoneInfo.Local.IsAmbiguousTime(endLocal) && !preserveEnd)
        {
            error = "The end time occurs twice locally because the daylight-saving clock moves backward. Choose an unambiguous time.";
            return false;
        }

        try
        {
            startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, TimeZoneInfo.Local);
            endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, TimeZoneInfo.Local);
        }
        catch (ArgumentException)
        {
            error = "One of these local times does not exist because the clock changes at daylight saving time.";
            return false;
        }

        // Changing only the project must not round an automatically recorded
        // timestamp. Keep its original UTC ticks whenever its visible local
        // date/time text is unchanged; edited endpoints use the entered precision.
        if (preserveStart)
        {
            startUtc = EnsureUtc(_record!.StartUtc);
        }
        if (preserveEnd && _record!.EndUtc is DateTime originalEndUtc)
        {
            endUtc = EnsureUtc(originalEndUtc);
        }

        if (endUtc <= startUtc)
        {
            error = "End time must be later than start time.";
            return false;
        }

        if (endUtc > DateTime.UtcNow.AddSeconds(1))
        {
            error = "A historical record cannot end in the future.";
            return false;
        }

        return true;
    }

    private static bool TryReadLocalDateTime(
        DatePicker datePicker,
        TextBox timeBox,
        string label,
        out DateTime local,
        out string error)
    {
        local = default;
        error = "";
        if (datePicker.SelectedDate is not DateTime date)
        {
            error = $"Choose the {label} date.";
            return false;
        }

        if (!DateTime.TryParseExact(
                timeBox.Text.Trim(),
                ["HH:mm", "HH:mm:ss"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsedTime))
        {
            error = $"Enter the {label} time as HH:mm or HH:mm:ss, for example 09:30:15.";
            return false;
        }

        local = DateTime.SpecifyKind(date.Date + parsedTime.TimeOfDay, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            error = $"The {label} time does not exist locally because the clock changes at daylight saving time.";
            return false;
        }

        return true;
    }

    private static bool EndpointMatchesOriginal(
        DatePicker datePicker,
        TextBox timeBox,
        DateTime originalUtc)
    {
        DateTime originalLocal = EnsureUtc(originalUtc).ToLocalTime();
        return datePicker.SelectedDate?.Date == originalLocal.Date
            && string.Equals(
                timeBox.Text.Trim(),
                originalLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationText.Text = "";
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 24)
            return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m";
        return $"{Math.Max(0, (int)duration.TotalSeconds)}s";
    }

    private sealed record ProjectChoice(string Key, string Name, bool IsNew);
}
