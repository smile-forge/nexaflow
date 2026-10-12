using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// A slider for an edit value that has a neutral position: double-clicking it goes back there. A drag of its thumb
/// raises the ordinary <c>Thumb.DragStarted</c> / <c>DragCompleted</c>, which the edit view turns into one undo step.
/// </summary>
public class AdjustmentSlider : Slider
{
    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(AdjustmentSlider), new PropertyMetadata(0.0));

    /// <summary>The neutral value a double-click restores.</summary>
    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        Value = DefaultValue;
        e.Handled = true;
        base.OnMouseDoubleClick(e);
    }
}
