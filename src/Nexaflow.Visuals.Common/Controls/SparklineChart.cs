using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A lightweight history sparkline drawn directly via <see cref="OnRender"/>. Feed it a sequence of
/// values (oldest → newest) and a <see cref="Maximum"/>; it draws a filled line graph that fills its
/// layout bounds. Colours come from the caller (bind to theme brushes) — the control hard-codes none.
/// Used for live CPU / memory history in the Processes feature; reusable anywhere a tiny trend line fits.
/// </summary>
public class SparklineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double>), typeof(SparklineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SparklineChart),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SparklineChart),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SparklineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(SparklineChart),
            new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>History values, oldest first. Reassign a new list each tick to repaint.</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>Value mapped to the top of the chart (default 100).</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Optional area fill under the line; null draws the line only.</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var values = Values;
        if (values is not { Count: > 1 }) return;

        double max = Maximum > 0 ? Maximum : 1;
        int n = values.Count;
        double dx = w / (n - 1);

        double X(int i) => i * dx;
        double Y(double v) => h - Math.Clamp(v / max, 0, 1) * h;

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(new Point(0, Y(values[0])), isFilled: false, isClosed: false);
            for (int i = 1; i < n; i++)
                ctx.LineTo(new Point(X(i), Y(values[i])), isStroked: true, isSmoothJoin: true);
        }
        line.Freeze();

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, h), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(0, Y(values[0])), isStroked: false, isSmoothJoin: false);
                for (int i = 1; i < n; i++)
                    ctx.LineTo(new Point(X(i), Y(values[i])), isStroked: false, isSmoothJoin: true);
                ctx.LineTo(new Point(w, h), isStroked: false, isSmoothJoin: false);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        if (Stroke is { } stroke)
            dc.DrawGeometry(null, new Pen(stroke, StrokeThickness), line);
    }
}
