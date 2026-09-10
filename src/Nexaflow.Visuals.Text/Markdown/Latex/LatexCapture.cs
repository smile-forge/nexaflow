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
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Vector = System.Windows.Vector;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// Walks a typeset formula and builds it into a <see cref="LayoutTree"/>: what was drawn, where, and
/// which slice of the LaTeX produced it.
/// <para>
/// This needs no cooperation from WpfMath beyond what is already public.
/// <see cref="IElementRenderer.RenderElement"/> is called for every box of the formula, parents before
/// children, and each <see cref="Box"/> carries the part of the parse tree it was built from.
/// Rendering with a renderer that draws nothing and only remembers therefore yields the whole
/// structure — the recursion in <see cref="RenderElement"/> <em>is</em> the tree, so parentage is kept
/// rather than inferred afterwards from rectangles.
/// </para>
/// <para>
/// Every box becomes a piece, spacing included. Dropping the ones that are not interesting would break
/// the nesting for whatever sits beneath them, and it costs nothing to keep them: they are simply not ink.
/// </para>
/// <para>
/// <strong>Two frames met here and now only one does.</strong> A box's rectangle used to be absolute
/// while its drawing stayed in the typesetter's own coordinates, reconciled at paint time by pushing
/// every ancestor's transform — which is why a formula needed a painter of its own. A piece is anchored
/// where it sits and its drawing recorded from that anchor, so the shared painter draws it like anything
/// else. The one thing that cannot be folded in is a rotation, which stays as a turn on the piece
/// because turning is not moving; it is centred on the typesetter's origin, which is exactly where it
/// was applied before.
/// </para>
/// <para>
/// Coordinates arrive in logical units with <paramref name="y"/> on the box's <em>baseline</em>; they are
/// scaled here so the tree is in the same pixels the element is painted in.
/// </para>
/// </summary>
internal sealed class LatexCapture : IElementRenderer
{
    private readonly LayoutBuilder _build = new();
    private readonly double _scale;
    private readonly TexReading _reading;

    /// <summary>
    /// For each open piece: where it sits, and where its own drawing is measured from.
    ///
    /// <para>
    /// The two differ by however far the transforms above it have moved it. A box's rectangle takes the
    /// move; its drawing does not, because the move is a transform the picture pushes — so both numbers
    /// are needed and neither can be worked out from the other.
    /// </para>
    /// </summary>
    private readonly Stack<(Point Origin, Point Raw)> _open = new();

    /// <summary>
    /// The parts of everything the piece being built is inside, nearest last — what says whether its own
    /// part is a new one. See <see cref="Owns"/>.
    /// </summary>
    private readonly List<Nexaflow.Maths.Latex.TexPart> _above = [];

    /// <summary>Whether anything inside the piece being built stands for a part of its own.</summary>
    // OverUnderBox (\overrightarrow and friends) draws through RenderTransformed, so a box can be shifted
    // away from the coordinates it is handed. Translations are accumulated into where the piece sits; a
    // rotation is deliberately not, because an axis-aligned bounding box is what a hit test wants either
    // way — so it stays on the piece as a turn instead.
    private double _offsetX;
    private double _offsetY;
    private IReadOnlyList<Transformation> _pending = [];



    /// <summary>
    /// Whether any box arrived at all. Not the same question as whether the union is empty: a formula
    /// that is nothing but a thin space lays out a box that reserves room and draws no ink, and it is a
    /// formula of no size rather than no formula.
    /// </summary>
    private bool _built;

    public LatexCapture(double scale, TexReading reading)
    {
        _scale = scale;
        _reading = reading;
    }

    /// <summary>The formula's whole layout, or null when nothing was drawn at all.</summary>
    public LayoutTree? Tree { get; private set; }

    /// <summary>How big it came out.</summary>
    public Size Size { get; private set; }

    /// <summary>
    /// How far down from its top the formula's baseline is — what a second layout set beside it lines up on.
    /// </summary>
    public double Baseline { get; private set; }

