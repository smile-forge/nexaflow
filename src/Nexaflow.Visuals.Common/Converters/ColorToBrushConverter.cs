using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>
/// A <see cref="Color"/> to a frozen brush of it. Binding the colour into a brush written inside the element instead
/// binds a freezable, which has no element of its own to find its data on when a template first makes it.
/// </summary>
[ValueConversion(typeof(Color), typeof(Brush))]
public sealed class ColorToBrushConverter : IValueConverter
{
    public static ColorToBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Color color) return DependencyProperty.UnsetValue;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
