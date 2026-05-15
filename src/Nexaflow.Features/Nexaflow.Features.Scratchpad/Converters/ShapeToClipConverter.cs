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
            "Rounded" => new RectangleGeometry(new Rect(0, 0, w, h), 14, 14),
            "Star"    => CreateStarGeometry(w, h),
            "Heart"   => CreateHeartGeometry(w, h),
            "Cloud"   => CreateCloudGeometry(w, h),
            _         => new RectangleGeometry(new Rect(0, 0, w, h)),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Geometry CreateStarGeometry(double w, double h)
    {
        const int pts = 5;
        double cx = w / 2, cy = h / 2;
        double outerR = Math.Min(w, h) / 2 * 0.92;
        double innerR = outerR * 0.42;

        var geo = new StreamGeometry();
        using var ctx = geo.Open();

        for (int i = 0; i < pts * 2; i++)
        {
            double angle = (i * Math.PI / pts) - Math.PI / 2;
            double r     = (i % 2 == 0) ? outerR : innerR;
            var pt = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            if (i == 0) ctx.BeginFigure(pt, isFilled: true, isClosed: true);
            else        ctx.LineTo(pt, isStroked: true, isSmoothJoin: false);
        }

        geo.Freeze();
        return geo;
    }

    private static Geometry CreateHeartGeometry(double w, double h)
    {
        // Classic heart: two Bezier curves meeting at the bottom tip
        double cx = w / 2;
        var geo = new StreamGeometry();
        using var ctx = geo.Open();

        ctx.BeginFigure(new Point(cx, h * 0.88), isFilled: true, isClosed: true);
        // Left lobe
        ctx.BezierTo(
            new Point(w * 0.02, h * 0.55),
            new Point(w * 0.02, h * 0.12),
            new Point(cx, h * 0.33),
            isStroked: true, isSmoothJoin: false);
        // Right lobe
        ctx.BezierTo(
            new Point(w * 0.98, h * 0.12),
            new Point(w * 0.98, h * 0.55),
            new Point(cx, h * 0.88),
            isStroked: true, isSmoothJoin: false);

        geo.Freeze();
        return geo;
    }

    private static Geometry CreateCloudGeometry(double w, double h)
    {
        // Five overlapping ellipses + a flat base rectangle
        var g1   = new EllipseGeometry(new Point(w * 0.20, h * 0.63), w * 0.18, h * 0.20);
        var g2   = new EllipseGeometry(new Point(w * 0.38, h * 0.48), w * 0.22, h * 0.24);
        var g3   = new EllipseGeometry(new Point(w * 0.62, h * 0.48), w * 0.22, h * 0.24);
        var g4   = new EllipseGeometry(new Point(w * 0.80, h * 0.63), w * 0.18, h * 0.20);
        var g5   = new EllipseGeometry(new Point(w * 0.50, h * 0.36), w * 0.20, h * 0.22);
        var base1 = new RectangleGeometry(new Rect(w * 0.02, h * 0.55, w * 0.96, h * 0.41));

        Geometry combined = new CombinedGeometry(GeometryCombineMode.Union, g1, g2);
        combined = new CombinedGeometry(GeometryCombineMode.Union, combined, g3);
        combined = new CombinedGeometry(GeometryCombineMode.Union, combined, g4);
        combined = new CombinedGeometry(GeometryCombineMode.Union, combined, g5);
        combined = new CombinedGeometry(GeometryCombineMode.Union, combined, base1);
        combined.Freeze();
        return combined;
    }
}
