using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A lightweight pie-chart element that draws directly via OnRender.
/// Feed it a list of (value, brush) slices; it renders them proportionally.
/// Shows a grey circle when the slice list is empty or all values are zero.
/// </summary>
public class PieChart : FrameworkElement
{
    // ── Dependency properties ──────────────────────────────────────────────

    public static readonly DependencyProperty SlicesProperty =
        DependencyProperty.Register(nameof(Slices),
            typeof(IReadOnlyList<PieSlice>), typeof(PieChart),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DiameterProperty =
        DependencyProperty.Register(nameof(Diameter),
            typeof(double), typeof(PieChart),
            new FrameworkPropertyMetadata(80.0,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public IReadOnlyList<PieSlice>? Slices
    {
        get => (IReadOnlyList<PieSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Diameter, Diameter);

    protected override void OnRender(DrawingContext dc)
    {
        var slices = Slices;
        double r   = Diameter / 2.0;
        var centre = new Point(r, r);

        if (slices is not { Count: > 0 })
        {
            dc.DrawEllipse(Brushes.Gray, null, centre, r, r);
            return;
        }

        double total = 0;
        foreach (var s in slices) total += s.Value;

        if (total <= 0)
        {
            dc.DrawEllipse(Brushes.Gray, null, centre, r, r);
            return;
        }

        // Single-slice or full-pie optimisation
        if (slices.Count == 1)
        {
            dc.DrawEllipse(slices[0].Fill, null, centre, r, r);
            return;
        }

        double startAngle = -Math.PI / 2;   // start at 12 o'clock
        foreach (var slice in slices)
        {
            if (slice.Value <= 0) continue;
            double sweep = (slice.Value / total) * (2 * Math.PI);

            if (Math.Abs(sweep - 2 * Math.PI) < 1e-9)
            {
                dc.DrawEllipse(slice.Fill, null, centre, r, r);
                return;
            }

            double endAngle = startAngle + sweep;
            var geo = BuildSliceGeometry(centre, r, startAngle, endAngle);
            dc.DrawGeometry(slice.Fill, null, geo);
            startAngle = endAngle;
        }
    }

    private static StreamGeometry BuildSliceGeometry(
        Point centre, double r,
        double startAngle, double endAngle)
    {
        var startPt = new Point(centre.X + r * Math.Cos(startAngle),
                                centre.Y + r * Math.Sin(startAngle));
        var endPt   = new Point(centre.X + r * Math.Cos(endAngle),
                                centre.Y + r * Math.Sin(endAngle));
        bool largeArc = (endAngle - startAngle) > Math.PI;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(centre, isFilled: true, isClosed: true);
            ctx.LineTo(startPt, isStroked: false, isSmoothJoin: false);
            ctx.ArcTo(endPt, new Size(r, r), rotationAngle: 0,
                isLargeArc: largeArc,
                sweepDirection: SweepDirection.Clockwise,
                isStroked: false, isSmoothJoin: false);
        }
        geo.Freeze();
        return geo;
    }
}

/// <summary>One slice of a <see cref="PieChart"/>.</summary>
public record PieSlice(double Value, Brush Fill);
