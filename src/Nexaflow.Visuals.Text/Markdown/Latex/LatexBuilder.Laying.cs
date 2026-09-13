using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Latex;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Fonts;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering.Transformations;

using GuidelineSet = System.Windows.Media.GuidelineSet;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using RotateTransform = System.Windows.Media.RotateTransform;
using Size = System.Windows.Size;
using Transform = System.Windows.Media.Transform;
using Vector = System.Windows.Vector;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// The laying half: a formula already set, placed piece by piece into the layout tree — what was drawn, where, and
/// which part of the reading it stands for.
/// <para>
/// A piece places itself: <see cref="Place"/> opens it, its <see cref="Set.Draw"/> places its children and marks
/// through this builder, and the piece is closed. The recursion <em>is</em> the tree, so parentage is kept rather
/// than inferred afterwards from rectangles.
/// </para>
/// <para>
/// A piece is anchored where it sits and its drawing is recorded from that anchor, so the shared painter draws it
/// like anything else. The one thing that cannot be folded into an anchor is a rotation, which stays as a turn on the
/// piece because turning is not moving; it is centred on the setting's origin, which is where it was applied.
/// </para>
/// <para>
/// Coordinates arrive in the setting's units with <c>y</c> on the piece's <em>baseline</em>, and are scaled here so
/// the tree is in the same pixels the element is painted in.
/// </para>
/// </summary>
public sealed partial class LatexBuilder
{
    /// <summary>What laying one formula made: its tree, how big it came out, where its baseline is, and what in it was set as its characters because nothing draws it.</summary>
    internal sealed record Placed(LayoutTree? Tree, Size Size, double Baseline, IReadOnlyList<ContentPart> Undrawn);

    // What is being laid, for the length of one LayFormula.
    private LayoutBuilder _laying = new();
    private ContentReading? _layingReading;

    /// <summary>
    /// For each open piece: where it sits, and where its own drawing is measured from.
    /// <para>
    /// The two differ by however far the transforms above it have moved it. A piece's rectangle takes the move; its
    /// drawing does not, because the move is a transform the picture pushes — so both numbers are needed and neither
    /// can be worked out from the other.
    /// </para>
    /// </summary>
    private readonly Stack<(Point Origin, Point Raw)> _open = new();

    /// <summary>The parts of everything the piece being placed is inside, nearest last — see <see cref="Owns"/>.</summary>
    private readonly List<ContentPart> _above = [];

    // A piece placed through PlaceTransformed can be shifted away from the coordinates it is handed. Translations are
    // accumulated into where the piece sits; a rotation is deliberately not, because an axis-aligned bounding box is
    // what a hit test wants either way — so it stays on the piece as a turn instead.
    private double _offsetX;
    private double _offsetY;
    private IReadOnlyList<Transformation> _pending = [];

    private readonly List<ContentPart> _undrawn = [];

    /// <summary>
    /// Whether any piece arrived at all. Not the same question as whether the union is empty: a formula that is nothing
    /// but a thin space lays out room and draws no ink, and it is a formula of no size rather than no formula.
    /// </summary>
    private bool _placedAny;

    /// <summary>Lays a formula already set, as a builder with nothing else to do would — what a test measures.</summary>
    internal static Placed LayFormula(Set formula, ContentReading reading, double scale) =>
        new LatexBuilder(string.Empty, scale, false, "Arial", null, false, 1.0, 0).LayFormula(formula, reading);

    /// <summary>Lays a formula already set: its top at the origin, then the tree sealed and settled onto its ink.</summary>
    private Placed LayFormula(Set formula, ContentReading reading)
    {
        _laying = new LayoutBuilder();
        _layingReading = reading;
        _open.Clear();
        _above.Clear();
        _undrawn.Clear();
        _offsetX = _offsetY = 0;
        _pending = [];
        _placedAny = false;

        Place(formula, 0, formula.Height);

        // Nothing placed is no formula; settling it onto the origin is one number rather than a rewrite of every
        // rectangle, and keeps a piece laid above or left of where the pen started from putting the caret outside the
        // control that draws it.
        if (!_placedAny) return new Placed(null, default, 0, [.. _undrawn]);

        var tree = _laying.Seal();
        var covers = Extent(tree.Root);
        tree.Settle(new Vector(-covers.X, -covers.Y));

        return new Placed(tree, new Size(covers.Width, covers.Height), -covers.Y, [.. _undrawn]);
    }

