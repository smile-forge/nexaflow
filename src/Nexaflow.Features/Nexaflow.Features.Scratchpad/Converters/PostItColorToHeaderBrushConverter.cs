using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Converters;

/// <summary>Returns a slightly darker brush for the post-it header strip.</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class PostItColorToHeaderBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Color> _headers = new()
    {
        ["Yellow"] = Color.FromRgb(0xFF, 0xE5, 0x57),
        ["Blue"]   = Color.FromRgb(0x42, 0xA5, 0xF5),
        ["Green"]  = Color.FromRgb(0x66, 0xBB, 0x6A),
        ["Pink"]   = Color.FromRgb(0xEC, 0x40, 0x7A),
        ["Orange"] = Color.FromRgb(0xFF, 0x87, 0x00),
        ["Purple"] = Color.FromRgb(0xAB, 0x47, 0xBC),
        ["White"]  = Color.FromRgb(0xE0, 0xE0, 0xE0),
    };

    private static readonly Brush _fallback = new SolidColorBrush(Color.FromRgb(0xFF, 0xE5, 0x57));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name || !_headers.TryGetValue(name, out var color))
            return _fallback;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
