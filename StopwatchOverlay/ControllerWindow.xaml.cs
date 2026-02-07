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
        private const int HOTKEY_START_STOP = 1;
        private const int HOTKEY_RESET = 2;
        private const int HOTKEY_TOGGLE_OVERLAY = 3;
        private const int HOTKEY_LAP = 4;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_WIN = 0x0008;
        private const uint VK_F5 = 0x74;
        private const uint VK_F6 = 0x75;
        private const uint VK_F7 = 0x76;
        private const uint VK_F8 = 0x77;

        private readonly Stopwatch _stopwatch = new();
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _blinkTimer;
        private readonly List<OverlayWindow> _overlayWindows = new();
        private bool _isRunning = false;
        private Screen? _selectedScreen;
        
        // Mode: 0=Stopwatch, 1=Clock, 2=Countdown, 3=Timecode
        private int _currentMode = 0;
        private TimeSpan _countdownDuration = TimeSpan.FromMinutes(5);
        private TimeSpan _countdownRemaining;
        private bool _colonVisible = true;
        private int _timeFormat = 0; // 0=HH:MM:SS.t, 1=HH:MM:SS, 2=MM:SS.t, 3=MM:SS
        private int _frameRate = 30;

        private readonly ObservableCollection<string> _lapTimes = new();
        private int _lapCount = 0;
        private HwndSource? _hwndSource;

        public ControllerWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += Timer_Tick;

            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blinkTimer.Tick += BlinkTimer_Tick;

            LapListBox.ItemsSource = _lapTimes;

            PopulateScreens();
            UpdateButtonStates();
            _timer.Start();
            _blinkTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Register global hotkeys
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(HwndHook);

            RegisterHotKey(helper.Handle, HOTKEY_START_STOP, MOD_WIN, VK_F5);
            RegisterHotKey(helper.Handle, HOTKEY_RESET, MOD_WIN, VK_F6);
            RegisterHotKey(helper.Handle, HOTKEY_TOGGLE_OVERLAY, MOD_WIN, VK_F7);
            RegisterHotKey(helper.Handle, HOTKEY_LAP, MOD_WIN, VK_F8);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                switch (hotkeyId)
                {
                    case HOTKEY_START_STOP:
                        StartStopButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case HOTKEY_RESET:
                        ResetButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case HOTKEY_TOGGLE_OVERLAY:
                        ToggleOverlayButton_Click(this, new RoutedEventArgs());
                        handled = true;
                        break;
                    case HOTKEY_LAP:
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

                case 2: // Countdown
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
            UpdateButtonStates();
            UpdateTimeDisplay();

            string[] modeNames = { "Stopwatch", "Clock", "Countdown", "Timecode" };
            UpdateStatus($"{modeNames[_currentMode]} Mode", Brushes.DeepSkyBlue);
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                // Stop
                _stopwatch.Stop();
                _isRunning = false;
                StartStopButton.Content = "▶ Start (Win+F5)";
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
                    if (!_isRunning)
                    {
                        int.TryParse(CountdownMinutes.Text, out int mins);
                        int.TryParse(CountdownSeconds.Text, out int secs);
                        _countdownDuration = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
                        _countdownRemaining = _countdownDuration;
                    }
                }
                
                _stopwatch.Start();
                _isRunning = true;
                StartStopButton.Content = "⏹ Stop (Win+F5)";
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
            StartStopButton.Content = "▶ Start (Win+F5)";
            StartStopButton.Style = (Style)FindResource("StartButton");
            
            if (_currentMode == 2)
            {
                int.TryParse(CountdownMinutes.Text, out int mins);
                int.TryParse(CountdownSeconds.Text, out int secs);
                _countdownDuration = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
                _countdownRemaining = _countdownDuration;
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
                ToggleOverlayButton.Content = "👁 Show (Win+F7)";
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

                ToggleOverlayButton.Content = "🙈 Hide (Win+F7)";
                UpdateStatus($"Overlay visible on {_overlayWindows.Count} screen(s)", Brushes.DeepSkyBlue);
            }
        }

        private void CreateOverlayForScreen(Screen screen)
        {
            var overlay = new OverlayWindow();
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
            // Reposition all overlays
            if (_overlayWindows.Count > 0)
            {
                foreach (var overlay in _overlayWindows) overlay.Close();
                _overlayWindows.Clear();
                ToggleOverlayButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void PositionOverlay(OverlayWindow overlay, Screen screen)
        {
            var bounds = screen.Bounds;
            var position = (PositionSelector.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Top Center";

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

        private void ApplyAllOverlaySettings()
        {
            foreach (var overlay in _overlayWindows)
            {
                ApplyOverlaySettings(overlay);
            }
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Unregister hotkeys
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_START_STOP);
            UnregisterHotKey(helper.Handle, HOTKEY_RESET);
            UnregisterHotKey(helper.Handle, HOTKEY_TOGGLE_OVERLAY);
            UnregisterHotKey(helper.Handle, HOTKEY_LAP);

            foreach (var overlay in _overlayWindows) overlay.Close();
            _timer.Stop();
            _blinkTimer.Stop();
        }
    }
}
