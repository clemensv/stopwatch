using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace StopwatchOverlay
{
    public partial class TimerNameWindow : Window
    {
        private static readonly object AddNewProjectTag = new();
        private readonly string _currentName;
        private readonly int _projectCount;

        public TimerNameWindow(string currentName, IEnumerable<string> projectNames)
        {
            InitializeComponent();

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

            _projectCount = projects.Count;
            ProjectSelector.Items.Add(new ComboBoxItem
            {
                Content = "No project (do not track)",
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

            ProjectSelector.Items.Add(new ComboBoxItem
            {
                Content = "＋ Add a new project…",
                Tag = AddNewProjectTag
            });

            SelectInitialProject();
        }

        public string TimerName { get; private set; } = "";

        private bool IsAddingProject
            => ProjectSelector.SelectedItem is ComboBoxItem item
                && ReferenceEquals(item.Tag, AddNewProjectTag);

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

            ProjectSelector.SelectedIndex = _projectCount == 0
                ? ProjectSelector.Items.Count - 1
                : 0;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsAddingProject)
            {
                NewProjectBox.Focus();
                NewProjectBox.SelectAll();
            }
            else
            {
                ProjectSelector.Focus();
            }
        }

        private void ProjectSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NewProjectPanel == null)
                return;

            NewProjectPanel.Visibility = IsAddingProject
                ? Visibility.Visible
                : Visibility.Collapsed;
            ValidationText.Visibility = Visibility.Collapsed;

            if (IsAddingProject && IsLoaded)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NewProjectBox.Focus();
                    NewProjectBox.SelectAll();
                }), DispatcherPriority.Input);
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
            if (IsAddingProject)
            {
                selectedName = NewProjectBox.Text.Trim();
                if (selectedName.Length == 0)
                {
                    ValidationText.Visibility = Visibility.Visible;
                    NewProjectBox.Focus();
                    return;
                }
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
