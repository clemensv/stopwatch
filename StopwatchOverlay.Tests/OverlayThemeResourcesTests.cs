using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Xunit;

namespace StopwatchOverlay.Tests;

[Collection("Acanthus visual resources")]
public sealed class OverlayThemeResourcesTests
{
    private static readonly string[] ConcreteThemes = OverlayThemeCatalog.All
        .Where(theme => theme != OverlayThemeCatalog.FollowApplicationTheme).ToArray();

    private static readonly (string Theme, string File)[] PanelPalettes =
    [
        (AppThemeCatalog.Midnight, "Midnight.xaml"),
        (AppThemeCatalog.Daylight, "Daylight.xaml"),
        (AppThemeCatalog.PixelDeckNight, "PixelDeck.xaml"),
        (AppThemeCatalog.PixelDeckDay, "PixelDeckDay.xaml"),
        (AppThemeCatalog.Acanthus, "Acanthus.xaml")
    ];

    [Fact]
    public void EveryConcretePalette_LoadsTheSameCompleteTypedResourceContract()
    {
        RunSta(() =>
        {
            Assert.Equal(8, ConcreteThemes.Length);
            ResourceDictionary baseline = OverlayThemeManager.LoadPalette(OverlayThemeCatalog.Midnight);
            string[] expectedKeys = ResolvedKeys(baseline);
            foreach (string theme in ConcreteThemes)
            {
                ResourceDictionary palette = OverlayThemeManager.LoadPalette(theme);
                Assert.Equal(expectedKeys, ResolvedKeys(palette));
                Assert.Contains(palette.MergedDictionaries,
                    dictionary => dictionary.Source?.OriginalString.EndsWith("OverlayDefaults.xaml", StringComparison.Ordinal) == true);
                foreach (string key in expectedKeys)
                    Assert.Equal(baseline[key].GetType(), palette[key].GetType());

                foreach (string key in new[]
                {
                    "OverlayChromeBrush", "OverlayChromeBorderBrush", "OverlayToolbarSurfaceBrush", "OverlayToolbarBorderBrush",
                    "OverlayTimerForegroundBrush", "OverlayProjectForegroundBrush", "OverlayActionForegroundBrush",
                    "OverlayHoverBrush", "OverlayPressedBrush", "OverlayInnerRuleBrush", "AccentBrush", "RecBrush",
                    "BorderSoftBrush", "ActiveItemBorderBrush", "OverlayActionBackgroundBrush", "OverlayActionBorderBrush",
                    "OverlayCloseBackgroundBrush", "OverlayCloseBorderBrush", "OverlayCloseForegroundBrush", "OverlayCloseHoverForegroundBrush",
                    "OverlayPauseBackgroundBrush", "OverlayPauseBorderBrush", "OverlayPauseForegroundBrush", "OverlayPauseHoverForegroundBrush",
                    "OverlayActionHoverForegroundBrush", "OverlayOrnamentBrush", "OverlayOrnamentAccentBrush"
                })
                    Assert.IsType<SolidColorBrush>(palette[key]);

                foreach (string key in new[] { "AppFontFamily", "ThemeTimerFontFamily" })
                    Assert.IsType<FontFamily>(palette[key]);
                foreach (string key in new[]
                {
                    "ThemePanelBorderThickness", "ThemeOverlayPadding", "ThemeOverlayFrameBorderThickness",
                    "ThemeOverlayButtonBorderThickness", "OverlayActionBorderThickness", "OverlayToolbarPadding", "OverlayProjectMargin"
                })
                    Assert.IsType<Thickness>(palette[key]);
                foreach (string key in new[] { "ThemeOverlayCornerRadius", "ThemeOverlayButtonCornerRadius", "OverlayToolbarCornerRadius" })
                    Assert.IsType<CornerRadius>(palette[key]);
                foreach (string key in new[] { "OverlayActionSize", "OverlayToolbarGap", "OverlayProjectFontSize", "OverlayProjectOpacity", "OverlayMinimumWidth" })
                    Assert.IsType<double>(palette[key]);
                foreach (string key in new[] { "OverlayProjectFontWeight", "OverlayTimerFontWeight" })
                    Assert.IsType<FontWeight>(palette[key]);
                foreach (string key in new[]
                {
                    "OverlayCornerVisibility", "OverlayInnerRuleVisibility", "OverlayCrestVisibility",
                    "OverlayLeafVisibility", "OverlayActiveEdgeVisibility"
                })
                    Assert.IsType<Visibility>(palette[key]);
                Assert.IsType<bool>(palette["OverlayUseThemedChrome"]);
                Assert.IsType<DropShadowEffect>(palette["OverlayShadowEffect"]);
                Assert.IsType<DropShadowEffect>(palette["OverlayToolbarShadowEffect"]);
                foreach (string key in new[] { "OverlayCornerImage", "OverlayRightCornerImage", "OverlayCrestImage", "OverlayLeafImage" })
                {
                    DrawingImage image = Assert.IsType<DrawingImage>(palette[key]);
                    Assert.NotNull(image.Drawing);
                    Assert.True(image.CanFreeze);
                    Assert.NotEmpty(Assert.IsType<DrawingGroup>(image.Drawing).Children);
                }
            }
        });
    }

