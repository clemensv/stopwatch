using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace StopwatchOverlay.Tests;

[Collection("Acanthus visual resources")]
public sealed class OverlayWindowThemeTests
{
    [Fact]
    public void ActualClockAndPopup_SwitchAll45CombinationsInPlace_WithoutLosingCustomAppearance()
    {
        RunSta(() =>
        {
            var overlay = new OverlayWindow { Left = -234, Top = 147 };
            try
            {
                overlay.UpdateTime("00:05:10");
                overlay.SetTimerName("Programming");
                overlay.SetActive(true);
                var timer = Node<TextBlock>(overlay, "TimeText");
                var project = Node<TextBlock>(overlay, "TimerNameText");
                var root = Node<Grid>(overlay, "TimerSurface");
                var popup = Node<Grid>(overlay, "ActionPopupRoot");
                var originalTimer = timer;
                foreach (string panel in AppThemeCatalog.All)
                foreach (string choice in OverlayThemeCatalog.All)
                {
                    overlay.ApplyTheme(choice, panel);
                    overlay.ApplySettings(Colors.Cyan, Colors.Red, 61, 3, "Arial", 0.37);
                    Measure(root);
                    Measure(popup);
                    string effective = OverlayThemeCatalog.Resolve(choice, panel);
                    Assert.Equal(effective, overlay.EffectiveOverlayTheme);
                    Assert.Same(originalTimer, Node<TextBlock>(overlay, "TimeText"));
                    Assert.Equal(-234d, overlay.Left);
                    Assert.Equal(147d, overlay.Top);
                    Assert.Equal("00:05:10", timer.Text);
                    Assert.Equal("Programming", project.Text);
                    Assert.Equal(61d, timer.FontSize);
                    Assert.Equal("Arial", timer.FontFamily.Source);
                    Assert.Equal(Colors.Cyan, BrushColor(timer.Foreground));
                    Assert.Equal(Colors.Cyan, BrushColor(project.Foreground));
                    Assert.Equal(Colors.Red, BrushColor(Node<TextBlock>(overlay, "TimeTextShadow1").Foreground));
                    Assert.Equal(3d, Assert.IsType<TranslateTransform>(Node<TextBlock>(overlay, "TimeTextShadow1").RenderTransform).X);
                    Assert.Equal((byte)94, BrushColor(Node<Border>(overlay, "OverlayBackgroundSurface").Background).A);
                    Assert.Equal(Visibility.Visible, project.Visibility);
                    Assert.Equal(effective == OverlayThemeCatalog.AcanthusLight ? Visibility.Visible : Visibility.Collapsed,
                        Node<Border>(overlay, "AcanthusActiveEdge").Visibility);
                    Assert.Equal(OverlayThemeManager.ResourceColor(overlay, "OverlayActionForegroundBrush", Colors.Magenta),
                        OverlayThemeManager.ResourceColor(popup, "OverlayActionForegroundBrush", Colors.Magenta));
                    Point timerBottom = timer.TranslatePoint(new Point(0, timer.ActualHeight), root);
                    Point projectTop = project.TranslatePoint(new Point(0, 0), root);
                    Assert.True(projectTop.Y >= timerBottom.Y, $"{panel} / {choice}: timer must be first");
                }
            }
            finally { overlay.Close(); }
        });
    }

