using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A piece of a typeset formula: where it sits, what part of the LaTeX it came from, and what it drew.
/// <para>
/// Holding the drawing on the node is what lets a formula be painted by walking its own tree rather than
/// by asking the typesetter to lay it out again. One pass produces structure, geometry and picture
/// together, so they cannot disagree — and a single term can be repainted, or revealed, on its own.
/// </para>
/// <para>
/// <b>Two coordinate frames meet here, deliberately.</b> <see cref="ILayoutNode.Bounds"/> is absolute and
/// normalised — where the piece really is on the element, which is what a caret, a wash and a hit test
/// need. The marks are raw, exactly as the typesetter handed them over, because the transforms that place
/// them are pushed onto the drawing context as the walk descends. Baking those into the marks instead
/// would lose the rotation an accent is drawn with.
/// </para>
/// </summary>
internal sealed class LatexNode : LayoutNode
{
    public LatexNode(Rect bounds, string kind, bool isInk)
        : base(bounds, part: null, kind, isInk)
    {
    }

    /// <summary>
    /// The part of the parse tree this piece was drawn from, typed — the link back that says what it
    /// <em>is</em> rather than merely where it came from.
    /// <para>
    /// Several pieces share one: a fraction's box and its bar are one construct drawn in parts. What a
    /// piece is <em>to</em> the thing holding it lives on the parse tree, not here, which is why this is a
    /// reference and not a copy of anything — the layout tree stays about layout.
    /// </para>
    /// <para>
    /// The same part <see cref="ILayoutNode.Part"/> carries, unwrapped. It is one field, read two ways: the
    /// seam wants a stretch of source and this side wants the formula, and reading them off one reference
    /// is what stops them disagreeing.
    /// </para>
    /// </summary>
    public Nexaflow.Maths.Latex.TexPart? Origin => (Part as TexSourcePart)?.Of;

    /// <summary>
    /// Says what part of the parse tree this piece was drawn from, and everything that follows from it.
    /// <para>
    /// <see cref="ILayoutNode.IsEnclosure"/> is one such thing: whether a caret can be inside this and
    /// then outside it is a question about the construct, and the part is the only thing that can
    /// answer it — so it is answered here rather than anywhere that happens to need it.
    /// </para>
    /// <para>
    /// Where the piece sits in the source used to be the other, projected here from the part and stored
    /// beside it. It is not stored anywhere now: the seam asks the part, and a piece drawn from none takes
    /// its place from whatever it was drawn inside, so there is no longer an anchor to be told either.
    /// </para>
    /// </summary>
    public void Owns(Nexaflow.Maths.Latex.TexPart? part)
    {
        Part = part is null ? null : new TexSourcePart(part);
        IsEnclosure = part is { } piece && piece.Parts.Any() && !IsRun(piece);
    }

    /// <summary>Takes this piece's part away, leaving it standing for nothing.</summary>
    public void Disown() => Part = null;

    /// <summary>
    /// Whether a part is a run of things rather than one thing made of parts. A row names every piece
    /// of it <c>element</c>, because that is all a sequence can say about what it holds, where a
    /// construct names its parts <c>numerator</c>, <c>radicand</c>, <c>superscript</c> — each meaning
    /// something to the construct. So the roles already carry the distinction.
    /// </summary>
    internal static bool IsRun(Nexaflow.Maths.Latex.TexPart part) =>
        part.Parts.Any() && part.Parts.All(inner => inner.Role == Nexaflow.Maths.Latex.TexRole.Element);

    /// <summary>
    /// The pixel grid this piece's edges snap to. WpfMath pushes one per box so glyph stems land on whole
    /// device pixels; without it the same formula rasterises a shade differently.
    /// </summary>
    public GuidelineSet? Guidelines { get; set; }

    /// <summary>Transforms to place this piece and everything under it — <c>\overrightarrow</c> and friends.</summary>
    public IReadOnlyList<Transform> Transforms { get; set; } = [];

    /// <summary>A wash behind the piece, from <c>\colorbox</c>, and where it goes. Painted under everything.</summary>
    public Brush? Background { get; set; }

    public Rect BackgroundBounds { get; set; }
}
