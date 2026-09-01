using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace StopwatchOverlay;

public static class AppThemeCatalog
{
    public const string Midnight = "Midnight";
    public const string Daylight = "Daylight";
    public const string PixelDeckNight = "Pixel Deck Night";
    public const string PixelDeckDay = "Pixel Deck Day";
    public const string PixelDeck = PixelDeckNight;

    public static IReadOnlyList<string> All { get; } =
        [Midnight, Daylight, PixelDeckNight, PixelDeckDay];

    public static string Normalize(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
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

    public static string CurrentTheme => _currentTheme;
    public static bool IsPixelDeck => _currentTheme is
        AppThemeCatalog.PixelDeckNight or AppThemeCatalog.PixelDeckDay;
    public static bool IsPixelDeckDay => _currentTheme == AppThemeCatalog.PixelDeckDay;
    public static bool IsDaylight => _currentTheme == AppThemeCatalog.Daylight;
    public static bool UsesThemedOverlayChrome => _currentTheme != AppThemeCatalog.Midnight;

    public static event EventHandler? ThemeChanged;

    public static void Apply(string? requestedTheme)
    {
        string theme = AppThemeCatalog.Normalize(requestedTheme);
        var application = Application.Current;
        if (application == null)
        {
            _currentTheme = theme;
            return;
        }

        var palette = new ResourceDictionary
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
                    _ => "/StopwatchOverlay;component/Themes/Midnight.xaml"
                },
                UriKind.RelativeOrAbsolute)
        };

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

        _currentTheme = theme;

        // Match native non-client controls and system scrollbars to the app theme.
        #pragma warning disable WPF0001
        application.ThemeMode = theme is AppThemeCatalog.Daylight or AppThemeCatalog.PixelDeckDay
            ? ThemeMode.Light
            : ThemeMode.Dark;
        #pragma warning restore WPF0001

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
