using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using StopwatchOverlay.Themes;
using Xunit;

namespace StopwatchOverlay.Tests;

[CollectionDefinition("Acanthus visual resources", DisableParallelization = true)]
public sealed class AcanthusVisualResourcesCollection;

[Collection("Acanthus visual resources")]
public sealed class AcanthusThemeResourcesTests
{
    private static readonly string[] ProtectedPalettes =
        ["Midnight.xaml", "Daylight.xaml", "PixelDeck.xaml", "PixelDeckDay.xaml"];

    [Fact]
    public void AcanthusPalette_KeepsTheExistingSemanticTokenContract()
    {
        RunSta(() =>
        {
            ResourceDictionary acanthus = Load("Acanthus.xaml");
            string[] acanthusKeys = Keys(acanthus);
            Assert.Equal(74, acanthusKeys.Length);
            foreach (string file in ProtectedPalettes)
            {
                ResourceDictionary other = Load(file);
                Assert.Equal(Keys(other), acanthusKeys);
                foreach (string key in acanthusKeys)
                    Assert.Equal(other[key].GetType(), acanthus[key].GetType());
            }
            Assert.Equal(Color.FromRgb(44, 41, 36), ColorOf(acanthus, "TimerTextBrush"));
            Assert.Equal(Color.FromRgb(68, 81, 64), ColorOf(acanthus, "AccentBrush"));
        });
    }

    [Fact]
    public void AcanthusTextAndIcons_MaintainContrastAcrossNormalHoverAndPressedStates()
    {
        RunSta(() =>
        {
            ResourceDictionary palette = Load("Acanthus.xaml");
            foreach ((string foreground, string background) in new[]
            {
                ("PrimaryTextBrush", "SurfaceBrush"),
                ("SecondaryTextBrush", "SurfaceBrush"),
                ("OnActionTextBrush", "PrimaryActionBrush"),
                ("OnActionTextBrush", "DangerActionBrush"),
                ("NeutralButtonHoverForegroundBrush", "NeutralButtonHoverBackgroundBrush"),
                ("NeutralButtonPressedForegroundBrush", "NeutralButtonPressedBackgroundBrush"),
                ("OverlayActionForegroundBrush", "OverlayToolbarSurfaceBrush"),
                ("OverlayActionForegroundBrush", "OverlayHoverBrush"),
                ("OverlayActionForegroundBrush", "OverlayPressedBrush")
            })
            {
                double ratio = Contrast(ColorOf(palette, foreground), ColorOf(palette, background));
                Assert.True(ratio >= 4.5, $"{foreground} on {background}: {ratio:F2}:1");
            }
            Assert.Equal(255, ColorOf(palette, "OverlayToolbarSurfaceBrush").A);
            Assert.Equal(1d, Assert.IsType<SolidColorBrush>(palette["OverlayToolbarSurfaceBrush"]).Opacity);
        });
    }

