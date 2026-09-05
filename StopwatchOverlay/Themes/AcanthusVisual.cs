using System.Windows;
using System;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Data;

namespace StopwatchOverlay.Themes;

/// <summary>
/// Inherited, resource-driven scope for the Acanthus study's visual overrides.
/// The existing palette's OrnamentVisibility resets this scope on every theme
/// switch; no window recreation, persistence, or event subscription is involved.
/// </summary>
public static class AcanthusVisual
{
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached(
        "Scope", typeof(Visibility), typeof(AcanthusVisual),
        new FrameworkPropertyMetadata(Visibility.Collapsed, FrameworkPropertyMetadataOptions.Inherits, ScopeChanged));

    public static Visibility GetScope(DependencyObject element) => (Visibility)element.GetValue(ScopeProperty);
    public static void SetScope(DependencyObject element, Visibility value) => element.SetValue(ScopeProperty, value);

    // Native Fluent controls must keep their implicit style outside Acanthus.
    // Even an empty explicit Style suppresses that lookup. Use this opt-in only
    // on controls with no local Style; removing it restores native theme lookup.
    public static readonly DependencyProperty OverrideStyleProperty = DependencyProperty.RegisterAttached(
        "OverrideStyle", typeof(Style), typeof(AcanthusVisual), new PropertyMetadata(null, ScopeChanged));
    private static readonly DependencyProperty AppliedOverrideProperty = DependencyProperty.RegisterAttached(
        "AppliedOverride", typeof(bool), typeof(AcanthusVisual), new PropertyMetadata(false));

    public static Style? GetOverrideStyle(DependencyObject element) => (Style?)element.GetValue(OverrideStyleProperty);
    public static void SetOverrideStyle(DependencyObject element, Style value) => element.SetValue(OverrideStyleProperty, value);

    private static void ScopeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element || GetOverrideStyle(element) is not Style acanthusStyle)
            return;

        bool applied = (bool)element.GetValue(AppliedOverrideProperty);
        if (GetScope(element) == Visibility.Visible && !applied)
        {
            // Never take ownership of another local style or binding.
            if (element.ReadLocalValue(FrameworkElement.StyleProperty) != DependencyProperty.UnsetValue)
                return;
            element.SetValue(AppliedOverrideProperty, true);
            element.Style = acanthusStyle;
        }
        else if (GetScope(element) != Visibility.Visible && applied)
        {
            if (ReferenceEquals(element.ReadLocalValue(FrameworkElement.StyleProperty), acanthusStyle))
                element.ClearValue(FrameworkElement.StyleProperty);
            element.SetValue(AppliedOverrideProperty, false);
        }
    }

    // Honor the persisted selection. Resolve only Acanthus's named design font
    // to its bundled copy; custom fonts and every other theme remain unchanged.
    public static FontFamily ResolveTimerFont(string family)
        => AppThemeManager.IsAcanthus
           && string.Equals(family, "Cascadia Mono", StringComparison.OrdinalIgnoreCase)
           && Application.Current?.TryFindResource("ThemeTimerFontFamily") is FontFamily bundled
            ? bundled
            : new FontFamily(family);

    public static string PrimaryActionStyleKey(bool hasTimer, bool running)
        => AppThemeManager.IsAcanthus ? "AcanthusPrimaryAction"
            : hasTimer && running ? "StopButton" : "StartButton";
}

/// <summary>Figma's concise action labels, while Content/tooltips keep shortcuts.</summary>
public sealed class AcanthusActionLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string label) return value;
        int shortcut = label.IndexOf('·');
        return shortcut < 0 ? label : label[..shortcut].TrimEnd();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
