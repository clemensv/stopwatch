using System;
using System.Globalization;
using System.Windows;

namespace StopwatchOverlay;

public partial class ProjectRecordDeleteWindow : Window
{
    public ProjectRecordDeleteWindow(ProjectWorkIntervalView record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.IsOpen)
            throw new ArgumentException("An active project record cannot be deleted.", nameof(record));

        InitializeComponent();

        DateTime startLocal = EnsureUtc(record.StartUtc).ToLocalTime();
        DateTime endLocal = EnsureUtc(record.EndUtc!.Value).ToLocalTime();
        ProjectText.Text = record.ProjectName;
        DateText.Text = startLocal.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        TimeText.Text = endLocal.Date == startLocal.Date
            ? $"{startLocal:HH:mm} – {endLocal:HH:mm}"
            : $"{startLocal:MMM d, HH:mm} – {endLocal:MMM d, HH:mm}";
        DurationText.Text = FormatDuration(record.Duration(record.EndUtc.Value));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string FormatDuration(TimeSpan duration)
    {
        int hours = Math.Max(0, (int)duration.TotalHours);
        return hours > 0 ? $"{hours}:{duration.Minutes:00}" : $"{duration.Minutes}m";
    }
}
