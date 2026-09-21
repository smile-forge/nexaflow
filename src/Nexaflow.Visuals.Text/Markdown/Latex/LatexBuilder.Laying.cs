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
/// which part of the reading it stands for. A piece places itself: <see cref="Place"/> opens it, its
/// <see cref="Set.Draw"/> places its children through this builder, and the piece is closed — the recursion
/// <em>is</em> the tree, so parentage is kept rather than inferred afterwards from rectangles. A rotation cannot be
/// folded into a piece's anchor (turning is not moving), so it stays as a turn on the piece, centred on the
/// setting's origin where it was applied. Coordinates arrive in the setting's units with <c>y</c> on the piece's
/// baseline, and are scaled here to the pixels the element is painted in.
/// </summary>
public sealed partial class LatexBuilder
{
    /// <summary>What laying one formula made: its tree, how big it came out, where its baseline is, and what in it was set as its characters because nothing draws it.</summary>
    internal sealed record Placed(LayoutTree? Tree, Size Size, double Baseline, IReadOnlyList<ContentPart> Undrawn);

    // What is being laid, for the length of one LayFormula.
    private LayoutBuilder _laying = new();
    private ContentReading? _layingReading;

    /// <summary>
    /// For each open piece: where it sits, and where its own drawing is measured from. The two differ by however far
    /// the transforms above it have moved it — a piece's rectangle takes the move, its drawing does not (the move is
    /// a transform the picture pushes) — so neither can be worked out from the other.
    /// </summary>
    private readonly Stack<(Point Origin, Point Raw)> _open = new();

    /// <summary>The parts of everything the piece being placed is inside, nearest last — see <see cref="Owns"/>.</summary>
    private readonly List<ContentPart> _above = [];

    // A piece placed through PlaceTransformed can be shifted away from the coordinates it is handed. Translations
    // accumulate into where it sits; a rotation deliberately does not (a hit test wants an axis-aligned box either
    // way) and stays on the piece as a turn instead.
    private double _offsetX;
    private double _offsetY;
    private IReadOnlyList<Transformation> _pending = [];

    private readonly List<ContentPart> _undrawn = [];

    /// <summary>Whether any piece arrived at all — not the same as the union being empty: a formula that is only a thin space is a formula of no size, not no formula.</summary>
    private bool _placedAny;

    /// <summary>Lays a formula already set, as a builder with nothing else to do would — what a test measures.</summary>
    internal static Placed LayFormula(Set formula, ContentReading reading, double scale) =>
        new LatexBuilder(ContentReading.Of(ContentNode.Leaf(Kinds.Sequence, string.Empty)), EditState.For(string.Empty),
                         StyleFormat.Dark with { TextSize = scale }, isReadOnly: true)
            .LayFormula(formula, reading);

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

        // Settling onto the origin keeps a piece laid above/left of where the pen started from putting the caret
        // outside the control that draws it.
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

        // From two corners rather than an origin and a size: a piece's extent is signed (TeX kerns a root's degree
        // backwards over its sign), and Rect can't hold a negative size without clamping and silently moving the
        // left edge.
        var left = _scale * x;
        var top = _scale * (y - piece.Height);
        var right = left + _scale * piece.TotalWidth;
        var bottom = top + _scale * piece.TotalHeight;

        var raw = new Point(Math.Min(left, right), Math.Min(top, bottom));
        var size = new Size(Math.Abs(right - left), Math.Abs(bottom - top));

        var origin = new Point(raw.X + _offsetX, raw.Y + _offsetY);
        var parent = _open.Count > 0 ? _open.Peek().Origin : new Point(0, 0);

        if (piece.Undrawn is { } undrawn) _undrawn.Add(undrawn);

        // Spacing is not a thing on the page: a strut or glue is room the setting reserved, and what follows is
        // placed at the offset that room produces — there is nothing to make a piece for.
        if (piece.Spacing) return;

        var owns = Owns(piece);

        // What it *names* is narrower than what it was set from: a delimiter, command name or row separator has a
        // parse-tree node but naming it would make it the answer to "what did I press" — the group is what's
        // selectable, not half a bracket pair.
        var part = owns is not null && IsPlace(owns.Role) ? owns : null;

