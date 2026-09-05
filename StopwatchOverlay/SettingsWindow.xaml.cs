using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace StopwatchOverlay;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _previewTimer;
    private bool _loading;
    private bool _committing;
    private bool _sliderInteractionActive;
    private bool _closed;
    private bool _previewFailureReported;

    internal event Action<SettingsChangeKind>? SettingsChanged;
    internal event Action? SettingsInteractionStarted;
    internal event Action? SettingsInteractionCompleted;
    public event Action? ShowOverlayRequested;

    internal string CurrentCategory { get; private set; } = "Overlay";

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            if (!_closed)
                UpdatePreviewSafely();
        };

        LoadControls();
        WireChanges();
        UpdatePreviewSafely();
    }

    internal void ReloadFromSettings()
    {
        if (_closed)
            return;

        LoadControls();
        SchedulePreviewFromAppliedSettings();
    }

    internal void SchedulePreviewFromAppliedSettings()
    {
        if (_closed)
            return;

        if (!_previewTimer.IsEnabled)
            _previewTimer.Start();
    }

    private void LoadControls()
    {
        _loading = true;
        try
        {
            ThemeCombo.ItemsSource = AppThemeCatalog.All;
            ThemeCombo.SelectedItem = AppThemeCatalog.Normalize(_settings.ThemeMode);
            OverlayThemeCombo.ItemsSource = OverlayThemeCatalog.All;
            OverlayThemeCombo.SelectedItem = OverlayThemeCatalog.Normalize(_settings.OverlayTheme);

            ScreenCombo.Items.Clear();
            ScreenCombo.Items.Add("All displays");
            foreach (Screen screen in Screen.AllScreens)
                ScreenCombo.Items.Add(screen.Primary ? $"{screen.DeviceName} (Primary)" : screen.DeviceName);
            ScreenCombo.SelectedIndex = SettingsChangePolicy.ResolveScreenComboIndex(
                _settings.ScreenIndex,
                Screen.AllScreens.Length);

            SetItems(PositionCombo, ["Top Left", "Top Center", "Top Right", "Bottom Left", "Bottom Center", "Bottom Right", "Custom"], _settings.Position);
            SetItems(TextColorCombo, ["Theme default", "White", "Charcoal", "Yellow", "Cyan", "Lime", "Orange", "Red", "Magenta"], _settings.TextColor);
            SetItems(BorderColorCombo, ["Black", "White", "Dark Gray", "Red", "Blue"], _settings.BorderColor);
            SetItems(FontCombo, ["Consolas", "Cascadia Mono", "Segoe UI", "Arial", "Courier New", "Lucida Console"], _settings.FontFamily);
            SetItems(FormatCombo, ["HH:MM:SS.t", "HH:MM:SS", "MM:SS.t", "MM:SS", "HH:MM"], null);
            FormatCombo.SelectedIndex = Math.Clamp(_settings.TimeFormat, 0, FormatCombo.Items.Count - 1);

            TextSizeSlider.Value = _settings.TextSize;
            BorderWidthSlider.Value = _settings.BorderWidth;
            BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;
            BackgroundStrengthSlider.Value = _settings.PanelBackgroundStrength;
            ClickThroughCheck.IsChecked = _settings.ClickThrough;
            HideOverlayCaptureCheck.IsChecked = _settings.HideOverlayFromCapture;
            LightRingEnabledCheck.IsChecked = _settings.LightRingEnabled;
            LightRingBrightnessSlider.Value = _settings.LightRingBrightness;
            LightRingWidthSlider.Value = _settings.LightRingWidth;
            LightRingCaptureCheck.IsChecked = _settings.LightRingHideFromCapture;
            AutoStartCheck.IsChecked = _settings.AutoStart;
            RecCheck.IsChecked = _settings.ShowRecIndicator;
            BlinkCheck.IsChecked = _settings.BlinkColon;
            SmartInputCheck.IsChecked = _settings.UseSmartCountdownInput;
            StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
            RefreshBackgroundChoices(_settings.PanelBackgroundId);
            UpdateValueLabels();
            UpdateDependentControlStates();
        }
        finally
        {
            _loading = false;
        }
    }

    private static void SetItems(ComboBox comboBox, string[] items, string? selected)
    {
        comboBox.ItemsSource = items;
        comboBox.SelectedItem = items.FirstOrDefault(item =>
            item.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? items[0];
    }

    private void WireChanges()
    {
        ThemeCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.Theme);
        OverlayThemeCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.OverlayTheme);
        ScreenCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.OverlayScreen);
        PositionCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.OverlayPosition);
        TextColorCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.OverlayAppearance);
        BorderColorCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.OverlayAppearance);
        FontCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.OverlayGeometry);
        FormatCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.OverlayGeometry);
        BackgroundCombo.SelectionChanged += (_, _) => CommitControls(SettingsChangeKind.BackgroundSelection);

        WireSlider(TextSizeSlider, SettingsChangeKind.OverlayGeometry);
        WireSlider(BorderWidthSlider, SettingsChangeKind.OverlayAppearance);
        WireSlider(BackgroundOpacitySlider, SettingsChangeKind.OverlayAppearance);
        WireSlider(BackgroundStrengthSlider, SettingsChangeKind.BackgroundStrength);
        WireSlider(LightRingBrightnessSlider, SettingsChangeKind.LightRingAppearance);
        WireSlider(LightRingWidthSlider, SettingsChangeKind.LightRingAppearance);

        WireCheckBox(ClickThroughCheck, SettingsChangeKind.OverlayInteraction);
        WireCheckBox(HideOverlayCaptureCheck, SettingsChangeKind.OverlayInteraction);
        WireCheckBox(LightRingEnabledCheck, SettingsChangeKind.LightRingVisibility);
        WireCheckBox(LightRingCaptureCheck, SettingsChangeKind.LightRingAppearance);
        WireCheckBox(AutoStartCheck, SettingsChangeKind.Behavior);
        WireCheckBox(RecCheck, SettingsChangeKind.Behavior);
        WireCheckBox(BlinkCheck, SettingsChangeKind.Behavior);
        WireCheckBox(SmartInputCheck, SettingsChangeKind.Behavior);
        WireCheckBox(StartWithWindowsCheck, SettingsChangeKind.Startup);
    }

    private void WireSlider(Slider slider, SettingsChangeKind change)
    {
        slider.ValueChanged += (_, _) => CommitControls(change);
        slider.PreviewMouseLeftButtonDown += (_, _) => BeginSliderInteraction();
        slider.PreviewMouseLeftButtonUp += (_, _) => EndSliderInteraction();
        slider.LostMouseCapture += (_, _) => EndSliderInteraction();
        slider.LostKeyboardFocus += (_, _) => EndSliderInteraction();
        slider.PreviewKeyDown += (_, e) =>
        {
            if (IsSliderAdjustmentKey(e.Key))
                BeginSliderInteraction();
        };
        slider.PreviewKeyUp += (_, e) =>
        {
            if (IsSliderAdjustmentKey(e.Key))
                EndSliderInteraction();
        };
    }

    private void WireCheckBox(CheckBox checkBox, SettingsChangeKind change)
    {
        checkBox.Checked += (_, _) => CommitControls(change);
        checkBox.Unchecked += (_, _) => CommitControls(change);
    }

    private static bool IsSliderAdjustmentKey(Key key)
        => key is Key.Left or Key.Right or Key.Up or Key.Down
            or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    private void BeginSliderInteraction()
    {
        if (_sliderInteractionActive || _closed)
            return;

        _sliderInteractionActive = true;
        SettingsInteractionStarted?.Invoke();
    }

    private void EndSliderInteraction()
    {
        if (!_sliderInteractionActive)
            return;

        _sliderInteractionActive = false;
        SettingsInteractionCompleted?.Invoke();
    }

    private void CommitControls(SettingsChangeKind change)
    {
        if (_loading || _committing || _closed)
            return;

        _committing = true;
        try
        {
            if ((change & SettingsChangeKind.Theme) != 0)
                _settings.ThemeMode = AppThemeCatalog.Normalize(ThemeCombo.SelectedItem?.ToString());

            if ((change & SettingsChangeKind.OverlayTheme) != 0)
                _settings.OverlayTheme = OverlayThemeCatalog.Normalize(OverlayThemeCombo.SelectedItem?.ToString());

            if ((change & SettingsChangeKind.OverlayScreen) != 0)
                _settings.ScreenIndex = Math.Max(0, ScreenCombo.SelectedIndex);

            if ((change & SettingsChangeKind.OverlayPosition) != 0)
                _settings.Position = PositionCombo.SelectedItem?.ToString() ?? "Top Center";

            if ((change & (SettingsChangeKind.OverlayAppearance | SettingsChangeKind.OverlayGeometry)) != 0)
            {
                _settings.TextColor = TextColorCombo.SelectedItem?.ToString() ?? "White";
                _settings.BorderColor = BorderColorCombo.SelectedItem?.ToString() ?? "Black";
                _settings.FontFamily = FontCombo.SelectedItem?.ToString() ?? "Consolas";
                _settings.TimeFormat = Math.Max(0, FormatCombo.SelectedIndex);
                _settings.TextSize = TextSizeSlider.Value;
                _settings.BorderWidth = BorderWidthSlider.Value;
                _settings.BackgroundOpacity = BackgroundOpacitySlider.Value;
            }

            if ((change & SettingsChangeKind.BackgroundSelection) != 0
                && BackgroundCombo.SelectedItem is AppBackgroundChoice { IsAvailable: true } background)
            {
                _settings.PanelBackgroundId = background.Id;
            }

            if ((change & SettingsChangeKind.BackgroundStrength) != 0)
            {
                _settings.PanelBackgroundStrength = Math.Clamp(
                    BackgroundStrengthSlider.Value,
                    AppBackgroundCatalog.MinimumPatternStrength,
                    AppBackgroundCatalog.MaximumPatternStrength);
            }

            if ((change & SettingsChangeKind.OverlayInteraction) != 0)
            {
                _settings.ClickThrough = ClickThroughCheck.IsChecked == true;
                _settings.HideOverlayFromCapture = HideOverlayCaptureCheck.IsChecked == true;
            }

            if ((change & (SettingsChangeKind.LightRingVisibility | SettingsChangeKind.LightRingAppearance)) != 0)
            {
                _settings.LightRingEnabled = LightRingEnabledCheck.IsChecked == true;
                _settings.LightRingBrightness = LightRingBrightnessSlider.Value;
                _settings.LightRingWidth = LightRingWidthSlider.Value;
                _settings.LightRingHideFromCapture = LightRingCaptureCheck.IsChecked == true;
            }

            if ((change & SettingsChangeKind.Behavior) != 0)
            {
                _settings.AutoStart = AutoStartCheck.IsChecked == true;
                _settings.ShowRecIndicator = RecCheck.IsChecked == true;
                _settings.BlinkColon = BlinkCheck.IsChecked == true;
                _settings.UseSmartCountdownInput = SmartInputCheck.IsChecked == true;
            }

            if ((change & SettingsChangeKind.Startup) != 0)
                _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;

            UpdateValueLabels();
            UpdateDependentControlStates();
            if ((change & (SettingsChangeKind.Theme
                           | SettingsChangeKind.OverlayTheme
                           | SettingsChangeKind.OverlayAppearance
                           | SettingsChangeKind.OverlayGeometry
                           | SettingsChangeKind.BackgroundSelection
                           | SettingsChangeKind.BackgroundStrength)) != 0)
            {
                SchedulePreviewFromAppliedSettings();
            }

            CrashLogger.RecordUiAction($"Settings change: {change}", CurrentCategory);
            SettingsChanged?.Invoke(change);
        }
        finally
        {
            _committing = false;
        }
    }

    private void UpdateValueLabels()
    {
        TextSizeValueText.Text = $"{Math.Round(TextSizeSlider.Value):0} px";
        BorderWidthValueText.Text = $"{Math.Round(BorderWidthSlider.Value):0} px";
        BackgroundOpacityValueText.Text = $"{Math.Round(BackgroundOpacitySlider.Value):0}%";
        BackgroundStrengthValueText.Text = $"{Math.Round(BackgroundStrengthSlider.Value):0}%";
        LightRingBrightnessValueText.Text = $"{Math.Round(LightRingBrightnessSlider.Value):0}%";
        LightRingWidthValueText.Text = $"{Math.Round(LightRingWidthSlider.Value):0} px";
    }

    private void UpdateDependentControlStates()
    {
        bool hasPattern = BackgroundCombo.SelectedItem is AppBackgroundChoice
            { IsThemeDefault: false, IsAvailable: true };
        BackgroundStrengthSlider.IsEnabled = hasPattern;
        RemoveBackgroundButton.IsEnabled = BackgroundCombo.SelectedItem is AppBackgroundChoice
            { IsCustom: true };

        bool ringEnabled = LightRingEnabledCheck.IsChecked == true;
        LightRingBrightnessSlider.IsEnabled = ringEnabled;
        LightRingWidthSlider.IsEnabled = ringEnabled;
        LightRingCaptureCheck.IsEnabled = ringEnabled;
    }

    private void UpdatePreviewSafely()
    {
        try
        {
            string effectiveTheme = OverlayThemeManager.Apply(
                PreviewThemeScope, _settings.OverlayTheme, _settings.ThemeMode);
            OverlayThemeManager.Apply(
                PreviewToolbarSurface, _settings.OverlayTheme, _settings.ThemeMode);
            PreviewRightCornerTransform.ScaleX = effectiveTheme == OverlayThemeCatalog.AcanthusLight ? -1 : 1;
            Color chrome = OverlayThemeManager.ResourceColor(
                PreviewThemeScope, "OverlayChromeBrush", Colors.Black);
            Brush nextSurface = AppBackgroundManager.CreateOverlaySurfaceBrush(
                chrome,
                OverlayPresentationPolicy.ClampBackgroundOpacity(_settings.BackgroundOpacity / 100.0));
            bool useThemeTextColor = _settings.TextColor == "Theme default";
            Color textColor = useThemeTextColor
                ? OverlayThemeManager.ResourceColor(PreviewThemeScope, "OverlayTimerForegroundBrush", Colors.White)
                : SelectedTextColor(_settings.TextColor);
            Color projectColor = useThemeTextColor
                ? OverlayThemeManager.ResourceColor(PreviewThemeScope, "OverlayProjectForegroundBrush", textColor)
                : textColor;
            var nextTextBrush = new SolidColorBrush(textColor);
            var nextFont = OverlayThemeManager.ResolveTimerFont(
                PreviewThemeScope, effectiveTheme, _settings.FontFamily);
            double nextSize = Math.Clamp(_settings.TextSize, 24, 58);
            var outline = new DropShadowEffect
            {
                Color = SelectedBorderColor(_settings.BorderColor),
                BlurRadius = Math.Clamp(_settings.BorderWidth * 1.6, 1.6, 8),
                ShadowDepth = 0,
                Opacity = 1,
                RenderingBias = RenderingBias.Quality
            };
            outline.Freeze();

            PreviewSurface.Background = nextSurface;
            PreviewTimeText.Foreground = nextTextBrush;
            PreviewTimeText.Effect = outline;
            PreviewProjectText.Foreground = new SolidColorBrush(projectColor);
            PreviewTimeText.FontFamily = nextFont;
            PreviewTimeText.FontSize = nextSize;
            _previewFailureReported = false;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException)
        {
            if (!_previewFailureReported)
            {
                _previewFailureReported = true;
                CrashLogger.LogRecoverable(exception, "SettingsPreview");
            }
        }
    }

    private static Color SelectedTextColor(string value) => value switch
    {
        "Yellow" => Colors.Yellow,
        "Cyan" => Colors.Cyan,
        "Lime" => Colors.Lime,
        "Orange" => Colors.Orange,
        "Red" => Colors.Red,
        "Magenta" => Colors.Magenta,
        "Charcoal" => Color.FromRgb(44, 41, 36),
        _ => Colors.White
    };

    private static Color SelectedBorderColor(string value) => value switch
    {
        "White" => Colors.White,
        "Dark Gray" => Colors.DarkGray,
        "Red" => Colors.Red,
        "Blue" => Colors.Blue,
        _ => Colors.Black
    };

    private void RefreshBackgroundChoices(string? preferredId)
    {
        var choices = AppBackgroundCatalog.GetAvailableChoices(_settings);
        BackgroundCombo.ItemsSource = choices;
        BackgroundCombo.SelectedItem = choices.FirstOrDefault(choice =>
            choice.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
    }

    private void AddBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Add application background",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp",
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
            return;

        string previousSelection = _settings.PanelBackgroundId;
        if (!AppBackgroundCatalog.TryImport(
                picker.FileName,
                _settings.CustomBackgrounds,
                out CustomAppBackground? imported,
                out string? error)
            || imported == null)
        {
            MessageBox.Show(this, error ?? "The image could not be added.",
                "Background", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.CustomBackgrounds.Add(imported);
        _settings.PanelBackgroundId = AppBackgroundCatalog.CustomSelectionId(imported.Id);
        if (!SettingsStore.Save(_settings))
        {
            _settings.CustomBackgrounds.RemoveAll(item =>
                item.Id.Equals(imported.Id, StringComparison.OrdinalIgnoreCase));
            _settings.PanelBackgroundId = previousSelection;
            AppBackgroundCatalog.DeleteManagedCopy(imported);
            MessageBox.Show(this, "The background could not be saved. No changes were kept.",
                "Background", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _loading = true;
        try
        {
            RefreshBackgroundChoices(_settings.PanelBackgroundId);
            UpdateDependentControlStates();
        }
        finally
        {
            _loading = false;
        }
        CommitControls(SettingsChangeKind.BackgroundSelection);
    }

    private void RemoveBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackgroundCombo.SelectedItem is not AppBackgroundChoice { IsCustom: true } choice)
            return;

        CustomAppBackground? custom = _settings.CustomBackgrounds.FirstOrDefault(item =>
            AppBackgroundCatalog.CustomSelectionId(item.Id).Equals(choice.Id, StringComparison.OrdinalIgnoreCase));
        if (custom == null)
            return;

        string previousSelection = _settings.PanelBackgroundId;
        _settings.CustomBackgrounds.RemoveAll(item =>
            item.Id.Equals(custom.Id, StringComparison.OrdinalIgnoreCase));
        _settings.PanelBackgroundId = AppBackgroundCatalog.ThemeDefault;
        if (!SettingsStore.Save(_settings))
        {
            _settings.CustomBackgrounds.Add(custom);
            _settings.PanelBackgroundId = previousSelection;
            MessageBox.Show(this, "The background could not be removed because the updated library could not be saved.",
                "Background", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!AppBackgroundCatalog.DeleteManagedCopy(custom))
        {
            MessageBox.Show(this, "The background was removed from the library, but its managed image could not be deleted.",
                "Background", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _loading = true;
        try
        {
            RefreshBackgroundChoices(_settings.PanelBackgroundId);
            UpdateDependentControlStates();
        }
        finally
        {
            _loading = false;
        }
        CommitControls(SettingsChangeKind.BackgroundSelection);
    }

    private void NavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OverlayPanel == null)
            return;

        string tag = (NavigationList.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "Overlay";
        CurrentCategory = tag;
        OverlayPanel.Visibility = tag == "Overlay" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = tag == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        LightRingPanel.Visibility = tag == "LightRing" ? Visibility.Visible : Visibility.Collapsed;
        BehaviorPanel.Visibility = tag == "Behavior" ? Visibility.Visible : Visibility.Collapsed;
        ApplicationPanel.Visibility = tag == "Application" ? Visibility.Visible : Visibility.Collapsed;
        CrashLogger.RecordUiAction("Settings category changed", CurrentCategory);
        Dispatcher.BeginInvoke(
            new Action(() => SettingsScrollViewer?.ScrollToTop()),
            DispatcherPriority.Loaded);
    }

    private void ShowOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        CrashLogger.RecordUiAction("Overlay visibility requested", CurrentCategory);
        ShowOverlayRequested?.Invoke();
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _previewTimer.Stop();
        EndSliderInteraction();
        base.OnClosed(e);
    }
}
