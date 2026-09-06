using System.Windows;

namespace StopwatchOverlay;

public partial class ConfirmationDialogWindow : Window
{
    public ConfirmationDialogWindow(
        string title,
        string heading,
        string message,
        string confirmText,
        bool destructive = false)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (destructive)
            ConfirmButton.Style = (Style)FindResource("StopButton");
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
