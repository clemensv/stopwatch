using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace StopwatchOverlay
{
    public partial class ControllerWindow : Window
    {
        // Win32 API for global hotkeys
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly Stopwatch _stopwatch = new();
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _blinkTimer;
        private readonly List<OverlayWindow> _overlayWindows = new();
        private readonly List<LightRingWindow> _lightRingWindows = new();
        private bool _isRunning = false;
        private Screen? _selectedScreen;
        
        // Mode: 0=Stopwatch, 1=Clock, 2=Countdown, 3=Timecode
        private int _currentMode = 0;
        private TimeSpan _countdownDuration = TimeSpan.FromMinutes(5);
        private TimeSpan _countdownRemaining;
        private DateTime _clockTarget;
        // Countdown sub-mode: false = fixed duration, true = count down to a wall-clock time
        private bool _useClockTarget = false;
        private bool _colonVisible = true;
        private int _timeFormat = 0; // 0=HH:MM:SS.t, 1=HH:MM:SS, 2=MM:SS.t, 3=MM:SS
        private int _frameRate = 30;

        private readonly ObservableCollection<string> _lapTimes = new();
        private int _lapCount = 0;
        private HwndSource? _hwndSource;
        private AppSettings _settings = new();
        private Dictionary<ShortcutAction, Shortcut> _shortcuts = new();

        // Custom overlay position (absolute, device-independent) set by dragging the overlay.
        private bool _hasCustomPosition = false;
        private double _customLeft = 0;
        private double _customTop = 0;
        private bool _suppressReposition = false;

        public ControllerWindow()
        {
            // Set dark theme before InitializeComponent so UI renders correctly from the start
            #pragma warning disable WPF0001
            System.Windows.Application.Current.ThemeMode = ThemeMode.Dark;
            #pragma warning restore WPF0001

            InitializeComponent();

            _settings = SettingsStore.Load();
            _shortcuts = new Dictionary<ShortcutAction, Shortcut>(_settings.Shortcuts);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += Timer_Tick;

            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blinkTimer.Tick += BlinkTimer_Tick;

            LapListBox.ItemsSource = _lapTimes;

            PopulateScreens();
            ApplySettingsToUi();
            UpdateButtonStates();
            _timer.Start();
            _blinkTimer.Start();
        }

        private void ThemeModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeModeSelector?.SelectedItem is not ComboBoxItem selected) return;
            var mode = selected.Content?.ToString() switch
            {
                "Light" => ThemeMode.Light,
                "System" => ThemeMode.System,
                _ => ThemeMode.Dark
            };
            #pragma warning disable WPF0001
            System.Windows.Application.Current.ThemeMode = mode;
            #pragma warning restore WPF0001
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Register global hotkeys
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(HwndHook);

            ApplyShortcuts(_shortcuts);
            UpdateShortcutLabels();
        }

        // Unregisters all 4 hotkey ids, then registers each non-unbound shortcut.
        // Returns the actions whose RegisterHotKey failed (combo held by another app).
        private List<ShortcutAction> ApplyShortcuts(Dictionary<ShortcutAction, Shortcut> shortcuts)
        {
            var failures = new List<ShortcutAction>();
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero) return failures; // HWND not ready yet

            foreach (ShortcutAction action in Enum.GetValues<ShortcutAction>())
                UnregisterHotKey(helper.Handle, (int)action);

            foreach (var (action, shortcut) in shortcuts)
            {
                if (shortcut.VirtualKey == 0) continue; // unbound
                bool ok = RegisterHotKey(helper.Handle, (int)action, shortcut.Modifiers, shortcut.VirtualKey);
                if (!ok) failures.Add(action);
            }
            return failures;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                ShortcutAction action = (ShortcutAction)wParam.ToInt32();
                switch (action)
                {
                    case ShortcutAction.StartStop:
                        StartStopButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case ShortcutAction.Reset:
                        ResetButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case ShortcutAction.ToggleOverlay:
                        ToggleOverlayButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case ShortcutAction.Lap:
                        LapButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }

        private void PopulateScreens()
        {
            ScreenSelector.Items.Clear();
            ScreenSelector.Items.Add(new ComboBoxItem { Content = "All Screens", Tag = null });
            
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                string name = screen.Primary ? $"Screen {i + 1} (Primary)" : $"Screen {i + 1}";
                name += $" - {screen.Bounds.Width}x{screen.Bounds.Height}";
                ScreenSelector.Items.Add(new ComboBoxItem { Content = name, Tag = screen });
            }

            ScreenSelector.SelectedIndex = screens.Length > 1 ? 1 : 0;
            _selectedScreen = screens.Length > 0 ? screens[0] : null;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_currentMode == 2 && _isRunning) // Countdown mode
            {
                // Until-clock-time recomputes from the wall clock (drift-free); fixed
                // duration decrements. Both write _countdownRemaining for rendering.
                if (_useClockTarget)
                    _countdownRemaining = _clockTarget - DateTime.Now;
                else
                    _countdownRemaining -= TimeSpan.FromMilliseconds(50);

                // Flash status when hitting zero
                if (_countdownRemaining <= TimeSpan.Zero && _countdownRemaining > TimeSpan.FromMilliseconds(-100))
                {
                    UpdateStatus("Time's up! (counting negative)", Brushes.Red);
                }
            }

            UpdateTimeDisplay();
        }

        private void BlinkTimer_Tick(object? sender, EventArgs e)
        {
            if (BlinkColonCheckBox?.IsChecked == true && _currentMode == 1) // Clock mode
            {
                _colonVisible = !_colonVisible;
                UpdateTimeDisplay();
            }
            else
            {
                _colonVisible = true;
            }

            // Blink REC indicator
            if (_isRunning && ShowRecIndicatorCheckBox?.IsChecked == true)
            {
                RecIndicator.Visibility = RecIndicator.Visibility == Visibility.Visible 
                    ? Visibility.Hidden : Visibility.Visible;
                foreach (var overlay in _overlayWindows)
                {
                    overlay.SetRecIndicatorVisible(RecIndicator.Visibility == Visibility.Visible);
                }
            }
        }

        private void UpdateTimeDisplay()
        {
            string timeText = GetFormattedTime();
            TimeDisplay.Text = timeText;
            
            foreach (var overlay in _overlayWindows)
            {
                overlay.UpdateTime(timeText);
            }
        }

        private string GetFormattedTime()
        {
            string colon = _colonVisible ? ":" : " ";
            
            switch (_currentMode)
            {
                case 1: // Clock
                    var now = DateTime.Now;
                    return _timeFormat switch
                    {
                        0 => $"{now.Hour:D2}{colon}{now.Minute:D2}{colon}{now.Second:D2}.{now.Millisecond / 100:D1}",
                        1 => $"{now.Hour:D2}{colon}{now.Minute:D2}{colon}{now.Second:D2}",
                        2 => $"{now.Minute:D2}{colon}{now.Second:D2}.{now.Millisecond / 100:D1}",
                        3 => $"{now.Minute:D2}{colon}{now.Second:D2}",
                        _ => now.ToString("HH:mm:ss")
                    };

                case 2: // Countdown (both fixed-duration and until-clock-time)
                    var remaining = _countdownRemaining;
                    bool isNegative = remaining < TimeSpan.Zero;
                    var absRemaining = isNegative ? remaining.Negate() : remaining;
                    string sign = isNegative ? "-" : "";
                    return _timeFormat switch
                    {
                        0 => $"{sign}{(int)absRemaining.TotalHours:D2}:{absRemaining.Minutes:D2}:{absRemaining.Seconds:D2}.{absRemaining.Milliseconds / 100:D1}",
                        1 => $"{sign}{(int)absRemaining.TotalHours:D2}:{absRemaining.Minutes:D2}:{absRemaining.Seconds:D2}",
                        2 => $"{sign}{(int)absRemaining.TotalMinutes:D2}:{absRemaining.Seconds:D2}.{absRemaining.Milliseconds / 100:D1}",
                        3 => $"{sign}{(int)absRemaining.TotalMinutes:D2}:{absRemaining.Seconds:D2}",
                        _ => $"{sign}{absRemaining.Hours:D2}:{absRemaining.Minutes:D2}:{absRemaining.Seconds:D2}.{absRemaining.Milliseconds / 100:D1}"
                    };

                case 3: // Timecode (with frames)
                    var tc = _stopwatch.Elapsed;
                    int frames = (int)(tc.Milliseconds / (1000.0 / _frameRate));
                    return $"{tc.Hours:D2}:{tc.Minutes:D2}:{tc.Seconds:D2}:{frames:D2}";

                default: // Stopwatch
                    var elapsed = _stopwatch.Elapsed;
                    return _timeFormat switch
                    {
                        0 => $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}",
                        1 => $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}",
                        2 => $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}",
                        3 => $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}",
                        _ => elapsed.ToString(@"hh\:mm\:ss\.f")
                    };
            }
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (CountdownPanel == null) return;

            if (StopwatchModeRadio?.IsChecked == true) _currentMode = 0;
            else if (ClockModeRadio?.IsChecked == true) _currentMode = 1;
            else if (CountdownModeRadio?.IsChecked == true) _currentMode = 2;
            else if (TimecodeModeRadio?.IsChecked == true) _currentMode = 3;

            CountdownPanel.Visibility = _currentMode == 2 ? Visibility.Visible : Visibility.Collapsed;

            // Refresh the prefilled target whenever the user enters until-clock-time countdown
            if (_currentMode == 2 && _useClockTarget) PrefillClockTarget();

            UpdateButtonStates();
            UpdateTimeDisplay();

            string[] modeNames = { "Stopwatch", "Clock", "Countdown", "Timecode" };
            UpdateStatus($"{modeNames[_currentMode]} Mode", Brushes.DeepSkyBlue);
        }

        private void CountdownTypeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (CountdownDurationPanel == null || CountdownUntilPanel == null) return;

            _useClockTarget = CountdownUntilRadio?.IsChecked == true;
            CountdownDurationPanel.Visibility = _useClockTarget ? Visibility.Collapsed : Visibility.Visible;
            CountdownUntilPanel.Visibility = _useClockTarget ? Visibility.Visible : Visibility.Collapsed;

            if (_useClockTarget) PrefillClockTarget();
            UpdateTimeDisplay();
        }

        private void PrefillClockTarget()
        {
            if (ClockTargetHours == null) return;
            var t = DateTime.Now.AddMinutes(15);
            ClockTargetHours.Text = t.Hour.ToString("D2");
            ClockTargetMinutes.Text = t.Minute.ToString("D2");
            ClockTargetSeconds.Text = "00";
        }

        // Switches the countdown panel between the classic spinners and the smart text box.
        private void ApplyCountdownInputMode()
        {
            if (CountdownClassicPanel == null || CountdownSmartPanel == null) return;

            bool smart = _settings.UseSmartCountdownInput;
            SmartInputMenuItem.IsChecked = smart;
            CountdownClassicPanel.Visibility = smart ? Visibility.Collapsed : Visibility.Visible;
            CountdownSmartPanel.Visibility = smart ? Visibility.Visible : Visibility.Collapsed;

            if (smart) UpdateSmartPreview();
        }

        private void SmartInputMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _settings.UseSmartCountdownInput = SmartInputMenuItem.IsChecked;
            ApplyCountdownInputMode();
            SettingsStore.Save(_settings);
        }

        private void SmartInputBox_TextChanged(object sender, TextChangedEventArgs e)
            => UpdateSmartPreview();

        // Renders the parsed interpretation (or the error) below the smart text box.
        private void UpdateSmartPreview()
        {
            if (SmartPreview == null || SmartInputBox == null) return;

            var text = SmartInputBox.Text?.Trim() ?? "";
            if (text.Length == 0)
            {
                SmartPreview.Text = "e.g. 5m, 1h30m, 2 pm, tomorrow 9 am";
                SmartPreview.Foreground = (Brush)FindResource(SystemColors.GrayTextBrushKey);
                return;
            }

            var now = DateTime.Now;
            var result = CountdownParser.Parse(text, now);
            if (!result.Success)
            {
                SmartPreview.Text = result.Error;
                SmartPreview.Foreground = Brushes.OrangeRed;
                return;
            }

            TimeSpan remaining = result.Duration ?? (result.Target!.Value - now);
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            string span = FormatApproxSpan(remaining);
            string target = (now + remaining).ToString("ddd HH:mm");
            SmartPreview.Text = $"in {span}  →  {target}";
            SmartPreview.Foreground = (Brush)FindResource(SystemColors.GrayTextBrushKey);
        }

        // Compact human duration like "1h 30m" / "2d 3h" / "45s".
        private static string FormatApproxSpan(TimeSpan t)
        {
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                // Stop
                _stopwatch.Stop();
                _isRunning = false;
                StartStopButton.Content = "▶ Start" + ComboSuffix(ShortcutAction.StartStop);
                StartStopButton.Style = (Style)FindResource("StartButton");
                UpdateButtonStates();
                UpdateStatus("Paused", Brushes.Orange);

                RecIndicator.Visibility = Visibility.Collapsed;
                foreach (var overlay in _overlayWindows)
                {
                    overlay.SetRecIndicatorVisible(false);
                }
            }
            else
            {
                // Start
                if (_currentMode == 2) // Countdown
                {
                    if (_settings.UseSmartCountdownInput)
                    {
                        var now = DateTime.Now;
                        var parsed = CountdownParser.Parse(SmartInputBox.Text, now);
                        if (!parsed.Success)
                        {
                            UpdateSmartPreview();
                            UpdateStatus(parsed.Error ?? "Invalid input", Brushes.OrangeRed);
                            return; // abort start; nothing changes
                        }

                        if (parsed.Target.HasValue)
                        {
                            _useClockTarget = true;
                            _clockTarget = parsed.Target.Value;
                            _countdownRemaining = _clockTarget - now;
                        }
                        else
                        {
                            _useClockTarget = false;
                            _countdownDuration = parsed.Duration!.Value;
                            _countdownRemaining = _countdownDuration;
                        }
                    }
                    else if (_useClockTarget)
                    {
                        int.TryParse(ClockTargetHours.Text, out int h);
                        int.TryParse(ClockTargetMinutes.Text, out int m);
                        int.TryParse(ClockTargetSeconds.Text, out int s);
                        h = Math.Clamp(h, 0, 23);
                        m = Math.Clamp(m, 0, 59);
                        s = Math.Clamp(s, 0, 59);

                        var now = DateTime.Now;
                        var target = new DateTime(now.Year, now.Month, now.Day, h, m, s);
                        if (target <= now) target = target.AddDays(1); // roll to tomorrow
                        _clockTarget = target;
                        _countdownRemaining = _clockTarget - now;
                    }
                    else
                    {
                        int.TryParse(CountdownMinutes.Text, out int mins);
                        int.TryParse(CountdownSeconds.Text, out int secs);
                        _countdownDuration = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
                        _countdownRemaining = _countdownDuration;
                    }
                }

                _stopwatch.Start();
                _isRunning = true;
                StartStopButton.Content = "⏹ Stop" + ComboSuffix(ShortcutAction.StartStop);
                StartStopButton.Style = (Style)FindResource("StopButton");
                UpdateButtonStates();
                UpdateStatus("Running", Brushes.LimeGreen);

                if (ShowRecIndicatorCheckBox?.IsChecked == true)
                {
                    RecIndicator.Visibility = Visibility.Visible;
                    foreach (var overlay in _overlayWindows)
                    {
                        overlay.SetRecIndicatorVisible(true);
                    }
                }
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _stopwatch.Reset();
            _isRunning = false;
            StartStopButton.Content = "▶ Start" + ComboSuffix(ShortcutAction.StartStop);
            StartStopButton.Style = (Style)FindResource("StartButton");
            
            if (_currentMode == 2)
            {
                if (_useClockTarget)
                {
                    PrefillClockTarget();
                    _countdownRemaining = TimeSpan.Zero;
                }
                else
                {
                    int.TryParse(CountdownMinutes.Text, out int mins);
                    int.TryParse(CountdownSeconds.Text, out int secs);
                    _countdownDuration = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
                    _countdownRemaining = _countdownDuration;
                }
            }

            _lapTimes.Clear();
            _lapCount = 0;
            LapPlaceholder.Visibility = Visibility.Visible;
            
            UpdateTimeDisplay();
            UpdateButtonStates();
            UpdateStatus("Reset", Brushes.Gray);

            RecIndicator.Visibility = Visibility.Collapsed;
            foreach (var overlay in _overlayWindows)
            {
                overlay.SetRecIndicatorVisible(false);
            }
        }

        private void LapButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == 1) return; // No lap for clock mode

            _lapCount++;
            string lapTime = $"Lap {_lapCount}: {GetFormattedTime()}";
            _lapTimes.Insert(0, lapTime);
            LapPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayWindows.Count > 0)
            {
                // Hide overlays
                foreach (var overlay in _overlayWindows) overlay.Close();
                _overlayWindows.Clear();
                ToggleOverlayButton.Content = "👁 Show" + ComboSuffix(ShortcutAction.ToggleOverlay);
                UpdateStatus(_isRunning ? "Running (Overlay Hidden)" : "Overlay Hidden", 
                    _isRunning ? Brushes.LimeGreen : Brushes.Gray);
            }
            else
            {
                // Show overlays
                var selectedItem = ScreenSelector.SelectedItem as ComboBoxItem;
                
                if (selectedItem?.Tag == null) // "All Screens"
                {
                    foreach (var screen in Screen.AllScreens)
                    {
                        CreateOverlayForScreen(screen);
                    }
                }
                else if (selectedItem.Tag is Screen screen)
                {
                    CreateOverlayForScreen(screen);
                }

                if (AutoStartCheckBox?.IsChecked == true && !_isRunning && _currentMode != 1)
                {
                    StartStopButton_Click(sender, e);
                }

                ToggleOverlayButton.Content = "🙈 Hide" + ComboSuffix(ShortcutAction.ToggleOverlay);
                UpdateStatus($"Overlay visible on {_overlayWindows.Count} screen(s)", Brushes.DeepSkyBlue);
            }
        }

        private void CreateOverlayForScreen(Screen screen)
        {
            var overlay = new OverlayWindow();
            overlay.Tag = screen; // remember which screen for repositioning
            overlay.PositionChangedByUser += () => OnOverlayMoved(overlay);
            ApplyOverlaySettings(overlay);
            PositionOverlay(overlay, screen);
            overlay.Show();
            overlay.UpdateTime(GetFormattedTime());
            
            if (ClickThroughCheckBox?.IsChecked == true)
            {
                overlay.SetClickThrough(true);
            }
            
            if (_isRunning && ShowRecIndicatorCheckBox?.IsChecked == true)
            {
                overlay.SetRecIndicatorVisible(true);
            }
            
            _overlayWindows.Add(overlay);
        }

        private void ScreenSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreenSelector.SelectedItem is ComboBoxItem item && item.Tag is Screen screen)
            {
                _selectedScreen = screen;
            }
            
            // Reposition if overlays are showing
            if (_overlayWindows.Count > 0)
            {
                // Close and reopen to reposition
                foreach (var overlay in _overlayWindows) overlay.Close();
                _overlayWindows.Clear();
                ToggleOverlayButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void PositionSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressReposition) return; // selection flipped to "Custom" by a drag, overlay already placed

            // Reposition all overlays
            if (_overlayWindows.Count > 0)
            {
                foreach (var overlay in _overlayWindows) overlay.Close();
                _overlayWindows.Clear();
                ToggleOverlayButton_Click(sender, new RoutedEventArgs());
            }
        }

        // The user dragged an overlay: capture its spot as the custom position and switch the selector to "Custom".
        private void OnOverlayMoved(OverlayWindow overlay)
        {
            _customLeft = overlay.Left;
            _customTop = overlay.Top;
            _hasCustomPosition = true;

            _suppressReposition = true;
            SelectByContent(PositionSelector, "Custom");
            _suppressReposition = false;
        }

        private void RepositionAllOverlays()
        {
            // Overlays use SizeToContent, so changing format/font/size changes their width.
            // Re-run positioning (after a layout pass) to keep them anchored correctly.
            foreach (var overlay in _overlayWindows)
            {
                if (overlay.Tag is Screen screen)
                {
                    PositionOverlay(overlay, screen);
                }
            }
        }

        private void PositionOverlay(OverlayWindow overlay, Screen screen)
        {
            var bounds = screen.Bounds;
            var position = (PositionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Top Center";

            // Custom: place at the saved absolute coordinates instead of a preset anchor.
            if (position == "Custom" && _hasCustomPosition)
            {
                overlay.Left = _customLeft;
                overlay.Top = _customTop;
                return;
            }

            overlay.UpdateLayout();
            var dpiScale = GetDpiScaleForScreen(screen);
            
            double overlayWidth = overlay.ActualWidth > 0 ? overlay.ActualWidth : 300;
            double overlayHeight = overlay.ActualHeight > 0 ? overlay.ActualHeight : 80;

            double screenLeft = bounds.Left / dpiScale;
            double screenTop = bounds.Top / dpiScale;
            double screenWidth = bounds.Width / dpiScale;
            double screenHeight = bounds.Height / dpiScale;
            double screenRight = screenLeft + screenWidth;
            double screenBottom = screenTop + screenHeight;

            int margin = 10;

            (overlay.Left, overlay.Top) = position switch
            {
                "Top Left" => (screenLeft + margin, screenTop + margin),
                "Top Center" => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin),
                "Top Right" => (screenRight - overlayWidth - margin, screenTop + margin),
                "Bottom Left" => (screenLeft + margin, screenBottom - overlayHeight - margin),
                "Bottom Center" => (screenLeft + (screenWidth - overlayWidth) / 2, screenBottom - overlayHeight - margin),
                "Bottom Right" => (screenRight - overlayWidth - margin, screenBottom - overlayHeight - margin),
                _ => (screenLeft + (screenWidth - overlayWidth) / 2, screenTop + margin)
            };
        }

        private double GetDpiScaleForScreen(Screen screen)
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }
            catch { }
            return 1.0;
        }

        private void AppearanceChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyAllOverlaySettings();
        }

        private void AppearanceSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextSizeLabel != null) TextSizeLabel.Text = ((int)TextSizeSlider.Value).ToString();
            if (BorderWidthLabel != null) BorderWidthLabel.Text = ((int)BorderWidthSlider.Value).ToString();
            if (BackgroundOpacityLabel != null) BackgroundOpacityLabel.Text = $"{(int)BackgroundOpacitySlider.Value}%";
            
            ApplyAllOverlaySettings();
        }

        private void TimeFormatSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _timeFormat = TimeFormatSelector?.SelectedIndex ?? 0;
            UpdateTimeDisplay();
            RepositionAllOverlays();
        }

        private void ShowRecIndicatorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool show = ShowRecIndicatorCheckBox?.IsChecked == true && _isRunning;
            RecIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            foreach (var overlay in _overlayWindows)
            {
                overlay.SetRecIndicatorVisible(show);
            }
        }

        private void ClickThroughCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool clickThrough = ClickThroughCheckBox?.IsChecked == true;
            foreach (var overlay in _overlayWindows)
            {
                overlay.SetClickThrough(clickThrough);
            }
        }

        #region Light Ring

        private void LightRingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (LightRingCheckBox?.IsChecked == true)
            {
                ShowLightRing();
            }
            else
            {
                HideLightRing();
            }
        }

        private void LightRingSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LightRingBrightnessLabel != null)
                LightRingBrightnessLabel.Text = $"{(int)LightRingBrightnessSlider.Value}%";
            if (LightRingWidthLabel != null)
                LightRingWidthLabel.Text = $"{(int)LightRingWidthSlider.Value}px";
            
            UpdateLightRingSettings();
        }

        private void LightRingSliderChanged(object sender, RoutedEventArgs e)
        {
            // Overload for checkbox events
            UpdateLightRingSettings();
        }

        private void ShowLightRing()
        {
            HideLightRing();

            var selectedItem = ScreenSelector.SelectedItem as ComboBoxItem;
            
            if (selectedItem?.Tag == null) // "All Screens"
            {
                foreach (var screen in Screen.AllScreens)
                {
                    CreateLightRingForScreen(screen);
                }
            }
            else if (selectedItem.Tag is Screen screen)
            {
                CreateLightRingForScreen(screen);
            }
        }

        private void CreateLightRingForScreen(Screen screen)
        {
            var lightRing = new LightRingWindow();
            var brightness = (LightRingBrightnessSlider?.Value ?? 100) / 100.0;
            var width = (int)(LightRingWidthSlider?.Value ?? 20);
            var hideFromCapture = LightRingHideFromCaptureCheckBox?.IsChecked == true;
            
            lightRing.Show();
            lightRing.PositionOnScreen(screen);
            lightRing.ApplySettings(brightness, width, hideFromCapture);
            
            _lightRingWindows.Add(lightRing);
        }

        private void HideLightRing()
        {
            foreach (var lightRing in _lightRingWindows)
            {
                lightRing.Close();
            }
            _lightRingWindows.Clear();
        }

        private void UpdateLightRingSettings()
        {
            if (_lightRingWindows.Count == 0) return;

            var brightness = (LightRingBrightnessSlider?.Value ?? 100) / 100.0;
            var width = (int)(LightRingWidthSlider?.Value ?? 20);
            var hideFromCapture = LightRingHideFromCaptureCheckBox?.IsChecked == true;

            foreach (var lightRing in _lightRingWindows)
            {
                lightRing.ApplySettings(brightness, width, hideFromCapture);
            }
        }

        #endregion

        private void ApplyAllOverlaySettings()
        {
            foreach (var overlay in _overlayWindows)
            {
                ApplyOverlaySettings(overlay);
            }
            RepositionAllOverlays();
        }

        private void ApplyOverlaySettings(OverlayWindow overlay)
        {
            if (TextColorSelector == null) return;

            var textColor = GetColorFromSelection(TextColorSelector);
            var borderColor = GetColorFromSelection(BorderColorSelector);
            var fontSize = (int)(TextSizeSlider?.Value ?? 48);
            var borderWidth = (int)(BorderWidthSlider?.Value ?? 2);
            var fontFamily = (FontSelector?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Consolas";
            var bgOpacity = (BackgroundOpacitySlider?.Value ?? 50) / 100.0;

            overlay.ApplySettings(textColor, borderColor, fontSize, borderWidth, fontFamily, bgOpacity);
        }

        private Color GetColorFromSelection(System.Windows.Controls.ComboBox comboBox)
        {
            var selection = (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "White";
            return selection switch
            {
                "White" => Colors.White,
                "Yellow" => Colors.Yellow,
                "Cyan" => Colors.Cyan,
                "Lime" => Colors.Lime,
                "Orange" => Colors.Orange,
                "Red" => Colors.Red,
                "Magenta" => Colors.Magenta,
                "Black" => Colors.Black,
                "Dark Gray" => Colors.DarkGray,
                "Blue" => Colors.Blue,
                _ => Colors.White
            };
        }

        private void UpdateButtonStates()
        {
            bool isClockMode = _currentMode == 1;
            StartStopButton.IsEnabled = !isClockMode;
            ResetButton.IsEnabled = !isClockMode;
            LapButton.IsEnabled = !isClockMode;
        }

        private void UpdateStatus(string text, Brush color)
        {
            StatusText.Text = text;
            StatusIndicator.Fill = color;
        }

        // " (Win+F5)" suffix for button captions; "" if the action is unbound.
        private string ComboSuffix(ShortcutAction action)
        {
            if (_shortcuts.TryGetValue(action, out var s))
            {
                var text = s.Format();
                if (text.Length > 0) return $" ({text})";
            }
            return "";
        }

        // Rewrites every caption/hint that mentions a hotkey combo.
        private void UpdateShortcutLabels()
        {
            string startVerb = _isRunning ? "⏹ Stop" : "▶ Start";
            StartStopButton.Content = startVerb + ComboSuffix(ShortcutAction.StartStop);
            ResetButton.Content = "↺ Reset" + ComboSuffix(ShortcutAction.Reset);
            ToggleOverlayButton.Content = (_overlayWindows.Count > 0 ? "🙈 Hide" : "👁 Show")
                + ComboSuffix(ShortcutAction.ToggleOverlay);
            LapButton.Content = "🏁 Lap" + ComboSuffix(ShortcutAction.Lap);

            string s(ShortcutAction a) => (_shortcuts.TryGetValue(a, out var v) ? v : new Shortcut(0, 0)).Format();
            ShortcutHintText.Text =
                $"{s(ShortcutAction.StartStop)} Start/Stop  {s(ShortcutAction.Reset)} Reset  " +
                $"{s(ShortcutAction.ToggleOverlay)} Overlay  {s(ShortcutAction.Lap)} Lap";

            var lapCombo = (_shortcuts.TryGetValue(ShortcutAction.Lap, out var lv) ? lv : new Shortcut(0, 0)).Format();
            LapPlaceholder.Text = lapCombo.Length > 0
                ? $"Press {lapCombo} or click Lap to record split times"
                : "Click Lap to record split times";
        }

        // Pushes persisted settings into the UI controls. Their change handlers fire as a
        // side effect (labels update, theme applies); overlays don't exist yet so those calls are no-ops.
        private void ApplySettingsToUi()
        {
            SelectByContent(ThemeModeSelector, _settings.ThemeMode);
            SelectByContent(TextColorSelector, _settings.TextColor);
            SelectByContent(BorderColorSelector, _settings.BorderColor);
            SelectByContent(FontSelector, _settings.FontFamily);
            if (_settings.TimeFormat >= 0 && _settings.TimeFormat < TimeFormatSelector.Items.Count)
                TimeFormatSelector.SelectedIndex = _settings.TimeFormat;

            TextSizeSlider.Value = _settings.TextSize;
            BorderWidthSlider.Value = _settings.BorderWidth;
            BackgroundOpacitySlider.Value = _settings.BackgroundOpacity;

            // Layout (screen before light ring, which reads the screen selection).
            _hasCustomPosition = _settings.HasCustomPosition;
            _customLeft = _settings.CustomLeft;
            _customTop = _settings.CustomTop;
            SelectByContent(PositionSelector, _settings.Position);
            if (_settings.ScreenIndex >= 0 && _settings.ScreenIndex < ScreenSelector.Items.Count)
                ScreenSelector.SelectedIndex = _settings.ScreenIndex;

            LightRingBrightnessSlider.Value = _settings.LightRingBrightness;
            LightRingWidthSlider.Value = _settings.LightRingWidth;
            LightRingHideFromCaptureCheckBox.IsChecked = _settings.LightRingHideFromCapture;

            AutoStartCheckBox.IsChecked = _settings.AutoStart;
            ShowRecIndicatorCheckBox.IsChecked = _settings.ShowRecIndicator;
            ClickThroughCheckBox.IsChecked = _settings.ClickThrough;
            BlinkColonCheckBox.IsChecked = _settings.BlinkColon;

            // Last-used mode (drives _currentMode + panel visibility via the Checked handler).
            switch (_settings.Mode)
            {
                case 1: ClockModeRadio.IsChecked = true; break;
                case 2: CountdownModeRadio.IsChecked = true; break;
                case 3: TimecodeModeRadio.IsChecked = true; break;
                default: StopwatchModeRadio.IsChecked = true; break;
            }

            // Light ring last, after screen selection is settled.
            LightRingCheckBox.IsChecked = _settings.LightRingEnabled;

            // Countdown input mode (classic vs smart text).
            ApplyCountdownInputMode();
        }

        // Snapshots the current UI control values back into _settings (everything except shortcuts).
        private void PopulateSettingsFromUi()
        {
            _settings.ThemeMode = SelectedContent(ThemeModeSelector, "Dark");
            _settings.TextColor = SelectedContent(TextColorSelector, "White");
            _settings.BorderColor = SelectedContent(BorderColorSelector, "Black");
            _settings.FontFamily = SelectedContent(FontSelector, "Consolas");
            _settings.TimeFormat = TimeFormatSelector.SelectedIndex;
            _settings.TextSize = TextSizeSlider.Value;
            _settings.BorderWidth = BorderWidthSlider.Value;
            _settings.BackgroundOpacity = BackgroundOpacitySlider.Value;

            _settings.Position = SelectedContent(PositionSelector, "Top Center");
            _settings.ScreenIndex = ScreenSelector.SelectedIndex;
            _settings.HasCustomPosition = _hasCustomPosition;
            _settings.CustomLeft = _customLeft;
            _settings.CustomTop = _customTop;

            _settings.LightRingEnabled = LightRingCheckBox.IsChecked == true;
            _settings.LightRingBrightness = LightRingBrightnessSlider.Value;
            _settings.LightRingWidth = LightRingWidthSlider.Value;
            _settings.LightRingHideFromCapture = LightRingHideFromCaptureCheckBox.IsChecked == true;

            _settings.AutoStart = AutoStartCheckBox.IsChecked == true;
            _settings.ShowRecIndicator = ShowRecIndicatorCheckBox.IsChecked == true;
            _settings.ClickThrough = ClickThroughCheckBox.IsChecked == true;
            _settings.BlinkColon = BlinkColonCheckBox.IsChecked == true;

            _settings.Mode = _currentMode;
        }

        private static void SelectByContent(System.Windows.Controls.ComboBox cb, string content)
        {
            foreach (var obj in cb.Items)
                if (obj is ComboBoxItem item && item.Content?.ToString() == content)
                {
                    cb.SelectedItem = item;
                    return;
                }
        }

        private static string SelectedContent(System.Windows.Controls.ComboBox cb, string fallback)
            => (cb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;

        // Opens the modal shortcut editor; commits the result if the user saves.
        private void OpenShortcuts_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ShortcutsWindow(_shortcuts) { Owner = this };
            if (dlg.ShowDialog() == true)
                CommitPendingShortcuts(dlg.Result);
        }

        // Validates a candidate set, warns+confirms on conflicts, then registers, saves, and refreshes.
        private void CommitPendingShortcuts(Dictionary<ShortcutAction, Shortcut> candidate)
        {
            var problems = new List<string>();

            // 1. In-app duplicates (ignore unbound).
            var seen = new Dictionary<(uint, uint), ShortcutAction>();
            var duplicateCombos = new HashSet<(uint, uint)>();
            foreach (var (action, s) in candidate)
            {
                if (s.VirtualKey == 0) continue;
                var key = (s.Modifiers, s.VirtualKey);
                if (seen.TryGetValue(key, out var other))
                {
                    problems.Add($"{action} and {other} share {s.Format()}.");
                    duplicateCombos.Add(key);
                }
                else
                    seen[key] = action;
            }

            // 2. OS-level rejection (combo held by another running app).
            var failures = ApplyShortcuts(candidate);
            foreach (var action in failures)
            {
                var s = candidate[action];
                if (duplicateCombos.Contains((s.Modifiers, s.VirtualKey)))
                    continue; // already reported as an in-app duplicate above
                problems.Add($"{action} ({s.Format()}) is already in use by another app.");
            }

            if (problems.Count > 0)
            {
                var msg = "Some shortcuts have conflicts:\n\n" + string.Join("\n", problems)
                    + "\n\nKeep these assignments anyway?";
                var result = System.Windows.MessageBox.Show(msg, "Shortcut conflicts", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    // Revert registration to the last committed set; leave pending values in the boxes.
                    ApplyShortcuts(_shortcuts);
                    return;
                }
            }

            // Commit.
            _shortcuts = new Dictionary<ShortcutAction, Shortcut>(candidate);
            _settings.Shortcuts = new Dictionary<ShortcutAction, Shortcut>(candidate);
            PopulateSettingsFromUi(); // keep appearance/layout current in the same file
            SettingsStore.Save(_settings);
            UpdateShortcutLabels();
            UpdateStatus("Shortcuts updated", Brushes.DeepSkyBlue);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Persist appearance/layout/options (shortcuts are already saved on edit).
            PopulateSettingsFromUi();
            SettingsStore.Save(_settings);

            // Unregister hotkeys
            var helper = new WindowInteropHelper(this);
            foreach (ShortcutAction action in Enum.GetValues<ShortcutAction>())
                UnregisterHotKey(helper.Handle, (int)action);

            foreach (var overlay in _overlayWindows) overlay.Close();
            foreach (var lightRing in _lightRingWindows) lightRing.Close();
            _timer.Stop();
            _blinkTimer.Stop();
        }
    }
}
