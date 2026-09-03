using Nexaflow.Maths.Latex;
using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Editing;
using XamlMath;
using XamlMath.Boxes;
using XamlMath.Rendering;
using XamlMath.Rendering.Transformations;

using WpfMath.Fonts;
using WpfMath.Rendering;

using Rect = System.Windows.Rect;
using Transform = System.Windows.Media.Transform;
using TranslateTransform = System.Windows.Media.TranslateTransform;
using RotateTransform = System.Windows.Media.RotateTransform;
using GuidelineSet = System.Windows.Media.GuidelineSet;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// Walks a typeset formula and rebuilds its box tree as a <see cref="ILayoutNode"/> tree: what was drawn,
/// where, and which slice of the LaTeX produced it.
/// <para>
/// This needs no cooperation from WpfMath beyond what is already public.
/// <see cref="IElementRenderer.RenderElement"/> is called for every box of the formula, parents before
/// children, and each <see cref="Box"/> carries the part of the parse tree it was built from.
/// Rendering with a renderer that draws nothing and only remembers therefore yields the whole
/// structure — the recursion in <see cref="RenderElement"/> <em>is</em> the tree, so parentage is kept
/// rather than inferred afterwards from rectangles.
/// </para>
/// <para>
/// Every box becomes a node, spacing included. Dropping the ones that are not interesting would break the
/// nesting for whatever sits beneath them, and it costs nothing to keep them: they are simply not ink.
/// </para>
/// <para>
/// Coordinates arrive in logical units with <paramref name="y"/> on the box's <em>baseline</em>; they are
/// scaled here so the tree is in the same pixels the element is painted in.
/// </para>
/// </summary>
internal sealed class LatexLayoutCapture : IElementRenderer
{
    private readonly Stack<LatexNode> _open = new();
    private readonly double _scale;
    private readonly string _latex;

    // OverUnderBox (\overrightarrow and friends) draws through RenderTransformed, so a box can be shifted
    // away from the coordinates it is handed. Translations are accumulated; a rotation is deliberately not
    // applied, because an axis-aligned bounding box is what a hit test wants either way.
    private double _offsetX;
    private double _offsetY;
    private IReadOnlyList<Transform> _pending = [];

    public LatexLayoutCapture(double scale, string latex)
    {
        _scale = scale;
        _latex = latex;
    }

    /// <summary>The formula's whole layout, or null when nothing was drawn at all.</summary>
    public LatexNode? Root { get; private set; }

    public void RenderElement(Box box, double x, double y)
    {
        var parent = _open.Count > 0 ? _open.Peek() : null;
        var (start, length) = SourceOf(box);

        // From two corners rather than an origin and a size, because a box's extent is signed: TeX kerns
        // backwards to tuck a root's degree over its sign, so that strut is genuinely three-quarters of a
        // unit wide *to the left*. A Rect will not hold a negative size, and clamping one to zero would
        // quietly move its left edge to where the pen already was.
        var left = _scale * x + _offsetX;
        var top = _scale * (y - box.Height) + _offsetY;
        var right = left + _scale * box.TotalWidth;
        var bottom = top + _scale * box.TotalHeight;

        var node = new LatexNode(
            new Rect(
                Math.Min(left, right),
                Math.Min(top, bottom),
                Math.Abs(right - left),
                Math.Abs(bottom - top)),
            start,
            length,
            box.GetType().Name,
            isInk: false)   // decided in Finish, once it is known what lies beneath
        {
            Formula = box.Node,
            Transforms = _pending,
            Guidelines = Snap(box, x, y),
            Background = (box.Background as WpfBrush)?.Value,
            BackgroundBounds = Raw(box, x, y),
        };

        _pending = [];
        if (parent is null) Root = node;
        else parent.Add(node);

        _open.Push(node);
        box.RenderTo(this, x, y);   // the recursion — children report themselves through RenderElement
        _open.Pop();
    }

