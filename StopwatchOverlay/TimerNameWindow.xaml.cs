using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace StopwatchOverlay
{
    public partial class TimerNameWindow : Window
    {
        private readonly string _currentName;
        private readonly bool _isCreatingTimer;
        private bool _isAddingProject;

        public TimerNameWindow(
            string currentName,
            IEnumerable<string> projectNames,
            bool isCreatingTimer = false,
            string renameShortcut = "")
        {
            InitializeComponent();

            _isCreatingTimer = isCreatingTimer;
            _currentName = (currentName ?? "").Trim();
            var projects = (projectNames ?? Enumerable.Empty<string>())
                .Select(name => name?.Trim() ?? "")
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (_currentName.Length > 0
                && !projects.Contains(_currentName, StringComparer.OrdinalIgnoreCase))
                projects.Insert(0, _currentName);

            ProjectSelector.Items.Add(new ComboBoxItem
            {
                Content = "Select a project",
                Tag = null
            });

            foreach (string project in projects)
            {
                ProjectSelector.Items.Add(new ComboBoxItem
                {
                    Content = project,
                    Tag = project
                });
            }

            if (_isCreatingTimer)
            {
                Title = "Create timer";
                HeadingText.Text = "Choose a project for this timer";
                DescriptionText.Text =
                    "Select an existing project or use + to add a new one.";
                string assignmentHint = string.IsNullOrWhiteSpace(renameShortcut)
                    ? "You can assign it later from Timers > Set project."
                    : $"You can assign it later with {renameShortcut}.";
                NoProjectHintText.Text =
                    $"Leave ‘Select a project’ selected to create an unnamed timer. {assignmentHint}";
                SaveButton.Content = "Create timer";
            }
            else
            {
                NoProjectHintText.Text =
                    "Choose ‘Select a project’ to make this an unnamed timer.";
                SaveButton.Content = "Apply project";
            }

            SelectInitialProject();
        }

        public string TimerName { get; private set; } = "";

        private void SelectInitialProject()
        {
            if (_currentName.Length > 0)
            {
                foreach (object candidate in ProjectSelector.Items)
                {
                    if (candidate is ComboBoxItem item
                        && item.Tag is string project
                        && string.Equals(project, _currentName, StringComparison.OrdinalIgnoreCase))
                    {
                        ProjectSelector.SelectedItem = item;
                        return;
                    }
                }
            }

            ProjectSelector.SelectedIndex = 0;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ProjectSelector.Focus();
        }

        private void ProjectSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NewProjectPanel == null)
                return;

            if (_isAddingProject)
                ShowNewProjectEditor(false);
            else
                ValidationText.Visibility = Visibility.Collapsed;
        }

        private void ProjectSelector_DropDownClosed(object? sender, EventArgs e)
        {
            // SelectionChanged does not fire when the user reselects the current
            // row. Closing the dropdown still makes that choice authoritative.
            if (_isAddingProject && ProjectSelector.SelectedIndex >= 0)
                ShowNewProjectEditor(false);
        }

        private void AddProjectButton_Click(object sender, RoutedEventArgs e)
            => ShowNewProjectEditor(!_isAddingProject);

        private void ShowNewProjectEditor(bool show)
        {
            _isAddingProject = show;
            NewProjectPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            AddProjectButton.Content = show ? "×" : "+";
            AddProjectButton.ToolTip = show ? "Cancel adding project" : "Add new project";
            AutomationProperties.SetName(
                AddProjectButton,
                show ? "Cancel adding project" : "Add new project");
            AutomationProperties.SetHelpText(
                AddProjectButton,
                show
                    ? "Close the new project name field"
                    : "Open a field for entering a new project name");
            ValidationText.Visibility = Visibility.Collapsed;

            if (show)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NewProjectBox.Focus();
                    NewProjectBox.SelectAll();
                }), DispatcherPriority.Input);
            }
            else if (IsLoaded)
            {
                ProjectSelector.Focus();
            }
        }

        private void NewProjectBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            AcceptSelection();
        }

        private void ProjectSelector_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || ProjectSelector.IsDropDownOpen)
                return;

            e.Handled = true;
            AcceptSelection();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !ProjectSelector.IsDropDownOpen)
            {
                e.Handled = true;
                if (_isAddingProject)
                    ShowNewProjectEditor(false);
                else
                    DialogResult = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void Save_Click(object sender, RoutedEventArgs e)
            => AcceptSelection();

        private void AcceptSelection()
        {
            string selectedName;
            if (_isAddingProject)
            {
                selectedName = NewProjectBox.Text.Trim();
                if (selectedName.Length == 0)
                {
                    ValidationText.Visibility = Visibility.Visible;
                    NewProjectBox.Focus();
                    return;
                }

                if (!ProjectTimeHistory.TryNormalizeProjectName(
                        selectedName,
                        out string? normalizedName))
                {
                    ValidationText.Text = "Enter a valid project name.";
                    ValidationText.Visibility = Visibility.Visible;
                    NewProjectBox.Focus();
                    return;
                }

                selectedName = normalizedName!;
            }
            else
            {
                selectedName = (ProjectSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            }

            TimerName = selectedName;
            DialogResult = true;
        }
    }
}
