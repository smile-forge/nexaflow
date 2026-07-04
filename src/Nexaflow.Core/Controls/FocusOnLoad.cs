using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Attached behaviour: focuses (and selects, for a <see cref="TextBox"/>) an element when it loads.
/// Used by the shell input-prompt overlay, whose textbox now lives inside a <c>DataTemplate</c> and so
/// can't be focused from the window code-behind by name.
/// </summary>
public static class FocusOnLoad
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(FocusOnLoad),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || !(bool)e.NewValue) return;
        fe.Loaded += (_, _) =>
        {
            fe.Focus();
            if (fe is TextBox tb) tb.SelectAll();
        };
    }
}
