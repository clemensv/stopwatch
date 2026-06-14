using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StopwatchOverlay
{
    public enum ShortcutAction
    {
        StartStop = 1,
        Reset = 2,
        ToggleOverlay = 3,
        Lap = 4
    }

    // VirtualKey == 0 means the action is unbound (no global hotkey).
    public record Shortcut(uint Modifiers, uint VirtualKey)
    {
        // Win32 modifier flags (used for both registration and capture).
        public const uint MOD_ALT     = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT   = 0x0004;
        public const uint MOD_WIN     = 0x0008;

        // Renders as "Ctrl+Shift+S", "Win+F5", etc. Empty string if unbound.
        public string Format()
        {
            if (VirtualKey == 0) return "";
            var parts = new List<string>();
            if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString());
            return string.Join("+", parts);
        }
    }

    public class AppSettings
    {
        private const uint VK_F5 = 0x74;
        private const uint VK_F6 = 0x75;
        private const uint VK_F7 = 0x76;
        private const uint VK_F8 = 0x77;

        public Dictionary<ShortcutAction, Shortcut> Shortcuts { get; set; } = new();

        // Appearance (global, shared across modes). Stored as the ComboBox item text
        // so the controller can match it back without an extra enum mapping.
        public string ThemeMode { get; set; } = "Dark";
        public string TextColor { get; set; } = "White";
        public string BorderColor { get; set; } = "Black";
        public string FontFamily { get; set; } = "Consolas";
        public int TimeFormat { get; set; } = 0;
        public double TextSize { get; set; } = 48;
        public double BorderWidth { get; set; } = 2;
        public double BackgroundOpacity { get; set; } = 50;

        // Layout
        public string Position { get; set; } = "Top Center";
        public int ScreenIndex { get; set; } = -1; // -1 = use default selection
        // Absolute overlay coordinates when Position == "Custom" (set by dragging the overlay).
        public bool HasCustomPosition { get; set; } = false;
        public double CustomLeft { get; set; } = 0;
        public double CustomTop { get; set; } = 0;

        // Light ring
        public bool LightRingEnabled { get; set; } = false;
        public double LightRingBrightness { get; set; } = 100;
        public double LightRingWidth { get; set; } = 20;
        public bool LightRingHideFromCapture { get; set; } = false;

        // Options
        public bool AutoStart { get; set; } = false;
        public bool ShowRecIndicator { get; set; } = false;
        public bool ClickThrough { get; set; } = false;
        public bool BlinkColon { get; set; } = false;

        // Last-used mode (0=Stopwatch, 1=Clock, 2=Countdown, 3=Timecode)
        public int Mode { get; set; } = 0;

        public static Dictionary<ShortcutAction, Shortcut> DefaultShortcuts() => new()
        {
            [ShortcutAction.StartStop] = new Shortcut(Shortcut.MOD_WIN, VK_F5),
            [ShortcutAction.Reset] = new Shortcut(Shortcut.MOD_WIN, VK_F6),
            [ShortcutAction.ToggleOverlay] = new Shortcut(Shortcut.MOD_WIN, VK_F7),
            [ShortcutAction.Lap] = new Shortcut(Shortcut.MOD_WIN, VK_F8),
        };

        // Fill any missing action with its default so the rest of the app can assume all 4 keys exist.
        public void EnsureAllActions()
        {
            foreach (var kv in DefaultShortcuts())
            {
                if (!Shortcuts.ContainsKey(kv.Key))
                    Shortcuts[kv.Key] = kv.Value;
            }
        }
    }

    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StopwatchOverlay",
            "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                    if (settings != null)
                    {
                        settings.EnsureAllActions();
                        return settings;
                    }
                }
            }
            catch
            {
                // Corrupt/unreadable file -> fall through to defaults.
            }

            var fresh = new AppSettings { Shortcuts = AppSettings.DefaultShortcuts() };
            return fresh;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(settings, Options);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Best-effort persistence; ignore write failures (e.g. locked file).
            }
        }
    }
}
