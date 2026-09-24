using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>A <see cref="ColorSpec"/> to the brush it paints — a swatch token through the live theme. The theme
/// default has no brush of its own and comes back unset, so a binding's <c>FallbackValue</c> (or the property's own
/// default) shows instead.</summary>
[ValueConversion(typeof(ColorSpec), typeof(System.Windows.Media.Brush))]
public sealed class ColorSpecToBrushConverter : IValueConverter
{
    public static ColorSpecToBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ColorSpec spec && spec.ToBrush() is { } brush ? brush : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