    [Fact]
    public void DarkPalettes_MatchTheThreeAllowlistedFigmaConceptsWithoutBakedSurfaceOpacity()
    {
        RunSta(() =>
        {
            foreach (var expected in new[]
            {
                (Theme: OverlayThemeCatalog.AcanthusDarkElegantOlive, Surface: "#252A24", Border: "#D9B08A4D", ToolbarBorder: "#A6B08A4D",
                    Timer: "#F3EFE5", Action: "#30372F", Pause: "#4D5D47", Bottom: 32d, Ornament: "OverlayCornerVisibility"),
                (Theme: OverlayThemeCatalog.AcanthusDarkGoldCrest, Surface: "#211F1A", Border: "#B08A4D", ToolbarBorder: "#A6D1BC8D",
                    Timer: "#FBF8F1", Action: "#34312A", Pause: "#6C5939", Bottom: 32d, Ornament: "OverlayCrestVisibility"),
                (Theme: OverlayThemeCatalog.AcanthusDarkMinimalBotanical, Surface: "#252827", Border: "#A68F9E89", ToolbarBorder: "#A68F9E89",
                    Timer: "#FBF8F1", Action: "#303732", Pause: "#4B6250", Bottom: 12d, Ornament: "OverlayLeafVisibility")
            })
            {
                ResourceDictionary palette = OverlayThemeManager.LoadPalette(expected.Theme);
                AssertColor(palette, "OverlayChromeBrush", expected.Surface);
                AssertColor(palette, "OverlayChromeBorderBrush", expected.Border);
                AssertColor(palette, "OverlayToolbarSurfaceBrush", expected.Surface);
                AssertColor(palette, "OverlayToolbarBorderBrush", expected.ToolbarBorder);
                AssertColor(palette, "OverlayTimerForegroundBrush", expected.Timer);
                AssertColor(palette, "OverlayProjectForegroundBrush", "#D5DDCF");
                AssertColor(palette, "OverlayActionBackgroundBrush", expected.Action);
                AssertColor(palette, "OverlayCloseBackgroundBrush", expected.Action);
                AssertColor(palette, "OverlayPauseBackgroundBrush", expected.Pause);
                foreach (string key in new[] { "OverlayActionForegroundBrush", "OverlayCloseForegroundBrush", "OverlayPauseForegroundBrush" })
                    AssertColor(palette, key, "#FBF8F1");
                foreach (string key in new[] { "OverlayActionHoverForegroundBrush", "OverlayCloseHoverForegroundBrush", "OverlayPauseHoverForegroundBrush" })
                    AssertColor(palette, key, "#FFFFFF");
                Assert.Equal(new Thickness(24, 16, 24, expected.Bottom), Assert.IsType<Thickness>(palette["ThemeOverlayPadding"]));
                Assert.Equal(new Thickness(1), Assert.IsType<Thickness>(palette["ThemeOverlayFrameBorderThickness"]));
                Assert.Equal(new Thickness(0), Assert.IsType<Thickness>(palette["OverlayActionBorderThickness"]));
                Assert.Equal(new CornerRadius(8), Assert.IsType<CornerRadius>(palette["ThemeOverlayCornerRadius"]));
                Assert.Equal(new CornerRadius(4), Assert.IsType<CornerRadius>(palette["ThemeOverlayButtonCornerRadius"]));
                Assert.Equal(new CornerRadius(7), Assert.IsType<CornerRadius>(palette["OverlayToolbarCornerRadius"]));
                Assert.Equal(36d, palette["OverlayActionSize"]);
                Assert.Equal(360d, palette["OverlayMinimumWidth"]);
                Assert.Equal(12d, palette["OverlayToolbarGap"]);
                Assert.Equal(14d, palette["OverlayProjectFontSize"]);
                Assert.Equal(FontWeights.Medium, palette["OverlayProjectFontWeight"]);
                Assert.Equal(FontWeights.SemiBold, palette["OverlayTimerFontWeight"]);
                Assert.Equal(1d, palette["OverlayProjectOpacity"]);
                foreach (string key in new[] { "OverlayCornerVisibility", "OverlayInnerRuleVisibility", "OverlayCrestVisibility", "OverlayLeafVisibility", "OverlayActiveEdgeVisibility" })
                    Assert.Equal(key == expected.Ornament ? Visibility.Visible : Visibility.Collapsed, palette[key]);
                foreach (string key in new[] { "OverlayChromeBrush", "OverlayToolbarSurfaceBrush", "OverlayTimerForegroundBrush", "OverlayProjectForegroundBrush" })
                {
                    SolidColorBrush brush = Assert.IsType<SolidColorBrush>(palette[key]);
                    Assert.Equal((byte)255, brush.Color.A);
                    Assert.Equal(1d, brush.Opacity);
                }
                DropShadowEffect surfaceShadow = Assert.IsType<DropShadowEffect>(palette["OverlayShadowEffect"]);
                Assert.Equal(16d, surfaceShadow.BlurRadius);
                Assert.Equal(6d, surfaceShadow.ShadowDepth);
                Assert.Equal(0.2, surfaceShadow.Opacity);
                DropShadowEffect toolbarShadow = Assert.IsType<DropShadowEffect>(palette["OverlayToolbarShadowEffect"]);
                Assert.Equal(10d, toolbarShadow.BlurRadius);
                Assert.Equal(4d, toolbarShadow.ShadowDepth);
                Assert.Equal(0.16, toolbarShadow.Opacity);
            }
        });
    }

