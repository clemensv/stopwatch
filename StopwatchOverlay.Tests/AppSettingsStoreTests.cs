using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    [Fact]
    public void BackgroundDefaults_AreThemeDefaultWithReadableStrength()
    {
        var settings = new AppSettings();

        Assert.Equal(AppBackgroundCatalog.ThemeDefault, settings.PanelBackgroundId);
        Assert.Equal(
            AppBackgroundCatalog.DefaultPatternStrength,
            settings.PanelBackgroundStrength);
        Assert.Empty(settings.CustomBackgrounds);
    }

    [Theory]
    [InlineData("Theme default", AppBackgroundCatalog.ThemeDefault)]
    [InlineData("Festive Chalk", AppBackgroundCatalog.FestiveChalk)]
    [InlineData("Sapphire Garden", AppBackgroundCatalog.SapphireGarden)]
    [InlineData("preset:cosmic-doodles", AppBackgroundCatalog.ThemeDefault)]
    [InlineData("unknown", AppBackgroundCatalog.ThemeDefault)]
    public void NormalizeBackground_MigratesLabelsAndRejectsUnknownValues(
        string requested,
        string expected)
    {
        var settings = new AppSettings { PanelBackgroundId = requested };

        AppBackgroundCatalog.NormalizeSettings(settings);

        Assert.Equal(expected, settings.PanelBackgroundId);
    }

    [Fact]
    public void BackgroundCatalog_ExposesThemeDefaultAndNineStablePresets()
    {
        Assert.Equal(
            [
                AppBackgroundCatalog.ThemeDefault,
                AppBackgroundCatalog.FestiveChalk,
                AppBackgroundCatalog.WoodlandMushrooms,
                AppBackgroundCatalog.AutumnPatchwork,
                AppBackgroundCatalog.GreenCreatures,
                AppBackgroundCatalog.AquaTattoo,
                AppBackgroundCatalog.SapphireGarden,
                AppBackgroundCatalog.TurquoisePomegranate,
                AppBackgroundCatalog.MidnightPaisley,
                AppBackgroundCatalog.AzureMosaic
            ],
            AppBackgroundCatalog.BuiltInIds);
    }

    [Fact]
    public void SaveThenLoad_BackgroundAndCustomCatalogRoundTripAcrossRestart()
    {
        using var scope = new TemporarySettingsFile();
        string id = Guid.NewGuid().ToString("N");
        var settings = NewSettings(AppThemeCatalog.PixelDeckDay);
        settings.PanelBackgroundId = AppBackgroundCatalog.CustomSelectionId(id);
        settings.PanelBackgroundStrength = 41;
        settings.CustomBackgrounds.Add(new CustomAppBackground
        {
            Id = id,
            DisplayName = "My pattern",
            FileName = $"custom-{id}.png"
        });

        Assert.True(SettingsStore.Save(settings, scope.Path));

        AppSettings restarted = SettingsStore.Load(scope.Path);
        Assert.Equal(AppBackgroundCatalog.CustomSelectionId(id), restarted.PanelBackgroundId);
        Assert.Equal(41, restarted.PanelBackgroundStrength);
        CustomAppBackground custom = Assert.Single(restarted.CustomBackgrounds);
        Assert.Equal("My pattern", custom.DisplayName);
        Assert.Equal($"custom-{id}.png", custom.FileName);
    }

    [Fact]
    public void NormalizeBackground_ClampsStrengthAndRejectsUnsafeCustomMetadata()
    {
        string validId = Guid.NewGuid().ToString("N");
        string otherId = Guid.NewGuid().ToString("N");
        var settings = new AppSettings
        {
            PanelBackgroundId = AppBackgroundCatalog.CustomSelectionId(validId),
            PanelBackgroundStrength = double.NaN,
            CustomBackgrounds =
            [
                new CustomAppBackground
                {
                    Id = validId,
                    DisplayName = "  Safe\0 name  ",
                    FileName = $"custom-{validId}.jpg"
                },
                new CustomAppBackground
                {
                    Id = validId,
                    DisplayName = "Duplicate",
                    FileName = $"custom-{validId}.png"
                },
                new CustomAppBackground
                {
                    Id = otherId,
                    DisplayName = "Traversal",
                    FileName = "..\\outside.png"
                }
            ]
        };

        AppBackgroundCatalog.NormalizeSettings(settings);

        CustomAppBackground custom = Assert.Single(settings.CustomBackgrounds);
        Assert.Equal(validId, custom.Id);
        Assert.Equal("Safe name", custom.DisplayName);
        Assert.Equal(
            AppBackgroundCatalog.DefaultPatternStrength,
            settings.PanelBackgroundStrength);
        Assert.Equal(
            AppBackgroundCatalog.CustomSelectionId(validId),
            settings.PanelBackgroundId);
    }

    [Theory]
    [InlineData(-100, AppBackgroundCatalog.MinimumPatternStrength)]
    [InlineData(100, AppBackgroundCatalog.MaximumPatternStrength)]
    public void NormalizeBackground_ClampsFiniteStrength(double value, double expected)
    {
        var settings = new AppSettings { PanelBackgroundStrength = value };

        AppBackgroundCatalog.NormalizeSettings(settings);

        Assert.Equal(expected, settings.PanelBackgroundStrength);
    }

    [Fact]
    public void MissingManagedBackground_ResolvesAndRepairsToThemeDefault()
    {
        using var scope = new TemporarySettingsFile();
        string id = Guid.NewGuid().ToString("N");
        var settings = new AppSettings
        {
            PanelBackgroundId = AppBackgroundCatalog.CustomSelectionId(id),
            CustomBackgrounds =
            [
                new CustomAppBackground
                {
                    Id = id,
                    DisplayName = "Missing",
                    FileName = $"custom-{id}.jpg"
                }
            ]
        };

        AppBackgroundChoice unavailable = Assert.Single(
            AppBackgroundCatalog.GetAvailableChoices(settings, scope.Directory),
            choice => choice.IsCustom);
        Assert.Equal(AppBackgroundCatalog.CustomSelectionId(id), unavailable.Id);
        Assert.False(unavailable.IsAvailable);
        Assert.Contains("unavailable", unavailable.DisplayLabel, StringComparison.OrdinalIgnoreCase);

        AppBackgroundChoice resolved = AppBackgroundCatalog.ResolveChoice(
            settings,
            scope.Directory);

        Assert.True(resolved.IsThemeDefault);
        Assert.Equal(AppBackgroundCatalog.ThemeDefault, settings.PanelBackgroundId);
        Assert.Single(settings.CustomBackgrounds);
        Assert.True(AppBackgroundCatalog.DeleteManagedCopy(
            settings.CustomBackgrounds[0],
            scope.Directory));
    }

    [Fact]
    public void CorruptManagedBackground_RemainsVisibleAndCanBeRemoved()
    {
        using var scope = new TemporarySettingsFile();
        string id = Guid.NewGuid().ToString("N");
        string fileName = $"custom-{id}.jpg";
        string path = Path.Combine(scope.Directory, fileName);
        File.WriteAllBytes(path, [0x4E, 0x4F, 0x54, 0x2D, 0x41, 0x4E, 0x2D, 0x49, 0x4D, 0x41, 0x47, 0x45]);
        var settings = new AppSettings
        {
            PanelBackgroundId = AppBackgroundCatalog.CustomSelectionId(id),
            CustomBackgrounds =
            [
                new CustomAppBackground
                {
                    Id = id,
                    DisplayName = "Damaged pattern",
                    FileName = fileName
                }
            ]
        };

        AppBackgroundChoice unavailable = Assert.Single(
            AppBackgroundCatalog.GetAvailableChoices(settings, scope.Directory),
            choice => choice.IsCustom);
        Assert.False(unavailable.IsAvailable);
        Assert.True(AppBackgroundCatalog.ResolveChoice(settings, scope.Directory).IsThemeDefault);
        Assert.Single(settings.CustomBackgrounds);

        Assert.True(AppBackgroundCatalog.DeleteManagedCopy(
            settings.CustomBackgrounds[0],
            scope.Directory));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RemovingUnavailableBackground_FreesCustomLibraryCapacity()
    {
        using var scope = new TemporarySettingsFile();
        var settings = new AppSettings();
        for (int index = 0; index < 64; index++)
        {
            string id = Guid.NewGuid().ToString("N");
            settings.CustomBackgrounds.Add(new CustomAppBackground
            {
                Id = id,
                DisplayName = $"Missing {index + 1}",
                FileName = $"custom-{id}.jpg"
            });
        }

        string source = Path.Combine(scope.Directory, "source.jpg");
        string managed = Path.Combine(scope.Directory, "managed");
        RunSta(() => WriteTestBitmap(source, jpeg: true));

        Assert.Equal(
            64,
            AppBackgroundCatalog.GetAvailableChoices(settings, managed)
                .Count(choice => choice.IsCustom && !choice.IsAvailable));
        Assert.False(AppBackgroundCatalog.TryImport(
            source,
            settings.CustomBackgrounds,
            out _,
            out string? fullError,
            managed));
        Assert.Contains("up to 64", fullError, StringComparison.OrdinalIgnoreCase);

        CustomAppBackground removed = settings.CustomBackgrounds[0];
        settings.CustomBackgrounds.RemoveAt(0);
        Assert.True(AppBackgroundCatalog.DeleteManagedCopy(removed, managed));
        Assert.True(AppBackgroundCatalog.TryImport(
            source,
            settings.CustomBackgrounds,
            out CustomAppBackground? imported,
            out string? error,
            managed), error);
        Assert.NotNull(imported);
        settings.CustomBackgrounds.Add(imported!);
        Assert.Equal(64, settings.CustomBackgrounds.Count);
        Assert.True(AppBackgroundCatalog.DeleteManagedCopy(imported!, managed));
    }

    [Fact]
    public void DeleteManagedCopy_RejectsTraversalMetadata()
    {
        using var scope = new TemporarySettingsFile();
        string sentinel = Path.Combine(scope.Directory, "sentinel.jpg");
        File.WriteAllBytes(sentinel, [1, 2, 3, 4]);
        var malicious = new CustomAppBackground
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = "Unsafe",
            FileName = "..\\sentinel.jpg"
        };

        Assert.False(AppBackgroundCatalog.DeleteManagedCopy(
            malicious,
            Path.Combine(scope.Directory, "managed")));
        Assert.True(File.Exists(sentinel));
    }

    [Fact]
    public void ImportBackground_CopiesManagedImageAndDoesNotLockEitherFile()
    {
        using var scope = new TemporarySettingsFile();
        string source = Path.Combine(scope.Directory, "source.jpg");
        string managed = Path.Combine(scope.Directory, "managed");

        RunSta(() => WriteTestBitmap(source, jpeg: true));

        Assert.True(AppBackgroundCatalog.TryImport(
            source,
            [],
            out CustomAppBackground? imported,
            out string? error,
            managed), error);
        Assert.NotNull(imported);

        string managedPath = Path.Combine(managed, imported!.FileName);
        Assert.True(File.Exists(managedPath));
        File.Delete(source);

        var settings = new AppSettings
        {
            PanelBackgroundId = AppBackgroundCatalog.CustomSelectionId(imported.Id),
            CustomBackgrounds = [imported]
        };
        AppBackgroundChoice choice = AppBackgroundCatalog.ResolveChoice(settings, managed);
        Assert.True(choice.IsCustom);

        RunSta(() => _ = AppBackgroundManager.CreatePreviewBrush(
            choice,
            AppBackgroundCatalog.DefaultPatternStrength));
        using (var exclusive = new FileStream(
                   managedPath,
                   FileMode.Open,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            Assert.True(exclusive.Length > 0);
        }
        Assert.Empty(Directory.GetFiles(managed, "*.tmp"));
        Assert.True(AppBackgroundCatalog.DeleteManagedCopy(imported, managed));
        Assert.False(File.Exists(managedPath));
    }

    [Fact]
    public void ImportedBackground_LoadsAfterRestartWhenOriginalWasDeleted()
    {
        using var scope = new TemporarySettingsFile();
        string source = Path.Combine(scope.Directory, "restart-source.png");
        string managed = Path.Combine(scope.Directory, "managed");
        RunSta(() => WriteTestBitmap(source, jpeg: false));

        Assert.True(AppBackgroundCatalog.TryImport(
            source,
            [],
            out CustomAppBackground? imported,
            out string? importError,
            managed), importError);
        Assert.NotNull(imported);

        var settings = NewSettings(AppThemeCatalog.PixelDeckDay);
        settings.PanelBackgroundId = AppBackgroundCatalog.CustomSelectionId(imported!.Id);
        settings.PanelBackgroundStrength = 37;
        settings.CustomBackgrounds.Add(imported);
        Assert.True(SettingsStore.Save(settings, scope.Path));
        File.Delete(source);

        AppSettings restarted = SettingsStore.Load(scope.Path);
        AppBackgroundChoice choice = AppBackgroundCatalog.ResolveChoice(restarted, managed);
        Assert.True(choice.IsCustom);
        Assert.True(choice.IsAvailable);
        Assert.Equal(37, restarted.PanelBackgroundStrength);
        RunSta(() =>
        {
            DrawingBrush preview = Assert.IsType<DrawingBrush>(
                AppBackgroundManager.CreatePreviewBrush(
                    choice,
                    restarted.PanelBackgroundStrength));
            Assert.Equal(TileMode.Tile, preview.TileMode);
        });

        string managedPath = Path.Combine(managed, imported.FileName);
        using (var exclusive = new FileStream(
                   managedPath,
                   FileMode.Open,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            Assert.True(exclusive.Length > 0);
        }

        AppBackgroundManager.ClearImageCache();
        Assert.True(AppBackgroundCatalog.DeleteManagedCopy(imported, managed));
    }

    [Fact]
    public void ImportBackground_RejectsImageWhoseContentsDoNotMatchExtension()
    {
        using var scope = new TemporarySettingsFile();
        string source = Path.Combine(scope.Directory, "renamed.jpg");
        string managed = Path.Combine(scope.Directory, "managed");
        RunSta(() => WriteTestBitmap(source, jpeg: false));

        Assert.False(AppBackgroundCatalog.TryImport(
            source,
            [],
            out CustomAppBackground? imported,
            out string? error,
            managed));

        Assert.Null(imported);
        Assert.NotNull(error);
        Assert.False(Directory.Exists(managed)
            && Directory.EnumerateFiles(managed).Any());
    }

    [Fact]
    public void ImportBackground_DisambiguatesNamesFromBuiltInPresets()
    {
        using var scope = new TemporarySettingsFile();
        string source = Path.Combine(scope.Directory, "Festive Chalk.jpg");
        string managed = Path.Combine(scope.Directory, "managed");
        RunSta(() => WriteTestBitmap(source, jpeg: true));

        Assert.True(AppBackgroundCatalog.TryImport(
            source,
            [],
            out CustomAppBackground? imported,
            out string? error,
            managed), error);

        Assert.NotNull(imported);
        Assert.Equal("Festive Chalk (2)", imported!.DisplayName);
        Assert.True(AppBackgroundCatalog.DeleteManagedCopy(imported, managed));
    }

    [Fact]
    public void BuiltInBackgroundResources_LoadAsValidBitmaps()
    {
        RunSta(() =>
        {
            var settings = NewSettings(AppThemeCatalog.Midnight);
            IReadOnlyList<AppBackgroundChoice> choices =
                AppBackgroundCatalog.GetAvailableChoices(settings);

            foreach (AppBackgroundChoice choice in choices.Where(item => !item.IsThemeDefault))
            {
                Assert.False(string.IsNullOrWhiteSpace(choice.ResourceUri));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(choice.ResourceUri!, UriKind.Absolute);
                bitmap.EndInit();
                Assert.True(bitmap.PixelWidth > 0);
                Assert.True(bitmap.PixelHeight > 0);
            }
        });
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

                var backgroundSettings = NewSettings(AppThemeCatalog.PixelDeckDay);
                backgroundSettings.PanelBackgroundId = AppBackgroundCatalog.GreenCreatures;
                Assert.True(
                    AppBackgroundManager.Apply(backgroundSettings, out string? warning),
                    warning);
                var pixelBrush = Assert.IsType<DrawingBrush>(
                    application.Resources["AppBackgroundBrush"]);
                Assert.Equal(TileMode.Tile, pixelBrush.TileMode);
                Assert.Equal(BrushMappingMode.Absolute, pixelBrush.ViewportUnits);
                Assert.True(pixelBrush.Viewport.Width > 0);
                Assert.True(pixelBrush.Viewport.Height > 0);

                backgroundSettings.ThemeMode = AppThemeCatalog.Daylight;
                AppThemeManager.Apply(backgroundSettings.ThemeMode);
                Assert.True(AppBackgroundManager.Apply(backgroundSettings, out warning), warning);
                Assert.Equal(
                    TileMode.Tile,
                    Assert.IsType<DrawingBrush>(
                        application.Resources["AppBackgroundBrush"]).TileMode);
                Assert.True(AppBackgroundManager.HasPattern);
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
                    AppBackgroundManager.ClearImageCache();
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

    private static void WriteTestBitmap(string path, bool jpeg)
    {
        byte[] pixels =
        [
            20, 80, 160, 255,
            240, 210, 40, 255,
            80, 180, 100, 255,
            220, 60, 90, 255
        ];
        BitmapSource bitmap = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            8);
        BitmapEncoder encoder = jpeg
            ? new JpegBitmapEncoder()
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
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
