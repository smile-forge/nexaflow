using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Converters;

/// <summary>null → Visible, non-null → Collapsed  (inverse null check)</summary>
/// <summary>
/// null → Visible, anything else → Collapsed: what stands in while there is nothing to show — a placeholder, an empty state.
/// Its opposite, showing a value only while there is one, is <see cref="NullToVisibilityConverter"/>.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
