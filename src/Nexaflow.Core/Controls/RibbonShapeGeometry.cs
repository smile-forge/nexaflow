using System.Windows;
using System.Windows.Media;
using Nexaflow.Core.Models;
using System.Collections.Concurrent;

namespace Nexaflow.Core.Controls;

/// <summary>
/// The outlines a <see cref="RibbonItemButton"/> draws. Frame shapes are sized to the button, so they are built
/// when its size changes; badge shapes are a fixed size behind the icon, so each (shape, size, weight) is built once
/// and shared, frozen, by every button that uses it.
/// </summary>
public static class RibbonShapeGeometry
{
    /// <summary>Badge diameter behind a full button's icon (24px glyph) and a compact one's (16px glyph).</summary>
    public const double FullBadge = 34, CompactBadge = 22;

    // Frozen, so one geometry serves every window — and windows may run on their own UI threads.
    private static readonly ConcurrentDictionary<(RibbonButtonShape, bool, RibbonBorderWeight), Geometry> Badges = new();

    public static bool IsBadge(RibbonButtonShape shape)
        => shape is RibbonButtonShape.Circle or RibbonButtonShape.Squircle or RibbonButtonShape.Hexagon;

    public static double StrokeThickness(RibbonBorderWeight weight) => weight switch
    {
        RibbonBorderWeight.Thin   => 1,
        RibbonBorderWeight.Medium => 2,
        RibbonBorderWeight.Thick  => 3,
        _                         => 0,
    };

    public static double BadgeSize(bool compact) => compact ? CompactBadge : FullBadge;

    /// <summary>
    /// The outline around the whole button, inset by half the stroke so a border is never clipped. A badge shape's
    /// frame is the <see cref="RibbonButtonShape.Standard"/> one — the badge carries the shape.
    /// </summary>
    public static Geometry Frame(RibbonButtonShape shape, Size size, bool compact, double stroke)
    {
        var inset = stroke / 2;
        var rect  = new Rect(inset, inset, Math.Max(0, size.Width - stroke), Math.Max(0, size.Height - stroke));
        var half  = Math.Min(rect.Width, rect.Height) / 2;

        Geometry geometry = shape switch
        {
            RibbonButtonShape.Rounded => new RectangleGeometry(rect, Math.Min(compact ? 6 : 10, half), Math.Min(compact ? 6 : 10, half)),
            // Fully round ends on a compact button; on a tall one, full rounding would make a circle that clips the label.
            RibbonButtonShape.Pill    => new RectangleGeometry(rect, compact ? half : Math.Min(half, 22), compact ? half : Math.Min(half, 22)),
            RibbonButtonShape.Leaf    => Leaf(rect, Math.Min(compact ? 10 : 16, half), Math.Min(2, half)),
            _                         => new RectangleGeometry(rect, compact ? 4 : 0, compact ? 4 : 0),
        };
        geometry.Freeze();
        return geometry;
    }

    /// <summary>The badge behind the icon, or null for a frame shape.</summary>
    public static Geometry? Badge(RibbonButtonShape shape, bool compact, RibbonBorderWeight weight)
    {
        if (!IsBadge(shape)) return null;

            return Badges.GetOrAdd((shape, compact, weight), static key => BuildBadge(key.Item1, key.Item2, key.Item3));
        }

        private static Geometry BuildBadge(RibbonButtonShape shape, bool compact, RibbonBorderWeight weight)
        {
            var size   = BadgeSize(compact);
        var stroke = StrokeThickness(weight);
        var centre = new Point(size / 2, size / 2);
        var radius = (size - stroke) / 2;

        Geometry geometry = shape switch
        {
            RibbonButtonShape.Circle   => new EllipseGeometry(centre, radius, radius),
            RibbonButtonShape.Squircle => Polygon(Squircle(centre, radius)),
            _                          => Polygon(Hexagon(centre, radius)),
        };
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Big radius on the top-left and bottom-right corners, small on the other two.</summary>
    private static Geometry Leaf(Rect r, double big, double small)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + big, r.Top), isFilled: true, isClosed: true);
            c.LineTo(new Point(r.Right - small, r.Top), true, false);
            c.ArcTo(new Point(r.Right, r.Top + small), new Size(small, small), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Right, r.Bottom - big), true, false);
            c.ArcTo(new Point(r.Right - big, r.Bottom), new Size(big, big), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Left + small, r.Bottom), true, false);
            c.ArcTo(new Point(r.Left, r.Bottom - small), new Size(small, small), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Left, r.Top + big), true, false);
            c.ArcTo(new Point(r.Left + big, r.Top), new Size(big, big), 0, false, SweepDirection.Clockwise, true, false);
        }
        return g;
    }

    /// <summary>A superellipse |x|⁴ + |y|⁴ = r⁴ — the rounded square app icons wear.</summary>
    private static IEnumerable<Point> Squircle(Point centre, double radius)
    {
        const int steps = 64;
        for (var i = 0; i < steps; i++)
        {
            var t = 2 * Math.PI * i / steps;
            double cos = Math.Cos(t), sin = Math.Sin(t);
            yield return new Point(
                centre.X + radius * Math.Sign(cos) * Math.Sqrt(Math.Abs(cos)),
                centre.Y + radius * Math.Sign(sin) * Math.Sqrt(Math.Abs(sin)));
        }
    }

    /// <summary>A pointy-topped hexagon.</summary>
    private static IEnumerable<Point> Hexagon(Point centre, double radius)
    {
        for (var i = 0; i < 6; i++)
        {
            var t = Math.PI / 3 * i - Math.PI / 2;
            yield return new Point(centre.X + radius * Math.Cos(t), centre.Y + radius * Math.Sin(t));
        }
    }

    private static Geometry Polygon(IEnumerable<Point> points)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var first = true;
            foreach (var p in points)
            {
                if (first) { c.BeginFigure(p, isFilled: true, isClosed: true); first = false; }
                else c.LineTo(p, true, true);
            }
        }
        return g;
    }
}