    internal void Place(Set piece, double x, double y)
    {
        var turns = _pending;
        _pending = [];

        // From two corners rather than an origin and a size, because a piece's extent is signed: TeX kerns backwards
        // to tuck a root's degree over its sign, so that strut is genuinely three-quarters of a unit wide *to the
        // left*. A Rect will not hold a negative size, and clamping one to zero would quietly move its left edge to
        // where the pen already was.
        var left = _scale * x;
        var top = _scale * (y - piece.Height);
        var right = left + _scale * piece.TotalWidth;
        var bottom = top + _scale * piece.TotalHeight;

        var raw = new Point(Math.Min(left, right), Math.Min(top, bottom));
        var size = new Size(Math.Abs(right - left), Math.Abs(bottom - top));

        var origin = new Point(raw.X + _offsetX, raw.Y + _offsetY);
        var parent = _open.Count > 0 ? _open.Peek().Origin : new Point(0, 0);

        if (piece.Undrawn is { } undrawn) _undrawn.Add(undrawn);

        // Spacing is not a thing on the page. A strut and a piece of glue are room the setting reserved, and what
        // comes after them is placed at the offset that room produces — so the gap is the gap, and there is nothing
        // to make a piece for.
        if (piece.Spacing) return;

        var owns = Owns(piece);

        // What it *names*, which is narrower than what it was set from. A delimiter, a command name, a row separator:
        // the parse tree has nodes for those and the layout has no use for them. Naming one would make it the answer
        // to "what did I press", and half a bracket pair is not something a reader can be told they have selected —
        // the group is.
        var part = owns is not null && IsPlace(owns.Role) ? owns : null;

        _laying.Open(
            piece.Kind,
            part is null ? null : new TexSourcePart(part),
            new Point(origin.X - parent.X, origin.Y - parent.Y),

            // A run of things is not a place of its own: its ends are its contents' ends, so a stop there would be a
            // second mark drawn where the reader sees one, and the arrow key would walk between two identical
            // positions instead of leaving the formula.
            //
            // Unless the writer put something outside them, which is what `Covered` asks. `x + ` finishes with a
            // space no element covers, and that space is exactly where a reader arriving from the text after it has
            // to be able to stand.
            stops: owns is { } run && IsRun(run) && Covered(run) ? Stops.None : Stops.Both,

            // A set piece is the height and depth it reserves on its line rather than a piece around what it holds —
            // a subscript hangs below the very piece that holds it — so it states its own extent and never grows to
            // fit.
            gathers: false,

            paints: new LayoutPaint(Turn(turns, raw), Snap(piece, x, y, raw)));

        _laying.Covers(new Rect(0, 0, size.Width, size.Height));

        // A \colorbox, which goes under every glyph of the formula rather than only its own.
        if (piece.Background is { } wash)
            _laying.Draw(new WashMark(new Rect(0, 0, size.Width, size.Height), wash));

        _placedAny = true;

        if (owns is not null) _above.Add(owns);
        _open.Push((origin, raw));

        // The recursion: children place themselves through Place.
        piece.Draw?.Invoke(this, piece, x, y);

        // Nothing to say about whether a reader can point at this. The three letters of an operator name are drawn
        // and the name is what you point at; a bracket is drawn and the group is what you point at. Both fall out of
        // the same two rules — a leaf is the drawing, and a press means the first thing above it that names a stretch
        // of source — and neither needs declaring.
        _laying.Close();

        _open.Pop();
        if (owns is not null) _above.RemoveAt(_above.Count - 1);
    }

