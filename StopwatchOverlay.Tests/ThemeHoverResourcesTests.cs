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
                         "PixelDeckDay.xaml",
                         "Acanthus.xaml"
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
