using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace StopwatchOverlay
{
    public partial class OverlayWindow : Window
    {
        // Win32 API for making window click-through and truly topmost
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private bool _isClickThrough = false;

        public OverlayWindow()
        {
            InitializeComponent();
            
            // Set default appearance
            ApplySettings(Colors.White, Colors.Black, 48, 2, "Consolas", 0.5);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Make the window a tool window (doesn't show in Alt+Tab)
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);
        }

        public void UpdateTime(string timeText)
        {
            TimeText.Text = timeText;
            TimeTextShadow1.Text = timeText;
            TimeTextShadow2.Text = timeText;
            TimeTextShadow3.Text = timeText;
            TimeTextShadow4.Text = timeText;
        }

        public void ApplySettings(Color textColor, Color borderColor, int fontSize, int borderWidth, string fontFamily, double backgroundOpacity)
        {
            // Apply font family
            var font = new FontFamily(fontFamily);
            TimeText.FontFamily = font;
            TimeTextShadow1.FontFamily = font;
            TimeTextShadow2.FontFamily = font;
            TimeTextShadow3.FontFamily = font;
            TimeTextShadow4.FontFamily = font;

            // Apply text color
            TimeText.Foreground = new SolidColorBrush(textColor);
            TimeText.FontSize = fontSize;

            // Apply border/outline color and width
            var borderBrush = new SolidColorBrush(borderColor);
            TimeTextShadow1.Foreground = borderBrush;
            TimeTextShadow2.Foreground = borderBrush;
            TimeTextShadow3.Foreground = borderBrush;
            TimeTextShadow4.Foreground = borderBrush;

            TimeTextShadow1.FontSize = fontSize;
            TimeTextShadow2.FontSize = fontSize;
            TimeTextShadow3.FontSize = fontSize;
            TimeTextShadow4.FontSize = fontSize;

            // Adjust border offset based on border width
            UpdateShadowOffset(TimeTextShadow1, borderWidth, borderWidth);
            UpdateShadowOffset(TimeTextShadow2, -borderWidth, -borderWidth);
            UpdateShadowOffset(TimeTextShadow3, borderWidth, -borderWidth);
            UpdateShadowOffset(TimeTextShadow4, -borderWidth, borderWidth);

            // Apply background opacity
            byte alpha = (byte)(backgroundOpacity * 255);
            OverlayBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        }

        private void UpdateShadowOffset(System.Windows.Controls.TextBlock textBlock, double x, double y)
        {
            textBlock.RenderTransform = new TranslateTransform(x, y);
        }

        public void SetRecIndicatorVisible(bool visible)
        {
            RecIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the overlay window (only if not click-through)
            if (!_isClickThrough && e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public void SetClickThrough(bool clickThrough)
        {
            _isClickThrough = clickThrough;
            var hwnd = new WindowInteropHelper(this).Handle;
            
            if (hwnd == IntPtr.Zero) return;
            
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (clickThrough)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
            }
        }
    }
}