    public void RenderElement(Box box, double x, double y)
    {
        var turns = _pending;
        _pending = [];

        // From two corners rather than an origin and a size, because a box's extent is signed: TeX kerns
        // backwards to tuck a root's degree over its sign, so that strut is genuinely three-quarters of a
        // unit wide *to the left*. A Rect will not hold a negative size, and clamping one to zero would
        // quietly move its left edge to where the pen already was.
        var left = _scale * x;
        var top = _scale * (y - box.Height);
        var right = left + _scale * box.TotalWidth;
        var bottom = top + _scale * box.TotalHeight;

        var raw = new Point(Math.Min(left, right), Math.Min(top, bottom));
        var size = new Size(Math.Abs(right - left), Math.Abs(bottom - top));

        var origin = new Point(raw.X + _offsetX, raw.Y + _offsetY);
        var parent = _open.Count > 0 ? _open.Peek().Origin : new Point(0, 0);

        // Spacing is not a thing on the page. A strut and a piece of glue are room the typesetter reserved,
        // and the builder places what comes after them at the offset that room produces — so the gap is the
        // gap, and there is nothing to make a piece for.
        if (box is StrutBox or GlueBox) return;

        var kind = box.GetType().Name;
        var owns = Owns(box);

        // What it *names*, which is narrower than what it was built from. A delimiter, a command name, a
        // row separator: the parse tree has nodes for those and the layout has no use for them. Naming one
        // would make it the answer to "what did I press", and half a bracket pair is not something a reader
        // can be told they have selected — the group is.
        //
        // The layout tree does not mirror the parse tree and never needed to. What it needs from a part is
        // the stretch of source a piece stands for; punctuation stands for the construct that drew it, and
        // the climb finds that on its own.
        var part = owns is not null && IsPlace(owns.Role) ? owns : null;

        _build.Open(
            kind,
            part is null ? null : new TexSourcePart(part),
            new Point(origin.X - parent.X, origin.Y - parent.Y),


            // A run of things is not a place of its own. Its ends are its contents' ends — the first
            // element starts where the run starts and the last finishes where it finishes, which is what
            // makes it a run — so a stop of its own would be a second bar at an offset the reader sees
            // one place at, and the arrow key would walk between two identical positions instead of
            // leaving the formula.
            //
            // A run of things is not a place of its own: its ends are its contents' ends, so a stop there
            // would be a second mark drawn where the reader sees one, and the arrow key would walk between
            // two identical positions instead of leaving the formula.
            //
            // Unless the writer put something outside them, which is what `Covered` asks. `x + ` finishes
            // with a space no element covers, and that space is exactly where a reader arriving from the
            // text after it has to be able to stand.
            stops: owns is { } run && IsRun(run) && Covered(run) ? Stops.None : Stops.Both,


            // A typeset box is the height and depth it reserves on its line rather than a box around what
            // it holds — a subscript hangs below the very piece that holds it — so it states its own
            // extent and never grows to fit.
            gathers: false,

            paints: new LayoutPaint(Turn(turns, raw), Snap(box, x, y, raw)));

        _build.Covers(new Rect(0, 0, size.Width, size.Height));

        // A \colorbox, which goes under every glyph of the formula rather than only its own — see the
        // wash pass in LatexLayout.Paint.
        if (box.Background is WpfBrush wash)
            _build.Draw(new WashMark(new Rect(0, 0, size.Width, size.Height), wash.Value));

        _built = true;

        if (owns is not null) _above.Add(owns);
        _open.Push((origin, raw));


        box.RenderTo(this, x, y);   // the recursion - children report themselves through RenderElement



        // Nothing to say about whether a reader can point at this. The three letters of an operator name
        // are drawn and the name is what you point at; a bracket is drawn and the group is what you point
        // at. Both fall out of the same two rules — a leaf is the drawing, and a press means the first
        // thing above it that names a stretch of source — and neither needs a builder to declare it.
        _build.Close();

        _open.Pop();
        if (owns is not null) _above.RemoveAt(_above.Count - 1);

    }

    /// <summary>
    /// What a box was drawn from, or nothing where it repeats the part enclosing it or claims one from
    /// outside it.
    ///
    /// <para>
    /// Each piece of layout must stand for a <em>different</em> part, or the link back stops being an
    /// answer and becomes a question. A root is the case that proves it: the radical sign is built from
    /// the radical atom, which is the whole <c>\sqrt[3]{x+1}</c> — the same part the piece holding the
    /// whole root already carries. Left alone, a reader pointing at the sign and a reader selecting the
    /// root arrive at the same link and something downstream has to guess which was meant, and every
    /// version of that guess has been wrong somewhere. The sign is the root's own drawing, so it stands
    /// for nothing; the degree stands for the degree, the contents for the contents, and the piece above
    /// them for the whole.
    /// </para>
    /// <para>
    /// Against every part above it, not merely the nearest: an integral sign is a box inside a
    /// big-operator box built from the same atom, and something in between can be a different part
    /// again, so comparing one level up leaves the duplicate standing two. The builder's own open stack
    /// is that spine, which is why this is asked as each box arrives rather than walked afterwards.
    /// </para>
    /// <para>
    /// And a piece drawn inside another cannot have been written outside it, so a part that is not the
    /// enclosing one nor anything under it is not true of this piece and is taken away rather than
    /// trusted. The typesetter still builds a box or two that way — a style wraps a run and hands back a
    /// box holding a neighbour's — and the fault is contained: the piece keeps its place in the tree and
    /// its drawing, and simply stands for nothing, so a press on it resolves to whatever encloses it.
    /// </para>
    /// </summary>
    private Nexaflow.Maths.Latex.TexPart? Owns(Box box)
    {
        // A strut and a piece of glue are room rather than ink, and were written by nobody.
        var part = box is StrutBox or GlueBox ? null : box.Node?.Origin;

        // The whole layout stands for the whole formula, whatever the outermost box happened to be built
        // from. Without this a selection that grew all the way out would stand for nothing at all.
        if (_open.Count == 0) return part ?? _reading.Root;

        if (part is null) return null;

        return _above.Any(seen => ReferenceEquals(seen, part)) || !Within(part, _above[^1]) ? null : part;
    }

