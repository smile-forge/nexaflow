using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// A picture's depths laid along a strip, its nearest part at the left and its farthest at the right: at each step of distance
/// a bar as tall as the share of the picture lying there. A band or a marker over it is then set against what the picture
/// holds — the person, the street behind them, the sky — rather than against bare numbers.
/// </summary>
public sealed class DepthStrip : FrameworkElement
{
    public static readonly DependencyProperty SharesProperty = DependencyProperty.Register(
        nameof(Shares), typeof(IReadOnlyList<double>), typeof(DepthStrip), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(DepthStrip), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(DepthStrip), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The share of the picture at each step of distance, nearest first, the fullest 1; none until the depth is known.</summary>
    public IReadOnlyList<double>? Shares
    {
        get => (IReadOnlyList<double>?)GetValue(SharesProperty);
        set => SetValue(SharesProperty, value);
    }

    /// <summary>The strip itself.</summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>The bars.</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        dc.DrawRoundedRectangle(Background, null, new Rect(0, 0, width, height), 3, 3);
        if (Shares is not { Count: > 0 } shares || Fill is null) return;
        double step = width / shares.Count, tallest = height - 4;
        var bars = new StreamGeometry();
        using (var draw = bars.Open())
            for (int i = 0; i < shares.Count; i++)
            {
                double tall = Math.Clamp(shares[i], 0, 1) * tallest;
                if (tall < 0.5) continue;
                double left = i * step, top = height - 2 - tall;
                draw.BeginFigure(new Point(left, height - 2), true, true);
                draw.LineTo(new Point(left, top), false, false);
                draw.LineTo(new Point(left + step, top), false, false);
                draw.LineTo(new Point(left + step, height - 2), false, false);
            }
        bars.Freeze();
        dc.DrawGeometry(Fill, null, bars);
    }
}