    /// <summary>
    /// What a piece was set from, or nothing where it repeats the part enclosing it or claims one from outside it.
    /// <para>
    /// Each piece of layout must stand for a <em>different</em> part, or the link back stops being an answer and
    /// becomes a question. A root is the case that proves it: the radical sign is set from the whole
    /// <c>\sqrt[3]{x+1}</c> — the same part the piece holding the whole root already carries. Left alone, a reader
    /// pointing at the sign and a reader selecting the root arrive at the same link and something downstream has to
    /// guess which was meant. The sign is the root's own drawing, so it stands for nothing; the degree stands for the
    /// degree, the contents for the contents, and the piece above them for the whole.
    /// </para>
    /// <para>
    /// Against every part above it, not merely the nearest: an integral sign is a piece inside a big operator set
    /// from the same part, and something in between can be a different part again, so comparing one level up leaves
    /// the duplicate standing two. The open stack is that spine, which is why this is asked as each piece arrives.
    /// </para>
    /// <para>
    /// And a piece drawn inside another cannot have been written outside it, so a part that is not the enclosing one
    /// nor anything under it is not true of this piece and is taken away rather than trusted. The piece keeps its place
    /// in the tree and its drawing, and simply stands for nothing, so a press on it resolves to whatever encloses it.
    /// </para>
    /// </summary>
    private ContentPart? Owns(Set piece)
    {
        // A strut and a piece of glue are room rather than ink, and were written by nobody.
        var part = piece.Spacing ? null : piece.Part;

        // The whole layout stands for the whole formula, whatever the outermost piece happened to be set from.
        // Without this a selection that grew all the way out would stand for nothing at all.
        if (_open.Count == 0) return part ?? _layingReading!.Root;

        if (part is null) return null;

        return _above.Any(seen => ReferenceEquals(seen, part)) || !Within(part, _above[^1]) ? null : part;
    }

    /// <summary>Whether one part is the other, or written somewhere inside it.</summary>
    private static bool Within(ContentPart part, ContentPart enclosing) =>
        ReferenceEquals(part, enclosing) || part.Ancestors().Any(up => ReferenceEquals(up, enclosing));

    /// <summary>
    /// Whether a part is a run of things rather than one thing made of parts. A row names every piece of it
    /// <c>element</c>, because that is all a sequence can say about what it holds, where a construct names its parts
    /// <c>numerator</c>, <c>radicand</c>, <c>superscript</c> — each meaning something to the construct. So the roles
    /// already carry the distinction.
    /// </summary>
    internal static bool IsRun(ContentPart part) =>
        part.Parts.Any() && part.Parts.All(inner => inner.Role == Roles.Element);

    /// <summary>
    /// Whether the things in a run reach both of its ends, so that it has no edge of its own for a caret to stand at.
    /// <para>
    /// Nearly always true, and the exception is what this is for: <c>x + </c> ends in a space no element covers, and a
    /// reader arriving from the text after the formula has to be able to stand past it.
    /// </para>
    /// <para>
    /// Asked in the terms the <em>piece</em> will report, which is what <see cref="TexSourcePart"/> decides and is not
    /// always what the part spans: a cell stands for what was written in it rather than for the separator and the
    /// spacing around it. Comparing raw spans made the cell of <c>c &amp;= d\, </c> look as though it reached a
    /// character past its own contents, so it declared a stop there — and backspace at the end of a line of an align
    /// block un-rendered the whole cell instead of taking a character.
    /// </para>
    /// <para>
    /// Wrappers are walked past first. A run holding one thing is not a row of things — the parse wraps a formula that
    /// is a single fraction in an element — and the layout draws a wrapper and the thing in it as one piece, so asking
    /// a wrapper what its parts cover only asks about the wrapper.
    /// </para>
    /// </summary>
    private static bool Covered(ContentPart run)
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