    /// <summary>Whether one part is the other, or written somewhere inside it.</summary>
    private static bool Within(Nexaflow.Maths.Latex.TexPart part, Nexaflow.Maths.Latex.TexPart enclosing) =>
        ReferenceEquals(part, enclosing) || part.Ancestors().Any(up => ReferenceEquals(up, enclosing));

    /// <summary>
    /// Whether a part is a run of things rather than one thing made of parts. A row names every piece
    /// of it <c>element</c>, because that is all a sequence can say about what it holds, where a
    /// construct names its parts <c>numerator</c>, <c>radicand</c>, <c>superscript</c> — each meaning
    /// something to the construct. So the roles already carry the distinction.
    /// </summary>
    internal static bool IsRun(Nexaflow.Maths.Latex.TexPart part) =>
        part.Parts.Any() && part.Parts.All(inner => inner.Role == Nexaflow.Maths.Latex.TexRole.Element);

    /// <summary>
    /// Whether the things in a run reach both of its ends, so that it has no edge of its own for a caret to
    /// stand at.
    ///
    /// <para>
    /// Nearly always true, and the exception is what this is for: <c>x + </c> ends in a space no element
    /// covers, and a reader arriving from the text after the formula has to be able to stand past it.
    /// </para>
    /// <para>
    /// Asked in the terms the <em>piece</em> will report, which is what <see cref="TexSourcePart"/> decides
    /// and is not always what the part spans: a cell stands for what was written in it rather than for the
    /// separator and the spacing around it. Comparing raw spans made the cell of <c>c &amp;= d\, </c> look
    /// as though it reached a character past its own contents, so it declared a stop there — and backspace
    /// at the end of a line of an align block un-rendered the whole cell instead of taking a character.
    /// </para>
    /// <para>
    /// Wrappers are walked past first. A run holding one thing is not a row of things — the parse wraps a
    /// formula that is a single fraction in an element — and the layout draws a wrapper and the thing in it
    /// as one piece, so asking a wrapper what its parts cover only asks about the wrapper.
    /// </para>
    /// </summary>
    private static bool Covered(Nexaflow.Maths.Latex.TexPart run)
    {
        var inner = run;
        while (inner.Parts.Count() == 1 && IsRun(inner)) inner = inner.Parts.First();

        var parts = inner.Parts.ToList();
        if (parts.Count == 0) return false;

        var whole = new TexSourcePart(run);
        var first = new TexSourcePart(parts[0]);
        var last = new TexSourcePart(parts[^1]);

        return first.Start <= whole.Start && last.Start + last.Length >= whole.Start + whole.Length;
    }

    public void RenderTransformed(Box box, IEnumerable<Transformation> transforms, double x, double y)
    {
        var scaled = transforms.Select(t => t.Scale(_scale)).ToList();

        // Two things are wanted from a transform and they are not the same thing. Where the box ends up
        // is a move, and a move belongs in the anchor, where the tree can use it. Being turned is not a
        // move and cannot go there, so it stays as a turn on the piece.
        double dx = 0, dy = 0;
        foreach (var transform in scaled)
            if (transform is Transformation.Translate translate)
            {
                dx += translate.X;
                dy += translate.Y;
            }

        _pending = scaled;

        _offsetX += dx;
        _offsetY += dy;
        RenderElement(box, x, y);
        _offsetX -= dx;
        _offsetY -= dy;
    }

    public void RenderCharacter(CharInfo info, double x, double y, IBrush? foreground)
    {
        var raw = _open.Peek().Raw;

        // In the piece's own frame, which the glyph run has to be built in: its baseline origin is baked
        // into it and there is no offset to give a DrawGlyphRun afterwards.
        _build.Draw(new GlyphMark(
            info.GetGlyphRun(x - (raw.X / _scale), y - (raw.Y / _scale), _scale),
            (foreground as WpfBrush)?.Value));
    }

