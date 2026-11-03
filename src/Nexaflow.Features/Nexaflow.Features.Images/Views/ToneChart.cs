using Nexaflow.Features.Images.Editing.Rendering;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// A picture's tones drawn as a histogram, black at the left and white at the right: its luminance as a filled shape, with red,
/// green and blue as lines over it, each as tall as the share of the picture at that level. It is scaled to the fullest level
/// short of the two ends, so a spike of pure black or white — which the clip marks report — does not flatten the rest.
/// </summary>
public sealed class ToneChart : FrameworkElement
{
    /// <summary>Room left inside the chart's edges.</summary>
    private const double Inset = 2;

    public static readonly DependencyProperty HistogramProperty = DependencyProperty.Register(
        nameof(Histogram), typeof(ToneHistogram), typeof(ToneChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(ToneChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LuminanceFillProperty = DependencyProperty.Register(
        nameof(LuminanceFill), typeof(Brush), typeof(ToneChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RedStrokeProperty = DependencyProperty.Register(
        nameof(RedStroke), typeof(Brush), typeof(ToneChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GreenStrokeProperty = DependencyProperty.Register(
        nameof(GreenStroke), typeof(Brush), typeof(ToneChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BlueStrokeProperty = DependencyProperty.Register(
        nameof(BlueStroke), typeof(Brush), typeof(ToneChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The tones drawn; none until a frame is read.</summary>
    public ToneHistogram? Histogram
    {
        get => (ToneHistogram?)GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    /// <summary>The chart itself.</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>The luminance's shape.</summary>
    public Brush? LuminanceFill
    {
        get => (Brush?)GetValue(LuminanceFillProperty);
        set => SetValue(LuminanceFillProperty, value);
    }

    /// <summary>The red channel's line.</summary>
    public Brush? RedStroke
    {
        get => (Brush?)GetValue(RedStrokeProperty);
        set => SetValue(RedStrokeProperty, value);
    }

    /// <summary>The green channel's line.</summary>
    public Brush? GreenStroke
    {
        get => (Brush?)GetValue(GreenStrokeProperty);
        set => SetValue(GreenStrokeProperty, value);
    }

    /// <summary>The blue channel's line.</summary>
    public Brush? BlueStroke
    {
        get => (Brush?)GetValue(BlueStrokeProperty);
        set => SetValue(BlueStrokeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        dc.DrawRoundedRectangle(Background, null, new Rect(0, 0, width, height), 3, 3);
        if (Histogram is not { Pixels: > 0 } tones) return;

        double tallest = Tallest(tones);
        if (tallest <= 0) return;
        var plot = new Rect(Inset, Inset, width - 2 * Inset, height - 2 * Inset);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        dc.PushOpacity(0.55);
        dc.DrawGeometry(LuminanceFill, null, Shape(tones.Luminance, plot, tallest, closed: true));
        dc.Pop();
        dc.PushOpacity(0.9);
        Line(dc, RedStroke, tones.Red, plot, tallest);
        Line(dc, GreenStroke, tones.Green, plot, tallest);
        Line(dc, BlueStroke, tones.Blue, plot, tallest);
        dc.Pop();
    }

    /// <summary>The fullest level short of the ends, across every series; the ends themselves where nothing else holds any.</summary>
    private static double Tallest(ToneHistogram tones)
    {
        int tallest = 0, atEnds = 0;
        Take(tones.Luminance);
        Take(tones.Red);
        Take(tones.Green);
        Take(tones.Blue);
        return tallest > 0 ? tallest : atEnds;

        void Take(ReadOnlySpan<int> series)
        {
            for (int level = 1; level < series.Length - 1; level++) tallest = Math.Max(tallest, series[level]);
            atEnds = Math.Max(atEnds, Math.Max(series[0], series[^1]));
        }
    }

    private static void Line(DrawingContext dc, Brush? stroke, ReadOnlySpan<int> series, Rect plot, double tallest)
    {
        if (stroke is null) return;
        var pen = new Pen(stroke, 1.25) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, Shape(series, plot, tallest, closed: false));
    }

    /// <summary>A series as a line across the plot — closed down to its foot, as a shape to fill.</summary>
    private static StreamGeometry Shape(ReadOnlySpan<int> series, Rect plot, double tallest, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var draw = geometry.Open())
        {
            double step = plot.Width / (ToneHistogram.Levels - 1);
            Point At(int level, int count) => new(plot.Left + level * step, plot.Bottom - Math.Min(1, count / tallest) * plot.Height);
            draw.BeginFigure(closed ? new Point(plot.Left, plot.Bottom) : At(0, series[0]), closed, closed);
            for (int level = closed ? 0 : 1; level < series.Length; level++) draw.LineTo(At(level, series[level]), true, true);
            if (closed) draw.LineTo(new Point(plot.Right, plot.Bottom), true, true);
        }
        geometry.Freeze();
        return geometry;
    }
}
