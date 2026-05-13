using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>true → Visible, false → Collapsed</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible;
}

