using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.Converters;

public class RibbonKindVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is RibbonItemKind kind && p is string s
            && Enum.TryParse<RibbonItemKind>(s, out var target))
            return kind == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