    [Fact]
    public void AcanthusScope_InheritsAndReversesAllOverridesInTheSameVisualTree()
    {
        RunSta(() =>
        {
            // Local dictionaries exercise actual WPF resource invalidation and
            // inherited triggers without a second global Application singleton,
            // application startup, real settings, or any persistence access.
            var window = new Window();
            window.Resources.MergedDictionaries.Add(Load("AcanthusStyles.xaml"));
            var windowStyle = new Style(typeof(Window));
            windowStyle.Setters.Add(new Setter(AcanthusVisual.ScopeProperty,
                new DynamicResourceExtension("OrnamentVisibility")));
            window.Style = windowStyle;
            var content = new StackPanel();
            var hero = new Border { Style = (Style)window.FindResource("AcanthusTimerHero") };
            var timer = new TextBlock { Text = "00:05:10", Style = (Style)window.FindResource("AcanthusControllerTime") };
            hero.Child = timer;
            var heading = new TextBlock { Text = "Stopwatch Overlay", Style = (Style)window.FindResource("AcanthusHeaderTitle") };
            var navigation = new ListBoxItem { Content = "Appearance", Style = (Style)window.FindResource("AcanthusNavigationItem") };
            content.Children.Add(heading);
            content.Children.Add(hero);
            content.Children.Add(navigation);
            window.Content = content;

            Style timerStyle = timer.Style;
            foreach (string file in ProtectedPalettes)
            {
                ApplyPalette(window, content, file);
                Assert.Equal(Visibility.Collapsed, AcanthusVisual.GetScope(timer));
                Assert.Equal(38, timer.FontSize);
                Assert.Equal(HorizontalAlignment.Center, timer.HorizontalAlignment);
                Assert.Equal(new Thickness(20, 17, 20, 17), hero.Padding);
                Assert.Equal(0, hero.MinHeight);
                Assert.Equal(17, heading.FontSize);
                ControlTemplate? originalNavigationTemplate = navigation.Template;
                string originalFont = timer.FontFamily.Source;
                Color originalTimerColor = Assert.IsType<SolidColorBrush>(timer.Foreground).Color;
                Color originalHeroColor = Assert.IsType<SolidColorBrush>(hero.Background).Color;

                ApplyPalette(window, content, "Acanthus.xaml");
                Assert.True(timerStyle.IsSealed);
                Assert.Same(timerStyle, timer.Style);
                Assert.Equal(Visibility.Visible, AcanthusVisual.GetScope(window));
                Assert.Equal(Visibility.Visible, AcanthusVisual.GetScope(timer));
                Assert.Equal(54, timer.FontSize);
                Assert.Equal(HorizontalAlignment.Left, timer.HorizontalAlignment);
                Assert.Equal(new Thickness(32, 24, 32, 24), hero.Padding);
                Assert.Equal(178, hero.MinHeight);
                Assert.Equal(18, heading.FontSize);
                Assert.Equal(Color.FromRgb(251, 248, 241), Assert.IsType<SolidColorBrush>(hero.Background).Color);
                Assert.NotNull(navigation.Template);
                Assert.NotSame(originalNavigationTemplate, navigation.Template);

                ApplyPalette(window, content, file);
                Assert.Same(timerStyle, timer.Style);
                Assert.Equal(Visibility.Collapsed, AcanthusVisual.GetScope(window));
                Assert.Equal(Visibility.Collapsed, AcanthusVisual.GetScope(timer));
                Assert.Equal(38, timer.FontSize);
                Assert.Equal(HorizontalAlignment.Center, timer.HorizontalAlignment);
                Assert.Equal(new Thickness(20, 17, 20, 17), hero.Padding);
                Assert.Equal(0, hero.MinHeight);
                Assert.Equal(17, heading.FontSize);
                Assert.Equal(originalFont, timer.FontFamily.Source);
                Assert.Equal(originalTimerColor, Assert.IsType<SolidColorBrush>(timer.Foreground).Color);
                Assert.Equal(originalHeroColor, Assert.IsType<SolidColorBrush>(hero.Background).Color);
                Assert.Same(originalNavigationTemplate, navigation.Template);
            }
            window.Close();
        });
    }

    [Fact]
    public void AcanthusOrnaments_LoadAsFreezableDrawingsWithExportedViewboxBounds()
    {
        RunSta(() =>
        {
            ResourceDictionary ornaments = Load("AcanthusOrnaments.xaml");
            foreach ((string key, double width, double height) in new[]
            {
                ("AcanthusCrestImage", 28d, 28d),
                ("AcanthusCornerImage", 18d, 16d),
                ("AcanthusDividerImage", 684d, 8d),
                ("AcanthusHeroFrameImage", 716d, 178d)
            })
            {
                DrawingImage ornament = Assert.IsType<DrawingImage>(ornaments[key]);
                Assert.NotNull(ornament.Drawing);
                Assert.True(ornament.CanFreeze);
                Assert.Equal(new Rect(0, 0, width, height), ornament.Drawing.Bounds);
                Assert.NotEmpty(Assert.IsType<DrawingGroup>(ornament.Drawing).Children);
            }
        });
    }