    [Fact]
    public void LegacyOverlayPalettes_PreserveEveryPreviouslyConsumedPanelResourceValue()
    {
        RunSta(() =>
        {
            string[] legacyKeys =
            [
                "OverlayChromeBrush", "OverlayChromeBorderBrush", "OverlayToolbarSurfaceBrush", "OverlayActionForegroundBrush",
                "OverlayHoverBrush", "OverlayPressedBrush", "OverlayInnerRuleBrush", "AccentBrush", "RecBrush", "AppFontFamily",
                "ThemeTimerFontFamily", "ThemePanelBorderThickness", "ThemeOverlayPadding", "ThemeOverlayFrameBorderThickness",
                "ThemeOverlayButtonBorderThickness", "ThemeOverlayCornerRadius", "ThemeOverlayButtonCornerRadius", "BorderSoftBrush", "ActiveItemBorderBrush"
            ];
            foreach ((string theme, string file) in PanelPalettes)
            {
                ResourceDictionary panel = LoadPanel(file);
                ResourceDictionary overlay = OverlayThemeManager.LoadPalette(OverlayThemeCatalog.Resolve(OverlayThemeCatalog.FollowApplicationTheme, theme));
                foreach (string key in legacyKeys)
                    AssertResourceEqual(panel[key], overlay[key]);
                Assert.Equal(theme == AppThemeCatalog.Acanthus ? 36d : 28d, overlay["OverlayActionSize"]);
                Assert.Equal(3d, Assert.IsType<Thickness>(overlay["OverlayToolbarPadding"]).Left);
                Assert.Equal(6d, overlay["OverlayToolbarGap"]);
                Assert.Equal(0d, Assert.IsType<DropShadowEffect>(overlay["OverlayShadowEffect"]).Opacity);
            }
        });
    }

