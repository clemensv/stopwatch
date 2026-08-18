using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace StopwatchOverlay
{
    // Modal editor for the global shortcuts. Edits a working copy and, on Save,
    // exposes it via Result; the controller does the actual registration and persistence.
    public partial class ShortcutsWindow : Window
    {
        private static readonly Dictionary<ShortcutAction, string> _boxNames = new()
        {
            [ShortcutAction.StartStop] = "StartStopShortcutBox",
            [ShortcutAction.Reset] = "ResetShortcutBox",
            [ShortcutAction.ToggleOverlay] = "ToggleOverlayShortcutBox",
            [ShortcutAction.Lap] = "LapShortcutBox",
            [ShortcutAction.ToggleClock] = "ToggleClockShortcutBox",
        };

        private Dictionary<ShortcutAction, Shortcut> _pending;

        // The edited assignments, valid only after the dialog returns true.
        public Dictionary<ShortcutAction, Shortcut> Result => _pending;

        public ShortcutsWindow(Dictionary<ShortcutAction, Shortcut> current)
        {
            InitializeComponent();
            _pending = new Dictionary<ShortcutAction, Shortcut>(current);
            RenderAll();
        }

        private void RenderAll()
        {
            foreach (var action in _boxNames.Keys)
                RenderShortcutBox(action);
        }

        private System.Windows.Controls.TextBox? BoxFor(ShortcutAction action)
            => FindName(_boxNames[action]) as System.Windows.Controls.TextBox;

        private void RenderShortcutBox(ShortcutAction action)
        {
            var box = BoxFor(action);
            if (box == null) return;
            var combo = _pending.TryGetValue(action, out var s) ? s.Format() : "";
            box.Text = combo.Length > 0 ? combo : "(none)";
        }

        private void ShortcutBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox box) box.Text = "Press a key combo...";
        }

        private void ShortcutBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox box && box.Tag is string tag
                && Enum.TryParse<ShortcutAction>(tag, out var action))
                RenderShortcutBox(action);
        }

        private void ShortcutBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox box || box.Tag is not string tag
                || !Enum.TryParse<ShortcutAction>(tag, out var action))
                return;

            e.Handled = true;

            // Alt combos arrive as Key.System; the real key is in SystemKey.
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Ignore presses that are only a modifier — wait for a real key.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
                return;

            uint mods = 0;
            var m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Control) != 0) mods |= Shortcut.MOD_CONTROL;
            if ((m & ModifierKeys.Alt) != 0) mods |= Shortcut.MOD_ALT;
            if ((m & ModifierKeys.Shift) != 0) mods |= Shortcut.MOD_SHIFT;
            if ((m & ModifierKeys.Windows) != 0) mods |= Shortcut.MOD_WIN;

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            _pending[action] = new Shortcut(mods, vk);
            box.Text = _pending[action].Format();
            Keyboard.ClearFocus(); // commit visually; user clicks Save to persist
        }

        private void ClearShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag
                && Enum.TryParse<ShortcutAction>(tag, out var action))
            {
                _pending[action] = new Shortcut(0, 0); // unbound
                RenderShortcutBox(action);
            }
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            _pending = AppSettings.DefaultShortcuts();
            RenderAll();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
