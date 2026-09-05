using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StopwatchOverlay
{
    public enum ShortcutAction
    {
        StartStop = 1,
        Reset = 2,
        ToggleOverlay = 3,
        Lap = 4,
        ToggleClock = 5,
        NewTimer = 6,
        NextTimer = 7,
        CloseTimer = 8,
        RenameTimer = 9,
        OpenDashboard = 10,
        ToggleCombinedOverlay = 11
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
        private const uint VK_F2 = 0x71;
        private const uint VK_F3 = 0x72;
        private const uint VK_F4 = 0x73;
        private const uint VK_F5 = 0x74;
        private const uint VK_F6 = 0x75;
        private const uint VK_F7 = 0x76;
        private const uint VK_F8 = 0x77;
        private const uint VK_F9 = 0x78;
        private const uint VK_F10 = 0x79;
        private const uint VK_F11 = 0x7A;
        private const uint VK_F12 = 0x7B;

        public Dictionary<ShortcutAction, Shortcut> Shortcuts { get; set; } = new();

        // Application chrome theme. Stable display names are kept in JSON for
        // backwards compatibility with the legacy "Dark" setting.
        public string ThemeMode { get; set; } = AppThemeCatalog.Midnight;

        // ThemeMode remains the JSON contract so older releases can still read
        // the panel preference. The independent overlay choice is never folded
        // back into it, even when it resolves to the same palette.
        [JsonIgnore]
        public string ApplicationTheme
        {
            get => ThemeMode;
            set => ThemeMode = value;
        }

        public string OverlayTheme { get; set; } = OverlayThemeCatalog.FollowApplicationTheme;

        // Tiled application background. Custom images are copied into the app's
        // managed data folder; settings persist only their safe leaf filenames.
        public string PanelBackgroundId { get; set; } = AppBackgroundCatalog.ThemeDefault;
        public double PanelBackgroundStrength { get; set; } =
            AppBackgroundCatalog.DefaultPatternStrength;
        public List<CustomAppBackground> CustomBackgrounds { get; set; } = new();

        // Floating-overlay appearance (global, shared across timer modes).
        public string TextColor { get; set; } = "White";
        public string BorderColor { get; set; } = "Black";
        public string FontFamily { get; set; } = "Consolas";
        public int TimeFormat { get; set; } = 0;
        public double TextSize { get; set; } = 48;
        public double BorderWidth { get; set; } = 2;
        public double BackgroundOpacity { get; set; } = 50;
        public bool HideOverlayFromCapture { get; set; } = false;

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
        public bool UseSmartCountdownInput { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;

        // Last-used mode (0=Stopwatch, 1=Clock, 2=Countdown, 3=Timecode)
        public int Mode { get; set; } = 0;

        public static Dictionary<ShortcutAction, Shortcut> DefaultShortcuts() => new()
        {
            [ShortcutAction.NewTimer] = new Shortcut(Shortcut.MOD_WIN, VK_F2),
            [ShortcutAction.NextTimer] = new Shortcut(Shortcut.MOD_WIN, VK_F3),
            [ShortcutAction.CloseTimer] = new Shortcut(Shortcut.MOD_WIN, VK_F4),
            [ShortcutAction.StartStop] = new Shortcut(Shortcut.MOD_WIN, VK_F5),
            [ShortcutAction.Reset] = new Shortcut(Shortcut.MOD_WIN, VK_F6),
            [ShortcutAction.ToggleOverlay] = new Shortcut(Shortcut.MOD_WIN, VK_F7),
            [ShortcutAction.Lap] = new Shortcut(Shortcut.MOD_WIN, VK_F8),
            [ShortcutAction.ToggleClock] = new Shortcut(Shortcut.MOD_WIN, VK_F9),
            [ShortcutAction.RenameTimer] = new Shortcut(Shortcut.MOD_WIN, VK_F10),
            [ShortcutAction.OpenDashboard] = new Shortcut(Shortcut.MOD_WIN, VK_F11),
            [ShortcutAction.ToggleCombinedOverlay] = new Shortcut(Shortcut.MOD_WIN, VK_F12),
        };

        // Fill any missing action with its default so the rest of the app can assume all keys exist.
        public void EnsureAllActions()
        {
            Shortcuts ??= new Dictionary<ShortcutAction, Shortcut>();
            foreach (var kv in DefaultShortcuts())
            {
                if (!Shortcuts.ContainsKey(kv.Key))
                    Shortcuts[kv.Key] = kv.Value;
            }
        }

        public void NormalizeForRuntime()
        {
            ThemeMode = AppThemeCatalog.Normalize(ThemeMode);
            OverlayTheme = OverlayThemeCatalog.Normalize(OverlayTheme);
            TextColor = NormalizeChoice(
                TextColor,
                "White",
                "Theme default", "White", "Charcoal", "Yellow", "Cyan", "Lime", "Orange", "Red", "Magenta");
            BorderColor = NormalizeChoice(
                BorderColor,
                "Black",
                "Black", "White", "Dark Gray", "Red", "Blue");
            FontFamily = NormalizeChoice(
                FontFamily,
                "Consolas",
                "Consolas", "Cascadia Mono", "Segoe UI", "Arial", "Courier New", "Lucida Console");
            Position = NormalizeChoice(
                Position,
                "Top Center",
                "Top Left", "Top Center", "Top Right", "Bottom Left", "Bottom Center", "Bottom Right", "Custom");
            TimeFormat = Math.Clamp(TimeFormat, 0, 4);
            TextSize = NormalizeRange(TextSize, 16, 120, 48);
            BorderWidth = NormalizeRange(BorderWidth, 1, 5, 2);
            BackgroundOpacity = NormalizeRange(BackgroundOpacity, 0, 100, 50);
            LightRingBrightness = NormalizeRange(LightRingBrightness, 10, 100, 100);
            LightRingWidth = NormalizeRange(LightRingWidth, 5, 100, 20);
            ScreenIndex = Math.Clamp(ScreenIndex, -1, 64);
            if (!double.IsFinite(CustomLeft) || !double.IsFinite(CustomTop))
            {
                HasCustomPosition = false;
                CustomLeft = 0;
                CustomTop = 0;
            }
            Mode = Math.Clamp(Mode, 0, 3);
        }

        private static string NormalizeChoice(
            string? value,
            string fallback,
            params string[] choices)
            => choices.FirstOrDefault(choice =>
                   choice.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? fallback;

        private static double NormalizeRange(
            double value,
            double minimum,
            double maximum,
            double fallback)
            => double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
        private static readonly object UnavailablePathsGate = new();
        private static readonly HashSet<string> UnavailablePrimaryPaths =
            new(StringComparer.OrdinalIgnoreCase);

        private enum FileReadResult
        {
            Missing,
            Success,
            Corrupt,
            Unavailable
        }

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StopwatchOverlay",
            "settings.json");

        public static AppSettings Load() => Load(SettingsPath);

        public static AppSettings Load(string path)
        {
            FileReadResult primaryResult = TryLoadFile(
                path,
                out AppSettings? primary,
                out Exception? primaryError);
            if (primaryResult == FileReadResult.Success)
            {
                ClearUnavailable(path);
                return primary!;
            }

            string backupPath = path + ".bak";
            if (primaryError != null)
                CrashLogger.LogRecoverable(primaryError, "SettingsLoadPrimary");

            // A sharing violation, denied access, or other transient read problem
            // is not evidence of corrupt data. A backup may keep this run usable,
            // but never replace or later overwrite the potentially newer primary.
            if (primaryResult == FileReadResult.Unavailable)
            {
                ProtectUnavailable(path);
                if (TryLoadFile(backupPath, out AppSettings? unavailableBackup, out Exception? backupReadError)
                    == FileReadResult.Success)
                {
                    return unavailableBackup!;
                }

                if (backupReadError != null)
                    CrashLogger.LogRecoverable(backupReadError, "SettingsLoadBackup");
                return CreateDefaults();
            }

            if (primaryResult == FileReadResult.Corrupt)
                PreserveUnreadablePrimary(path);

            FileReadResult backupResult = TryLoadFile(
                backupPath,
                out AppSettings? backup,
                out Exception? backupError);
            if (backupResult == FileReadResult.Success)
            {
                try
                {
                    File.Copy(backupPath, path, overwrite: true);
                }
                catch (Exception exception) when (exception is
                    IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    CrashLogger.LogRecoverable(exception, "SettingsPrimaryRepair");
                }
                ClearUnavailable(path);
                return backup!;
            }

            if (backupError != null)
                CrashLogger.LogRecoverable(backupError, "SettingsLoadBackup");
            if (backupResult == FileReadResult.Unavailable)
                ProtectUnavailable(path);

            return CreateDefaults();
        }

        private static AppSettings CreateDefaults()
        {
            var fresh = new AppSettings { Shortcuts = AppSettings.DefaultShortcuts() };
            fresh.NormalizeForRuntime();
            AppBackgroundCatalog.NormalizeSettings(fresh);
            return fresh;
        }

        private static FileReadResult TryLoadFile(
            string path,
            out AppSettings? settings,
            out Exception? error)
        {
            settings = null;
            error = null;
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is
                FileNotFoundException or DirectoryNotFoundException)
            {
                return FileReadResult.Missing;
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or NotSupportedException)
            {
                error = exception;
                return FileReadResult.Unavailable;
            }

            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings == null)
                    throw new JsonException("The settings document did not contain an object.");
                settings.EnsureAllActions();
                settings.NormalizeForRuntime();
                AppBackgroundCatalog.NormalizeSettings(settings);
                return FileReadResult.Success;
            }
            catch (Exception exception) when (exception is
                JsonException or NotSupportedException or ArgumentException)
            {
                error = exception;
                settings = null;
                return FileReadResult.Corrupt;
            }
        }

        private static void PreserveUnreadablePrimary(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                string preservedPath = path + ".corrupt-" +
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
                File.Copy(path, preservedPath, overwrite: false);
                RetainPreservedCorruptFiles(path, retainedCount: 3);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or NotSupportedException)
            {
                CrashLogger.LogRecoverable(exception, "SettingsCorruptPreservation");
            }
        }

        private static void RetainPreservedCorruptFiles(string path, int retainedCount)
        {
            string? directory = Path.GetDirectoryName(path);
            string leafName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(leafName))
                return;

            foreach (FileInfo obsolete in new DirectoryInfo(directory)
                         .GetFiles(leafName + ".corrupt-*", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                         .Skip(Math.Max(1, retainedCount)))
            {
                try
                {
                    obsolete.Delete();
                }
                catch (Exception exception) when (exception is
                    IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    CrashLogger.LogRecoverable(exception, "SettingsCorruptRetention");
                }
            }
        }

        private static bool TryNormalizeStorePath(string path, out string normalizedPath)
        {
            try
            {
                normalizedPath = Path.GetFullPath(path);
                return true;
            }
            catch (Exception exception) when (exception is
                ArgumentException or NotSupportedException or PathTooLongException)
            {
                normalizedPath = string.Empty;
                return false;
            }
        }

        private static void ProtectUnavailable(string path)
        {
            if (!TryNormalizeStorePath(path, out string normalizedPath))
                return;
            lock (UnavailablePathsGate)
                UnavailablePrimaryPaths.Add(normalizedPath);
        }

        private static void ClearUnavailable(string path)
        {
            if (!TryNormalizeStorePath(path, out string normalizedPath))
                return;
            lock (UnavailablePathsGate)
                UnavailablePrimaryPaths.Remove(normalizedPath);
        }

        internal static bool IsWriteProtected(string path)
        {
            if (!TryNormalizeStorePath(path, out string normalizedPath))
                return false;
            lock (UnavailablePathsGate)
                return UnavailablePrimaryPaths.Contains(normalizedPath);
        }

        public static bool Save(AppSettings settings) => Save(settings, SettingsPath);

        public static bool Save(AppSettings settings, string path)
        {
            if (IsWriteProtected(path))
                return false;

            string? temporaryPath = null;
            bool saved = false;
            try
            {
                settings.EnsureAllActions();
                settings.NormalizeForRuntime();
                AppBackgroundCatalog.NormalizeSettings(settings);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                temporaryPath = path + ".tmp." + Guid.NewGuid().ToString("N");
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(json);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(
                            temporaryPath,
                            path,
                            path + ".bak",
                            ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Move(temporaryPath, path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                temporaryPath = null;
                saved = true;
                ClearUnavailable(path);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or NotSupportedException
                or ArgumentException or JsonException)
            {
                CrashLogger.LogRecoverable(exception, "SettingsSave");
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch { }
                }
            }

            return saved;
        }
    }
}