    [Fact]
    public void LegacyActionRoles_PreserveLightAcanthusSemanticsAndOtherThemesWhiteIcons()
    {
        RunSta(() =>
        {
            ResourceDictionary light = OverlayThemeManager.LoadPalette(OverlayThemeCatalog.AcanthusLight);
            AssertColor(light, "OverlayActionBackgroundBrush", "#FBF8F1");
            AssertColor(light, "OverlayActionBorderBrush", "#D1BC8D");
            AssertColor(light, "OverlayActionForegroundBrush", "#445140");
            AssertColor(light, "OverlayCloseBorderBrush", "#8A3E45");
            AssertColor(light, "OverlayCloseForegroundBrush", "#8A3E45");
            AssertColor(light, "OverlayCloseHoverForegroundBrush", "#8A3E45");
            AssertColor(light, "OverlayPauseBackgroundBrush", "#445140");
            AssertColor(light, "OverlayPauseBorderBrush", "#445140");
            AssertColor(light, "OverlayPauseForegroundBrush", "#FBF8F1");
            AssertColor(light, "OverlayPauseHoverForegroundBrush", "#445140");
            Assert.Equal(new Thickness(1), Assert.IsType<Thickness>(light["OverlayActionBorderThickness"]));
            foreach (string theme in new[]
            {
                OverlayThemeCatalog.Midnight, OverlayThemeCatalog.Daylight,
                OverlayThemeCatalog.PixelDeckNight, OverlayThemeCatalog.PixelDeckDay
            })
            {
                ResourceDictionary palette = OverlayThemeManager.LoadPalette(theme);
                foreach (string key in new[]
                {
                    "OverlayActionForegroundBrush", "OverlayCloseForegroundBrush", "OverlayPauseForegroundBrush",
                    "OverlayActionHoverForegroundBrush", "OverlayCloseHoverForegroundBrush", "OverlayPauseHoverForegroundBrush"
                })
                    AssertColor(palette, key, "#FFFFFF");
                Assert.Equal((byte)0, Assert.IsType<SolidColorBrush>(palette["OverlayActionBackgroundBrush"]).Color.A);
            }
        });
    }

    [Fact]
    public void AllFortyFivePanelOverlayCombinations_KeepPanelResourcesAndVisualsIsolated()
    {
        RunSta(() =>
        {
            int combinations = 0;
            foreach ((string panelTheme, string file) in PanelPalettes)
            {
                // This tree models inherited panel resources without starting App,
                // touching Application.Resources, or loading stored user settings.
                var root = new StackPanel();
                ResourceDictionary panel = LoadPanel(file);
                root.Resources.MergedDictionaries.Add(panel);
                var panelProbe = new Border();
                panelProbe.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                var panelText = new TextBlock { Text = "Panel stays unchanged" };
                panelText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
                panelText.SetResourceReference(TextBlock.FontFamilyProperty, "AppFontFamily");
                panelProbe.Child = panelText;
                var overlayTarget = new Grid();
                root.Children.Add(panelProbe);
                root.Children.Add(overlayTarget);
                object[] panelKeys = panel.Keys.Cast<object>().ToArray();
                var originalResources = panelKeys.ToDictionary(key => key, key => panel[key]);
                Brush panelSurface = panelProbe.Background;
                Brush panelForeground = panelText.Foreground;
                string panelFont = panelText.FontFamily.Source;

                foreach (string requestedTheme in OverlayThemeCatalog.All)
                {
                    string effective = OverlayThemeCatalog.Resolve(requestedTheme, panelTheme);
                    Assert.Equal(effective, OverlayThemeManager.Apply(overlayTarget, requestedTheme, panelTheme));
                    ResourceDictionary expected = OverlayThemeManager.LoadPalette(effective);
                    AssertResourceEqual(expected["OverlayChromeBrush"], overlayTarget.FindResource("OverlayChromeBrush"));
                    AssertResourceEqual(expected["AppFontFamily"], overlayTarget.FindResource("AppFontFamily"));
                    Assert.Single(overlayTarget.Resources.MergedDictionaries);
                    Assert.Single(root.Resources.MergedDictionaries);
                    Assert.Same(panel, root.Resources.MergedDictionaries[0]);
                    Assert.Same(panelSurface, panelProbe.Background);
                    Assert.Same(panelForeground, panelText.Foreground);
                    Assert.Equal(panelFont, panelText.FontFamily.Source);
                    foreach (object key in panelKeys)
                    {
                        Assert.Same(originalResources[key], panel[key]);
                        Assert.Same(originalResources[key], root.FindResource(key));
                    }
                    combinations++;
                }
            }
            Assert.Equal(45, combinations);
        });
    }