    [Fact]
    public void BackgroundOpacity_DoesNotFadeTextBorderOrToolbar_AndThemedTextIsOptIn()
    {
        RunSta(() =>
        {
            var overlay = new OverlayWindow();
            try
            {
                overlay.SetTimerName("Project");
                foreach (string choice in OverlayThemeCatalog.All)
                {
                    overlay.ApplyTheme(choice, AppThemeCatalog.Acanthus);
                    Color border = default;
                    Color foreground = default;
                    foreach (double opacity in new[] { 0d, 0.5, 1d })
                    {
                        overlay.ApplySettings(Colors.Cyan, Colors.Red, 48, 2, "Cascadia Mono", opacity, useThemeTextColor: true);
                        var surface = Node<Border>(overlay, "OverlayBackgroundSurface");
                        var chrome = Node<Border>(overlay, "OverlayBorder");
                        var toolbar = Node<Border>(overlay, "ActionSurface");
                        var time = Node<TextBlock>(overlay, "TimeText");
                        Assert.Equal((byte)Math.Round(opacity * 255), BrushColor(surface.Background).A);
                        Assert.Equal(1d, time.Opacity);
                        Assert.Equal(1d, chrome.Opacity);
                        Assert.Equal(1d, Node<Grid>(overlay, "ActionPopupRoot").Opacity);
                        Assert.Equal(255, BrushColor(toolbar.Background).A);
                        Assert.Equal(1d, Node<Button>(overlay, "ResetActionButton").Opacity);
                        if (opacity == 0)
                        {
                            border = BrushColor(chrome.BorderBrush);
                            foreground = BrushColor(time.Foreground);
                        }
                        Assert.Equal(border, BrushColor(chrome.BorderBrush));
                        Assert.Equal(foreground, BrushColor(time.Foreground));
                        Assert.Equal(OverlayThemeManager.ResourceColor(overlay, "OverlayTimerForegroundBrush", Colors.White), foreground);
                    }
                    overlay.ApplySettings(Colors.Cyan, Colors.Red, 48, 2, "Cascadia Mono", 0.5);
                    Assert.Equal(Colors.Cyan, BrushColor(Node<TextBlock>(overlay, "TimeText").Foreground));
                }
            }
            finally { overlay.Close(); }
        });
    }

    [Fact]
    public void AllThemes_RetainTimerNameRecRunningStateAndActionEventRouting()
    {
        RunSta(() =>
        {
            var overlay = new OverlayWindow();
            try
            {
                int activated = 0, paused = 0, reset = 0, closed = 0;
                overlay.ActivationRequested += () => activated++;
                overlay.PauseResumeRequested += () => paused++;
                overlay.ResetRequested += () => reset++;
                overlay.CloseRequested += () => closed++;
                foreach (string choice in OverlayThemeCatalog.All)
                {
                    overlay.ApplyTheme(choice, AppThemeCatalog.Midnight);
                    overlay.SetTimerName(null);
                    Assert.Equal(Visibility.Collapsed, Node<TextBlock>(overlay, "TimerNameText").Visibility);
                    overlay.SetTimerName("  Work  ");
                    Assert.Equal("Work", Node<TextBlock>(overlay, "TimerNameText").Text);
                    overlay.SetRecIndicatorVisible(true);
                    Assert.Equal(Visibility.Visible, Node<System.Windows.Shapes.Ellipse>(overlay, "RecIndicator").Visibility);
                    overlay.SetRunning(false);
                    Assert.Equal(Visibility.Visible, Node<System.Windows.Shapes.Path>(overlay, "ResumeIcon").Visibility);
                    Assert.Equal(Visibility.Collapsed, Node<Grid>(overlay, "PauseIcon").Visibility);
                    overlay.SetRunning(true);
                    Assert.Equal(Visibility.Visible, Node<Grid>(overlay, "PauseIcon").Visibility);
                    overlay.SetPauseResumeEnabled(false);
                    Assert.False(Node<Button>(overlay, "PauseResumeActionButton").IsEnabled);
                    overlay.SetPauseResumeEnabled(true);
                    foreach (string name in new[] { "PauseResumeActionButton", "ResetActionButton", "CloseActionButton" })
                        Node<Button>(overlay, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    overlay.SetClickThrough(true);
                    overlay.SetClickThrough(false);
                    overlay.SetHideFromCapture(true);
                    overlay.SetHideFromCapture(false);
                }
                Assert.Equal(OverlayThemeCatalog.All.Count, paused);
                Assert.Equal(paused, reset);
                Assert.Equal(paused, closed);
                Assert.Equal(paused * 3, activated);
                Assert.True(overlay.Topmost);
                Assert.False(overlay.ShowInTaskbar);
                Assert.False(overlay.ShowActivated);
            }
            finally { overlay.Close(); }
        });
    }

    private static T Node<T>(OverlayWindow window, string name) where T : class
        => Assert.IsType<T>(window.FindName(name));
    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static void Measure(FrameworkElement root)
    {
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA overlay test timed out");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
