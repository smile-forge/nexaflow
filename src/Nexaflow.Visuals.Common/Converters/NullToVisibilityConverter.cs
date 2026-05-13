using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>non-null object → Visible, null → Collapsed</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
