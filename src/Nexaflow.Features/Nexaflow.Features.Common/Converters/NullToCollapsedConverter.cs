using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Features.Common.Converters;

/// <summary>null → Visible, non-null → Collapsed  (inverse null check)</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
