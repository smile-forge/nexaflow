using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// A slider for an edit value that has a neutral position: double-clicking it goes back there. A drag of its thumb
/// raises the ordinary <c>Thumb.DragStarted</c> / <c>DragCompleted</c>, which the edit view turns into one undo step.
/// Given a <see cref="Label"/>, it is one row of a panel: the name, the rail, and the value as <see cref="ValueFormat"/> writes it.
/// </summary>
public class AdjustmentSlider : Slider
{
    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(AdjustmentSlider), new PropertyMetadata(0.0));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(AdjustmentSlider), new PropertyMetadata(null));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(AdjustmentSlider), new PropertyMetadata("0", (d, _) => ((AdjustmentSlider)d).ShowValue()));

    private static readonly DependencyPropertyKey ValueTextKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueText), typeof(string), typeof(AdjustmentSlider), new PropertyMetadata("0"));

    public static readonly DependencyProperty ValueTextProperty = ValueTextKey.DependencyProperty;

    /// <summary>The neutral value a double-click restores.</summary>
    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    /// <summary>The value's name, at the head of its row; none for a slider laid out by its panel.</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>How the value is written at the end of its row — a .NET number format, in the user's culture.</summary>
    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    /// <summary>The value as <see cref="ValueFormat"/> writes it.</summary>
    public string ValueText => (string)GetValue(ValueTextProperty);

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        ShowValue();
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        Value = DefaultValue;
        e.Handled = true;
        base.OnMouseDoubleClick(e);
    }

    private void ShowValue() => SetValue(ValueTextKey, Value.ToString(ValueFormat, CultureInfo.CurrentCulture));
}
