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
        : base(bounds, sourceStart: 0, sourceLength: 0, kind, isInk)
    {
    }

    /// <summary>
    /// The part of the parse tree this piece was drawn from — the link back that says what it <em>is</em>
    /// rather than merely where it came from.
    /// <para>
    /// Several pieces share one: a fraction's box and its bar are one construct drawn in parts. What a
    /// piece is <em>to</em> the thing holding it lives on the parse tree, not here, which is why this is a
    /// reference and not a copy of anything — the layout tree stays about layout.
    /// </para>
    /// <para>
    /// Set once, when the formula is typeset, through <see cref="Owns"/> — never assigned directly, so
    /// what follows from it cannot be set separately and disagree.
    /// </para>
    /// </summary>
    public Nexaflow.Maths.Latex.TexPart? Part { get; private set; }

    /// <summary>
    /// Says what part of the parse tree this piece was drawn from, and everything that follows from it.
    /// <para>
    /// <see cref="ILayoutNode.IsEnclosure"/> is one such thing: whether a caret can be inside this and
    /// then outside it is a question about the construct, and the part is the only thing that can
    /// answer it — so it is answered here rather than anywhere that happens to need it.
    /// </para>
    /// <para>
    /// <see cref="ILayoutNode.SourceStart"/> and <see cref="ILayoutNode.SourceLength"/> are the other:
    /// they are a <em>projection</em> of the part, written here and nowhere else, so that the day the
    /// editing seam stops working in offsets they are one method to delete rather than a mechanism to
    /// unpick. <paramref name="anchor"/> is where a piece that stands for nothing sits in the text —
    /// wherever the thing containing it starts — and is part of the same projection.
    /// </para>
    /// </summary>
    public void Owns(Nexaflow.Maths.Latex.TexPart? part, int anchor)
    {
        Part = part;
        IsEnclosure = part is { } piece && piece.Parts.Any() && !IsRun(piece);

        var (start, length) = Named(part);
        SourceStart = part is null ? anchor : start;
        SourceLength = length;
    }

    /// <summary>Takes this piece's part away, leaving it standing for nothing.</summary>
    public void Disown()
    {
        Part = null;
        SourceLength = 0;
    }

    /// <summary>
    /// Which characters a part is named by, in the projection.
    /// <para>
    /// Named the way the reading this replaced named it, and deliberately: a braced argument's
    /// contents, a cell's ink, where the <em>part</em> is the whole <c>{a+b}</c> and the whole cell.
    /// Everything downstream still works in offsets and was written against that convention, and
    /// handing over the honest span instead re-braces an argument that is already braced. This is the
    /// last place an answer from the parse tree is narrowed to suit an offset, and it goes when the
    /// editor asks the part.
    /// </para>
    /// </summary>
    private static (int Start, int Length) Named(Nexaflow.Maths.Latex.TexPart? part) =>
        part is null ? (0, 0)
        : part.Kind switch
        {
            Nexaflow.Maths.Latex.TexKind.Group => part.Contents,
            Nexaflow.Maths.Latex.TexKind.Cell => part.Written,
            _ => (part.Start, part.Length),
        };

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
