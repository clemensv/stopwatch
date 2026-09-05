using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace StopwatchOverlay;

/// <summary>
/// A window/preview-local palette. Never writes Application.Resources, changes
/// native ThemeMode, persists settings, or subscribes to a global theme event.
/// </summary>
public static class OverlayThemeManager
{
    private sealed record AppliedPalette(string Theme, ResourceDictionary Resources);
    private static readonly ConditionalWeakTable<FrameworkElement, AppliedPalette> Applied = new();

    public static string Apply(FrameworkElement target, string? requestedTheme, string? applicationTheme)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Dispatcher.VerifyAccess();
        string theme = OverlayThemeCatalog.Resolve(requestedTheme, applicationTheme);
        if (Applied.TryGetValue(target, out AppliedPalette? previous)
            && previous.Theme == theme
            && target.Resources.MergedDictionaries.Contains(previous.Resources))
        {
            return theme;
        }

        // Resolve everything before swapping, so a malformed packaged resource
        // fails without leaving a partially installed palette. Do not hide it.
        ResourceDictionary next = LoadPalette(theme);
        if (previous != null)
            target.Resources.MergedDictionaries.Remove(previous.Resources);
        target.Resources.MergedDictionaries.Add(next);
        Applied.Remove(target);
        Applied.Add(target, new AppliedPalette(theme, next));
        return theme;
    }

    public static ResourceDictionary LoadPalette(string? resolvedTheme)
    {
        string theme = OverlayThemeCatalog.Normalize(resolvedTheme);
        string file = theme switch
        {
            OverlayThemeCatalog.Daylight => "DaylightOverlay.xaml",
            OverlayThemeCatalog.PixelDeckNight => "PixelDeckNightOverlay.xaml",
            OverlayThemeCatalog.PixelDeckDay => "PixelDeckDayOverlay.xaml",
            OverlayThemeCatalog.AcanthusLight => "AcanthusLightOverlay.xaml",
            OverlayThemeCatalog.AcanthusDarkElegantOlive => "AcanthusDarkElegantOliveOverlay.xaml",
            OverlayThemeCatalog.AcanthusDarkGoldCrest => "AcanthusDarkGoldCrestOverlay.xaml",
            OverlayThemeCatalog.AcanthusDarkMinimalBotanical => "AcanthusDarkMinimalBotanicalOverlay.xaml",
            _ => "MidnightOverlay.xaml"
        };
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/StopwatchOverlay;component/Themes/Overlay/{file}", UriKind.RelativeOrAbsolute)
        };
        var ornaments = new ResourceDictionary
        {
            Source = new Uri("/StopwatchOverlay;component/Themes/Overlay/OverlayOrnaments.xaml", UriKind.RelativeOrAbsolute)
        };
        bool light = theme == OverlayThemeCatalog.AcanthusLight;
        palette["OverlayCornerImage"] = ornaments[light ? "AcanthusCornerImage" : "OverlayOliveLeftImage"];
        palette["OverlayRightCornerImage"] = ornaments[light ? "AcanthusCornerImage" : "OverlayOliveRightImage"];
        palette["OverlayCrestImage"] = ornaments["OverlayGoldCrestImage"];
        palette["OverlayLeafImage"] = ornaments["OverlayBotanicalLeafImage"];
        return palette;
    }

    public static Color ResourceColor(FrameworkElement target, string key, Color fallback)
        => target.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

    public static FontFamily ResolveTimerFont(FrameworkElement target, string? effectiveTheme, string family)
        => OverlayThemeCatalog.IsAcanthus(effectiveTheme)
           && family.Equals("Cascadia Mono", StringComparison.OrdinalIgnoreCase)
           && target.TryFindResource("ThemeTimerFontFamily") is FontFamily bundled
            ? bundled
            : new FontFamily(family);
}
