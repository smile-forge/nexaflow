using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>
/// An angle in degrees to a frozen <see cref="RotateTransform"/> of it, for an element's own rotation. A rotation written
/// into a style's setter is one transform shared by every element the style dresses, so a binding inside it has no
/// element to find its data on; one made per element from its own angle has.
/// </summary>
[ValueConversion(typeof(double), typeof(Transform))]
public sealed class AngleToRotateTransformConverter : IValueConverter
{
    public static AngleToRotateTransformConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double angle) return DependencyProperty.UnsetValue;
        var rotation = new RotateTransform(angle);
        rotation.Freeze();
        return rotation;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
