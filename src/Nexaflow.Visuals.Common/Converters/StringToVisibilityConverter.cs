using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>non-null string → Visible, null/empty → Collapsed</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
