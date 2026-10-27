using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Typesetting;
using Nexaflow.Markdown.Typesetting.Tex;
using Nexaflow.Markdown.Typesetting.Tex.Exceptions;
using Nexaflow.Markdown.Typesetting.Tex.Rendering;
using Nexaflow.Markdown.Typesetting.Tex.Rendering.Transformations;
using Point = Nexaflow.Markdown.Typesetting.Tex.Rendering.Point;

namespace Nexaflow.Markdown.Layout.Maths;

/// <summary>
/// A formula, typeset and laid on the layout tree. <see cref="TexTypesetter"/> sets it and this lays what it set, as the
/// <see cref="ITexLayer"/> it draws through — each piece opened where its parent chose, each glyph, stroke and rule asked of the
/// medium. Set larger and apart as a block among a document's, and smaller in a line of words.
/// </summary>
internal sealed class LatexBuilder : ContentBuilder, ITexLayer
{
    private const double Display = 1.5;

    /// <summary>The family <c>\text{…}</c> is set in.</summary>
    private const string TextFamily = "Arial";

    internal LatexBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                  LayoutHost host)
        : base(reading, state, style, isReadOnly, medium, host)
    {
    }

    private double Scale => StandsAs == LayoutStanding.Block ? Style.TextSize * Display : Style.TextSize;

    private bool Inline => StandsAs == LayoutStanding.Inline;

    private double Block => double.IsInfinity(Room) ? 0 : Room;

    protected override LayoutCharacters Characters => new(Style.Face(Style.MonoFont), Scale * 0.6, Style.Text);
    protected override Laid? Build()
    {
        if (Reading.Root.Length == 0) return null;

        var environment = TexFonts.Of(Medium, TextFamily).Environment(Inline ? TexStyle.Text : TexStyle.Display, Scale);
        var formula = TexTypesetter.Formula(Reading.Root, environment);

        var placed = LayFormula(formula);
        if (placed.Tree is not { } laid) return null;

        var wrong = Reading.Root.SelfAndDescendants().Where(part => part.Node.Trouble is not null).ToList();
        var undrawn = placed.Undrawn.ToList();

        if (IsReadOnly && wrong.Count > 0) return AsSource([.. wrong.Select(part => (part, part.Node.Trouble!))]);

        var trouble = wrong.Select(part => LayoutDiagnostic.Of(part, part.Node.Trouble!) with { Part = new TexPart(part) })
            .Concat(undrawn.Select(part => LayoutDiagnostic.Of(part, Style.Says("Text.Latex.Undrawable"), LayoutSeverity.Warning) with { Part = new TexPart(part) }))
            .ToList();

        var (tree, width, height) = TexTypesetter.Number(Reading.Root, environment) is { } number
            ? Numbered(placed, number)
            : (laid, placed.Width, placed.Height);

        if (StandsAs == LayoutStanding.Block)
        {
            var air = Style.TextSize * 0.5;
            var left = double.IsFinite(Room) ? Math.Max(0, (Room - width) / 2) : 0;

            tree.Settle(new LayoutVector(left, air));
            (width, height) = (left + width, height + (air * 2));
        }

        Order(tree);

        return new Laid(tree, width, height, trouble);
    }

    private (LayoutTree Tree, double Width, double Height) Numbered(Placed formula, Set number)
    {
        var laid = LayFormula(number);
        if (laid.Tree is not { } tag) return (formula.Tree!, formula.Width, formula.Height);

        var build = new LayoutBuilder(Medium);
        build.Open("Block");
        build.Graft(formula.Tree!, default, LayoutSide.Centre);
        build.Graft(tag, new LayoutPoint(0, formula.Baseline - laid.Baseline), LayoutSide.Right, clear: Scale);
        build.Close();

        var block = Inline ? 0 : Block;
        var tree = build.Seal(block);

        var covers = Extent(tree.Root);
        tree.Settle(new LayoutVector(block > 0 ? 0 : -covers.X, -covers.Y));

        return (tree, block > 0 ? Math.Max(block, covers.Right) : covers.Width, covers.Height);
    }

    /// <summary>Which cells of a matrix read across and which read down, said to the tree once it is sealed.</summary>
    private void Order(LayoutTree tree)
    {
        List<int>? ink = null;

        foreach (var grid in TexGrid.In(Reading.Root.Node))
        {
            ink ??= [.. Enumerable.Range(0, tree.Count).Where(at => tree.Piece(at).Extent == 1 && tree.PartOf(at) is { Length: > 0 })];

            var cells = new int[grid.RowCount, grid.ColumnCount];
            for (var row = 0; row < grid.RowCount; row++)
                for (var column = 0; column < grid.ColumnCount; column++) cells[row, column] = -1;

            foreach (var cell in grid.Cells) cells[cell.Row, cell.Column] = Holding(tree, ink, cell);

            for (var row = 0; row < grid.RowCount; row++)
                Declare(Enumerable.Range(0, grid.ColumnCount).Select(at => cells[row, at]), vertical: false);

            for (var column = 0; column < grid.ColumnCount; column++)
                Declare(Enumerable.Range(0, grid.RowCount).Select(at => cells[at, column]), vertical: true);
        }

        void Declare(IEnumerable<int> cells, bool vertical) =>
            tree.Runs([.. cells.Where(cell => cell >= 0).Distinct().Select(tree.At)], vertical);
    }

    /// <summary>The lowest piece holding all the ink in a cell, found by containment.</summary>
    private static int Holding(LayoutTree tree, List<int> ink, TexCell cell)
    {
        var lowest = -1;

        foreach (var piece in ink)
        {
            var part = tree.PartOf(piece)!;
            if (part.Start < cell.Start || part.Start + part.Length > cell.End) continue;

            if (lowest < 0) { lowest = piece; continue; }

            while (lowest >= 0 && lowest != piece && !Above(tree, lowest, piece)) lowest = tree.Piece(lowest).Parent;
        }

        return lowest;
    }

    private static bool Above(LayoutTree tree, int ancestor, int piece)
    {
        for (var up = tree.Piece(piece).Parent; up >= 0; up = tree.Piece(up).Parent)
            if (up == ancestor) return true;

        return false;
    }

    // ── Laying what was set ─────────────────────────────────────────────────

    private sealed record Placed(LayoutTree? Tree, double Width, double Height, double Baseline, IReadOnlyList<ContentPart> Undrawn);

    private LayoutBuilder _laying = null!;
    private readonly Stack<(LayoutPoint Origin, LayoutPoint Raw)> _open = new();
    private readonly List<ContentPart> _above = [];
    private double _offsetX;
    private double _offsetY;
    private IReadOnlyList<Transformation> _pending = [];
    private readonly List<ContentPart> _undrawn = [];
    private bool _placedAny;

    private Placed LayFormula(Set formula)
    {
        _laying = new LayoutBuilder(Medium);
        _open.Clear();
        _above.Clear();
        _undrawn.Clear();
        _offsetX = _offsetY = 0;
        _pending = [];
        _placedAny = false;

        Place(formula, 0, formula.Height);

        if (!_placedAny) return new Placed(null, 0, 0, 0, [.. _undrawn]);

        var tree = _laying.Seal();
        var covers = Extent(tree.Root);
        tree.Settle(new LayoutVector(-covers.X, -covers.Y));

        return new Placed(tree, covers.Width, covers.Height, -covers.Y, [.. _undrawn]);
    }

    private void Place(Set piece, double x, double y)
    {
        var turns = _pending;
        _pending = [];

        var left = Scale * x;
        var top = Scale * (y - piece.Height);
        var right = left + Scale * piece.TotalWidth;
        var bottom = top + Scale * piece.TotalHeight;

        var raw = new LayoutPoint(Math.Min(left, right), Math.Min(top, bottom));
        var (width, height) = (Math.Abs(right - left), Math.Abs(bottom - top));

        var origin = new LayoutPoint(raw.X + _offsetX, raw.Y + _offsetY);
        var parent = _open.Count > 0 ? _open.Peek().Origin : new LayoutPoint(0, 0);

        if (piece.Undrawn is { } undrawn) _undrawn.Add(undrawn);

        if (piece.Spacing) return;

        var owns = Owns(piece);
        var part = owns is not null && IsPlace(owns.Role) ? owns : null;

        _laying.Open(
            piece.Kind,
            part is null ? null : new TexPart(part),
            new LayoutPoint(origin.X - parent.X, origin.Y - parent.Y),
            stops: owns is { } run && IsRun(run) && Covered(run) ? LayoutStops.None : LayoutStops.Both,
            gathers: false,
            paints: new LayoutPaint(Turn(turns, raw), Snap(piece, x, y, raw)));

        _laying.Covers(new LayoutRect(0, 0, width, height));

        if (Inked(piece.Background) is { } wash) _laying.Draw(new WashMark(new LayoutRect(0, 0, width, height), wash));

        _placedAny = true;

        if (owns is not null) _above.Add(owns);
        _open.Push((origin, raw));

        piece.Draw?.Invoke(this, piece, x, y);

        _laying.Close();

        _open.Pop();
        if (owns is not null) _above.RemoveAt(_above.Count - 1);
    }

    private ContentPart? Owns(Set piece)
    {
        var part = piece.Spacing ? null : piece.Part;

        if (_open.Count == 0) return part ?? Reading.Root;

        if (part is null) return null;

        return _above.Any(seen => ReferenceEquals(seen, part)) || !Within(part, _above[^1]) ? null : part;
    }

    private static bool Within(ContentPart part, ContentPart enclosing) =>
        ReferenceEquals(part, enclosing) || part.Ancestors().Any(up => ReferenceEquals(up, enclosing));

    private static bool IsRun(ContentPart part) => part.Parts.Any() && part.Parts.All(inner => inner.Role == Roles.Element);

    private static bool Covered(ContentPart run)
    {
        var inner = run;
        while (inner.Parts.Count() == 1 && IsRun(inner)) inner = inner.Parts.First();

        var parts = inner.Parts.ToList();
        if (parts.Count == 0) return false;

        var whole = new TexPart(run);
        var first = new TexPart(parts[0]);
        var last = new TexPart(parts[^1]);

        return first.Start <= whole.Start && last.Start + last.Length >= whole.Start + whole.Length;
    }

    private void PlaceTransformed(Set piece, IEnumerable<Transformation> transforms, double x, double y)
    {
        var scaled = transforms.Select(t => t.Scale(Scale)).ToList();

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

    private void DrawGlyph(CharInfo info, double x, double y, IBrush? foreground)
    {
        var raw = _open.Peek().Raw;
        var face = ((MediumTypeface)info.Font).Face;
        var at = new LayoutPoint((x - (raw.X / Scale)) * Scale, (y - (raw.Y / Scale)) * Scale);

        var glyphs = Medium.Character(face, info.Character, info.Size * Scale, at)
                     ?? throw new TexCharacterMappingNotFoundException(
                         $"The {face.Family} font does not support '{info.Character}' (U+{(int)info.Character:X4}) character.");

        _laying.Draw(new GlyphMark(glyphs, Inked(foreground)));
    }

    private void DrawLine(Point from, Point to, IBrush? foreground) =>
        _laying.Draw(new LineMark(Local(from.X, from.Y), Local(to.X, to.Y), Inked(foreground)));

    private void DrawRule(Rectangle rectangle, IBrush? foreground)
    {
        var at = Local(rectangle.X, rectangle.Y);
        _laying.Draw(new RuleMark(new LayoutRect(at.X, at.Y, Scale * rectangle.Width, Scale * rectangle.Height), Inked(foreground)));
    }

    // What the typesetter draws through, said only to it: a builder offers nothing a builder does not.
    void ITexLayer.Place(Set piece, double x, double y) => Place(piece, x, y);

    void ITexLayer.PlaceTransformed(Set piece, IEnumerable<Transformation> transforms, double x, double y) => PlaceTransformed(piece, transforms, x, y);

    void ITexLayer.DrawGlyph(CharInfo info, double x, double y, IBrush? foreground) => DrawGlyph(info, x, y, foreground);

    void ITexLayer.DrawLine(Point from, Point to, IBrush? foreground) => DrawLine(from, to, foreground);

    void ITexLayer.DrawRule(Rectangle rectangle, IBrush? foreground) => DrawRule(rectangle, foreground);

    private LayoutPoint Local(double x, double y)
    {
        var raw = _open.Peek().Raw;
        return new LayoutPoint((Scale * x) - raw.X, (Scale * y) - raw.Y);
    }

    /// <summary>An ink the typesetter carries, as the medium draws it — a colour the formula named, or one a host handed it.</summary>
    private LayoutInk? Inked(IBrush? ink) => ink switch
    {
        null => null,
        TexInk named => Medium.Ink(new LayoutColor(named.A, named.R, named.G, named.B)),
        InkBrush handed => handed.Ink,
        _ => throw new NotSupportedException($"an ink the layout cannot draw: {ink.GetType().Name}"),
    };

    private IReadOnlyList<LayoutTurn>? Turn(IReadOnlyList<Transformation> pending, LayoutPoint raw)
    {
        if (pending.Count == 0) return null;

        List<LayoutTurn>? turns = null;
        foreach (var transformation in pending)
            if (transformation is Transformation.Rotate rotate)
                (turns ??= []).Add(Medium.Rotation(rotate.RotationDegrees, new LayoutPoint(-raw.X, -raw.Y)));

        return turns;
    }

    private LayoutGuides Snap(Set piece, double x, double y, LayoutPoint raw) =>
        Medium.Guides([(Scale * x) - raw.X, (Scale * (x + piece.TotalWidth)) - raw.X],
                       [(Scale * y) - raw.Y, (Scale * (y + piece.TotalHeight)) - raw.Y]);

    private static LayoutRect Extent(LayoutPiece root)
    {
        var union = LayoutRect.Empty;

        foreach (var (piece, where) in root.Placed())
            if (piece.Kind is not ("StrutBox" or "GlueBox"))
                union = LayoutRect.Union(union, where);

        return union.IsEmpty ? new LayoutRect(0, 0, 0, 0) : union;
    }

    private static bool IsPlace(string role) =>
        role is not (Roles.Name or Roles.Open or Roles.Close or Roles.Separator or Roles.Trivia or Roles.Row);
}

/// <summary>An ink a host hands the typesetter — a theme's colour for what is drawn in no colour of its own — as a medium holds it.</summary>
public sealed record InkBrush(LayoutInk Ink) : IBrush;

/// <summary>
/// A part of a formula's reading, offered as the stretch of source it is named by: a braced argument by its contents, a cell by
/// its ink — the convention editing a formula needs, which is not the span the part covers.
/// </summary>
internal sealed class TexPart(ContentPart of) : IStandsFor
{
    public ContentPart Of { get; } = of;

    ISourcePart IStandsFor.Of => this.Of;

    public int Start => Named.Start;

    public int Length => Named.Length;

    private (int Start, int Length) Named => Of.Kind switch
    {
        TexKinds.Group => Of.Contents,
        TexKinds.Cell => Of.Written,
        _ => (Of.Start, Of.Length),
    };

    public override string ToString() => $"{Of.Role}[{Start},{Length}]";
}
