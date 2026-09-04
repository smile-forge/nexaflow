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
    private readonly TexReading _reading;

    // OverUnderBox (\overrightarrow and friends) draws through RenderTransformed, so a box can be shifted
    // away from the coordinates it is handed. Translations are accumulated; a rotation is deliberately not
    // applied, because an axis-aligned bounding box is what a hit test wants either way.
    private double _offsetX;
    private double _offsetY;
    private IReadOnlyList<Transform> _pending = [];

    public LatexLayoutCapture(double scale, TexReading reading)
    {
        _scale = scale;
        _reading = reading;
    }

    /// <summary>The formula's whole layout, or null when nothing was drawn at all.</summary>
    public LatexNode? Root { get; private set; }

    public void RenderElement(Box box, double x, double y)
    {
        var parent = _open.Count > 0 ? _open.Peek() : null;

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
            box.GetType().Name,
            isInk: false)   // decided in Finish, once it is known what lies beneath
        {
            Transforms = _pending,
            Guidelines = Snap(box, x, y),
            Background = (box.Background as WpfBrush)?.Value,
            BackgroundBounds = Raw(box, x, y),
        };

        // What it was drawn from, handed over rather than searched for: the atom that made this box was
        // built from a part and carries it. A strut and a piece of glue are the exception — they are room
        // rather than ink, and were written by nobody.
        node.Owns(box is StrutBox or GlueBox ? null : box.Node?.Origin);

        _pending = [];
        if (parent is null) Root = node;
        else parent.Add(node);

        _open.Push(node);
        box.RenderTo(this, x, y);   // the recursion - children report themselves through RenderElement
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

        // The whole layout stands for the whole formula, whatever the outermost box happened to be built
        // from. Without this a selection that grew all the way out would stand for nothing at all.
        if (Root.Origin is null) Root.Owns(_reading.Root);

        Detach(Root, [Root.Origin!]);
        MarkInk(Root);
    }

    /// <summary>
    /// Takes its part away from any node that repeats the one enclosing it, or claims one from outside it.
    /// <para>
    /// Each piece of layout must stand for a <em>different</em> part, or the link back stops being an
    /// answer and becomes a question. A root is the case that proves it: the radical sign is built from
    /// the radical atom, which is the whole <c>\sqrt[3]{x+1}</c> — the same part the node holding the
    /// whole root already carries. Left alone, a reader pointing at the sign and a reader selecting the
    /// root arrive at the same link and something downstream has to guess which was meant, and every
    /// version of that guess has been wrong somewhere. The sign is the root's own drawing, so it stands
    /// for nothing; the degree stands for the degree, the contents for the contents, and the node above
    /// them for the whole. Nothing then has to be resolved.
    /// </para>
    /// <para>
    /// Against every part above it, not merely the nearest: an integral sign is a box inside a
    /// big-operator box built from the same atom, and something in between can be a different part
    /// again, so comparing one level up leaves the duplicate standing two.
    /// </para>
    /// <para>
    /// And a piece drawn inside another cannot have been written outside it, so a part that is not the
    /// enclosing one nor anything under it is not true of this piece and is taken away rather than
    /// trusted. The typesetter still builds a box or two that way — a style wraps a run and hands back a
    /// box holding a neighbour's — and the fault is contained: the piece keeps its place in the tree and
    /// its drawing, and simply stands for nothing, so a press on it resolves to whatever encloses it.
    /// Believing it instead would let a selection come back as a range that does not contain what was
    /// selected.
    /// </para>
    /// </summary>
    private static void Detach(LatexNode node, List<Nexaflow.Maths.Latex.TexPart> above)
    {
        foreach (var child in node.Children.OfType<LatexNode>())
        {
            var taken = false;
            if (child.Origin is { } part)
            {
                if (above.Any(seen => ReferenceEquals(seen, part)) || !Within(part, above[^1]))
                {
                    child.Disown();
                }
                else
                {
                    above.Add(part);
                    taken = true;
                }
            }

            Detach(child, above);
            if (taken) above.RemoveAt(above.Count - 1);
        }
    }

    /// <summary>Whether one part is the other, or written somewhere inside it.</summary>
    private static bool Within(Nexaflow.Maths.Latex.TexPart part, Nexaflow.Maths.Latex.TexPart enclosing) =>
        ReferenceEquals(part, enclosing) || part.Ancestors().Any(up => ReferenceEquals(up, enclosing));

    /// <summary>
    /// A node is ink when it stands for a part and nothing beneath it stands for a smaller one — the
    /// leaves of the part-bearing subtree, which are exactly the things a reader can point at.
    /// <para>
    /// Leaf-of-the-box-tree would be the obvious rule and is wrong: an operator name such as <c>\sin</c>
    /// is a box holding a run of letter boxes, and those letters were built from no part of the reading
    /// at all — so the letters stand for nothing and <c>\sin</c> itself is the unit, container or not.
    /// </para>
    /// </summary>
    private static bool MarkInk(LatexNode node)
    {
        var below = false;
        foreach (var child in node.Children.OfType<LatexNode>())
            below |= MarkInk(child);

        // Standing for a part is normally what makes a piece worth pointing at. A hole is the exception
        // the rule needs: it stands for nothing written — that is what a hole is — and it is nonetheless
        // the most pointable thing on the page, being the one place the reader has been told to write. So
        // it counts as ink on the strength of being a hole rather than of standing for anything, and
        // everything that finds, hit-tests, selects or carries a symbol then finds it.
        var stands = node.Origin is { Derived: false } || node.IsPlaceholder();

        node.IsInk = stands && !below;
        return below || stands;
    }

    }
