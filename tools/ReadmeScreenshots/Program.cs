using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using StopwatchOverlay;

// Documentation rendering only. Never construct ControllerWindow, run App,
// show a native window, call persistence, or read the user's project history.
internal static class Program
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<object> Captures = new();
    private static long TotalImageBytes;
    private static readonly DateTime SampleLocal = new(2026, 9, 4, 16, 42, 18, DateTimeKind.Local);
    private static readonly string[] Projects =
        ["Website refresh", "Client portal", "Design system", "User research", "Sprint planning", "Documentation"];
    private static string Repo = "";
    private static string Output = "";

    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Repo = Path.GetFullPath(args.Length == 0 ? Directory.GetCurrentDirectory() : args[0]);
        if (!File.Exists(Path.Combine(Repo, "StopwatchOverlay", "ControllerWindow.xaml")))
            throw new InvalidOperationException("Run from the Stopwatch repository root, or pass its path.");
        Output = Path.Combine(Repo, "docs", "screenshots");
        Directory.CreateDirectory(Output);

        // A recoverable preview exception must fail this renderer rather than
        // append a diagnostic to the real user's AppData directory.
        typeof(App).Assembly.GetType("StopwatchOverlay.CrashLogger")!
            .GetField("_writeInProgress", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 1);
        var app = new App();
        app.InitializeComponent(); // Resources only; no Startup, Run, or Shutdown.
        var settings = new AppSettings
        {
            ThemeMode = AppThemeCatalog.Midnight,
            OverlayTheme = OverlayThemeCatalog.FollowApplicationTheme,
            TextColor = "Theme default", BorderColor = "Black", FontFamily = "Cascadia Mono",
            TextSize = 48, BorderWidth = 1, BackgroundOpacity = 88, TimeFormat = 1,
            Shortcuts = AppSettings.DefaultShortcuts(), CustomBackgrounds = new(),
            PanelBackgroundId = AppBackgroundCatalog.ThemeDefault,
            LightRingEnabled = true, LightRingBrightness = 65, LightRingWidth = 30
        };
        // A supported custom-shortcut state keeps the longer overlay caption
        // concise; all other sample controller bindings use the normal defaults.
        settings.Shortcuts[ShortcutAction.ToggleOverlay] = new Shortcut(0, 0);
        ProjectTimeHistory history = CreateSampleHistory();
        ProjectHistoryView view = history.CreateView(SampleLocal.ToUniversalTime());
        Window controller = LoadControllerMarkup();
        PopulateController(controller);

        foreach (string theme in AppThemeCatalog.All)
        {
            ApplyTheme(settings, theme);
            Node<Button>(controller, "StartStopButton").Style = (Style)controller.FindResource(
                theme == AppThemeCatalog.Acanthus ? "AcanthusPrimaryAction" : "StopButton");
            Node<System.Windows.Shapes.Ellipse>(controller, "StatusIndicator")
                .SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
            Save(Render((FrameworkElement)controller.Content, 1040, 748,
                    theme == AppThemeCatalog.PixelDeckNight ? 960 : 832),
                "controller-" + Slug(theme), theme, "Controller");
        }

        ApplyTheme(settings, AppThemeCatalog.Acanthus);
        settings.OverlayTheme = OverlayThemeCatalog.AcanthusDarkElegantOlive;
        var inspector = new SettingsWindow(settings);
        Node<ListBox>(inspector, "NavigationList").SelectedIndex = 1;
        Invoke(inspector, "UpdatePreviewSafely");
        AssertPreview(inspector);
        Save(Render((FrameworkElement)inspector.Content, 1040, 880, 960),
            "settings-acanthus", settings.ThemeMode, "Appearance and independent overlay theme");

        ApplyTheme(settings, AppThemeCatalog.PixelDeckNight);
        settings.OverlayTheme = OverlayThemeCatalog.PixelDeckNight;
        Invoke(inspector, "ReloadFromSettings");
        Node<ListBox>(inspector, "NavigationList").SelectedIndex = 2;
        Invoke(inspector, "UpdatePreviewSafely");
        AssertPreview(inspector);
        Save(Render((FrameworkElement)inspector.Content, 1040, 668, 880),
            "settings-light-ring", settings.ThemeMode, "Light ring controls");

        // The callbacks operate on this in-memory fixture only. The screenshot
        // path never invokes editing, but ordinary enabled buttons render accurately.
        ApplyTheme(settings, AppThemeCatalog.Daylight);
        var dashboard = new ProjectDashboardWindow(() => view,
            (_, _, _) => throw new InvalidOperationException("Screenshot fixture is not editable."),
            (_, _, _, _) => throw new InvalidOperationException("Screenshot fixture is not editable."),
            _ => throw new InvalidOperationException("Screenshot fixture is not editable."),
            () => true, () => null);
        Node<Button>(dashboard, "SevenDaysButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var dashboardRoot = (FrameworkElement)dashboard.Content;
        Render(dashboardRoot, 1120, 880, 960);
        Node<ScrollViewer>(dashboard, "HeatmapScrollViewer").ScrollToRightEnd();
        Save(Render(dashboardRoot, 1120, 880, 960), "analytics-daylight",
            settings.ThemeMode, "Seven-day summary, project totals and activity heatmap");

        ApplyTheme(settings, AppThemeCatalog.Midnight);
        Node<Button>(dashboard, "DayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Node<Expander>(dashboard, "ProjectRecordsExpander").IsExpanded = true;
        Invoke(dashboard, "RefreshFromHistory");
        Render(dashboardRoot, 1120, 900, 960);
        var scroll = Node<ScrollViewer>(dashboard, "DashboardScrollViewer");
        var scrollContent = (FrameworkElement)scroll.Content;
        var heatmap = Node<Border>(dashboard, "HeatmapCard");
        scroll.ScrollToVerticalOffset(heatmap.TranslatePoint(new Point(), scrollContent).Y + 10);
        Node<ScrollViewer>(dashboard, "HeatmapScrollViewer").ScrollToRightEnd();
        Render(dashboardRoot, 1120, 900, 960);
        Border secondRecord = Node<StackPanel>(dashboard, "RecordsPanel").Children.OfType<Border>().ElementAt(1);
        int detailHeight = (int)Math.Ceiling(secondRecord.TranslatePoint(new Point(0, secondRecord.ActualHeight), dashboardRoot).Y + 6);
        Save(Render(dashboardRoot, 1120, detailHeight, 960), "analytics-timeline-records",
            settings.ThemeMode, "Activity heatmap, daily timeline and sample project records");

        SaveOverlayGallery(settings);
        SaveTransparencyGallery(settings);
        long totalBytes = TotalImageBytes;
        if (totalBytes > 1_250_000)
            throw new InvalidOperationException($"Screenshot budget exceeded: {totalBytes:N0} bytes.");
        File.WriteAllText(Path.Combine(Output, "manifest.json"), JsonSerializer.Serialize(new
        {
            sampleData = true, sampleLocalTime = SampleLocal.ToString("yyyy-MM-dd HH:mm"),
            projects = Projects, intervalCount = view.Intervals.Count,
            sampleCustomization = "Overlay-toggle shortcut unbound; other controller shortcuts use defaults.",
            pixelDeckBackground = "Autumn Patchwork, 30% strength",
            totalBytes, captures = Captures,
            safety = "In-memory synthetic history; no production startup, user stores, or native windows."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Saved {Captures.Count} screenshots; {totalBytes:N0} bytes total; {view.Intervals.Count} synthetic intervals.");
        // Do not dispatch application exit/Window.Closing checkpoint handlers.
        Environment.Exit(0);
        return 0;
    }

    private static ProjectTimeHistory CreateSampleHistory()
    {
        var history = new ProjectTimeHistory();
        for (int daysAgo = 364; daysAgo >= 1; daysAgo--)
        {
            DateTime day = SampleLocal.Date.AddDays(-daysAgo);
            if (daysAgo % 23 == 0 || daysAgo is >= 120 and <= 128) continue;
            bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (weekend && daysAgo % 3 != 0) continue;
            int count = weekend ? 1 : 3 + (daysAgo % 3 == 0 ? 1 : 0);
            for (int slot = 0; slot < count; slot++)
            {
                // Varied, bounded sessions: believable daily workloads rather
                // than filling every heatmap square with the maximum intensity.
                int project = slot == 0 ? daysAgo % 2 : (daysAgo + slot * 2) % Projects.Length;
                int minutes = slot == 0 ? 80 + daysAgo * 13 % 95 : 25 + daysAgo * 7 % 75;
                DateTime start = day.AddHours(8.5 + slot * 2.15).AddMinutes(daysAgo % 4 * 5);
                history.AddManualInterval(Projects[project], start.ToUniversalTime(), start.AddMinutes(minutes).ToUniversalTime());
            }
        }
        void Add(string project, int hour, int minute, int duration)
        {
            DateTime start = SampleLocal.Date.AddHours(hour).AddMinutes(minute);
            history.AddManualInterval(project, start.ToUniversalTime(), start.AddMinutes(duration).ToUniversalTime());
        }
        Add("Sprint planning", 8, 30, 30);
        Add("Website refresh", 9, 10, 105);
        Add("Client portal", 11, 15, 70);
        Add("User research", 13, 0, 45);
        Add("Design system", 14, 0, 65);
        Add("Documentation", 14, 30, 35); // A deliberately overlapping independent timer.
        history.StartTracking(Guid.NewGuid(), "Website refresh", SampleLocal.Date.AddHours(16).ToUniversalTime());
        return history;
    }

    private static Window LoadControllerMarkup()
    {
        XDocument markup = XDocument.Load(Path.Combine(Repo, "StopwatchOverlay", "ControllerWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        markup.Root!.Attribute(x + "Class")?.Remove();
        foreach (XElement node in markup.Root.DescendantsAndSelf())
        {
            Type? type = typeof(Window).Assembly.GetType("System.Windows." + node.Name.LocalName)
                ?? typeof(Window).Assembly.GetType("System.Windows.Controls." + node.Name.LocalName)
                ?? typeof(Window).Assembly.GetType("System.Windows.Shapes." + node.Name.LocalName);
            foreach (XAttribute attribute in node.Attributes().ToArray())
                if (type?.GetEvent(attribute.Name.LocalName) != null) attribute.Remove();
        }
        return (Window)XamlReader.Parse(markup.ToString());
    }

    private static void PopulateController(Window window)
    {
        var rail = Node<ListBox>(window, "TimerRailList");
        rail.ItemsSource = new[]
        {
            new { DisplayName = "Website refresh", DisplaySummary = "00:42:18  ·  Running  ·  Stopwatch" },
            new { DisplayName = "Client portal", DisplaySummary = "01:10:00  ·  Paused  ·  Stopwatch" },
            new { DisplayName = "Design system", DisplaySummary = "00:25:00  ·  Paused  ·  Countdown" },
            new { DisplayName = "User research", DisplaySummary = "00:45:00  ·  Paused  ·  Stopwatch" }
        };
        rail.SelectedIndex = 0;
        Node<TextBlock>(window, "TimeDisplay").Text = "00:42:18";
        Node<TextBlock>(window, "ActiveWorkspaceTitle").Text = "Website refresh";
        Node<Button>(window, "StartStopButton").Content = "Stop  ·  Win+F5";
        Node<Button>(window, "ToggleOverlayButton").Content = "Hide overlay";
        Node<RadioButton>(window, "StopwatchModeRadio").IsChecked = true;
        Node<TextBlock>(window, "StatusText").Text = "Running · Website refresh";
        Node<TextBlock>(window, "ShortcutHintText").Text =
            "Win+F2 New  Win+F3 Next  Win+F4 Close  Win+F10 Project  Win+F11 Dashboard";
        Node<ListBox>(window, "LapListBox").ItemsSource = new[]
        {
            "02    00:35:42    +00:23:24", "01    00:12:18    +00:12:18"
        };
        Node<TextBlock>(window, "LapPlaceholder").Visibility = Visibility.Collapsed;
    }

    private static void ApplyTheme(AppSettings settings, string theme)
    {
        settings.ThemeMode = theme;
        settings.PanelBackgroundId = theme is AppThemeCatalog.PixelDeckNight or AppThemeCatalog.PixelDeckDay
            ? AppBackgroundCatalog.AutumnPatchwork : AppBackgroundCatalog.ThemeDefault;
        settings.PanelBackgroundStrength = 30;
        AppThemeManager.Apply(theme);
        AppBackgroundManager.Apply(settings, out string? warning);
        if (warning != null) throw new InvalidOperationException(warning);
    }

    private static void SaveOverlayGallery(AppSettings settings)
    {
        ApplyTheme(settings, AppThemeCatalog.Midnight);
        var overlay = new OverlayWindow();
        overlay.UpdateTime("00:42:18");
        overlay.SetTimerName("Website refresh");
        overlay.SetRunning(true);
        overlay.SetActive(false);
        var toolbar = Node<Border>(overlay, "ActionSurface");
        toolbar.BeginAnimation(UIElement.OpacityProperty, null);
        toolbar.Opacity = 1;
        toolbar.RenderTransform = new TranslateTransform();
        string[] choices =
        [
            OverlayThemeCatalog.AcanthusDarkElegantOlive,
            OverlayThemeCatalog.AcanthusDarkGoldCrest,
            OverlayThemeCatalog.AcanthusDarkMinimalBotanical,
            OverlayThemeCatalog.Midnight, OverlayThemeCatalog.Daylight,
            OverlayThemeCatalog.AcanthusLight,
            OverlayThemeCatalog.PixelDeckNight, OverlayThemeCatalog.PixelDeckDay
        ];
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(231, 234, 229)), null, new Rect(0, 0, 960, 680));
            for (int i = 0; i < choices.Length; i++)
            {
                int x = 16 + i % 3 * 314, y = 14 + i / 3 * 220;
                string label = choices[i].Replace("Acanthus Dark ", "Acanthus Dark · ");
                dc.DrawText(Label(label, 13, FontWeights.SemiBold), new Point(x + 10, y));
                overlay.ApplyTheme(choices[i], settings.ThemeMode);
                overlay.ApplySettings(Colors.White, Colors.Black, 40, 0, "Cascadia Mono", 0.92, useThemeTextColor: true);
                var surface = (FrameworkElement)overlay.Content;
                BitmapSource clock = RenderNatural(surface);
                double scale = Math.Min(1, 294d / clock.PixelWidth);
                double clockWidth = clock.PixelWidth * scale, clockHeight = clock.PixelHeight * scale;
                dc.DrawImage(clock, new Rect(x + (294 - clockWidth) / 2, y + 27, clockWidth, clockHeight));
                BitmapSource actions = RenderNatural(toolbar);
                dc.DrawImage(actions, new Rect(x + (294 - actions.PixelWidth) / 2,
                    y + 27 + clockHeight + 10, actions.PixelWidth, actions.PixelHeight));
            }
            dc.DrawText(Label("Independent of your\napplication theme.\n\nHover toolbar shown.", 14, FontWeights.Normal), new Point(662, 478));
        }
        var bitmap = new RenderTargetBitmap(960, 680, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, "floating-clock-themes", "Eight independent overlay styles", "Theme comparison; hover controls shown");
    }

    private static FormattedText Label(string text, double size, FontWeight weight) => new(text,
        CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
        size, new SolidColorBrush(Color.FromRgb(44, 54, 48)), 1);

    private static void SaveTransparencyGallery(AppSettings settings)
    {
        ApplyTheme(settings, AppThemeCatalog.Midnight);
        var overlay = new OverlayWindow();
        overlay.UpdateTime("00:42:18");
        overlay.SetTimerName("Website refresh");
        overlay.SetRunning(true);
        overlay.SetActive(false);
        var toolbar = Node<Border>(overlay, "ActionSurface");
        toolbar.BeginAnimation(UIElement.OpacityProperty, null);
        toolbar.Opacity = 1;
        toolbar.RenderTransform = new TranslateTransform();
        string[] themes = [OverlayThemeCatalog.AcanthusDarkElegantOlive, OverlayThemeCatalog.PixelDeckNight];
        double[] opacities = [1, 0.5, 0];
        var backdropA = new SolidColorBrush(Color.FromRgb(92, 110, 101));
        var backdropB = new SolidColorBrush(Color.FromRgb(143, 156, 143));
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(231, 234, 229)), null, new Rect(0, 0, 960, 550));
            dc.DrawText(Label("Background opacity — the clock stays readable", 18, FontWeights.SemiBold), new Point(18, 14));
            string[] headings = ["100%  ·  Opaque", "50%  ·  See-through", "0%  ·  No background"];
            for (int column = 0; column < 3; column++)
                dc.DrawText(Label(headings[column], 14, FontWeights.SemiBold), new Point(26 + column * 314, 52));

            for (int row = 0; row < themes.Length; row++)
            {
                int y = 92 + row * 230;
                dc.DrawText(Label(themes[row], 13, FontWeights.SemiBold), new Point(18, y));
                overlay.ApplyTheme(themes[row], settings.ThemeMode);
                Color? textColor = null, borderColor = null;
                for (int column = 0; column < opacities.Length; column++)
                {
                    int x = 16 + column * 314;
                    Rect desktop = new(x, y + 24, 298, 132);
                    dc.PushClip(new RectangleGeometry(desktop, 6, 6));
                    // A repeated sample backdrop makes transparency visible. It
                    // is not part of the overlay's production rendering.
                    for (int tileY = 0; tileY < 6; tileY++)
                    for (int tileX = 0; tileX < 13; tileX++)
                        dc.DrawRectangle((tileX + tileY) % 2 == 0 ? backdropA : backdropB,
                            null, new Rect(x + tileX * 24, y + 24 + tileY * 24, 24, 24));
                    dc.Pop();
                    overlay.ApplySettings(Colors.White, Colors.Black, 40, 1, "Cascadia Mono", opacities[column], useThemeTextColor: true);
                    var surface = Node<Border>(overlay, "OverlayBackgroundSurface");
                    var timer = Node<TextBlock>(overlay, "TimeText");
                    var border = Node<Border>(overlay, "OverlayBorder");
                    Color currentText = ((SolidColorBrush)timer.Foreground).Color;
                    Color currentBorder = ((SolidColorBrush)border.BorderBrush).Color;
                    textColor ??= currentText;
                    borderColor ??= currentBorder;
                    if (((SolidColorBrush)surface.Background).Color.A != (byte)Math.Round(opacities[column] * 255)
                        || currentText != textColor || currentBorder != borderColor
                        || timer.Opacity != 1 || border.Opacity != 1 || toolbar.Opacity != 1
                        || ((SolidColorBrush)toolbar.Background).Color.A != 255)
                        throw new InvalidOperationException("Transparency example faded more than the background.");
                    BitmapSource clock = RenderNatural((FrameworkElement)overlay.Content);
                    double scale = Math.Min(1, 278d / clock.PixelWidth);
                    double width = clock.PixelWidth * scale, height = clock.PixelHeight * scale;
                    dc.DrawImage(clock, new Rect(x + (298 - width) / 2, y + 32, width, height));
                    BitmapSource actions = RenderNatural(toolbar);
                    dc.DrawImage(actions, new Rect(x + (298 - actions.PixelWidth) / 2,
                        y + 32 + height + 6, actions.PixelWidth, actions.PixelHeight));
                }
            }
            dc.DrawText(Label("Only the background fades. Text, border, ornaments and hover controls stay visible.", 13, FontWeights.Normal), new Point(18, 521));
        }
        var bitmap = new RenderTargetBitmap(960, 550, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(bitmap, "floating-clock-transparency", "Elegant Olive and Pixel Deck Night",
            "Actual WPF clock surfaces at 100%, 50% and 0% opacity over a sample backdrop");
    }

    private static RenderTargetBitmap Render(FrameworkElement root, int width, int height, int outputWidth)
    {
        if (root is Panel panel) panel.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        double scale = (double)outputWidth / width;
        var bitmap = new RenderTargetBitmap(outputWidth, (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static BitmapSource RenderNatural(FrameworkElement root)
    {
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        // DesiredSize includes the popup surface's top margin. ActualHeight
        // excludes that margin and would clip the lower edge of the toolbar.
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.DesiredSize.Width), (int)Math.Ceiling(root.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string name, string theme, string view)
    {
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        if (pixels.Distinct().Take(100).Count() < 100 || pixels.Count(p => (p & unchecked((int)0xFF000000)) != 0) < pixels.Length / 2)
            throw new InvalidOperationException(name + " is blank or not a real rendered screenshot.");
        bool textured = name is "controller-pixel-deck-night" or "controller-pixel-deck-day" or "settings-light-ring";
        BitmapEncoder encoder = textured
            ? new JpegBitmapEncoder { QualityLevel = 90 }
            : new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0)));
        string fileName = name + (textured ? ".jpg" : ".png");
        string path = Path.Combine(Output, fileName);
        using (var stream = File.Create(path)) encoder.Save(stream);
        TotalImageBytes += new FileInfo(path).Length;
        Captures.Add(new { file = fileName, theme, view, width = bitmap.PixelWidth, height = bitmap.PixelHeight,
            bytes = new FileInfo(path).Length, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) });
        Console.WriteLine($"{name}: {bitmap.PixelWidth}x{bitmap.PixelHeight}, {new FileInfo(path).Length:N0} bytes");
    }

    private static T Node<T>(Window window, string name) where T : class
        => window.FindName(name) as T ?? throw new InvalidOperationException("Missing control " + name);
    private static string Slug(string value) => value.ToLowerInvariant().Replace(' ', '-');
    private static void Invoke(object target, string method)
        => target.GetType().GetMethod(method, Hidden)!.Invoke(target, null);
    private static void AssertPreview(SettingsWindow window)
    {
        if ((bool)typeof(SettingsWindow).GetField("_previewFailureReported", Hidden)!.GetValue(window)!)
            throw new InvalidOperationException("The Settings preview failed to render.");
    }
}
