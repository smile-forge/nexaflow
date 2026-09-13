using System;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;
using System.Collections.Generic;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A piece of a formula, measured and ready to be laid: how much room it takes, what it stands for, and how to
/// draw it once its parent has decided where it goes.
/// <para>
/// TeX sizes a parent from its children before it places any of them — a fraction's bar is as wide as the wider of
/// its two halves, a delimiter grows to what it holds — so a construct cannot be laid the moment it is built. This
/// is what it hands back instead: the measurements a parent needs, and a <see cref="Draw"/> that lays the piece at
/// the point the parent chose. Nothing holds on to it once the formula is laid, and it has no kinds of its own; the
/// shape of the formula is in the builder functions that make these, not in a hierarchy of classes.
/// </para>
/// <para>
/// Coordinates follow TeX's: <see cref="Draw"/> is handed the left end of the baseline, <see cref="Height"/> rises
/// above it and <see cref="Depth"/> hangs below.
/// </para>
/// </summary>
internal sealed record Set
{
    /// <summary>What the layout tree calls the piece.</summary>
    public required string Kind { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public double Depth { get; init; }

    /// <summary>How far a slanted glyph leans past <see cref="Width"/>.</summary>
    public double Italic { get; init; }

    /// <summary>
    /// How far a parent moves this from where it would otherwise sit — down within a row, right within a stack.
    /// The parent's to say, which is why it is on the piece rather than in the parent.
    /// </summary>
    public double Shift { get; init; }

    /// <summary>The part of the reading this was set from, when it was set from one.</summary>
    public ContentPart? Part { get; init; }

    /// <summary>Room rather than ink: glue and struts, which make no piece of the layout.</summary>
    public bool Spacing { get; init; }

    /// <summary>A <c>\colorbox</c> washed under the piece.</summary>
    public Brush? Background { get; init; }

    /// <summary>
    /// Lays the piece's own marks and children, with the left end of its baseline at the point given. Handed the piece
    /// itself, so a measurement a parent overrides afterwards (TeX pins a stack's height that way) is the one drawn.
    /// </summary>
    public Action<LatexCapture, Set, double, double>? Draw { get; init; }

    /// <summary>The font of the last glyph this draws, or none: what a space measured in x-heights is measured against.</summary>
    public int LastFontId { get; init; } = Nexaflow.Visuals.Text.Markdown.Latex.Tex.TexFontUtilities.NoFontId;

    public double TotalWidth => Width + Italic;

    public double TotalHeight => Height + Depth;

    /// <summary>One glyph, in the colours of the environment it is set in.</summary>
    public static Set Glyph(TexEnvironment environment, CharInfo info)
    {
        var foreground = environment.Foreground;

        return new()
        {
            Kind = "CharBox",
            Width = info.Metrics.Width,
            Height = info.Metrics.Height,
            Depth = info.Metrics.Depth,
            Italic = info.Metrics.Italic,
            Background = Wash(environment),
            LastFontId = info.FontId,
            Draw = (layer, _, x, y) => layer.Glyph(info, x, y, foreground),
        };
    }

    /// <summary>A filled bar standing on the baseline: a fraction's, an overline's, an underscore.</summary>
    public static Set Rule(TexEnvironment environment, double thickness, double width, double shift)
    {
        var foreground = environment.Foreground;

        return new()
        {
            Kind = "HorizontalRule",
            Width = width,
            Height = thickness,
            Shift = shift,
            Background = Wash(environment),
            Draw = (layer, _, x, y) => layer.Rule(new Rectangle(x, y - thickness, width, thickness), foreground),
        };
    }

    /// <summary>The room TeX puts between two classes of thing, which draws nothing.</summary>
    public static Set Glue(double width) => new()
    {
        Kind = "GlueBox",
        Width = width,
        Spacing = true,
    };

    /// <summary>
    /// The hollow box standing where an argument has still to be written.
    /// <para>
    /// An empty argument sets as nothing at all, so <c>\frac{}{}</c> draws a bar with two invisible sides — a formula a
    /// reader cannot see, cannot aim at and cannot tell from a broken one. A box gives the hole a size and a place, which
    /// is all anything else needs to treat it as an ordinary symbol. Drawn as four hairlines rather than a filled block or
    /// a glyph: filled would read as content, and a glyph would be content.
    /// </para>
    /// </summary>
    public static Set Placeholder(TexEnvironment environment)
    {
        // How far across the em the box runs and how far up — squat enough to read as a slot — and a line thin enough
        // not to read as ink.
        const double widthInEm = 0.55, heightInEm = 0.62, hairline = 0.07;

        var size = environment.MathFont.GetXHeight(environment.Style, environment.LastFontId);
        var width = size * (widthInEm / heightInEm);
        var thickness = System.Math.Max(size * hairline, 0.4);
        var foreground = environment.Foreground;

        return new()
        {
            Kind = "PlaceholderBox",
            Width = width,
            Height = size,
            Background = Wash(environment),
            Draw = (layer, _, x, y) =>
            {
                var top = y - size;
                layer.Rule(new Rectangle(x, top, width, thickness), foreground);
                layer.Rule(new Rectangle(x, y - thickness, width, thickness), foreground);
                layer.Rule(new Rectangle(x, top, thickness, size), foreground);
                layer.Rule(new Rectangle(x + width - thickness, top, thickness, size), foreground);
            },
        };
    }

    /// <summary>A <c>\cancel</c> stroke drawn corner to corner across the room given.</summary>
    public static Set Stroke(StrokeMode mode, double width, double height, double depth) => new()
    {
        Kind = "StrokeBox",
        Width = width,
        Height = height,
        Depth = depth,
        Draw = (layer, _, x, y) =>
        {
            if (mode.HasFlag(StrokeMode.Normal))
                layer.Line(new Point(x, y + depth), new Point(x + width, y - height), null);

            if (mode.HasFlag(StrokeMode.Back))
                layer.Line(new Point(x, y - height), new Point(x + width, y + depth), null);
        },
    };

    /// <summary>
    /// A horizontal arrow spanning the width given: a shaft, arrowheads at either or both ends, and optionally a tail bar.
    /// Both the stretchy accents (<c>\overrightarrow</c>) and the extensible arrows (<c>\xrightarrow</c>) are one of these.
    /// </summary>
    public static Set Arrow(TexEnvironment environment, double width, double thickness, ArrowDecoration decoration)
    {
        var headHalfHeight = 2.0 * thickness;
        var height = 2.0 * headHalfHeight;
        var foreground = environment.Foreground;

        return new()
        {
            Kind = "ArrowBox",
            Width = width,
            Height = height,
            Background = Wash(environment),
            Draw = (layer, _, x, y) =>
            {
                // The shaft runs along the vertical middle; y is the lower edge.
                var shaftY = y - height / 2;
                var left = x;
                var right = x + width;

                if (decoration.HasFlag(ArrowDecoration.DoubleShaft))
                {
                    layer.Line(new Point(left, shaftY - thickness), new Point(right, shaftY - thickness), foreground);
                    layer.Line(new Point(left, shaftY + thickness), new Point(right, shaftY + thickness), foreground);
                }
                else
                {
                    layer.Line(new Point(left, shaftY), new Point(right, shaftY), foreground);
                }

                // Arrowheads: two short strokes converging on the pointing end.
                var headLength = System.Math.Min(5.0 * thickness, width);
                if (decoration.HasFlag(ArrowDecoration.HeadRight))
                {
                    layer.Line(new Point(right, shaftY), new Point(right - headLength, shaftY - headHalfHeight), foreground);
                    layer.Line(new Point(right, shaftY), new Point(right - headLength, shaftY + headHalfHeight), foreground);
                }

                if (decoration.HasFlag(ArrowDecoration.HeadLeft))
                {
                    layer.Line(new Point(left, shaftY), new Point(left + headLength, shaftY - headHalfHeight), foreground);
                    layer.Line(new Point(left, shaftY), new Point(left + headLength, shaftY + headHalfHeight), foreground);
                }

                if (decoration.HasFlag(ArrowDecoration.TailBarLeft))
                    layer.Line(new Point(left, shaftY - headHalfHeight), new Point(left, shaftY + headHalfHeight), foreground);
            },
        };
    }

    /// <summary>A rectangular frame round the room given, and nothing else — laid over what it frames, it is <c>\boxed</c>.</summary>
    public static Set Frame(TexEnvironment environment, double thickness, double width, double height, double depth)
    {
        var foreground = environment.Foreground;

        return new()
        {
            Kind = "FrameBox",
            Width = width,
            Height = height,
            Depth = depth,
            Background = Wash(environment),
            Draw = (layer, _, x, y) =>
            {
                var top = y - height;
                var total = height + depth;

                layer.Rule(new Rectangle(x, top, width, thickness), foreground);
                layer.Rule(new Rectangle(x, top + total - thickness, width, thickness), foreground);
                layer.Rule(new Rectangle(x, top, thickness, total), foreground);
                layer.Rule(new Rectangle(x + width - thickness, top, thickness, total), foreground);
            },
        };
    }

    /// <summary>
    /// The rules of an array — the vertical ones <c>|</c> asks for, the horizontal ones <c>\hline</c> does — laid over the
    /// grid they belong to, so they span the whole of it rather than being cut into the rows.
    /// </summary>
    /// <param name="verticalAt">X offsets, measured from the left edge of the grid.</param>
    /// <param name="horizontalAt">Y offsets, measured down from the top edge of the grid.</param>
    public static Set GridRules(
        TexEnvironment environment, IReadOnlyList<double> verticalAt, IReadOnlyList<double> horizontalAt, double thickness,
        double width, double height, double depth)
    {
        var foreground = environment.Foreground;

        return new()
        {
            Kind = "GridRulesBox",
            Width = width,
            Height = height,
            Depth = depth,
            Background = Wash(environment),
            Draw = (layer, _, x, y) =>
            {
                var top = y - height;
                var total = height + depth;

                foreach (var offset in verticalAt)
                    layer.Rule(new Rectangle(x + offset, top, thickness, total), foreground);

                foreach (var offset in horizontalAt)
                    layer.Rule(new Rectangle(x, top + offset, width, thickness), foreground);
            },
        };
    }

    /// <summary>The <c>\colorbox</c> an environment carries, as the brush a piece set in it is washed with.</summary>
    private static Brush? Wash(TexEnvironment environment) => (environment.Background as WpfBrush)?.Value;
}
