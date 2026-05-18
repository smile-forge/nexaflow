using Nexaflow.Features.Json.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.Json.Views;

/// <summary>
/// Returns Visible when the NodeViewMode value matches the ConverterParameter string ("Text" or "Table").
/// </summary>
[ValueConversion(typeof(NodeViewMode), typeof(Visibility))]
internal sealed class ViewModeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not NodeViewMode mode || parameter is not string param) return Visibility.Collapsed;
        return mode.ToString() == param ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
