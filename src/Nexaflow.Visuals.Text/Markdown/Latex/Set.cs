using System;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using WpfMath.Rendering;
using XamlMath.Boxes;

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
    public int LastFontId { get; init; } = XamlMath.TexFontUtilities.NoFontId;

    public double TotalWidth => Width + Italic;

    public double TotalHeight => Height + Depth;

    /// <summary>
    /// A box from the typesetter, as a piece — while the typesetter's constructs are moved into the builder one
    /// family at a time.
    /// </summary>
    public static Set Of(Box box) => new()
    {
        Kind = box.GetType().Name,
        Width = box.Width,
        Height = box.Height,
        Depth = box.Depth,
        Italic = box.Italic,
        Shift = box.Shift,
        Part = box.Node?.Origin,
        Spacing = box is StrutBox or GlueBox,
        Background = (box.Background as WpfBrush)?.Value,
        Draw = (layer, _, x, y) => box.Lay(layer, x, y),
        LastFontId = box.GetLastFontId(),
    };
}