    public void RenderTransformed(Box box, IEnumerable<Transformation> transforms, double x, double y)
    {
        var scaled = transforms.Select(t => t.Scale(_scale)).ToList();

        // Two things are wanted from a transform and they are not the same thing. The picture needs it
        // whole, pushed on the drawing context so a rotated accent lands rotated. The tree needs only how
        // far the box moved, because a hit test wants an axis-aligned box either way.
        double dx = 0, dy = 0;
        foreach (var transform in scaled)
            if (transform is Transformation.Translate translate)
            {
                dx += translate.X;
                dy += translate.Y;
            }

        _pending = [.. scaled.Select(ToTransform)];

        _offsetX += dx;
        _offsetY += dy;
        RenderElement(box, x, y);
        _offsetX -= dx;
        _offsetY -= dy;
    }

    public void RenderCharacter(CharInfo info, double x, double y, IBrush? foreground) =>
        _open.Peek().Drew(new GlyphMark(info.GetGlyphRun(x, y, _scale), (foreground as WpfBrush)?.Value));

    public void RenderLine(Point point0, Point point1, IBrush? foreground) =>
        _open.Peek().Drew(new LineMark(
            new System.Windows.Point(_scale * point0.X, _scale * point0.Y),
            new System.Windows.Point(_scale * point1.X, _scale * point1.Y),
            (foreground as WpfBrush)?.Value));

    public void RenderRectangle(Rectangle rectangle, IBrush? foreground) =>
        _open.Peek().Drew(new RuleMark(
            new Rect(
                _scale * rectangle.X,
                _scale * rectangle.Y,
                _scale * rectangle.Width,
                _scale * rectangle.Height),
            (foreground as WpfBrush)?.Value));

    private static Transform ToTransform(Transformation transformation) => transformation switch
    {
        Transformation.Translate translate => new TranslateTransform(translate.X, translate.Y),
        Transformation.Rotate rotate => new RotateTransform(rotate.RotationDegrees),
        _ => Transform.Identity,
    };

    /// <summary>
    /// The pixel grid this box's edges snap to, matching WpfMath's own renderer exactly — its numbers come
    /// from the raw coordinates, take the box's <em>baseline</em> rather than its top, and carry none of
    /// the accumulated transform. Reproducing that faithfully is what keeps the picture identical.
    /// </summary>
    private GuidelineSet Snap(Box box, double x, double y)
    {
        var guidelines = new GuidelineSet
        {
            GuidelinesX = { _scale * x, _scale * (x + box.TotalWidth) },
            GuidelinesY = { _scale * y, _scale * (y + box.TotalHeight) },
        };
        guidelines.Freeze();
        return guidelines;
    }

    /// <summary>Where a box's own wash goes: raw, untransformed, as WpfMath draws it.</summary>
    private Rect Raw(Box box, double x, double y)
    {
        var left = _scale * x;
        var top = _scale * (y - box.Height);
        var right = left + _scale * box.TotalWidth;
        var bottom = top + _scale * box.TotalHeight;
        return new Rect(
            Math.Min(left, right), Math.Min(top, bottom),
            Math.Abs(right - left), Math.Abs(bottom - top));
    }

    /// <summary>
    /// Marks the smallest piece of source each part of the tree stands for. Deferred to the end because it
    /// is a question about a node's descendants, and those have not arrived while it is being built.
    /// </summary>
    public void FinishRendering()
    {
        if (Root is null) return;

        // The whole layout stands for the whole formula, whatever the outermost box happened to say it
        // came from. Without this a selection that grew all the way out would stand for nothing at all.
        if (Root.SourceLength <= 0)
        {
            Root.SourceStart = 0;
            Root.SourceLength = _latex.Length;
        }

        Detach(Root, [(Root.SourceStart, Root.SourceLength)]);
        MarkInk(Root);
    }

