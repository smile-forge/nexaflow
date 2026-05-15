using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Converters;

/// <summary>Maps a post-it color name to a LinearGradientBrush for the note background.</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class PostItColorToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, (Color top, Color bottom)> _palette = new()
    {
        ["Yellow"] = (Color.FromRgb(0xFF, 0xF5, 0x9D), Color.FromRgb(0xFF, 0xF1, 0x76)),
        ["Blue"]   = (Color.FromRgb(0x90, 0xCA, 0xF9), Color.FromRgb(0x64, 0xB5, 0xF6)),
        ["Green"]  = (Color.FromRgb(0xA5, 0xD6, 0xA7), Color.FromRgb(0x81, 0xC7, 0x84)),
        ["Pink"]   = (Color.FromRgb(0xF4, 0x8F, 0xB1), Color.FromRgb(0xF0, 0x62, 0x92)),
        ["Orange"] = (Color.FromRgb(0xFF, 0xCC, 0x80), Color.FromRgb(0xFF, 0xA7, 0x26)),
        ["Purple"] = (Color.FromRgb(0xCE, 0x93, 0xD8), Color.FromRgb(0xBA, 0x68, 0xC8)),
        ["White"]  = (Color.FromRgb(0xFA, 0xFA, 0xFA), Color.FromRgb(0xF5, 0xF5, 0xF5)),
    };

    private static readonly Brush _fallback = new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0x9D));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name || !_palette.TryGetValue(name, out var colors))
            return _fallback;

        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint   = new System.Windows.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(colors.top,    0));
        brush.GradientStops.Add(new GradientStop(colors.bottom, 1));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
