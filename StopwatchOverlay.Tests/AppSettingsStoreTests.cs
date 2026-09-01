using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using StopwatchOverlay;
using Xunit;

namespace StopwatchOverlay.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void DefaultTheme_IsMidnight()
    {
        Assert.Equal(AppThemeCatalog.Midnight, new AppSettings().ThemeMode);
    }

    [Theory]
    [InlineData(null, AppThemeCatalog.Midnight)]
    [InlineData("", AppThemeCatalog.Midnight)]
    [InlineData("Dark", AppThemeCatalog.Midnight)]
    [InlineData("unknown", AppThemeCatalog.Midnight)]
    [InlineData("Midnight", AppThemeCatalog.Midnight)]
    [InlineData("Daylight", AppThemeCatalog.Daylight)]
    [InlineData("light", AppThemeCatalog.Daylight)]
    [InlineData("Light Mode", AppThemeCatalog.Daylight)]
    [InlineData("PixelDeck", AppThemeCatalog.PixelDeckNight)]
    [InlineData("Pixel Deck", AppThemeCatalog.PixelDeckNight)]
    [InlineData("PixelDeckNight", AppThemeCatalog.PixelDeckNight)]
    [InlineData("Pixel Deck Night", AppThemeCatalog.PixelDeckNight)]
    [InlineData("PixelDeckDay", AppThemeCatalog.PixelDeckDay)]
    [InlineData("Pixel Deck Day", AppThemeCatalog.PixelDeckDay)]
    public void NormalizeTheme_MigratesLegacyAndRejectsUnknownValues(
        string? value,
        string expected)
    {
        Assert.Equal(expected, AppThemeCatalog.Normalize(value));
    }

    [Fact]
    public void ThemeCatalog_ExposesAllFourStableChoicesInDisplayOrder()
    {
        Assert.Equal(
            [
                AppThemeCatalog.Midnight,
                AppThemeCatalog.Daylight,
                AppThemeCatalog.PixelDeckNight,
                AppThemeCatalog.PixelDeckDay
            ],
            AppThemeCatalog.All);
    }

    [Theory]
    [InlineData(AppThemeCatalog.Midnight)]
    [InlineData(AppThemeCatalog.Daylight)]
    [InlineData(AppThemeCatalog.PixelDeckNight)]
    [InlineData(AppThemeCatalog.PixelDeckDay)]
    public void SaveThenLoad_ThemeRoundTripsAcrossRestart(string theme)
    {
        using var scope = new TemporarySettingsFile();
        var settings = NewSettings(theme);

        Assert.True(SettingsStore.Save(settings, scope.Path));

        var restarted = SettingsStore.Load(scope.Path);
        Assert.Equal(theme, restarted.ThemeMode);
    }

    [Fact]
    public void SecondSave_AtomicallyReplacesPixelDeckNightWithPixelDeckDay()
    {
        using var scope = new TemporarySettingsFile();
        Assert.True(SettingsStore.Save(NewSettings(AppThemeCatalog.PixelDeckNight), scope.Path));
        Assert.True(SettingsStore.Save(NewSettings(AppThemeCatalog.PixelDeckDay), scope.Path));

        Assert.Equal(
            AppThemeCatalog.PixelDeckDay,
            SettingsStore.Load(scope.Path).ThemeMode);
    }

    [Fact]
    public void LegacyDarkValue_LoadsAsMidnight()
    {
        using var scope = new TemporarySettingsFile();
        var legacy = NewSettings("Dark");
        File.WriteAllText(scope.Path, JsonSerializer.Serialize(legacy));

        Assert.Equal(
            AppThemeCatalog.Midnight,
            SettingsStore.Load(scope.Path).ThemeMode);
    }

    [Fact]
    public void InterruptedTemporaryWrite_DoesNotReplaceLastValidTheme()
    {
        using var scope = new TemporarySettingsFile();
        Assert.True(SettingsStore.Save(NewSettings(AppThemeCatalog.Midnight), scope.Path));
        File.WriteAllText(scope.Path + ".tmp.interrupted", "{\"ThemeMode\":");

        Assert.Equal(
            AppThemeCatalog.Midnight,
            SettingsStore.Load(scope.Path).ThemeMode);
    }

    [Fact]
    public void FailedAtomicReplace_PreservesPreviousThemeAndCleansTemporaryFile()
    {
        using var scope = new TemporarySettingsFile();
        Assert.True(SettingsStore.Save(NewSettings(AppThemeCatalog.Midnight), scope.Path));

        using (var locked = new FileStream(
            scope.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
        {
            Assert.False(SettingsStore.Save(NewSettings(AppThemeCatalog.PixelDeck), scope.Path));
        }

        Assert.Equal(
            AppThemeCatalog.Midnight,
            SettingsStore.Load(scope.Path).ThemeMode);
        Assert.Empty(Directory.GetFiles(scope.Directory, "settings.json.tmp.*"));
    }

    [Fact]
    public void CorruptPrimary_FallsBackToSafeMidnightTheme()
    {
        using var scope = new TemporarySettingsFile();
        File.WriteAllText(scope.Path, "not json");

        var settings = SettingsStore.Load(scope.Path);

        Assert.Equal(AppThemeCatalog.Midnight, settings.ThemeMode);
        Assert.NotEmpty(settings.Shortcuts);
    }

    [Fact]
    public void ThemeDictionaries_LoadAndExposeTheSameTokenContract()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var midnight = (ResourceDictionary)Application.LoadComponent(
                    new Uri(
                        "/StopwatchOverlay;component/Themes/Midnight.xaml",
                        UriKind.Relative));
                var pixelDeck = (ResourceDictionary)Application.LoadComponent(
                    new Uri(
                        "/StopwatchOverlay;component/Themes/PixelDeck.xaml",
                        UriKind.Relative));
                var daylight = (ResourceDictionary)Application.LoadComponent(
                    new Uri(
                        "/StopwatchOverlay;component/Themes/Daylight.xaml",
                        UriKind.Relative));
                var pixelDeckDay = (ResourceDictionary)Application.LoadComponent(
                    new Uri(
                        "/StopwatchOverlay;component/Themes/PixelDeckDay.xaml",
                        UriKind.Relative));

                foreach (ResourceDictionary candidate in new[]
                         {
                             daylight,
                             pixelDeck,
                             pixelDeckDay
                         })
                {
                    object[] expectedKeys = midnight.Keys.Cast<object>()
                        .OrderBy(key => key.ToString(), StringComparer.Ordinal)
                        .ToArray();
                    object[] actualKeys = candidate.Keys.Cast<object>()
                        .OrderBy(key => key.ToString(), StringComparer.Ordinal)
                        .ToArray();
                    Assert.Equal(expectedKeys, actualKeys);

                    foreach (object key in expectedKeys)
                    {
                        Assert.Equal(midnight[key].GetType(), candidate[key].GetType());
                    }
                }

                Assert.Equal(
                    Color.FromRgb(23, 27, 32),
                    ((SolidColorBrush)midnight["SurfaceBrush"]).Color);
                Assert.Equal(
                    Color.FromRgb(35, 35, 52),
                    ((SolidColorBrush)pixelDeck["SurfaceBrush"]).Color);
                Assert.Equal(
                    Color.FromRgb(255, 255, 255),
                    ((SolidColorBrush)daylight["SurfaceBrush"]).Color);
                Assert.Equal(
                    Color.FromRgb(23, 33, 41),
                    ((SolidColorBrush)daylight["PrimaryTextBrush"]).Color);
                Assert.Equal(
                    Color.FromRgb(255, 247, 223),
                    ((SolidColorBrush)pixelDeckDay["SurfaceBrush"]).Color);
                Assert.Equal(
                    Color.FromRgb(29, 38, 48),
                    ((SolidColorBrush)pixelDeckDay["PrimaryTextBrush"]).Color);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void ApplyTheme_UpdatesControlsUsingAnAlreadySealedApplicationStyle()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? application = null;
            Window? hostWindow = null;
            try
            {
                application = new Application();
                AppThemeManager.Apply(AppThemeCatalog.Midnight);

                var styles = (ResourceDictionary)XamlReader.Parse(
                    """
                    <ResourceDictionary
                        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="LiveThemeButtonStyle" TargetType="{x:Type Button}">
                            <Setter Property="Background" Value="{DynamicResource SurfaceRaisedBrush}"/>
                            <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
                        </Style>
                    </ResourceDictionary>
                    """);
                application.Resources["LiveThemeButtonStyle"] =
                    styles["LiveThemeButtonStyle"];

                var button = new Button
                {
                    Style = (Style)application.Resources["LiveThemeButtonStyle"]
                };
                hostWindow = new Window
                {
                    Content = button,
                    Width = 240,
                    Height = 80,
                    Left = -10000,
                    Top = -10000,
                    Opacity = 0,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None
                };
                hostWindow.Show();
                button.ApplyTemplate();

                Style originalStyle = button.Style;
                Assert.True(originalStyle.IsSealed);
                Assert.Equal(
                    Color.FromRgb(30, 36, 42),
                    ((SolidColorBrush)button.Background).Color);

                AppThemeManager.Apply(AppThemeCatalog.PixelDeckDay);
                button.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.DataBind,
                    new Action(() => { }));

                Assert.Same(originalStyle, button.Style);
                Assert.Equal(
                    Color.FromRgb(255, 253, 245),
                    ((SolidColorBrush)button.Background).Color);
                Assert.Equal(
                    Color.FromRgb(29, 38, 48),
                    ((SolidColorBrush)button.Foreground).Color);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                hostWindow?.Close();
                if (application != null)
                {
                    AppThemeManager.Apply(AppThemeCatalog.Midnight);
                    application.Shutdown();
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static AppSettings NewSettings(string theme) => new()
    {
        ThemeMode = theme,
        Shortcuts = AppSettings.DefaultShortcuts()
    };

    private sealed class TemporarySettingsFile : IDisposable
    {
        public TemporarySettingsFile()
        {
            Directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "StopwatchOverlay.Tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Path = System.IO.Path.Combine(Directory, "settings.json");
        }

        public string Directory { get; }
        public string Path { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // A failed cleanup must not hide the behavior under test.
            }
        }
    }
}
