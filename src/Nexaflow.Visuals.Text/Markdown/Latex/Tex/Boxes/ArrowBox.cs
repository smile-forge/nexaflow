using System;
using XamlMath.Rendering;

namespace XamlMath.Boxes;

/// <summary>What an <see cref="ArrowBox"/> draws on top of its shaft.</summary>
[Flags]
internal enum ArrowDecoration
{
    None = 0,

    /// <summary>An arrowhead at the left end.</summary>
    HeadLeft = 1,

    /// <summary>An arrowhead at the right end.</summary>
    HeadRight = 2,

    /// <summary>Two parallel shafts instead of one, for the \Rightarrow family.</summary>
    DoubleShaft = 4,

    /// <summary>A vertical bar at the left end, for \mapsto.</summary>
    TailBarLeft = 8,
}

// Box that draws a horizontal arrow spanning its full width: a shaft, arrowheads at either or both ends, and
// optionally a tail bar. Used both for the stretchy accents (\overrightarrow) and for the extensible arrows
// (\xrightarrow).
internal sealed class ArrowBox : Box
{
    private readonly ArrowDecoration _decoration;
    private readonly double _thickness;
    private readonly double _headHalfHeight;
    private readonly double _headLength;

    public ArrowBox(TexEnvironment environment, double width, double thickness, ArrowDecoration decoration)
    {
        _decoration = decoration;
        _thickness = thickness;
        _headHalfHeight = 2.0 * thickness;
        _headLength = 5.0 * thickness;

        this.Width = width;
        this.Height = 2.0 * _headHalfHeight; // full vertical extent, shaft drawn down the middle
        this.Depth = 0;
        this.Foreground = environment.Foreground;
        this.Background = environment.Background;
    }

    public override void RenderTo(IElementRenderer renderer, double x, double y)
    {
        // The shaft runs along the vertical middle of the box; y is the box baseline (its lower edge).
        var shaftY = y - this.Height / 2;
        var left = x;
        var right = x + this.Width;

        if (_decoration.HasFlag(ArrowDecoration.DoubleShaft))
        {
            var offset = _thickness;
            renderer.RenderLine(new Point(left, shaftY - offset), new Point(right, shaftY - offset), this.Foreground);
            renderer.RenderLine(new Point(left, shaftY + offset), new Point(right, shaftY + offset), this.Foreground);
        }
        else
        {
            renderer.RenderLine(new Point(left, shaftY), new Point(right, shaftY), this.Foreground);
        }

        // Arrowheads: two short strokes converging on the pointing end.
        var headLength = Math.Min(_headLength, this.Width);
        if (_decoration.HasFlag(ArrowDecoration.HeadRight))
        {
            renderer.RenderLine(
                new Point(right, shaftY), new Point(right - headLength, shaftY - _headHalfHeight), this.Foreground);
            renderer.RenderLine(
                new Point(right, shaftY), new Point(right - headLength, shaftY + _headHalfHeight), this.Foreground);
        }

        if (_decoration.HasFlag(ArrowDecoration.HeadLeft))
        {
            renderer.RenderLine(
                new Point(left, shaftY), new Point(left + headLength, shaftY - _headHalfHeight), this.Foreground);
            renderer.RenderLine(
                new Point(left, shaftY), new Point(left + headLength, shaftY + _headHalfHeight), this.Foreground);
        }

        if (_decoration.HasFlag(ArrowDecoration.TailBarLeft))
        {
            renderer.RenderLine(
                new Point(left, shaftY - _headHalfHeight),
                new Point(left, shaftY + _headHalfHeight),
                this.Foreground);
        }
    }

    public override int GetLastFontId()
    {
        return TexFontUtilities.NoFontId;
    }
}