    public void RenderLine(XamlMath.Rendering.Point point0, XamlMath.Rendering.Point point1, IBrush? foreground) =>
        _build.Draw(new LineMark(Local(point0.X, point0.Y), Local(point1.X, point1.Y),
                                 (foreground as WpfBrush)?.Value));

    public void RenderRectangle(Rectangle rectangle, IBrush? foreground)
    {
        var at = Local(rectangle.X, rectangle.Y);
        _build.Draw(new RuleMark(
            new Rect(at.X, at.Y, _scale * rectangle.Width, _scale * rectangle.Height),
            (foreground as WpfBrush)?.Value));
    }

    /// <summary>A coordinate the typesetter gave, in the frame of the piece being built.</summary>
    private Point Local(double x, double y)
    {
        var raw = _open.Peek().Raw;
        return new Point((_scale * x) - raw.X, (_scale * y) - raw.Y);
    }

    /// <summary>
    /// How the piece is turned, if at all. The translations are already in its anchor, so only a rotation
    /// is left — centred on the typesetter's origin, which is where it was applied before and is what
    /// keeps <c>\overrightarrow</c> drawing exactly as it did.
    /// </summary>
    private static IReadOnlyList<Transform>? Turn(IReadOnlyList<Transformation> pending, Point raw)
    {
        if (pending.Count == 0) return null;

        List<Transform>? turns = null;
        foreach (var transformation in pending)
            if (transformation is Transformation.Rotate rotate)
                (turns ??= []).Add(new RotateTransform(rotate.RotationDegrees, -raw.X, -raw.Y));

        return turns;
    }

    /// <summary>
    /// The pixel grid this box's edges snap to, matching WpfMath's own renderer exactly — its numbers come
    /// from the raw coordinates and take the box's <em>baseline</em> rather than its top. Reproducing that
    /// faithfully is what keeps the picture identical; they are stated in the piece's own frame, which is
    /// the same frame its drawing is in.
    /// </summary>
    private GuidelineSet Snap(Box box, double x, double y, Point raw)
    {
        var guidelines = new GuidelineSet
        {
            GuidelinesX = { (_scale * x) - raw.X, (_scale * (x + box.TotalWidth)) - raw.X },
            GuidelinesY = { (_scale * y) - raw.Y, (_scale * (y + box.TotalHeight)) - raw.Y },
        };
        guidelines.Freeze();
        return guidelines;
    }

    /// <summary>
    /// Seals the tree and settles it onto the origin, so nothing sits at a negative coordinate — which is
    /// one number now rather than a rewrite of every rectangle. A box laid out above or left of where the
    /// pen started would otherwise put the caret outside the control that draws it.
    ///
    /// <para>
    /// Measured from the finished tree rather than gathered on the way through it, because it is a
    /// question about the tree: spacing is left out — a strut is as tall as the line it reserves room on,
    /// so counting it would pad the element with margin nothing is drawn in — and that is a rule about
    /// what a piece turned out to be rather than about the order the boxes arrived in.
    /// </para>
    /// </summary>
    public void FinishRendering()
    {
        if (!_built) return;

        var tree = _build.Seal();
        var covers = Extent(tree.Root);

        tree.Settle(new Vector(-covers.X, -covers.Y));

        Size = new Size(covers.Width, covers.Height);
        Baseline = -covers.Y;
        Tree = tree;
    }

    /// <summary>
    /// How much of the page the formula actually covers. Spacing is left out: a strut is as tall as the
    /// line it reserves room on, so counting it would pad the element with margin nothing is drawn in.
    /// </summary>
    internal static Rect Extent(Piece root)
    {
        var union = Rect.Empty;

        foreach (var (piece, where) in root.Placed())
            if (piece.Kind is not ("StrutBox" or "GlueBox"))
                union.Union(where);

        return union.IsEmpty ? new Rect(0, 0, 0, 0) : union;
    }

    /// <summary>What the typesetter calls the hollow box it stands in an argument nobody has written yet.</summary>
    private const string HoleKind = "PlaceholderBox";

    /// <summary>
    /// Whether a role names a place content goes, as against the punctuation that holds it. A brace, a
    /// command name and a row separator are how the writer said what they meant; none of them is a thing
    /// they can point at on its own.
    /// </summary>
    private static bool IsPlace(string role) =>
        role is not (TexRole.Name or TexRole.Open or TexRole.Close
                     or TexRole.Separator or TexRole.Trivia or TexRole.Row);
}
