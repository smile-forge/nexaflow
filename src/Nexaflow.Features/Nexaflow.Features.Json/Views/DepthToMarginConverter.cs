using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.Json.Views;

[ValueConversion(typeof(int), typeof(Thickness))]
internal sealed class DepthToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int depth ? new Thickness(depth * 16, 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
