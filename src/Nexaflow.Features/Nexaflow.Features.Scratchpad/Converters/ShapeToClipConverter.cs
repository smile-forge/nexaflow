using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Nexaflow.Features.Scratchpad.Converters;

/// <summary>
/// MultiValueConverter: [string shape, double width, double height] → Geometry clip.
/// </summary>
public sealed class ShapeToClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [string shape, double w, double h] || w <= 0 || h <= 0)
            return DependencyProperty.UnsetValue;

        return shape switch
        {
            // SpeechBubble keeps a plain rounded body (full resize + text area); its tail
            // is bolted on as a separate Path in PostItControl, outside this clip.
            "Rounded" or "SpeechBubble" => new RectangleGeometry(new Rect(0, 0, w, h), 14, 14),
            "DiagonalsRounded"          => BuildDiagonalsRounded(w, h, 22),
            _                           => new RectangleGeometry(new Rect(0, 0, w, h)),
        };
    }

    // Rounded on one diagonal: top-left & bottom-right corners rounded, the other two sharp.
    private static Geometry BuildDiagonalsRounded(double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r, 0), isFilled: true, isClosed: true);
            c.LineTo(new Point(w, 0),     true, false);   // top edge → top-right (sharp)
            c.LineTo(new Point(w, h - r), true, false);   // right edge
            c.ArcTo(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false); // bottom-right round
            c.LineTo(new Point(0, h),     true, false);   // bottom edge → bottom-left (sharp)
            c.LineTo(new Point(0, r),     true, false);   // left edge
            c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);     // top-left round
        }
        g.Freeze();
        return g;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
