using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace StopwatchOverlay.Tests;

public sealed class ThemeHoverResourcesTests
{
    [Fact]
    public void PixelDeckNight_NeutralAndOverlayHoverTokensUseOpaqueWhiteChrome()
    {
        RunSta(() =>
        {
            ResourceDictionary theme = Load("PixelDeck.xaml");

            Assert.Equal(
                BrushColor(theme, "OverlayHoverBrush"),
                BrushColor(theme, "NeutralButtonHoverBackgroundBrush"));
            Assert.Equal(
                BrushColor(theme, "OverlayPressedBrush"),
                BrushColor(theme, "NeutralButtonPressedBackgroundBrush"));
            Assert.Equal(
                BrushColor(theme, "BorderSoftBrush"),
                BrushColor(theme, "NeutralButtonHoverBorderBrush"));
            Assert.Equal(
                BrushColor(theme, "BorderSoftBrush"),
                BrushColor(theme, "NeutralButtonPressedBorderBrush"));
            Assert.Equal(Colors.White, BrushColor(theme, "NeutralButtonHoverForegroundBrush"));
            Assert.Equal(Colors.White, BrushColor(theme, "NeutralButtonPressedForegroundBrush"));
            Assert.Equal(Colors.White, BrushColor(theme, "OverlayActionForegroundBrush"));
            Assert.Equal(1d, Assert.IsType<double>(theme["NeutralButtonHoverOpacity"]));
            Assert.Equal(1d, Assert.IsType<double>(theme["NeutralButtonPressedOpacity"]));

            // Semantic yellow/pink actions deliberately retain their existing
            // dark action foreground instead of inheriting neutral hover chrome.
            Assert.Equal(Color.FromRgb(7, 28, 33), BrushColor(theme, "OnActionTextBrush"));
        });
    }

    [Fact]
    public void OtherThemes_PreserveTheirPreviousOpacityOnlyNeutralButtonStates()
    {
        RunSta(() =>
        {
            foreach (string fileName in new[]
                     {
                         "Midnight.xaml",
                         "Daylight.xaml",
                         "PixelDeckDay.xaml"
                     })
            {
                ResourceDictionary theme = Load(fileName);

                Assert.Equal(
                    BrushColor(theme, "SurfaceRaisedBrush"),
                    BrushColor(theme, "NeutralButtonHoverBackgroundBrush"));
                Assert.Equal(
                    BrushColor(theme, "SurfaceRaisedBrush"),
                    BrushColor(theme, "NeutralButtonPressedBackgroundBrush"));
                Assert.Equal(
                    BrushColor(theme, "BorderBrush"),
                    BrushColor(theme, "NeutralButtonHoverBorderBrush"));
                Assert.Equal(
                    BrushColor(theme, "BorderBrush"),
                    BrushColor(theme, "NeutralButtonPressedBorderBrush"));
                Assert.Equal(
                    BrushColor(theme, "PrimaryTextBrush"),
                    BrushColor(theme, "NeutralButtonHoverForegroundBrush"));
                Assert.Equal(
                    BrushColor(theme, "PrimaryTextBrush"),
                    BrushColor(theme, "NeutralButtonPressedForegroundBrush"));
                Assert.Equal(
                    BrushColor(theme, "OnActionTextBrush"),
                    BrushColor(theme, "OverlayActionForegroundBrush"));
                Assert.Equal(0.88d, Assert.IsType<double>(theme["NeutralButtonHoverOpacity"]));
                Assert.Equal(0.72d, Assert.IsType<double>(theme["NeutralButtonPressedOpacity"]));
            }
        });
    }

    [Fact]
    public void Acanthus_NeutralButtonsUseOpaqueSageAndStoneWithReadableOliveOverlayIcons()
    {
        RunSta(() =>
        {
            ResourceDictionary theme = Load("Acanthus.xaml");

            Assert.Equal(Color.FromRgb(213, 221, 207), BrushColor(theme, "NeutralButtonHoverBackgroundBrush"));
            Assert.Equal(Color.FromRgb(229, 221, 207), BrushColor(theme, "NeutralButtonPressedBackgroundBrush"));
            Assert.Equal(Color.FromRgb(176, 138, 77), BrushColor(theme, "NeutralButtonHoverBorderBrush"));
            Assert.Equal(Color.FromRgb(176, 138, 77), BrushColor(theme, "NeutralButtonPressedBorderBrush"));
            Assert.Equal(Color.FromRgb(44, 41, 36), BrushColor(theme, "NeutralButtonHoverForegroundBrush"));
            Assert.Equal(Color.FromRgb(44, 41, 36), BrushColor(theme, "NeutralButtonPressedForegroundBrush"));
            Assert.Equal(1d, Assert.IsType<double>(theme["NeutralButtonHoverOpacity"]));
            Assert.Equal(1d, Assert.IsType<double>(theme["NeutralButtonPressedOpacity"]));
            Assert.Equal(Color.FromRgb(68, 81, 64), BrushColor(theme, "OverlayActionForegroundBrush"));
            Assert.Equal(Color.FromRgb(251, 248, 241), BrushColor(theme, "OverlayToolbarSurfaceBrush"));

            // Semantic primary actions retain ivory text on olive rather than
            // inheriting the neutral controls' charcoal foreground.
            Assert.Equal(Color.FromRgb(251, 248, 241), BrushColor(theme, "OnActionTextBrush"));
            Assert.Equal(Color.FromRgb(68, 81, 64), BrushColor(theme, "PrimaryActionBrush"));
            Assert.Equal(Color.FromRgb(138, 62, 69), BrushColor(theme, "DangerActionBrush"));
        });
    }

    private static ResourceDictionary Load(string fileName)
        => (ResourceDictionary)Application.LoadComponent(
            new Uri(
                $"/StopwatchOverlay;component/Themes/{fileName}",
                UriKind.Relative));

    private static Color BrushColor(ResourceDictionary theme, string key)
        => Assert.IsType<SolidColorBrush>(theme[key]).Color;

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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
}
