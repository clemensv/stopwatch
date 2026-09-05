using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace StopwatchOverlay;

public static class AppThemeCatalog
{
    public const string Midnight = "Midnight";
    public const string Daylight = "Daylight";
    public const string PixelDeckNight = "Pixel Deck Night";
    public const string PixelDeckDay = "Pixel Deck Day";
    public const string Acanthus = "Acanthus";
    public const string PixelDeck = PixelDeckNight;

    public static IReadOnlyList<string> All { get; } =
        [Midnight, Daylight, PixelDeckNight, PixelDeckDay, Acanthus];

    public static string Normalize(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Equals(Acanthus, StringComparison.OrdinalIgnoreCase))
        {
            return Acanthus;
        }

        if (candidate.Equals(PixelDeckDay, StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("PixelDeckDay", StringComparison.OrdinalIgnoreCase))
        {
            return PixelDeckDay;
        }

        if (candidate.Equals(PixelDeckNight, StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("Pixel Deck", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("PixelDeck", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("PixelDeckNight", StringComparison.OrdinalIgnoreCase))
        {
            return PixelDeckNight;
        }

        if (candidate.Equals(Daylight, StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("Light", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("Light Mode", StringComparison.OrdinalIgnoreCase))
        {
            return Daylight;
        }

        // "Dark" was the only value written by older releases.
        return Midnight;
    }
}

public static class AppThemeManager
{
    private static string _currentTheme = AppThemeCatalog.Midnight;
    private static Application? _appliedApplication;

    public static string CurrentTheme => _currentTheme;
    public static bool IsPixelDeck => _currentTheme is
        AppThemeCatalog.PixelDeckNight or AppThemeCatalog.PixelDeckDay;
    public static bool IsPixelDeckDay => _currentTheme == AppThemeCatalog.PixelDeckDay;
    public static bool IsDaylight => _currentTheme == AppThemeCatalog.Daylight;
    public static bool IsAcanthus => _currentTheme == AppThemeCatalog.Acanthus;
    public static bool UsesThemedOverlayChrome => _currentTheme != AppThemeCatalog.Midnight;

    public static event EventHandler? ThemeChanged;

    public static void Apply(string? requestedTheme)
    {
        string theme = AppThemeCatalog.Normalize(requestedTheme);
        var application = Application.Current;
        if (application == null)
        {
            _currentTheme = theme;
            _appliedApplication = null;
            return;
        }

        // Reassigning Application.ThemeMode while a WPF Slider is routing input
        // can invalidate its ScrollBar/Thumb style tree. A same-theme request has
        // no work to do and must not re-enter that native theme machinery.
        if (ReferenceEquals(_appliedApplication, application)
            && _currentTheme == theme)
        {
            return;
        }

        ResourceDictionary palette;
        try
        {
            palette = LoadPalette(theme);
        }
        catch (Exception exception) when (exception is
            IOException or XamlParseException or UriFormatException)
        {
            CrashLogger.LogRecoverable(exception, "ThemeDictionaryLoad");
            if (theme == AppThemeCatalog.Midnight)
                throw;

            theme = AppThemeCatalog.Midnight;
            palette = LoadPalette(theme);
        }

        AppBackgroundManager.InvalidateThemeBase();

        // XAML palette references use DynamicResource, so replacing a frozen
        // resource invalidates every live visual. Preserve identity for mutable
        // brushes as well because a few code-created dashboard/overlay elements
        // intentionally hold the resolved brush instance between refreshes.
        foreach (object key in palette.Keys)
        {
            object next = palette[key];
            if (application.Resources[key] is SolidColorBrush currentBrush
                && next is SolidColorBrush nextBrush
                && !currentBrush.IsFrozen)
            {
                currentBrush.Color = nextBrush.Color;
                currentBrush.Opacity = nextBrush.Opacity;
            }
            else if (application.Resources[key] is DrawingBrush currentDrawing
                && next is DrawingBrush nextDrawing
                && !currentDrawing.IsFrozen)
            {
                currentDrawing.Drawing = nextDrawing.Drawing?.Clone();
                currentDrawing.TileMode = nextDrawing.TileMode;
                currentDrawing.ViewportUnits = nextDrawing.ViewportUnits;
                currentDrawing.Viewport = nextDrawing.Viewport;
                currentDrawing.ViewboxUnits = nextDrawing.ViewboxUnits;
                currentDrawing.Viewbox = nextDrawing.Viewbox;
                currentDrawing.Stretch = nextDrawing.Stretch;
                currentDrawing.AlignmentX = nextDrawing.AlignmentX;
                currentDrawing.AlignmentY = nextDrawing.AlignmentY;
                currentDrawing.Opacity = nextDrawing.Opacity;
            }
            else
            {
                application.Resources[key] = next is Freezable freezable
                    ? freezable.Clone()
                    : next;
            }
        }

        // Match native non-client controls and system scrollbars to the app theme.
        #pragma warning disable WPF0001
        ThemeMode desiredThemeMode = theme is AppThemeCatalog.Daylight
            or AppThemeCatalog.PixelDeckDay
            or AppThemeCatalog.Acanthus
            ? ThemeMode.Light
            : ThemeMode.Dark;
        if (application.ThemeMode != desiredThemeMode)
            application.ThemeMode = desiredThemeMode;
        #pragma warning restore WPF0001

        _currentTheme = theme;
        _appliedApplication = application;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static ResourceDictionary LoadPalette(string theme)
        => new()
        {
            Source = new Uri(
                theme switch
                {
                    AppThemeCatalog.PixelDeckNight =>
                        "/StopwatchOverlay;component/Themes/PixelDeck.xaml",
                    AppThemeCatalog.PixelDeckDay =>
                        "/StopwatchOverlay;component/Themes/PixelDeckDay.xaml",
                    AppThemeCatalog.Daylight =>
                        "/StopwatchOverlay;component/Themes/Daylight.xaml",
                    AppThemeCatalog.Acanthus =>
                        "/StopwatchOverlay;component/Themes/Acanthus.xaml",
                    _ => "/StopwatchOverlay;component/Themes/Midnight.xaml"
                },
                UriKind.RelativeOrAbsolute)
        };
}
