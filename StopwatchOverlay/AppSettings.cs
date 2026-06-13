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
    public record Shortcut(uint Modifiers, uint VirtualKey);

    public class AppSettings
    {
        // Win32 modifier flags (mirrors the MOD_* consts in ControllerWindow).
        private const uint MOD_WIN = 0x0008;
        private const uint VK_F5 = 0x74;
        private const uint VK_F6 = 0x75;
        private const uint VK_F7 = 0x76;
        private const uint VK_F8 = 0x77;

        public Dictionary<ShortcutAction, Shortcut> Shortcuts { get; set; } = new();

        public static Dictionary<ShortcutAction, Shortcut> DefaultShortcuts() => new()
        {
            [ShortcutAction.StartStop] = new Shortcut(MOD_WIN, VK_F5),
            [ShortcutAction.Reset] = new Shortcut(MOD_WIN, VK_F6),
            [ShortcutAction.ToggleOverlay] = new Shortcut(MOD_WIN, VK_F7),
            [ShortcutAction.Lap] = new Shortcut(MOD_WIN, VK_F8),
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