    [Fact]
    public void AcanthusTypography_ResolvesBundledFontFilesInsteadOfSystemFallbacks()
    {
        RunSta(() =>
        {
            ResourceDictionary palette = Load("Acanthus.xaml");
            foreach ((string key, string familyName, FontWeight weight) in new[]
            {
                ("AppFontFamily", "Inter", FontWeights.Normal),
                ("AppFontFamily", "Inter", FontWeights.Medium),
                ("AppFontFamily", "Inter", FontWeights.SemiBold),
                ("DisplayHeadingFontFamily", "Cormorant Garamond", FontWeights.Normal),
                ("DisplayHeadingFontFamily", "Cormorant Garamond", FontWeights.SemiBold),
                ("ThemeTimerFontFamily", "Cascadia Mono", FontWeights.Normal),
                ("ThemeTimerFontFamily", "Cascadia Mono", FontWeights.SemiBold)
            })
            {
                FontFamily family = Assert.IsType<FontFamily>(palette[key]);
                Assert.Contains("/Assets/Fonts/Acanthus/#" + familyName, family.Source);
                var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface glyph), $"{key}: {family.Source}");
                Assert.Contains("/assets/fonts/acanthus/", glyph.FontUri.ToString().ToLowerInvariant());
                Assert.Equal(weight, glyph.Weight);
                Assert.True(glyph.CharacterToGlyphMap.ContainsKey('A'));
                Assert.True(glyph.CharacterToGlyphMap.ContainsKey('0'));
                Assert.True(glyph.CharacterToGlyphMap.ContainsKey(':'));
            }
            Assert.Equal(
                Assert.IsType<FontFamily>(palette["ThemeTimerFontFamily"]).Source,
                Assert.IsType<FontFamily>(palette["ThemeMonoFontFamily"]).Source);
        });
    }

    [Fact]
    public void AcanthusStyleOverride_RestoresNativeImplicitLookupWhenInheritedScopeClears()
    {
        RunSta(() =>
        {
            ResourceDictionary styles = Load("AcanthusStyles.xaml");
            var parent = new StackPanel();
            AcanthusVisual.SetScope(parent, Visibility.Collapsed);
            foreach ((FrameworkElement control, string key) in new[]
            {
                ((FrameworkElement)new ListBoxItem(), "AcanthusNavigationItem"),
                ((FrameworkElement)new ScrollViewer(), "AcanthusInspectorContent")
            })
            {
                parent.Children.Add(control);
                Style acanthusStyle = Assert.IsType<Style>(styles[key]);
                AcanthusVisual.SetOverrideStyle(control, acanthusStyle);
                Assert.Same(DependencyProperty.UnsetValue, control.ReadLocalValue(FrameworkElement.StyleProperty));

                for (int repeat = 0; repeat < 3; repeat++)
                {
                    AcanthusVisual.SetScope(parent, Visibility.Visible);
                    Assert.Equal(Visibility.Visible, AcanthusVisual.GetScope(control));
                    Assert.Same(acanthusStyle, control.ReadLocalValue(FrameworkElement.StyleProperty));
                    AcanthusVisual.SetScope(parent, Visibility.Collapsed);
                    Assert.Equal(Visibility.Collapsed, AcanthusVisual.GetScope(control));
                    Assert.Same(DependencyProperty.UnsetValue, control.ReadLocalValue(FrameworkElement.StyleProperty));
                }
            }
        });
    }

    [Fact]
    public void AcanthusStyleOverride_NeverReplacesOrClearsAnotherLocalStyleOrBinding()
    {
        RunSta(() =>
        {
            Style acanthusStyle = Assert.IsType<Style>(Load("AcanthusStyles.xaml")["AcanthusNavigationItem"]);
            var customStyle = new Style(typeof(ListBoxItem));
            customStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7)));
            var parent = new StackPanel();
            var explicitStyleControl = new ListBoxItem { Style = customStyle };
            var boundStyleControl = new ListBoxItem();
            var externallyReplacedControl = new ListBoxItem();
            var styleProvider = new StyleProvider { Current = customStyle };
            BindingExpressionBase binding = BindingOperations.SetBinding(boundStyleControl, FrameworkElement.StyleProperty,
                new Binding(nameof(StyleProvider.Current)) { Source = styleProvider });
            parent.Children.Add(explicitStyleControl);
            parent.Children.Add(boundStyleControl);
            parent.Children.Add(externallyReplacedControl);
            foreach (FrameworkElement control in parent.Children)
                AcanthusVisual.SetOverrideStyle(control, acanthusStyle);

            AcanthusVisual.SetScope(parent, Visibility.Visible);
            Assert.Same(customStyle, explicitStyleControl.ReadLocalValue(FrameworkElement.StyleProperty));
            Assert.Same(binding, boundStyleControl.ReadLocalValue(FrameworkElement.StyleProperty));
            Assert.Same(customStyle, boundStyleControl.Style);
            Assert.Same(acanthusStyle, externallyReplacedControl.Style);
            externallyReplacedControl.Style = customStyle;

            AcanthusVisual.SetScope(parent, Visibility.Collapsed);
            Assert.Same(customStyle, explicitStyleControl.ReadLocalValue(FrameworkElement.StyleProperty));
            Assert.Same(binding, boundStyleControl.ReadLocalValue(FrameworkElement.StyleProperty));
            Assert.Same(customStyle, externallyReplacedControl.ReadLocalValue(FrameworkElement.StyleProperty));
            AcanthusVisual.SetScope(parent, Visibility.Visible);
            Assert.Same(customStyle, externallyReplacedControl.Style);
            Assert.Same(binding, boundStyleControl.ReadLocalValue(FrameworkElement.StyleProperty));
        });
    }

    [Theory]
    [InlineData("Pause  ·  Win+F5", "Pause")]
    [InlineData("Start  ·  Ctrl+Shift+S", "Start")]
    [InlineData("+ New timer", "+ New timer")]
    public void AcanthusActionLabelTemplate_StripsOnlyPresentationAndPreservesSourceContent(string label, string expected)
    {
        RunSta(() =>
        {
            ResourceDictionary styles = Load("AcanthusStyles.xaml");
            var converter = Assert.IsType<AcanthusActionLabelConverter>(styles["AcanthusActionLabelConverter"]);
            var button = new Button { Content = label };
            var template = Assert.IsType<DataTemplate>(styles["AcanthusActionLabelTemplate"]);
            var rendered = Assert.IsType<TextBlock>(template.LoadContent());
            rendered.DataContext = button.Content;
            // LoadContent alone does not attach the template to a presenter;
            // complete its deferred DataContext transfer before inspecting it.
            rendered.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.DataBind,
                new Action(() => { }));
            BindingOperations.GetBindingExpression(rendered, TextBlock.TextProperty)!.UpdateTarget();
            Assert.Equal(expected, rendered.Text);
            Assert.Equal(label, button.Content);
            Assert.Same(Binding.DoNothing,
                converter.ConvertBack(expected, typeof(string), null!, CultureInfo.InvariantCulture));
            var nonTextContent = new object();
            Assert.Same(nonTextContent,
                converter.Convert(nonTextContent, typeof(object), null!, CultureInfo.InvariantCulture));
        });
    }

    [Fact]
    public void AcanthusSwitchTemplate_PresentsValueAndDisabledStatesWithoutChangingTheValue()
    {
        RunSta(() =>
        {
            var checkBox = new CheckBox { Content = "Light ring", IsThreeState = true };
            ResourceDictionary palette = Load("Acanthus.xaml");
            foreach (object key in palette.Keys)
                checkBox.Resources[key] = palette[key];
            checkBox.Template = Assert.IsType<ControlTemplate>(Load("AcanthusStyles.xaml")["AcanthusSwitchTemplate"]);
            checkBox.Measure(new Size(200, 40));
            checkBox.Arrange(new Rect(0, 0, 200, 40));
            checkBox.ApplyTemplate();
            var thumb = Assert.IsType<System.Windows.Shapes.Ellipse>(checkBox.Template.FindName("Thumb", checkBox));
            var rail = Assert.IsType<Border>(checkBox.Template.FindName("Rail", checkBox));
            checkBox.IsChecked = false;
            Assert.Equal(HorizontalAlignment.Left, thumb.HorizontalAlignment);
            Assert.Equal(ColorOf(palette, "SurfaceRaisedBrush"), Assert.IsType<SolidColorBrush>(rail.Background).Color);
            checkBox.IsChecked = true;
            Assert.Equal(HorizontalAlignment.Right, thumb.HorizontalAlignment);
            Assert.Equal(ColorOf(palette, "PrimaryActionBrush"), Assert.IsType<SolidColorBrush>(rail.Background).Color);
            checkBox.IsEnabled = false;
            Assert.True(checkBox.IsChecked);
            Assert.Equal(0.4, checkBox.Opacity);
            checkBox.IsEnabled = true;
            Assert.True(checkBox.IsChecked);
            Assert.Equal(1, checkBox.Opacity);
            checkBox.IsChecked = null;
            Assert.Equal(HorizontalAlignment.Center, thumb.HorizontalAlignment);
            Assert.Null(checkBox.IsChecked);
        });
    }

    private sealed class StyleProvider
    {
        public required Style Current { get; init; }
    }

    [Fact]
    public void AcanthusSlider_PreservesValueBindingsBoundsCommandsAndNativeStyleRestoration()
    {
        RunSta(() =>
        {
            ResourceDictionary styles = Load("AcanthusStyles.xaml");
            ResourceDictionary palette = Load("Acanthus.xaml");
            // All six product ranges share one STA, like the real Settings
            // window. WPF caches native template resources per application and
            // cannot safely reuse those mutable resources across theory STAs.
            foreach ((double minimum, double maximum, double initialValue, double largeChange) in new[]
            {
                (16d, 120d, 48d, 10d), (1d, 5d, 2d, 1d), (0d, 100d, 82d, 10d),
                (10d, 45d, 25d, 5d), (10d, 100d, 75d, 10d), (5d, 100d, 20d, 10d)
            })
            foreach (bool reversed in new[] { false, true })
            {
                var parent = new StackPanel();
                foreach (object key in palette.Keys)
                    parent.Resources[key] = palette[key];
                var model = new SliderValueModel { Value = initialValue };
                var slider = new Slider
                {
                    Minimum = minimum, Maximum = maximum, SmallChange = 1,
                    LargeChange = largeChange, TickFrequency = 1,
                    IsSnapToTickEnabled = true, IsDirectionReversed = reversed
                };
                BindingOperations.SetBinding(slider, RangeBase.ValueProperty,
                    new Binding(nameof(SliderValueModel.Value))
                    {
                        Source = model, Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                parent.Children.Add(slider);
                AcanthusVisual.SetOverrideStyle(slider, Assert.IsType<Style>(styles["AcanthusSettingsSlider"]));
                Assert.Same(DependencyProperty.UnsetValue, slider.ReadLocalValue(FrameworkElement.StyleProperty));
                Assert.Equal(initialValue, slider.Value);

                AcanthusVisual.SetScope(parent, Visibility.Visible);
                parent.Measure(new Size(400, 100));
                parent.Arrange(new Rect(0, 0, 400, 100));
                slider.ApplyTemplate();
                parent.UpdateLayout();
                var track = Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));
                Assert.Equal(initialValue, slider.Value);
                Assert.Equal(initialValue, model.Value);
                Assert.Equal(minimum, track.Minimum);
                Assert.Equal(maximum, track.Maximum);
                Assert.Equal(initialValue, track.Value);
                Assert.Equal(reversed, track.IsDirectionReversed);
                Assert.NotNull(track.Thumb);
                Assert.Same(Slider.IncreaseLarge, track.IncreaseRepeatButton.Command);
                Assert.Same(Slider.DecreaseLarge, track.DecreaseRepeatButton.Command);

                double midpoint = minimum + Math.Floor((maximum - minimum) / 2);
                track.SetCurrentValue(Track.ValueProperty, midpoint);
                Assert.Equal(midpoint, slider.Value);
                Assert.Equal(midpoint, model.Value);
                Assert.NotNull(BindingOperations.GetBindingExpression(track, Track.ValueProperty));
                Assert.NotNull(BindingOperations.GetBindingExpression(slider, RangeBase.ValueProperty));

                model.Value = minimum;
                Assert.Equal(minimum, slider.Value);
                Assert.Equal(minimum, track.Value);
                Assert.True(Slider.IncreaseLarge.CanExecute(null, slider));
                Slider.IncreaseLarge.Execute(null, slider);
                Assert.Equal(Math.Min(maximum, minimum + largeChange), slider.Value);
                Assert.Equal(slider.Value, model.Value);
                Slider.DecreaseLarge.Execute(null, slider);
                Assert.Equal(minimum, slider.Value);
                model.Value = maximum;
                Slider.IncreaseLarge.Execute(null, slider);
                Assert.Equal(maximum, slider.Value);

                AcanthusVisual.SetScope(parent, Visibility.Collapsed);
                Assert.Same(DependencyProperty.UnsetValue, slider.ReadLocalValue(FrameworkElement.StyleProperty));
                Assert.Equal(maximum, slider.Value);
                Assert.Equal(maximum, model.Value);
                Assert.NotNull(BindingOperations.GetBindingExpression(slider, RangeBase.ValueProperty));
                Assert.Equal(minimum, slider.Minimum);
                Assert.Equal(maximum, slider.Maximum);
                Assert.Equal(reversed, slider.IsDirectionReversed);
            }
        });
    }

    private sealed class SliderValueModel : INotifyPropertyChanged
    {
        private double _value;
        public double Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static void ApplyPalette(Window window, FrameworkElement content, string file)
    {
        ResourceDictionary palette = Load(file);
        foreach (object key in palette.Keys)
            window.Resources[key] = palette[key] is Freezable item ? item.Clone() : palette[key];
        content.Measure(new Size(800, 600));
        content.Arrange(new Rect(0, 0, 800, 600));
        content.UpdateLayout();
    }

    private static string[] Keys(ResourceDictionary dictionary)
        => dictionary.Keys.Cast<string>().OrderBy(key => key, StringComparer.Ordinal).ToArray();

    private static ResourceDictionary Load(string name)
        => (ResourceDictionary)Application.LoadComponent(new Uri($"/StopwatchOverlay;component/Themes/{name}", UriKind.Relative));

    private static Color ColorOf(ResourceDictionary palette, string key)
        => Assert.IsType<SolidColorBrush>(palette[key]).Color;

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value)
        {
            double channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color)
            => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        double one = Luminance(first);
        double two = Luminance(second);
        return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
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
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
