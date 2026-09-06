using System;
using System.ComponentModel;
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
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // Win32 API to hide window from screen capture (Windows 10 2004+)
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private double _dpiScale = 1.0;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _requestedExcludeFromCapture;
        private bool? _lastCaptureAffinityAttempt;

        public LightRingWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _hwnd = new WindowInteropHelper(this).Handle;
            _lastCaptureAffinityAttempt = null;

            // A light ring must never activate or participate in input. This is
            // especially important while a Settings slider owns mouse capture.
            Marshal.SetLastPInvokeError(0);
            int extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            int readError = Marshal.GetLastPInvokeError();
            if (extendedStyle == 0 && readError != 0)
            {
                CrashLogger.LogRecoverable(
                    new Win32Exception(readError, "The light ring window style could not be read."),
                    "LightRingNativeStyleRead");
                Visibility = Visibility.Hidden;
                Dispatcher.BeginInvoke(new Action(Close));
                return;
            }

            Marshal.SetLastPInvokeError(0);
            int previousStyle = SetWindowLong(
                _hwnd,
                GWL_EXSTYLE,
                extendedStyle | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            int error = Marshal.GetLastPInvokeError();
            if (previousStyle == 0 && error != 0)
            {
                CrashLogger.LogRecoverable(
                    new Win32Exception(error, "The light ring could not be made input-transparent."),
                    "LightRingNativeStyle");
                Visibility = Visibility.Hidden;
                Dispatcher.BeginInvoke(new Action(Close));
                return;
            }

            ApplyCaptureAffinityIfNeeded();
        }

        public void ApplySettings(double brightness, int width, bool excludeFromCapture)
        {
            // Brightness: 0.0 to 1.0, where 1.0 is pure white
            byte alpha = (byte)Math.Round(Math.Clamp(brightness, 0.1, 1) * 255);
            var ringBrush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
            ringBrush.Freeze();
            LightRingBorder.BorderBrush = ringBrush;
            LightRingBorder.BorderThickness = new Thickness(Math.Clamp(width, 5, 100));

            _requestedExcludeFromCapture = excludeFromCapture;
            ApplyCaptureAffinityIfNeeded();
        }

        private void ApplyCaptureAffinityIfNeeded()
        {
            if (_hwnd == IntPtr.Zero
                || _lastCaptureAffinityAttempt == _requestedExcludeFromCapture)
            {
                return;
            }

            _lastCaptureAffinityAttempt = _requestedExcludeFromCapture;
            Marshal.SetLastPInvokeError(0);
            if (!SetWindowDisplayAffinity(
                    _hwnd,
                    _requestedExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error != 0)
                {
                    CrashLogger.LogRecoverable(
                        new Win32Exception(error, "The light ring capture preference could not be applied."),
                        "LightRingCaptureAffinity");
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _hwnd = IntPtr.Zero;
            _lastCaptureAffinityAttempt = null;
            base.OnClosed(e);
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
