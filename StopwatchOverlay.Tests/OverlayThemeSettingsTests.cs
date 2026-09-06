using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace StopwatchOverlay.Tests;

public sealed class OverlayThemeSettingsTests
{
    public static IEnumerable<object[]> ThemeCombinations()
        => from panel in AppThemeCatalog.All
           from overlay in OverlayThemeCatalog.All
           select new object[] { panel, overlay };

    [Fact]
    public void Catalog_ContainsExactlyTheNineIndependentChoices()
    {
        Assert.Equal(new[]
        {
            "Follow Application Theme", "Midnight", "Daylight", "Pixel Deck Night",
            "Pixel Deck Day", "Acanthus Light", "Acanthus Dark Elegant Olive",
            "Acanthus Dark Gold Crest", "Acanthus Dark Minimal Botanical"
        }, OverlayThemeCatalog.All);
        Assert.Equal(9, OverlayThemeCatalog.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(OverlayThemeCatalog.FollowApplicationTheme, new AppSettings().OverlayTheme);
    }

    [Theory]
    [InlineData(null, "Follow Application Theme")]
    [InlineData("", "Follow Application Theme")]
    [InlineData("future theme", "Follow Application Theme")]
    [InlineData("  acanthus dark gold crest ", "Acanthus Dark Gold Crest")]
    [InlineData("Acanthus", "Acanthus Light")]
    [InlineData("Dark", "Midnight")]
    [InlineData("Light Mode", "Daylight")]
    [InlineData("PixelDeck", "Pixel Deck Night")]
    [InlineData("PixelDeckDay", "Pixel Deck Day")]
    public void Normalize_KeepsStableNamesAndSafelyHandlesMissingValues(string? value, string expected)
        => Assert.Equal(expected, OverlayThemeCatalog.Normalize(value));

    [Theory]
    [MemberData(nameof(ThemeCombinations))]
    public void Settings_AllFortyFiveCombinationsPersistIndependently(string panel, string overlay)
    {
        using var files = new SettingsFiles();
        var settings = CustomizedSettings(panel, overlay);
        Assert.True(SettingsStore.Save(settings, files.Path));
        AppSettings restarted = SettingsStore.Load(files.Path);

        Assert.Equal(panel, restarted.ThemeMode);
        Assert.Equal(panel, restarted.ApplicationTheme);
        Assert.Equal(overlay, restarted.OverlayTheme);
        Assert.Equal(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(restarted));
        Assert.Equal(overlay == OverlayThemeCatalog.FollowApplicationTheme
                ? panel == AppThemeCatalog.Acanthus ? OverlayThemeCatalog.AcanthusLight : panel
                : overlay,
            OverlayThemeCatalog.Resolve(restarted.OverlayTheme, restarted.ApplicationTheme));
    }

    [Theory]
    [InlineData("Midnight")]
    [InlineData("Daylight")]
    [InlineData("Pixel Deck Night")]
    [InlineData("Pixel Deck Day")]
    [InlineData("Acanthus")]
    public void LegacySettings_WithoutOverlayThemeMigrateWithoutResettingCustomValues(string panel)
    {
        using var files = new SettingsFiles();
        var original = CustomizedSettings(panel, OverlayThemeCatalog.FollowApplicationTheme);
        original.EnsureAllActions();
        original.NormalizeForRuntime();
        AppBackgroundCatalog.NormalizeSettings(original);
        var legacy = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(original))!;
        Assert.True(legacy.Remove(nameof(AppSettings.OverlayTheme)));
        File.WriteAllText(files.Path, JsonSerializer.Serialize(legacy));

        AppSettings migrated = SettingsStore.Load(files.Path);
        Assert.Equal(OverlayThemeCatalog.FollowApplicationTheme, migrated.OverlayTheme);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(migrated));
        Assert.True(SettingsStore.Save(migrated, files.Path));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(SettingsStore.Load(files.Path)));
    }

    [Fact]
    public void ApplicationThemeAlias_DoesNotCreateADuplicateJsonPreference()
    {
        var settings = new AppSettings { ApplicationTheme = AppThemeCatalog.Daylight };
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        Assert.Equal(AppThemeCatalog.Daylight, settings.ThemeMode);
        Assert.Equal(AppThemeCatalog.Daylight, json.RootElement.GetProperty("ThemeMode").GetString());
        Assert.False(json.RootElement.TryGetProperty("ApplicationTheme", out _));
    }

    [Theory]
    [MemberData(nameof(ThemeCombinations))]
    public void SwitchingPanelAndOverlay_NeverOverwritesTheOtherSelection(string panel, string overlay)
    {
        var settings = CustomizedSettings(panel, overlay);
        foreach (string nextPanel in AppThemeCatalog.All)
        {
            settings.ApplicationTheme = nextPanel;
            settings.NormalizeForRuntime();
            Assert.Equal(overlay, settings.OverlayTheme);
            string resolved = OverlayThemeCatalog.Resolve(settings.OverlayTheme, settings.ApplicationTheme);
            Assert.Equal(overlay == OverlayThemeCatalog.FollowApplicationTheme
                    ? nextPanel == AppThemeCatalog.Acanthus ? OverlayThemeCatalog.AcanthusLight : nextPanel
                    : overlay,
                resolved);
        }

        settings.ApplicationTheme = panel;
        foreach (string nextOverlay in OverlayThemeCatalog.All)
        {
            settings.OverlayTheme = nextOverlay;
            settings.NormalizeForRuntime();
            Assert.Equal(panel, settings.ThemeMode);
            Assert.Equal(nextOverlay, settings.OverlayTheme);
        }
    }

    [Fact]
    public void OverlayThemeChange_DoesNotApplyPanelThemeBackgroundOrLightRing()
    {
        Assert.False(SettingsChangePolicy.RequiresThemeApply(SettingsChangeKind.OverlayTheme));
        Assert.False(SettingsChangePolicy.RequiresBackgroundApply(SettingsChangeKind.OverlayTheme));
        Assert.False(SettingsChangePolicy.RequiresLightRingRebuild(SettingsChangeKind.OverlayTheme));
        Assert.False(SettingsChangePolicy.IsContinuous(SettingsChangeKind.OverlayTheme));
        Assert.True(SettingsChangePolicy.RequiresThemeApply(
            SettingsChangeKind.Theme | SettingsChangeKind.OverlayTheme));
    }

    [Fact]
    public void ThemeTextColors_AreExplicitOptInAndNeverReplaceExistingColors()
    {
        Assert.Equal("White", new AppSettings().TextColor);
        foreach (string color in new[] { "Theme default", "White", "Charcoal", "Yellow", "Cyan", "Lime", "Orange", "Red", "Magenta" })
        {
            var settings = new AppSettings
            {
                TextColor = color,
                OverlayTheme = OverlayThemeCatalog.AcanthusDarkGoldCrest
            };
            settings.NormalizeForRuntime();
            Assert.Equal(color, settings.TextColor);
        }
    }

    private static AppSettings CustomizedSettings(string panel, string overlay) => new()
    {
        ThemeMode = panel,
        OverlayTheme = overlay,
        TextColor = "Cyan",
        BorderColor = "Blue",
        FontFamily = "Courier New",
        TextSize = 77,
        BorderWidth = 4,
        BackgroundOpacity = 23,
        TimeFormat = 3,
        Position = "Custom",
        ScreenIndex = 2,
        HasCustomPosition = true,
        CustomLeft = -1420.25,
        CustomTop = 53.5,
        ClickThrough = true,
        HideOverlayFromCapture = true,
        LightRingEnabled = true,
        LightRingBrightness = 37,
        LightRingWidth = 43,
        LightRingHideFromCapture = true,
        ShowRecIndicator = true,
        AutoStart = true,
        BlinkColon = true,
        UseSmartCountdownInput = true,
        StartWithWindows = true,
        Mode = 3,
        PanelBackgroundStrength = 31,
        Shortcuts = new Dictionary<ShortcutAction, Shortcut>
        {
            [ShortcutAction.StartStop] = new Shortcut(Shortcut.MOD_CONTROL | Shortcut.MOD_SHIFT, 0x51)
        }
    };

    private sealed class SettingsFiles : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "StopwatchOverlay-OverlayThemeTests-" + Guid.NewGuid().ToString("N"));

        internal string Path => System.IO.Path.Combine(_directory, "settings.json");

        internal SettingsFiles() => Directory.CreateDirectory(_directory);

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
