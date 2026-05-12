using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.Converters;


/// <summary>AI status → green or orange brush</summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public class AiStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green  = new(Color.FromRgb(0x22, 0xD3, 0xA5));
    private static readonly SolidColorBrush Orange = new(Color.FromRgb(0xF9, 0x73, 0x16));
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Orange : Green;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility (true = Visible, false = Collapsed)</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible;
}

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