    /// <summary>
    /// Takes a span away from any node that merely repeats the one enclosing it.
    /// <para>
    /// Each piece of layout must name a <em>different</em> part of the source, or the link back stops
    /// being an answer and becomes a question. A root is the case that proves it: WpfMath gives the
    /// radical <em>sign</em> the span of the entire <c>\sqrt[3]{x+1}</c>, the same span the node holding
    /// the whole root already carries. Left alone, a reader pointing at the sign and a reader selecting
    /// the root arrive at the same link and something downstream has to guess which was meant — and every
    /// version of that guess has been wrong somewhere. The sign is the root's own drawing, so it names
    /// nothing; the degree names the degree, the contents name the contents, and the node above them names
    /// the whole. Nothing then has to be resolved.
    /// </para>
    /// </summary>
    private void Detach(LayoutNode node, List<(int Start, int Length)> above)
    {
        foreach (var child in node.Children.OfType<LayoutNode>())
        {
            var taken = false;
            if (child.SourceLength > 0)
            {
                var enclosing = above[^1];

                // Against every name above it, not merely the nearest. An integral sign is a box inside a
                // big-operator box that carries the same `\int`, and something in between can name a
                // different stretch again — so comparing one level up leaves the duplicate standing two.
                if (above.Contains((child.SourceStart, child.SourceLength)))
                {
                    child.SourceLength = 0;
                }
                else if (child.SourceStart < enclosing.Start
                         || child.SourceEnd() > enclosing.Start + enclosing.Length)
                {
                    // A name reaching outside the thing containing it cannot be true — the piece is drawn
                    // inside its parent, so it cannot have come from text outside it. WpfMath still does
                    // this for a construct or two, and the fault is contained rather than trusted: the
                    // piece keeps its place in the tree and its drawing, and simply names nothing, so a
                    // press on it resolves to whatever encloses it. Believing it instead would let a
                    // selection come back as a range that does not contain what was selected.
                    // Whether anything below it still names a part of the source decides how much this
                    // costs: a level with named children keeps them, and promotion simply skips the level.
                    // A leaf has nothing to roll up from, so its own granularity is what goes.
                    child.SourceLength = 0;
                }
                else
                {
                    above.Add((child.SourceStart, child.SourceLength));
                    taken = true;
                }
            }

            Detach(child, above);
            if (taken) above.RemoveAt(above.Count - 1);
        }
    }

    /// <summary>
    /// A node is ink when it names a piece of the source and nothing beneath it names a smaller one — the
    /// leaves of the source-bearing subtree, which are exactly the things a reader can point at.
    /// <para>
    /// Leaf-of-the-box-tree would be the obvious rule and is wrong: an operator name such as <c>\sin</c>
    /// is a box holding a run of letter boxes, and those letters index the macro's own text rather than
    /// the user's — so the letters name nothing and <c>\sin</c> itself is the unit, container or not.
    /// </para>
    /// </summary>
    private static bool MarkInk(LayoutNode node)
    {
        var below = false;
        foreach (var child in node.Children.OfType<LayoutNode>())
            below |= MarkInk(child);

        // Standing for some of the source is normally what makes a piece worth pointing at. A hole is
        // the exception the rule needs: it stands for nothing written — that is what a hole is — and it
        // is nonetheless the most pointable thing on the page, being the one place the reader has been
        // told to write. So it counts as ink on the strength of being a hole rather than of covering
        // anything, and everything that finds, hit-tests, selects or carries a symbol then finds it.
        var stands = node.SourceLength > 0 || node.IsPlaceholder();

        node.IsInk = stands && !below;
        return below || stands;
    }

    /// <summary>
    /// Which characters of the formula this box was laid out from, or nothing.
    /// <para>
    /// A box built from a part knows, because the part knows where it stands. A strut or a piece of
    /// glue came from no character at all, and a rule — a fraction's bar, a radical's overline — is
    /// drawn for a construct rather than for any text within it. None of those is a fault: they are
    /// parts of their parent's layout, so they stay in the tree, name nothing of their own, and are
    /// painted and washed with whatever encloses them.
    /// </para>
    /// <para>
    /// Named the way the reading this replaced named it, though, and deliberately: a braced argument's
    /// contents, a cell's ink, where the <em>part</em> is the whole <c>{a+b}</c> and the whole cell.
    /// Everything downstream still works in offsets and was written against that convention, and
    /// handing over the honest span instead re-braces an argument that is already braced. This goes
    /// when the editor asks the part.
    /// </para>
    /// </summary>
    private (int Start, int Length) SourceOf(Box box)
    {
        if (box.Node?.Origin is not { } part || box.GetType().Name is "StrutBox" or "GlueBox")
            return (Anchor, 0);

        return part.Kind switch
        {
            TexKind.Group => part.Contents,
            TexKind.Cell => part.Written,
            _ => (part.Start, part.Length),
        };
    }

    /// <summary>Where a source-less node sits in the text: wherever the thing containing it starts.</summary>
    private int Anchor => _open.Count > 0 ? _open.Peek().SourceStart : 0;

    }
