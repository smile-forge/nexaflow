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

/// <summary>null string → default border brush; non-null → red (used for validation errors).</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public class ValidationErrorToBorderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Red     = new(Color.FromRgb(0xD0, 0x2F, 0x2F));
    private static readonly SolidColorBrush Default = new(Color.FromRgb(0x2A, 0x30, 0x47));

    static ValidationErrorToBorderBrushConverter()
    {
        Red.Freeze();
        Default.Freeze();
    }

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is string s && !string.IsNullOrEmpty(s) ? Red : Default;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
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
