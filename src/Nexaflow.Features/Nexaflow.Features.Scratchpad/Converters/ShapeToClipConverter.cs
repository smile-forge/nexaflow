using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Converters;

/// <summary>
/// MultiValueConverter: [string shape, double width, double height] → Geometry clip.
/// </summary>
public sealed class ShapeToClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [string shape, double w, double h] || w <= 0 || h <= 0)
            return DependencyProperty.UnsetValue;

        return shape switch
        {
            "Rounded" => new RectangleGeometry(new Rect(0, 0, w, h), 14, 14),
            _         => new RectangleGeometry(new Rect(0, 0, w, h)),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
