using System;
using System.Collections.Generic;

namespace StopwatchOverlay;

/// <summary>Persisted floating-clock choices, independent of the application palette.</summary>
public static class OverlayThemeCatalog
{
    public const string FollowApplicationTheme = "Follow Application Theme";
    public const string Midnight = AppThemeCatalog.Midnight;
    public const string Daylight = AppThemeCatalog.Daylight;
    public const string PixelDeckNight = AppThemeCatalog.PixelDeckNight;
    public const string PixelDeckDay = AppThemeCatalog.PixelDeckDay;
    public const string AcanthusLight = "Acanthus Light";
    public const string AcanthusDarkElegantOlive = "Acanthus Dark Elegant Olive";
    public const string AcanthusDarkGoldCrest = "Acanthus Dark Gold Crest";
    public const string AcanthusDarkMinimalBotanical = "Acanthus Dark Minimal Botanical";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        FollowApplicationTheme, Midnight, Daylight, PixelDeckNight, PixelDeckDay,
        AcanthusLight, AcanthusDarkElegantOlive, AcanthusDarkGoldCrest,
        AcanthusDarkMinimalBotanical
    });

    public static string Normalize(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        foreach (string choice in All)
        {
            if (candidate.Equals(choice, StringComparison.OrdinalIgnoreCase))
                return choice;
        }

        if (candidate.Equals(AppThemeCatalog.Acanthus, StringComparison.OrdinalIgnoreCase))
            return AcanthusLight;
        if (candidate.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            return Midnight;
        if (candidate.Equals("Light", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("Light Mode", StringComparison.OrdinalIgnoreCase))
            return Daylight;
        if (candidate.Equals("PixelDeck", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("Pixel Deck", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("PixelDeckNight", StringComparison.OrdinalIgnoreCase))
            return PixelDeckNight;
        if (candidate.Equals("PixelDeckDay", StringComparison.OrdinalIgnoreCase))
            return PixelDeckDay;

        // Missing values in old settings, and unknown future values, retain the
        // original behavior instead of guessing a new independent appearance.
        return FollowApplicationTheme;
    }

    public static string Resolve(string? overlayTheme, string? applicationTheme)
    {
        string requested = Normalize(overlayTheme);
        if (requested != FollowApplicationTheme)
            return requested;

        string panel = AppThemeCatalog.Normalize(applicationTheme);
        return panel == AppThemeCatalog.Acanthus ? AcanthusLight : panel;
    }

    public static bool IsAcanthus(string? theme)
        => Normalize(theme) is AcanthusLight
            or AcanthusDarkElegantOlive or AcanthusDarkGoldCrest
            or AcanthusDarkMinimalBotanical;

    public static bool IsDarkAcanthus(string? theme)
        => Normalize(theme) is AcanthusDarkElegantOlive
            or AcanthusDarkGoldCrest or AcanthusDarkMinimalBotanical;
}
