using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// One line that keeps its end in view. A child wider than the panel is laid against its right edge, so the start goes
/// off the left and fades there — for text that grows as it is written or spoken, where the newest words are the ones
/// to read. A child that fits sits at the left, as it would anywhere else.
/// </summary>
public sealed class TailClip : Decorator
{
    /// <summary>How far in from the left edge the start of a line that does not fit fades out.</summary>
    public static readonly DependencyProperty FadeWidthProperty = DependencyProperty.Register(
        nameof(FadeWidth), typeof(double), typeof(TailClip),
        new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    private Brush? _fade;
    private double _fadeEdge = -1;

    public TailClip() => ClipToBounds = true;

    public double FadeWidth
    {
        get => (double)GetValue(FadeWidthProperty);
        set => SetValue(FadeWidthProperty, value);
    }

    /// <summary>The line does not fit: its start is off the left edge.</summary>
    public bool IsOverflowing { get; private set; }

    /// <summary>The child as wide as it wants, however narrow the panel: it is the arrange that decides what shows.</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return new Size();
        Child.Measure(new Size(double.PositiveInfinity, constraint.Height));
        var wanted = Child.DesiredSize;
        return new Size(Math.Min(wanted.Width, constraint.Width), wanted.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null) return arrangeSize;
        double width = Child.DesiredSize.Width;
        IsOverflowing = width > arrangeSize.Width;
        Child.Arrange(new Rect(IsOverflowing ? arrangeSize.Width - width : 0, 0, width, arrangeSize.Height));
        OpacityMask = IsOverflowing && arrangeSize.Width > 0 ? Fade(Math.Min(1, FadeWidth / arrangeSize.Width)) : null;
        return arrangeSize;
    }

    /// <summary>An alpha ramp from nothing at the left edge to whole at <paramref name="edge"/> of the width — kept while
    /// the width holds, since the line is laid again with every word.</summary>
    private Brush Fade(double edge)
    {
        if (_fade is not null && _fadeEdge == edge) return _fade;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), edge));
        brush.Freeze();
        _fadeEdge = edge;
        return _fade = brush;
    }
}
