using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace StopwatchOverlay
{
    public partial class OverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        private readonly DispatcherTimer _hideControlsTimer;
        private bool _isClickThrough;
        private bool _isActive;
        private Color _textColor = Colors.White;
        private double _backgroundOpacity = 0.5;
        private bool _hideFromCapture;

        public event Action? PositionChangedByUser;
        public event Action? ActivationRequested;
        public event Action? ClockToggleRequested;
        public event Action? CloseRequested;
        public event Action? PauseResumeRequested;
        public event Action? ResetRequested;

        public OverlayWindow()
        {
            InitializeComponent();

            _hideControlsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _hideControlsTimer.Tick += (_, _) =>
            {
                _hideControlsTimer.Stop();
                if (!IsMouseOver && !ActionPopupRoot.IsMouseOver)
                    HideActionPopup();
            };

            ActionPopup.CustomPopupPlacementCallback = PlaceActionPopup;
            ApplySettings(Colors.White, Colors.Black, 48, 2, "Consolas", 0.5);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                extendedStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            ApplyCaptureAffinity(hwnd);
        }

        protected override void OnClosed(EventArgs e)
        {
            _hideControlsTimer.Stop();
            ActionPopup.IsOpen = false;
            base.OnClosed(e);
        }

        private CustomPopupPlacement[] PlaceActionPopup(
            Size popupSize, Size targetSize, Point offset)
        {
            double left = (targetSize.Width - popupSize.Width) / 2;
            return new[]
            {
                new CustomPopupPlacement(
                    new Point(left, Math.Max(0, targetSize.Height - 2)),
                    PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(
                    new Point(left, -popupSize.Height + 2),
                    PopupPrimaryAxis.Horizontal)
            };
        }

        public void UpdateTime(string timeText)
        {
            TimeText.Text = timeText;
            TimeTextShadow1.Text = timeText;
            TimeTextShadow2.Text = timeText;
            TimeTextShadow3.Text = timeText;
            TimeTextShadow4.Text = timeText;
        }

        public void SetTimerName(string? timerName)
        {
            string name = timerName?.Trim() ?? string.Empty;
            TimerNameText.Text = name;
            TimerNameText.Visibility = OverlayPresentationPolicy.ShouldShowProjectName(name)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            Color accent = GetThemeColor("AccentBrush", Color.FromRgb(56, 189, 248));
            ActiveIndicatorBorder.BorderBrush = active
                ? new SolidColorBrush(Color.FromArgb(220, accent.R, accent.G, accent.B))
                : Brushes.Transparent;
            AcanthusActiveEdge.Visibility = active && AppThemeManager.IsAcanthus
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public void SetRunning(bool running)
        {
            PauseIcon.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            ResumeIcon.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            PauseResumeActionButton.ToolTip = running ? "Pause timer" : "Resume timer";
        }

        public void SetPauseResumeEnabled(bool enabled)
        {
            PauseResumeActionButton.IsEnabled = enabled;
            PauseResumeActionButton.ToolTip = enabled
                ? (PauseIcon.Visibility == Visibility.Visible ? "Pause timer" : "Resume timer")
                : "Clock mode cannot be paused";
        }

        public void ApplySettings(
            Color textColor,
            Color borderColor,
            int fontSize,
            int borderWidth,
            string fontFamily,
            double backgroundOpacity)
        {
            _textColor = textColor;
            _backgroundOpacity = OverlayPresentationPolicy.ClampBackgroundOpacity(backgroundOpacity);

            var font = new FontFamily(fontFamily);
            TimeText.FontFamily = font;
            TimeTextShadow1.FontFamily = font;
            TimeTextShadow2.FontFamily = font;
            TimeTextShadow3.FontFamily = font;
            TimeTextShadow4.FontFamily = font;

            var textBrush = new SolidColorBrush(textColor);
            TimeText.Foreground = textBrush;
            TimeText.FontSize = fontSize;
            TimerNameText.Foreground = textBrush;

            var outlineBrush = new SolidColorBrush(borderColor);
            TimeTextShadow1.Foreground = outlineBrush;
            TimeTextShadow2.Foreground = outlineBrush;
            TimeTextShadow3.Foreground = outlineBrush;
            TimeTextShadow4.Foreground = outlineBrush;

            foreach (var shadow in new[]
            {
                TimeTextShadow1, TimeTextShadow2, TimeTextShadow3, TimeTextShadow4
            })
                shadow.FontSize = fontSize;

            UpdateShadowOffset(TimeTextShadow1, borderWidth, borderWidth);
            UpdateShadowOffset(TimeTextShadow2, -borderWidth, -borderWidth);
            UpdateShadowOffset(TimeTextShadow3, borderWidth, -borderWidth);
            UpdateShadowOffset(TimeTextShadow4, -borderWidth, borderWidth);

            Color chrome = GetThemeColor("OverlayChromeBrush", Colors.Black);
            Brush surface = AppBackgroundManager.CreateOverlaySurfaceBrush(
                chrome,
                _backgroundOpacity);
            OverlayBorder.Background = surface;
            Color chromeBorder = AppThemeManager.UsesThemedOverlayChrome
                ? GetThemeColor("OverlayChromeBorderBrush", textColor)
                : Color.FromArgb(255, textColor.R, textColor.G, textColor.B);
            var chromeBorderBrush = new SolidColorBrush(chromeBorder);
            OverlayBorder.BorderBrush = chromeBorderBrush;
            ActionSurface.BorderBrush = chromeBorderBrush;

            // Re-apply because appearance updates can occur after active selection.
            SetActive(_isActive);
        }

        public void SetHideFromCapture(bool hideFromCapture)
        {
            _hideFromCapture = hideFromCapture;
            ApplyCaptureAffinity(new WindowInteropHelper(this).Handle);
        }

        private void ApplyCaptureAffinity(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
                SetWindowDisplayAffinity(hwnd,
                    _hideFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }

        private static void UpdateShadowOffset(
            System.Windows.Controls.TextBlock textBlock, double x, double y)
        {
            textBlock.RenderTransform = new TranslateTransform(x, y);
        }

        private static Color GetThemeColor(string key, Color fallback)
            => Application.Current?.TryFindResource(key) is SolidColorBrush brush
                ? brush.Color
                : fallback;

        public void SetRecIndicatorVisible(bool visible)
        {
            RecIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isClickThrough) return;
            _hideControlsTimer.Stop();
            ShowActionPopup();
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
            => ScheduleActionPopupHide();

        private void ActionPopupRoot_MouseEnter(object sender, MouseEventArgs e)
            => _hideControlsTimer.Stop();

        private void ActionPopupRoot_MouseLeave(object sender, MouseEventArgs e)
            => ScheduleActionPopupHide();

        private void ScheduleActionPopupHide()
        {
            _hideControlsTimer.Stop();
            _hideControlsTimer.Start();
        }

        private void ShowActionPopup()
        {
            if (_isClickThrough) return;
            ActionPopup.IsOpen = true;

            var fade = new DoubleAnimation
            {
                From = ActionSurface.Opacity,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var slide = new DoubleAnimation
            {
                From = ActionTranslate.Y,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ActionSurface.BeginAnimation(OpacityProperty, fade);
            ActionTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
        }

        private void HideActionPopup(bool immediate = false)
        {
            _hideControlsTimer.Stop();
            if (!ActionPopup.IsOpen) return;

            if (immediate)
            {
                ActionSurface.BeginAnimation(OpacityProperty, null);
                ActionTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                ActionSurface.Opacity = 0;
                ActionTranslate.Y = -8;
                ActionPopup.IsOpen = false;
                return;
            }

            var duration = TimeSpan.FromMilliseconds(130);
            var fade = new DoubleAnimation(ActionSurface.Opacity, 0, duration);
            fade.Completed += (_, _) =>
            {
                ActionPopup.IsOpen = false;
                ActionTranslate.Y = -8;
            };
            ActionSurface.BeginAnimation(OpacityProperty, fade);
            ActionTranslate.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(ActionTranslate.Y, -5, duration));
        }

        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isClickThrough || e.RightButton != MouseButtonState.Pressed) return;
            e.Handled = true;
            ActivationRequested?.Invoke();
            ClockToggleRequested?.Invoke();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isClickThrough || e.LeftButton != MouseButtonState.Pressed) return;
            ActivationRequested?.Invoke();
            double originalLeft = Left;
            double originalTop = Top;
            HideActionPopup(true);
            try
            {
                DragMove();
                if (Math.Abs(Left - originalLeft) > 0.5 || Math.Abs(Top - originalTop) > 0.5)
                    PositionChangedByUser?.Invoke();
            }
            catch (InvalidOperationException)
            {
                // The button may have been released before WPF entered DragMove.
            }
        }

        private void CloseActionButton_Click(object sender, RoutedEventArgs e)
        {
            HideActionPopup(true);
            ActivationRequested?.Invoke();
            CloseRequested?.Invoke();
        }

        private void PauseResumeActionButton_Click(object sender, RoutedEventArgs e)
        {
            ActivationRequested?.Invoke();
            PauseResumeRequested?.Invoke();
        }

        private void ResetActionButton_Click(object sender, RoutedEventArgs e)
        {
            ActivationRequested?.Invoke();
            ResetRequested?.Invoke();
        }

        public void SetClickThrough(bool clickThrough)
        {
            _isClickThrough = clickThrough;
            if (clickThrough) HideActionPopup(true);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            extendedStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            extendedStyle = clickThrough
                ? extendedStyle | WS_EX_TRANSPARENT
                : extendedStyle & ~WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle);
        }
    }
}