    [Fact]
    public void FixedOverlayChoices_KeepTheirPaletteInstancesThroughEveryPanelThemeSwitch()
    {
        RunSta(() =>
        {
            foreach (string overlayTheme in ConcreteThemes)
            {
                var root = new StackPanel();
                var overlayTarget = new Grid();
                root.Children.Add(overlayTarget);
                OverlayThemeManager.Apply(overlayTarget, overlayTheme, AppThemeCatalog.Midnight);
                ResourceDictionary overlayPalette = Assert.Single(overlayTarget.Resources.MergedDictionaries);
                object surface = overlayTarget.FindResource("OverlayChromeBrush");
                object font = overlayTarget.FindResource("AppFontFamily");
                foreach ((string panelTheme, string file) in PanelPalettes)
                {
                    root.Resources.MergedDictionaries.Clear();
                    root.Resources.MergedDictionaries.Add(LoadPanel(file));
                    Assert.Equal(overlayTheme, OverlayThemeManager.Apply(overlayTarget, overlayTheme, panelTheme));
                    Assert.Same(overlayPalette, Assert.Single(overlayTarget.Resources.MergedDictionaries));
                    Assert.Same(surface, overlayTarget.FindResource("OverlayChromeBrush"));
                    Assert.Same(font, overlayTarget.FindResource("AppFontFamily"));
                }
            }
        });
    }

    [Fact]
    public void FollowApplicationTheme_UpdatesOnlyTheLocalOverlayPaletteAndMapsAcanthusToLight()
    {
        RunSta(() =>
        {
            var root = new StackPanel();
            var overlayTarget = new Grid();
            root.Children.Add(overlayTarget);
            ResourceDictionary? previous = null;
            foreach ((string panelTheme, string file) in PanelPalettes)
            {
                root.Resources.MergedDictionaries.Clear();
                ResourceDictionary panel = LoadPanel(file);
                root.Resources.MergedDictionaries.Add(panel);
                string expected = panelTheme == AppThemeCatalog.Acanthus ? OverlayThemeCatalog.AcanthusLight : panelTheme;
                Assert.Equal(expected, OverlayThemeManager.Apply(overlayTarget, OverlayThemeCatalog.FollowApplicationTheme, panelTheme));
                ResourceDictionary palette = Assert.Single(overlayTarget.Resources.MergedDictionaries);
                Assert.NotSame(previous, palette);
                AssertResourceEqual(panel["OverlayChromeBrush"], overlayTarget.FindResource("OverlayChromeBrush"));
                Assert.Same(panel, Assert.Single(root.Resources.MergedDictionaries));
                previous = palette;
            }
        });
    }