    /// <summary>A measured piece placed through transformations — moved into its anchor, turned on the piece.</summary>
    internal void PlaceTransformed(Set piece, IEnumerable<Transformation> transforms, double x, double y)
    {
        var scaled = transforms.Select(t => t.Scale(_scale)).ToList();

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
        Place(piece, x, y);
        _offsetX -= dx;
        _offsetY -= dy;
    }

    /// <summary>A glyph, into the piece being placed.</summary>
    internal void DrawGlyph(CharInfo info, double x, double y, IBrush? foreground)
    {
        var raw = _open.Peek().Raw;

        // In the piece's own frame, which the glyph run has to be built in: its baseline origin is baked into it and
        // there is no offset to give a DrawGlyphRun afterwards.
        _laying.Draw(new GlyphMark(
            info.GetGlyphRun(x - (raw.X / _scale), y - (raw.Y / _scale), _scale),
            (foreground as WpfBrush)?.Value));
    }

    /// <summary>A stroke, into the piece being placed.</summary>
    internal void DrawLine(Tex.Rendering.Point point0, Tex.Rendering.Point point1, IBrush? foreground) =>
        _laying.Draw(new LineMark(Local(point0.X, point0.Y), Local(point1.X, point1.Y), (foreground as WpfBrush)?.Value));

    /// <summary>A filled rectangle, into the piece being placed.</summary>
    internal void DrawRule(Rectangle rectangle, IBrush? foreground)
    {
        var at = Local(rectangle.X, rectangle.Y);
        _laying.Draw(new RuleMark(
            new Rect(at.X, at.Y, _scale * rectangle.Width, _scale * rectangle.Height),
            (foreground as WpfBrush)?.Value));
    }

    /// <summary>A coordinate in the setting's units, in the frame of the piece being placed.</summary>
    private Point Local(double x, double y)
    {
        var raw = _open.Peek().Raw;
        return new Point((_scale * x) - raw.X, (_scale * y) - raw.Y);
    }

    /// <summary>
    /// How the piece is turned, if at all. The translations are already in its anchor, so only a rotation is left —
    /// centred on the setting's origin, which is where it was applied and is what keeps <c>\overbrace</c> drawing
    /// exactly as it did.
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
    /// The pixel grid this piece's edges snap to. Its numbers come from the raw coordinates and take the piece's
    /// <em>baseline</em> rather than its top — which is how the typesetter's own renderer did it, and reproducing that
    /// is what keeps the picture identical. Stated in the piece's own frame, the same frame its drawing is in.
    /// </summary>
    private GuidelineSet Snap(Set piece, double x, double y, Point raw)
    {
        var guidelines = new GuidelineSet
        {
            GuidelinesX = { (_scale * x) - raw.X, (_scale * (x + piece.TotalWidth)) - raw.X },
            GuidelinesY = { (_scale * y) - raw.Y, (_scale * (y + piece.TotalHeight)) - raw.Y },
        };
        guidelines.Freeze();
        return guidelines;
    }

    /// <summary>
    /// How much of the page a laid formula actually covers. Spacing is left out: a strut is as tall as the line it
    /// reserves room on, so counting it would pad the element with margin nothing is drawn in.
    /// </summary>
    internal static Rect Extent(Piece root)
    {
        var union = Rect.Empty;

        foreach (var (piece, where) in root.Placed())
            if (piece.Kind is not ("StrutBox" or "GlueBox"))
                union.Union(where);

        return union.IsEmpty ? new Rect(0, 0, 0, 0) : union;
    }

    /// <summary>
    /// Whether a role names a place content goes, as against the punctuation that holds it. A brace, a command name and
    /// a row separator are how the writer said what they meant; none of them is a thing they can point at on its own.
    /// </summary>
    private static bool IsPlace(string role) =>
        role is not (Roles.Name or Roles.Open or Roles.Close or Roles.Separator or Roles.Trivia or Roles.Row);
}
