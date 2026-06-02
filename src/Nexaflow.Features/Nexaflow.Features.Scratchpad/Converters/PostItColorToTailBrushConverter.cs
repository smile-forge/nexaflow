using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Converters;

/// <summary>
/// Maps a post-it colour name to a SOLID brush of its <c>PostIt.&lt;name&gt;.Fill</c> gradient's END stop.
/// Used for the speech-bubble tail: it's a separate element, so painting it with the body's gradient
/// would restart the gradient and mismatch the body. The tail meets the body at its bottom (the
/// gradient end), so the end-stop colour lines up cleanly. Falls back to the brush itself if it isn't
/// a gradient. Mirrors <see cref="PostItColorToBrushConverter"/>'s themed-lookup / missing-key rules.
/// </summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class PostItColorToTailBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string name) return DependencyProperty.UnsetValue;

        var key = $"PostIt.{name}.Fill";
        if (Application.Current?.Resources[key] is not Brush themed)
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return DependencyProperty.UnsetValue;
            throw new InvalidOperationException(
                $"Theme brush '{key}' not found — is the scratchpad theme contribution merged?");
        }

        if (themed is GradientBrush { GradientStops.Count: > 0 } g)
        {
            var end   = g.GradientStops.Aggregate((a, b) => b.Offset >= a.Offset ? b : a);
            var brush = new SolidColorBrush(end.Color);
            brush.Freeze();
            return brush;
        }

        return themed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