        _laying.Open(
            piece.Kind,
            part is null ? null : new TexSourcePart(part),
            new Point(origin.X - parent.X, origin.Y - parent.Y),

            // A run of things is not a place of its own: its ends are its contents' ends, so a stop there would
            // duplicate a mark the reader already sees. Unless something was written outside them — `Covered` asks
            // that; `x + ` ends in a space no element covers, and a reader has to be able to stand there.
            stops: owns is { } run && IsRun(run) && Covered(run) ? Stops.None : Stops.Both,

            // A set piece states its own height/depth (what it reserves on its line) rather than growing to fit its
            // contents — a subscript hangs below the piece that holds it.
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

        // Pointability falls out of two rules already in force — a leaf is its drawing, a press resolves to the
        // first thing above it that names source — nothing to declare here.
        _laying.Close();

        _open.Pop();
        if (owns is not null) _above.RemoveAt(_above.Count - 1);
    }

    /// <summary>
    /// What a piece was set from, or nothing where it repeats the part enclosing it or claims one from outside it.
    /// Each piece must stand for a <em>different</em> part or a click can't tell which was meant — e.g. a root's
    /// radical sign is set from the same part as the whole root, so it stands for nothing and lets the degree,
    /// contents and whole each keep their own link. Checked against every part above, not just the nearest, since a
    /// duplicate can skip a level (an integral sign inside a big operator set from the same part). A piece drawn
    /// inside another can't have been written outside it, so a part outside the enclosing one is dropped rather than
    /// trusted — the piece keeps its place and drawing but stands for nothing, and a press on it resolves to whatever
    /// encloses it.
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

    /// <summary>Whether a part is a run of things (every child role is <c>element</c>) rather than a construct whose parts have meaningful roles like <c>numerator</c>, <c>radicand</c>.</summary>
    internal static bool IsRun(ContentPart part) =>
        part.Parts.Any() && part.Parts.All(inner => inner.Role == Roles.Element);

    /// <summary>
    /// Whether the things in a run reach both of its ends, so it has no edge of its own for a caret to stand at —
    /// nearly always true; <c>x + </c> ends in a space no element covers, and a reader has to be able to stand past
    /// it. Compared in <see cref="TexSourcePart"/> terms rather than raw spans: comparing raw spans once made the
    /// cell of <c>c &amp;= d\, </c> look like it reached past its own contents, so backspace at the end of an align
    /// line un-rendered the whole cell instead of taking a character. Wrappers are walked past first, since a run
    /// holding one thing (e.g. a formula that is a single fraction) draws as one piece with its wrapper.
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

        // In the piece's own frame: a glyph run's baseline origin is baked in, with no offset to give DrawGlyphRun
        // afterwards.
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

    /// <summary>How the piece is turned, if at all — translations are already in its anchor, so only rotation is left, centred on the setting's origin (where it was applied) to keep <c>\overbrace</c> drawing correctly.</summary>
    private static IReadOnlyList<Transform>? Turn(IReadOnlyList<Transformation> pending, Point raw)
    {
        if (pending.Count == 0) return null;

        List<Transform>? turns = null;
        foreach (var transformation in pending)
            if (transformation is Transformation.Rotate rotate)
                (turns ??= []).Add(new RotateTransform(rotate.RotationDegrees, -raw.X, -raw.Y));

        return turns;
    }

    /// <summary>The pixel grid this piece's edges snap to, from the piece's baseline rather than its top — matching the typesetter's own renderer so the picture is identical.</summary>
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

    /// <summary>How much of the page a laid formula actually covers. Spacing is left out — a strut is as tall as the line it reserves, so counting it would pad the element with an undrawn margin.</summary>
    internal static Rect Extent(Piece root)
    {
        var union = Rect.Empty;

        foreach (var (piece, where) in root.Placed())
            if (piece.Kind is not ("StrutBox" or "GlueBox"))
                union.Union(where);

        return union.IsEmpty ? new Rect(0, 0, 0, 0) : union;
    }

    /// <summary>Whether a role names a place content goes, as against the punctuation that holds it (a brace, command name or row separator) — none of which is pointable on its own.</summary>
    private static bool IsPlace(string role) =>
        role is not (Roles.Name or Roles.Open or Roles.Close or Roles.Separator or Roles.Trivia or Roles.Row);
}