    [Fact]
    public void RepeatedAndEquivalentSwitches_AreIdempotentAndNeverAccumulateDictionaries()
    {
        RunSta(() =>
        {
            var target = new Grid();
            var unrelatedDictionary = new ResourceDictionary { ["UnrelatedMergedResource"] = new object() };
            var localValue = new object();
            target.Resources["UnrelatedLocalResource"] = localValue;
            target.Resources.MergedDictionaries.Add(unrelatedDictionary);
            OverlayThemeManager.Apply(target, OverlayThemeCatalog.Midnight, AppThemeCatalog.Daylight);
            ResourceDictionary initial = target.Resources.MergedDictionaries[1];
            OverlayThemeManager.Apply(target, "Dark", AppThemeCatalog.Acanthus);
            Assert.Same(initial, target.Resources.MergedDictionaries[1]);
            OverlayThemeManager.Apply(target, OverlayThemeCatalog.FollowApplicationTheme, AppThemeCatalog.Midnight);
            Assert.Same(initial, target.Resources.MergedDictionaries[1]);

            for (int repeat = 0; repeat < 3; repeat++)
            foreach (string theme in ConcreteThemes)
            {
                OverlayThemeManager.Apply(target, theme, AppThemeCatalog.Midnight);
                ResourceDictionary installed = target.Resources.MergedDictionaries[1];
                OverlayThemeManager.Apply(target, theme, AppThemeCatalog.Acanthus);
                Assert.Same(installed, target.Resources.MergedDictionaries[1]);
                Assert.Equal(2, target.Resources.MergedDictionaries.Count);
                Assert.Same(unrelatedDictionary, target.Resources.MergedDictionaries[0]);
                Assert.Same(localValue, target.Resources["UnrelatedLocalResource"]);
            }

            ResourceDictionary removed = target.Resources.MergedDictionaries[1];
            target.Resources.MergedDictionaries.Remove(removed);
            OverlayThemeManager.Apply(target, ConcreteThemes[^1], AppThemeCatalog.Midnight);
            Assert.Equal(2, target.Resources.MergedDictionaries.Count);
            Assert.NotSame(removed, target.Resources.MergedDictionaries[1]);
            Assert.Same(unrelatedDictionary, target.Resources.MergedDictionaries[0]);
        });
    }

    [Fact]
    public void OverlayOrnamentImages_RetainExactExportedVectorViewboxes()
    {
        RunSta(() =>
        {
            ResourceDictionary palette = OverlayThemeManager.LoadPalette(OverlayThemeCatalog.AcanthusDarkElegantOlive);
            foreach ((string key, double width, double height) in new[]
            {
                ("OverlayCornerImage", 18d, 16d), ("OverlayRightCornerImage", 18d, 16d),
                ("OverlayCrestImage", 18d, 18d), ("OverlayLeafImage", 14d, 3d)
            })
            {
                DrawingImage image = Assert.IsType<DrawingImage>(palette[key]);
                Assert.Equal(new Rect(0, 0, width, height), image.Drawing.Bounds);
                Assert.True(image.CanFreeze);
                Assert.NotEmpty(Assert.IsType<DrawingGroup>(image.Drawing).Children);
            }
        });
    }

