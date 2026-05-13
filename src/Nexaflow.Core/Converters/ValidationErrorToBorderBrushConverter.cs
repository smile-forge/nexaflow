using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Core.Converters;

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
