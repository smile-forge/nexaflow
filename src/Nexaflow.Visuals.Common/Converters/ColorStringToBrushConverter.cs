using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Converters;

[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { }
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is SolidColorBrush b ? b.Color.ToString() : "#00000000";
}