    [Fact]
    public void AcanthusOverlayTypography_UsesBundledFontsAndLeavesCustomFamiliesAlone()
    {
        RunSta(() =>
        {
            foreach (string theme in ConcreteThemes.Where(OverlayThemeCatalog.IsAcanthus))
            {
                var target = new Grid();
                OverlayThemeManager.Apply(target, theme, AppThemeCatalog.Daylight);
                foreach ((string key, string familyName, FontWeight weight) in new[]
                {
                    ("AppFontFamily", "Inter", FontWeights.Medium),
                    ("ThemeTimerFontFamily", "Cascadia Mono", FontWeights.SemiBold)
                })
                {
                    FontFamily family = Assert.IsType<FontFamily>(target.FindResource(key));
                    Assert.Contains("/Assets/Fonts/Acanthus/#" + familyName, family.Source);
                    var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                    Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface glyph), $"{theme}, {key}: {family.Source}");
                    Assert.Contains("/assets/fonts/acanthus/", glyph.FontUri.ToString().ToLowerInvariant());
                    Assert.Equal(weight, glyph.Weight);
                }
                FontFamily bundled = Assert.IsType<FontFamily>(target.FindResource("ThemeTimerFontFamily"));
                Assert.Same(bundled, OverlayThemeManager.ResolveTimerFont(target, theme, "Cascadia Mono"));
                Assert.Same(bundled, OverlayThemeManager.ResolveTimerFont(target, theme, "cascadia mono"));
                Assert.Equal("Consolas", OverlayThemeManager.ResolveTimerFont(target, theme, "Consolas").Source);
            }
            var legacyTarget = new Grid();
            OverlayThemeManager.Apply(legacyTarget, OverlayThemeCatalog.PixelDeckNight, AppThemeCatalog.Acanthus);
            Assert.Equal("Cascadia Mono", OverlayThemeManager.ResolveTimerFont(legacyTarget, OverlayThemeCatalog.PixelDeckNight, "Cascadia Mono").Source);
        });
    }

    [Fact]
    public void ResourceColor_UsesTheLocalOverlayResourceOrTheExplicitFallback()
    {
        RunSta(() =>
        {
            var root = new StackPanel();
            root.Resources["OverlayChromeBrush"] = Brushes.Magenta;
            var target = new Grid();
            root.Children.Add(target);
            OverlayThemeManager.Apply(target, OverlayThemeCatalog.AcanthusDarkElegantOlive, AppThemeCatalog.Daylight);
            Assert.Equal(Color.FromRgb(37, 42, 36), OverlayThemeManager.ResourceColor(target, "OverlayChromeBrush", Colors.Red));
            Assert.Equal(Colors.Red, OverlayThemeManager.ResourceColor(target, "MissingResource", Colors.Red));
            target.Resources["NotABrush"] = "do not guess a color";
            Assert.Equal(Colors.Red, OverlayThemeManager.ResourceColor(target, "NotABrush", Colors.Red));
            Assert.Same(Brushes.Magenta, root.Resources["OverlayChromeBrush"]);
        });
    }

    [Fact]
    public void Apply_PropagatesInvalidTargetAndWrongDispatcherErrorsWithoutMutatingResources()
    {
        Assert.Throws<ArgumentNullException>(() => OverlayThemeManager.Apply(null!, OverlayThemeCatalog.Midnight, AppThemeCatalog.Midnight));
        RunSta(() =>
        {
            var target = new Grid();
            OverlayThemeManager.Apply(target, OverlayThemeCatalog.Midnight, AppThemeCatalog.Midnight);
            ResourceDictionary original = Assert.Single(target.Resources.MergedDictionaries);
            Exception? failure = null;
            var foreignThread = new Thread(() =>
            {
                try { OverlayThemeManager.Apply(target, OverlayThemeCatalog.Daylight, AppThemeCatalog.Midnight); }
                catch (Exception exception) { failure = exception; }
            });
            foreignThread.SetApartmentState(ApartmentState.STA);
            foreignThread.Start();
            foreignThread.Join();
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Same(original, Assert.Single(target.Resources.MergedDictionaries));
        });
    }

    private static void AssertColor(ResourceDictionary palette, string key, string expected)
        => Assert.Equal((Color)ColorConverter.ConvertFromString(expected)!, Assert.IsType<SolidColorBrush>(palette[key]).Color);

    private static void AssertResourceEqual(object expected, object actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case SolidColorBrush brush:
                SolidColorBrush actualBrush = Assert.IsType<SolidColorBrush>(actual);
                Assert.Equal(brush.Color, actualBrush.Color);
                Assert.Equal(brush.Opacity, actualBrush.Opacity);
                break;
            case FontFamily family:
                Assert.Equal(family.Source, Assert.IsType<FontFamily>(actual).Source);
                break;
            default:
                Assert.Equal(expected, actual);
                break;
        }
    }

    private static string[] ResolvedKeys(ResourceDictionary dictionary)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddKeys(dictionary);
        return keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

        void AddKeys(ResourceDictionary current)
        {
            foreach (string key in current.Keys)
                keys.Add(key);
            foreach (ResourceDictionary merged in current.MergedDictionaries)
                AddKeys(merged);
        }
    }

    private static ResourceDictionary LoadPanel(string file)
        => (ResourceDictionary)Application.LoadComponent(new Uri($"/StopwatchOverlay;component/Themes/{file}", UriKind.Relative));

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
}
