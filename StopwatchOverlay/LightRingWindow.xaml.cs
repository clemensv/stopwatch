using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace StopwatchOverlay
{
    public partial class LightRingWindow : Window
    {
        // Win32 API for making window click-through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // Win32 API to hide window from screen capture (Windows 10 2004+)
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private double _dpiScale = 1.0;
        private IntPtr _hwnd = IntPtr.Zero;

        public LightRingWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwnd = new WindowInteropHelper(this).Handle;
            
            // Make the window a tool window and click-through
            int extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        }

        public void ApplySettings(double brightness, int width, bool excludeFromCapture)
        {
            // Brightness: 0.0 to 1.0, where 1.0 is pure white
            byte alpha = (byte)(brightness * 255);
            LightRingBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
            LightRingBorder.BorderThickness = new Thickness(width);

            // Apply exclude from capture setting
            if (_hwnd != IntPtr.Zero)
            {
                SetWindowDisplayAffinity(_hwnd, excludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            }
        }

        public void PositionOnScreen(System.Windows.Forms.Screen screen)
        {
            // Get DPI scaling
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScale = source.CompositionTarget.TransformToDevice.M11;
            }

            // Use WorkingArea to avoid covering the taskbar
            var workArea = screen.WorkingArea;
            this.Left = workArea.Left / _dpiScale;
            this.Top = workArea.Top / _dpiScale;
            this.Width = workArea.Width / _dpiScale;
            this.Height = workArea.Height / _dpiScale;
        }
    }
}
