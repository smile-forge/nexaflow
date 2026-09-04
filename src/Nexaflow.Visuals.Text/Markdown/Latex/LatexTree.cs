using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Maths.Latex;
using Nexaflow.Visuals.Text.Editing;
using XamlMath;

using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A typeset formula's layout tree, and the offset-based questions an editor asks of it: where to draw a
/// caret and what shape to give it, what a click means, what a drag selected, where an arrow key goes,
/// and which command produced the glyph you just pressed backspace behind.
/// <para>
/// Almost nothing is decided here. The rules live in <see cref="LayoutQuery"/>, over
/// <see cref="ILayoutNode"/>, so they are shared with every other kind of embedded rendered content and
/// can be exercised against a hand-built tree. What remains in this class is the translation between
/// source offsets — which is how an editor thinks — and the nodes the query works in, plus the two rules
/// that are genuinely about LaTeX: how far past the edges a click still counts, and that backspace never
/// un-renders the entire formula.
/// </para>
/// <para>
/// It is deliberately free of the typesetter. Producing the tree needs fonts, and fonts need WPF and a
/// desktop; asking it questions needs neither.
/// </para>
/// </summary>
public sealed class LatexTree
{
    /// <summary>Rounding slack — two edges within this of each other are treated as touching.</summary>
    private const double Hair = 0.5;

    private readonly int[] _stops;

    /// <param name="latex">The source the tree refers into.</param>
    /// <param name="reading">
    /// The parse tree the layout was built from — the same one, handed over rather than read again.
    /// Reading the source a second time here produced a different tree: this is asked for a formula
    /// whose layout came out of <c>TexPipeline.Read</c>, which gathers runs and may have been told to
    /// show a stretch as written or to stand a hole in an empty argument, and none of that is in a bare
    /// parse. So a piece's part and the tree it was looked up in belonged to two different readings.
    /// </param>
    /// <param name="root">The formula's whole layout, parents holding children.</param>
    /// <param name="size">The formula's painted size.</param>
    public LatexTree(string latex, TexReading reading, ILayoutNode root, Size size,
                     IReadOnlyList<Diagnostic>? trouble = null)
    {
        Latex = latex ?? string.Empty;
        Reading = reading;
        Root = root;
        Size = size;
        Diagnostics = trouble ?? [];
        _stops = [.. root.CaretStops()];
    }

    /// <summary>The source this tree was built from.</summary>
    public string Latex { get; }

    /// <summary>The formula's whole layout.</summary>
    public ILayoutNode Root { get; }

    /// <summary>The formula's painted size in element pixels.</summary>
    public Size Size { get; }

    /// <summary>
    /// The stretches the typesetter could not read. Empty for a formula that parsed cleanly; otherwise
    /// each names a piece that is shown as written rather than understood.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Whether this piece was shown rather than read — inside a stretch the parser gave up on, so it
    /// stands for characters rather than for meaning.
    /// </summary>
    public bool IsGuesswork(ILayoutNode node) => Diagnostics.Any(d => d.Covers(node));

    /// <summary>
    /// What this piece is <em>to</em> the construct holding it — the degree of a root, a fraction's
    /// numerator, a script's base — together with that construct. Null when nothing holds it, or when it
    /// came from source the parser could not read.
    /// <para>
    /// The answer comes from the parse tree rather than from the layout, because that is where it is
    /// true: a <c>3</c> is the degree of a root whether or not it was ever drawn. It is what makes copying
    /// a piece able to carry more than the characters — copy the 3 out of <c>\sqrt[3]{x+1}</c> and you
    /// have a 3, and also "the degree of a root", and only the second reading lets pasting it onto
    /// something else produce a cube root of that something.
    /// </para>
    /// </summary>
    public (ILayoutNode Construct, string Role)? RoleOf(ILayoutNode node)
    {
        if (node.Part is not { Length: > 0 }) return null;

        // Recovered text was shown, not read. Whatever the parser wrapped it in while carrying on is an
        // artefact of the recovery rather than anything the writer expressed, so it names no part of
        // anything — and copying it can only ever yield the characters.
        if (IsGuesswork(node)) return null;

        if (Innermost(node) is not { } part) return null;

        // A braced group — or a cell — stands for the one thing written in it, so pointing at that thing
        // is pointing at the wrapper, and it is the wrapper the construct named. The braces are the
        // writer's way of saying "all of this is one argument"; nothing downstream should have to know
        // they were there.
        while (part.Parent is { IsWrapper: true } wrapper && ReferenceEquals(Alone(wrapper), part))
            part = wrapper;

        if (!IsPart(part.Role)) return null;

        // The construct comes back as a layout node, because that is what the caller can point at, draw
        // and hit-test. Nearest ancestor naming the same stretch of source: the parse tree says which
        // construct holds this part, and the layout says where that construct was drawn.
        foreach (var holder in part.Ancestors())
            if (Drawn(node, holder) is { } construct) return (construct, part.Role);

        return null;
    }

    /// <summary>
    /// The formula as a parse tree, with every part's place and parent worked out — the reading the
    /// layout was built from, handed over rather than worked out again here.
    ///
    /// <para>
    /// <b>Not the parser's tree.</b> It is what <see cref="TexPipeline.Read"/> handed back: the parse
    /// gathered into the shapes a builder can act on, possibly with holes put in the empty arguments, with
    /// undrawable commands marked, and with whatever is under the caret shown as it was written. Those
    /// stages can move the tree without moving the source or the picture, so this is a third thing worth
    /// looking at and not a restatement of either.
    /// </para>
    /// <para>
    /// It can also be a tree the parser never produced at all. Where nothing could be set as maths, the
    /// layout falls back to showing the source as typed, and this is that fallback — which is the honest
    /// record of what the builder was given.
    /// </para>
    /// <para>
    /// This object is a reading of one string, and changed source is a different
    /// <see cref="LatexTree"/>, so there is no moment at which it could be out of date.
    /// </para>
    /// </summary>
    public TexReading Reading { get; }

    /// <summary>Roles that name a place content goes, as against the punctuation that holds it.</summary>
    private static bool IsPart(string role) =>
        role is not (TexRole.Name or TexRole.Open or TexRole.Close
                     or TexRole.Separator or TexRole.Trivia or TexRole.Row);

    /// <summary>The one thing written in a wrapper, or null when it holds none or several.</summary>
    private static TexPart? Alone(TexPart wrapper)
    {
        TexPart? only = null;

        foreach (var part in wrapper.Parts)
        {
            if (only is not null) return null;
            only = part;
        }

        return only;
    }

    /// <summary>Where a part of the parse tree was drawn — the nearest thing above <paramref name="node"/>
    /// it was drawn from.</summary>
    private static ILayoutNode? Drawn(ILayoutNode node, TexPart part) =>
        node.Ancestors().FirstOrDefault(
            a => a.Part is TexSourcePart drawn && ReferenceEquals(drawn.Of, part));

    /// <summary>Where a caret is allowed to rest, ascending.</summary>
    public IReadOnlyList<int> CaretStops => _stops;

    /// <summary>
    /// The matrix holding <paramref name="offset"/>, as its cells and the place each one has — or null
    /// when the offset is not in one. Innermost first, so a matrix inside a matrix answers as the one
    /// being pointed into.
    /// <para>
    /// Read from the parse tree. The parser says which cells a matrix has and which row and column each
    /// is in; nothing here counts separators or clusters rectangles to find that out again. That is what
    /// makes "move this column", and every table edit after it, a question the tree can answer.
    /// </para>
    /// </summary>
    public LatexGrid? GridAt(int offset) => GridFrom(offset);

    /// <summary>
    /// The table around <paramref name="offset"/>, as the grid the editor works from.
    /// <para>
    /// The cells come from the parse tree rather than from the typesetter's atoms, because the
    /// typesetter's spans begin at a command's <em>name</em>: a cell holding <c>\alpha</c> was named as
    /// <c>alpha</c>, so every rewrite of a matrix took the backslash off every command in it and handed
    /// back LaTeX that no longer parsed. Nothing noticed, because every test written for grids until now
    /// had a single letter in each cell.
    /// </para>
    /// </summary>
    private LatexGrid? GridFrom(int offset) =>
        TexGrid.At(Reading.Root.Node, offset) is { } grid ? Shaped(grid) : null;

    /// <summary>That table as the editor's own model of it, which rewrites LaTeX by character.</summary>
    private LatexGrid Shaped(TexGrid grid) =>
        LatexGrid.From(
            Latex,
            grid.Start,
            grid.Length,
            [.. grid.Cells.Select(cell => (cell.Row, cell.Column, cell.Start, cell.Length))]);

    /// <summary>
    /// Where <paramref name="point"/> falls in a matrix: on a cell, or at a boundary between columns or
    /// rows — including the margin inside the brackets, past the last column or under the last row.
    /// <para>
    /// A boundary cannot be said as an offset, which is why this takes a point. Every position in a
    /// matrix belongs to some cell as far as the source is concerned; "just to the right of the last
    /// column, but still inside the brackets" is a fact about where the columns were drawn, and only
    /// the geometry has it. It is what tells a block dropped there to become new columns rather than to
    /// land in the cell it happens to be nearest.
    /// </para>
    /// <para>
    /// The shape comes from the parse tree and the extents from the layout, and the two are joined by
    /// identity: a drawn piece belongs to the cell whose node its part was built from. Nothing here
    /// compares an offset with an offset.
    /// </para>
    /// </summary>
    public GridDrop? GridDropAt(Point point)
    {
        foreach (var node in Root.SelfAndDescendants().OrderBy(n => n.Bounds.Width * n.Bounds.Height))
        {
            if (node is not LatexNode { Origin: { Kind: TexKind.Environment } part }) continue;
            if (TexGrid.Read(part.Node, part.Start) is not { } grid) continue;

            // How far the matrix reaches, brackets included. The cells' box stops at the cells: the
            // delimiters are drawn by the fence around them, which is a separate piece of the same
            // construct — so the margin a reader aims at when offering a column to the matrix belongs to
            // the fence, not to the box being asked. Anything drawn from the same part is that same
            // construct drawn in another part, so its extent counts as this one's.
            var reach = node.Bounds;
            foreach (var ancestor in node.Ancestors())
                if (ancestor is LatexNode { Part: { } outer } && ReferenceEquals(outer, part))
                    reach.Union(ancestor.Bounds);

            if (!reach.Contains(point)) continue;

            // Each cell's extent on the page, taken from the pieces drawn for its node.
            var boxes = new Dictionary<(int, int), Rect>();
            foreach (var cell in grid.Cells)
            {
                if (cell.Node is not { } written) continue;

                var drawn = node.SelfAndDescendants()
                    .OfType<LatexNode>()
                    .Where(n => n.Bounds.Width > 0 && Inside(n.Origin, written))
                    .Select(n => n.Bounds)
                    .ToList();
                if (drawn.Count == 0) continue;

                var box = drawn[0];
                foreach (var rect in drawn.Skip(1)) box.Union(rect);
                boxes[(cell.Row, cell.Column)] = box;
            }

            if (boxes.Count == 0) continue;
            return Land(Shaped(grid), boxes, point);
        }

        return null;
    }

    /// <summary>
    /// Whether a piece was drawn from what is written in one cell: from the cell itself, or from
    /// anything under it. Identity all the way down — a cell holding one term is often laid out from
    /// that term rather than from the cell, and a cell holding a matrix has that whole matrix under it.
    /// </summary>
    private static bool Inside(TexPart? part, TexNode cell) =>
        part is not null
        && (ReferenceEquals(part.Node, cell) || part.Ancestors().Any(up => ReferenceEquals(up.Node, cell)));

    /// <summary>Reads a point against a grid's drawn cells: on one of them, or between/past columns or rows.</summary>
    private static GridDrop Land(LatexGrid grid, Dictionary<(int, int), Rect> boxes, Point point)
    {
        foreach (var (at, box) in boxes)
            if (box.Contains(point)) return new GridDrop(grid, at.Item1, at.Item2, null, null);

        double? Edge(int index, bool column, bool far)
        {
            var of = boxes.Where(b => (column ? b.Key.Item2 : b.Key.Item1) == index).Select(b => b.Value).ToList();
            if (of.Count == 0) return null;
            return column ? (far ? of.Max(r => r.Right) : of.Min(r => r.Left))
                          : (far ? of.Max(r => r.Bottom) : of.Min(r => r.Top));
        }

        // How many columns finish before the pointer, and how many rows — which is the index a block
        // dropped here would be inserted at, counting from either end without a special case.
        var column = Enumerable.Range(0, grid.ColumnCount).Count(c => Edge(c, column: true, far: true) < point.X);
        var row = Enumerable.Range(0, grid.RowCount).Count(r => Edge(r, column: false, far: true) < point.Y);

        // Which way the pointer has actually left the cells decides. Past the ends of the columns is a
        // column; otherwise, past the ends of the rows is a row; a pointer in the gutter between two
        // columns is a column again, since that is the reading with somewhere to go.
        var outsideColumns = column == 0 || column == grid.ColumnCount;
        var outsideRows = row == 0 || row == grid.RowCount;

        return outsideColumns || !outsideRows
            ? new GridDrop(grid, null, null, column, null)
            : new GridDrop(grid, null, null, null, row);
    }

    /// <summary>
    /// The holes in this formula, in reading order — the arguments left empty, which the typesetter
    /// drew a box for. Tab walks these.
    /// </summary>
    public IReadOnlyList<ILayoutNode> Placeholders =>
        _placeholders ??= [.. Root.SelfAndDescendants().Where(n => n.IsPlaceholder()).OrderBy(n => n.Sits().Start)];

    private IReadOnlyList<ILayoutNode>? _placeholders;

    // ── Point → source ──────────────────────────────────────────────────────

    /// <summary>
    /// The caret stop <paramref name="point"/> means. The deepest piece under the pointer wins, and which
    /// half of it was hit decides whether the caret lands before or after.
    /// </summary>
    /// <summary>
    /// The piece of the formula <paramref name="point"/> is on — what a drag is really between. Past
    /// either side it is the piece at that end, as it is in text.
    /// </summary>
    public ILayoutNode? NodeAt(Point point)
    {
        if (point.X > Size.Width) return Root.Ink().LastOrDefault();
        if (point.X < 0) return Root.Ink().FirstOrDefault();
        return Root.NodeAt(point);
    }

    public int OffsetAt(Point point)
    {
        // Clicking past either side means the end, as it does in text. Letting the nearest node answer
        // would instead land just inside whatever construct happens to finish last — after the y of
        // `\sqrt{y}` rather than after the radical — which is the same pixel but a different place to type.
        if (point.X > Size.Width) return Latex.Length;
        if (point.X < 0) return 0;

        var hit = Root.NodeAt(point);
        if (hit is null) return 0;

        var offset = point.X < hit.Bounds.X + hit.Bounds.Width / 2 ? hit.Sits().Start : hit.Sits().End;
        return NearestStop(offset);
    }

    /// <summary>
    /// The place a press means: the stop under the pointer, and which of the bars drawn there is nearest
    /// to it — so pressing in the space TeX sets around an operator puts the caret in that space, which is
    /// where the reader pointed and where the arrow key would have taken them.
    /// <para>
    /// Ties go to the innermost. Two bars half a pixel apart — inside a trailing exponent and past the
    /// script — are not something anyone can aim between, and the inner one is where a reader who has
    /// just clicked behind a <c>2</c> means to be typing.
    /// </para>
    /// </summary>
    public CaretPlace PlaceAt(Point point)
    {
        var offset = OffsetAt(point);
        var bars = Root.CaretBars(offset);

        var level = 0;
        for (var at = 1; at < bars.Count; at++)
            if (Math.Abs(bars[at].X - point.X) < Math.Abs(bars[level].X - point.X) - Hair) level = at;

        return new CaretPlace(offset, level);
    }

    // ── Source → geometry ───────────────────────────────────────────────────

    /// <summary>
    /// Where and how tall to draw the caret at <paramref name="offset"/> — the piece it abuts decides,
    /// which is what makes it shrink and rise inside an exponent and take the numerator's height inside
    /// a fraction.
    /// </summary>
    public Rect CaretRect(int offset) => Root.CaretRect(offset);

    /// <summary>Where and how tall to draw the caret at <paramref name="place"/>.</summary>
    public Rect CaretRect(CaretPlace place) => Root.CaretRect(place);

    /// <summary>How many bars are drawn at <paramref name="offset"/> — see <see cref="CaretPlace"/>.</summary>
    public int PlacesAt(int offset) => Root.CaretBars(offset).Count;

    /// <summary>
    /// The rectangles to wash for the source range — one per run, already merged, so a translucent
    /// selection never paints the same pixel twice and darkens it.
    /// </summary>
    public IReadOnlyList<Rect> RangeRects(int start, int length)
    {
        if (length <= 0) return [];
        var end = start + length;

        // Wash whole nodes, not the glyphs inside them. A node's bounds cover the parts of it that no
        // character produced — a fraction's bar, a radical's hook — which washing the leaves alone would
        // leave clear, so a fully selected fraction would read as two selected numbers with a gap.
        // Holes included, which is why this asks whether a node stands for a place rather than whether
        // it covers any characters. A hole covers none by definition, so a selection sweeping across a
        // half-written fraction would wash everything except the part still missing — the one piece the
        // reader most needs to see they have picked up.
        var covered = Root.SelfAndDescendants()
            .Where(n => n.Stands() && n.Sits().Start >= start && n.Sits().End <= end)
            .ToHashSet();

        return Merge([.. covered.Where(n => !n.Ancestors().Any(covered.Contains)).Select(n => n.Bounds)]);
    }

    /// <summary>
    /// Collapses the rectangles of one contiguous source range into as few as possible: anything sharing
    /// a vertical band becomes one run.
    /// <para>
    /// Horizontal gaps are closed deliberately rather than preserved. The selection is a contiguous run
    /// of source, so whatever sits in the gap is inside it — the glue TeX puts around a binary operator,
    /// say — and leaving those unpainted would break one selection into a row of disconnected blocks.
    /// Stacked bands (a sum's limits above and below it) stay separate unless something spans them.
    /// </para>
    /// </summary>
    private static List<Rect> Merge(List<Rect> rects)
    {
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < rects.Count && !merged; i++)
                for (var j = i + 1; j < rects.Count && !merged; j++)
                {
                    var a = rects[i];
                    var b = rects[j];
                    if (a.Top >= b.Bottom - Hair || b.Top >= a.Bottom - Hair) continue;   // different bands

                    a.Union(b);
                    rects[i] = a;
                    rects.RemoveAt(j);
                    merged = true;
                }
        }
        return rects;
    }

    // ── Ranges and stepping ─────────────────────────────────────────────────

    /// <summary>
    /// The whole constructs a raw range covers, as a source range. Dragging across <c>x^2</c> selects the
    /// script rather than stopping mid-command at <c>x^{2</c>, and dragging from a fraction's numerator to
    /// its denominator selects the fraction rather than the <c>1}{x</c> the offsets alone would give.
    /// <para>
    /// Both fall out of promotion: the answer is made of whole nodes, and a node's source range is what
    /// the parser built it from, so it cannot be a half-open brace.
    /// </para>
    /// </summary>
    public (int Start, int Length) SnapRange(int start, int length)
    {
        var from = Math.Clamp(Math.Min(start, start + length), 0, Latex.Length);
        var to = Math.Clamp(Math.Max(start, start + length), 0, Latex.Length);
        if (from == to) return (from, 0);

        // Whatever lies wholly inside the range was dragged over — every node, not only the ink, so a
        // drag from before a root's sign to past its contents takes the root itself and not merely what
        // is under the bar. Anything only half inside is left to promotion, which is what stops a range
        // that clipped a brace of `^{n}` from coming back as `{n`.
        var touched = Root.SelfAndDescendants()
            .Where(n => n.Sits() is { Length: > 0 } at && at.Start >= from && at.End <= to)
            .ToList();

        if (touched.Count == 0)
        {
            // The range covers only characters nothing was drawn for — a lone brace, say, or half of a
            // command's name. Snap to whatever is nearest, so a selection is always of something visible.
            var nearest = Root.Ink()
                .OrderBy(n => Math.Min(Math.Abs(n.Sits().Start - from), Math.Abs(n.Sits().End - to)))
                .FirstOrDefault();
            if (nearest is null) return (from, to - from);
            touched.Add(nearest);
        }

        var promoted = LayoutQuery.Promote(touched);
        if (promoted.Count == 0) return (from, to - from);

        var (first, last) = (promoted.Min(n => n.Sits().Start), promoted.Max(n => n.Sits().End));
        return (first, last - first);
    }

    /// <summary>
    /// The next caret stop in <paramref name="forward"/>'s direction, or null at the formula's edge —
    /// which is the host's cue to move the caret out into the surrounding text.
    /// </summary>
    public int? Step(int offset, bool forward) => Root.Step(offset, forward);

    /// <summary>
    /// The next place in <paramref name="forward"/>'s direction, or null at the formula's edge. Walks the
    /// bars at one offset before moving on — see <see cref="CaretPlace"/>.
    /// </summary>
    public CaretPlace? Step(CaretPlace place, bool forward) => Root.Step(place, forward);

    /// <summary>
    /// The nearest caret stop on the line above or below — how the caret crosses a fraction bar or drops
    /// out of an exponent. Null when there is nothing that way.
    /// </summary>
    public int? StepVertical(int offset, bool up) => Root.StepVertical(offset, up);

    /// <summary>Moves <paramref name="offset"/> to the nearest place a caret may rest.</summary>
    public int NearestStop(int offset)
    {
        var best = _stops.Length > 0 ? _stops[0] : 0;
        var bestDistance = int.MaxValue;
        foreach (var stop in _stops)
        {
            var distance = Math.Abs(stop - offset);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = stop;
        }
        return best;
    }

    /// <summary>
    /// The construct drawn immediately before <paramref name="offset"/>, when it took more than one
    /// character to write it. This is what backspace un-renders: the caret sits after an <c>α</c> whose
    /// source is the six characters <c>\alpha</c>, and deleting one of those six is the wrong move.
    /// </summary>
    /// <summary>
    /// The construct's argument ending exactly at <paramref name="offset"/>, when the writer left its
    /// braces off — what typing there has to extend rather than walk out of.
    /// <para>
    /// LaTeX lets a one-token argument go unbraced, so <c>x^2</c> is x to the 2. Type a 3 meaning
    /// twenty-three and the characters say something else entirely: <c>x^23</c> is x squared followed
    /// by a 3, because the exponent was one token and that token has been used. Naming the case here is
    /// what lets a caller re-brace it, which is the only way the obvious keystroke means the obvious
    /// thing.
    /// </para>
    /// <para>
    /// Only the roles that are genuinely an argument the writer wrote after a command. A construct's
    /// <c>base</c> is not one — <c>x</c> in <c>x^2</c> — and neither is an <c>element</c> of a row: the
    /// <c>1</c> in <c>a + 1</c> plays a part in the row that holds it, so it has a role, but a 2 typed
    /// after it is twelve and bracing it would say nothing at all.
    /// </para>
    /// </summary>
    /// <summary>
    /// Writes <paramref name="text"/> in at <paramref name="caret"/> as a change to the construct that
    /// owns that position, rather than to the characters either side of it.
    /// <para>
    /// This is the difference between editing a formula and editing a string that happens to be one.
    /// A 3 typed after <c>x^2</c> means twenty-three; spliced in it says <c>x^23</c>, which is x
    /// squared followed by a 3, because LaTeX lets a one-token argument go unbraced and that token has
    /// been used. The tree knows which construct the caret is in and what its argument may hold, so it
    /// is the tree that re-braces — and the same call also covers writing at the <em>front</em> of an
    /// argument, inside one already braced, and outside any construct at all, none of which the caller
    /// should have to tell apart.
    /// </para>
    /// <para>
    /// Returns null when the position belongs to no construct in particular. Then the text is simply
    /// inserted, which the caller does itself: typing a character carries rules of its own — a
    /// backslash opens a command that is shown as it is spelled — and those are about the source,
    /// not the structure.
    /// </para>
    /// </summary>
    public LatexWrite? Write(int caret, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        caret = Math.Clamp(caret, 0, Latex.Length);
        if (ArgumentAt(caret) is not { } argument) return null;

        var at = argument.Sits();
        var (start, end) = (at.Start, at.End);
        if (start < 0 || end > Latex.Length || caret < start || caret > end) return null;

        // Already wrapped, or still one token: nothing structural to do, and the caller's own rules
        // should have the keystroke.
        if (IsBraced(argument) || IsOneToken(Latex[start..caret] + text + Latex[caret..end])) return null;

        return Place(Latex, caret, text, (start, end), braced: false);
    }

    /// <summary>
    /// Moves the stretches in <paramref name="ranges"/> to <paramref name="to"/> — a term dragged to a
    /// new place in the formula.
    /// <para>
    /// Merged into where it lands rather than dropped there. It is the same question typing asks, with
    /// more riding on it: a term dragged into an unbraced exponent has to brace it, or <c>x^2</c> and a
    /// dropped 3 would read as x squared beside a 3; and a command dragged against a letter has to keep
    /// a space, or <c>\alpha</c> next to <c>x</c> silently becomes the unknown command
    /// <c>\alphax</c>. Both are facts about the structure, so both are settled here.
    /// </para>
    /// <para>
    /// Null when there is nothing to move, or when the drop is inside what is being moved — a term
    /// dropped on itself has not gone anywhere, and cutting it first would leave nowhere to put it.
    /// </para>
    /// </summary>
    /// <param name="at">
    /// Where the pointer let go, when the caller has it. Only a point can say that a block was dropped
    /// between two columns of a matrix rather than onto one of its cells, so only with this can a block
    /// join a matrix as new columns instead of landing in a cell.
    /// </param>
    public LatexWrite? Move(IReadOnlyList<(int Start, int Length)> ranges, int to, Point? at = null)
    {
        if (ranges is null) return null;

        var ordered = ranges.Where(r => r.Length > 0 && r.Start >= 0 && r.Start + r.Length <= Latex.Length)
                            .OrderBy(r => r.Start)
                            .ToList();
        if (ordered.Count == 0) return null;
        if (ordered.Any(r => to > r.Start && to < r.Start + r.Length)) return null;

        // Cells first. A matrix is the one place where what a move means is not a splice of the source
        // the selection covers: moving a column has to move the separators too, so the rest shift over
        // rather than the column's three stretches arriving jammed together at the drop point.
        if (MoveInGrid(ordered, to, at) is { } inGrid) return inGrid;

        return MoveText(ordered, to);
    }

    /// <summary>
    /// A move as a splice of the stretches themselves — what a selection that is not a block of cells
    /// means, and what moving a whole matrix means too.
    /// </summary>
    private LatexWrite? MoveText(List<(int Start, int Length)> ordered, int to)
    {
        var moved = string.Concat(ordered.Select(r => Latex.Substring(r.Start, r.Length)));

        // Cut last first, so removing one stretch never moves the offsets of those still to go — the
        // same reason a disjoint selection can be deleted at all.
        var remainder = Latex;
        foreach (var range in Enumerable.Reverse(ordered)) remainder = remainder.Remove(range.Start, range.Length);

        var drop = Math.Clamp(Shift(to, ordered), 0, remainder.Length);

        // The destination is read off the formula as it stands, which is the one the reader dropped
        // onto, then shifted into the source the cut left behind. It only means anything if the drop
        // is still inside it once that is done — a drag can pass over a position whose argument the
        // cut has since taken apart, and the argument of somewhere else is no argument at all.
        var argument = ArgumentAt(to);
        var span = argument is null || to < argument.Sits().Start || to > argument.Sits().End
            ? ((int Start, int End)?)null
            : (Shift(argument.Sits().Start, ordered), Shift(argument.Sits().End, ordered));

        return Place(remainder, drop, moved, span, argument is not null && IsBraced(argument));

        // An offset in the formula as it stands, read as an offset into what the cut left behind.
        // A stretch wholly in front of it takes its whole length off; one the offset falls *inside*
        // takes only the part in front of the offset, because the rest of it is still to come. Left
        // out, that second case runs an offset backwards past a stretch that straddles it — which is
        // how dragging a term out of a denominator across the formula ended in a negative substring.
        static int Shift(int offset, List<(int Start, int Length)> cut)
        {
            var shifted = offset;
            foreach (var r in cut)
            {
                if (r.Start + r.Length <= offset) shifted -= r.Length;
                else if (r.Start < offset) shifted -= offset - r.Start;
            }
            return shifted;
        }
    }

    /// <summary>
    /// A move of cells, when that is what it is: a block selected in a matrix, dragged either somewhere
    /// else in the same matrix or out of it. Null for everything else, leaving the ordinary move to
    /// splice the source.
    /// <para>
    /// Inside the matrix, whole columns and whole rows reorder — the point of the whole exercise, since a
    /// column moved as three stretches of text arrives as three terms jammed together instead of as a
    /// column. Anything less than a whole line moves its contents to where it was dropped, as a block
    /// does on a sheet, and leaves holes behind.
    /// </para>
    /// <para>
    /// Dragged out, the block becomes a matrix of its own of the same kind and the same size, and what it
    /// came from closes up (a whole column or row) or is left holding holes (a partial block).
    /// </para>
    /// </summary>
    private LatexWrite? MoveInGrid(List<(int Start, int Length)> ranges, int to, Point? at)
    {
        if (GridAt(ranges[0].Start) is not { } grid) return null;
        if (grid.BlockOf(ranges) is not { } block) return null;

        // Not an array. Its columns carry an alignment spec — `\begin{array}{cc|c}` — that would have to
        // be reordered in step with them, and moving the cells alone would silently realign the table
        // while looking like it had worked. Left to the ordinary move until the spec moves too.
        if (grid.Environment is "array") return null;

        // The whole grid selected is the matrix itself being carried, not cells being taken out of it —
        // so it moves as the stretch of source it is, and what it leaves behind is nothing. Treated as
        // cells, every one of them was emptied, and the matrix stayed where it was as a blank one: drag a
        // matrix a little way and you had two, one of them holding a single hole.
        if (block.Rows == grid.RowCount && block.Columns == grid.ColumnCount)
            return MoveText([grid.Span], to);

        // Landing in a cell is what "inside the matrix" means — not landing within its span. When the
        // formula *is* the matrix, which is the ordinary case, every offset in it is within that span,
        // including the one past the closing brace that dropping to the right of it produces. Asking the
        // span sent every such drop down the move-within path, where there was no cell to land in, and
        // the whole thing fell through to splicing the source: the block arrived as its stretches run
        // together outside the matrix, and the matrix kept its shape with the cells cut out of it.
        // A boundary, when the caller gave us a pointer to read one from: dropped between two columns of
        // a matrix — its own or another's — the block joins as columns rather than landing in a cell.
        if (at is { } point && GridDropAt(point) is { Cell: null } boundary && Joined(grid, block, boundary) is { } joined)
            return joined;

        return grid.CellAt(to) is { } cell ? MovedWithin(grid, block, cell) : MovedOut(grid, block, to);
    }

    /// <summary>
    /// The block put into a matrix as new columns or rows — the merge. Within its own matrix that is a
    /// reorder, which is the move it already knows; between two, the block leaves one and joins the
    /// other, so both are rewritten at once.
    /// </summary>
    private LatexWrite? Joined(LatexGrid source, GridBlock block, GridDrop drop)
    {
        var target = drop.Grid;

        if (target.BodyStart == source.BodyStart)
        {
            if (drop.InsertColumn is { } column && source.IsWholeColumns(block))
                return Settled(source.WithColumnsMoved(block, column));
            if (drop.InsertRow is { } row && source.IsWholeRows(block))
                return Settled(source.WithRowsMoved(block, row));

            return null;
        }

        var contents = source.Contents(block);
        var move = drop.InsertColumn is { } into ? target.WithColumnsInserted(into, contents)
                 : drop.InsertRow is { } under ? target.WithRowsInserted(under, contents)
                 : (GridMove?)null;
        if (move is not { } joined) return null;

        var left = source.WithBlockTaken(block).Body();
        var gained = joined.Grid.Body();

        // Two bodies to put back into one formula. The later one first, so the earlier one's offsets are
        // still the offsets of the formula being written into when its turn comes.
        var latex = Latex;
        foreach (var (start, end, body) in new[]
                 {
                     (source.BodyStart, source.BodyEnd, left),
                     (target.BodyStart, target.BodyEnd, gained),
                 }.OrderByDescending(e => e.Item1))
            latex = latex[..start] + body + latex[end..];

        // Where the target's cells ended up. Its own rewrite says where each landed within it; the
        // source's rewrite, if it came first in the formula, has moved the whole of that along.
        var shift = source.BodyStart < target.BodyStart
            ? left.Length - (source.BodyEnd - source.BodyStart)
            : 0;
        var wrote = joined.Grid.Render().Grid.SpanOf(joined.Landed) ?? (target.BodyStart, 0);

        return Wrote(latex, wrote.Start + shift + wrote.Length, wrote.Length);
    }

    /// <summary>
    /// A grid move, settled: the formula it produces, and the moved block marked out in it so a drag can
    /// keep showing what is being carried. Both come from the grid itself — the rewrite says where every
    /// cell was put, so nothing has to read the text back to find out.
    /// </summary>
    private static LatexWrite Settled(GridMove move)
    {
        var (latex, settled) = move.Grid.Render();
        var wrote = settled.SpanOf(move.Landed) ?? (settled.BodyStart, 0);

        // The existing helper takes where the caret ends up, which is just past what was written.
        return Wrote(latex, wrote.Start + wrote.Length, wrote.Length);
    }

    private static LatexWrite? MovedWithin(LatexGrid grid, GridBlock block, (int Row, int Column) cell)
    {
        var move = grid.IsWholeColumns(block) ? grid.WithColumnsMoved(block, cell.Column)
                 : grid.IsWholeRows(block) ? grid.WithRowsMoved(block, cell.Row)
                 : grid.WithBlockMoved(block, cell.Row, cell.Column);

        return Settled(move);
    }

    private static LatexWrite? MovedOut(LatexGrid grid, GridBlock block, int to)
    {
        var taken = grid.Extracted(block);
        var (latex, left) = grid.WithBlockTaken(block).Render();

        // The drop was named against the formula as it was. Rewriting the matrix's cells changes the
        // length of everything from the body onwards, so a drop after it moves by the difference.
        var shift = (left.BodyEnd - left.BodyStart) - (grid.BodyEnd - grid.BodyStart);
        var at = Math.Clamp(to <= left.BodyStart ? to : to + shift, 0, latex.Length);

        var separated = Separated(latex, at, taken);
        return new LatexWrite(
            latex.Insert(at, separated),
            at + separated.Length,
            new LatexRange(at, separated.Length));
    }

    /// <summary>
    /// Puts <paramref name="text"/> into <paramref name="source"/> at <paramref name="at"/>, wrapping
    /// the argument it lands in when that argument can no longer hold it bare.
    /// </summary>
    private static LatexWrite Place(string source, int at, string text,
                                    (int Start, int End)? argument, bool braced)
    {
        var written = Separated(source, at, text);

        // An argument that does not hold the position being written to has nothing to say about it, so
        // it is treated as no argument rather than trusted — this is a private helper with two callers
        // and a precondition either could get wrong, and getting it wrong reads the source backwards.
        if (argument is { } bounds && (at < bounds.Start || at > bounds.End || bounds.End > source.Length))
            argument = null;

        // The caret always ends up just past what was written, so where that landed follows from it —
        // including when wrapping the argument shifted the whole lot along by a brace.
        if (argument is not { } span || braced
            || IsOneToken(source[span.Start..at] + written + source[at..span.End]))
            return Wrote(source.Insert(at, written), at + written.Length, written.Length);

        var content = source[span.Start..at] + written + source[at..span.End];
        return Wrote(
            source[..span.Start] + "{" + content + "}" + source[span.End..],
            span.Start + 1 + (at - span.Start) + written.Length,
            written.Length);
    }

    private static LatexWrite Wrote(string latex, int caret, int length) =>
        new(latex, caret, new LatexRange(caret - length, length));

    /// <summary>
    /// <paramref name="text"/> with a space added at either end where the join would otherwise change
    /// what the neighbouring characters say — a control word runs on until something that is not a
    /// letter ends its name, so <c>\alpha</c> written against <c>x</c> would become <c>\alphax</c>.
    /// </summary>
    private static string Separated(string source, int at, string text)
    {
        if (text.Length == 0) return text;

        var before = EndsWithControlWord(source[..at]) && char.IsLetter(text[0]) ? " " : string.Empty;
        var after = EndsWithControlWord(text) && at < source.Length && char.IsLetter(source[at]) ? " " : string.Empty;
        return before + text + after;
    }

    private static bool EndsWithControlWord(string text)
    {
        var i = text.Length;
        while (i > 0 && char.IsLetter(text[i - 1])) i--;
        return i < text.Length && i > 0 && text[i - 1] == '\\';
    }

    private ILayoutNode? ArgumentAt(int caret)
    {
        ILayoutNode? best = null;
        foreach (var node in Root.SelfAndDescendants())
        {
            var at = node.Sits();
            if (at.Length <= 0) continue;

            // Edges included: writing at either end of an argument is writing in it. That is the whole
            // question — the position after the 2 of x^2 is both "the end of the exponent" and "the end
            // of the formula", and only the construct that owns it can say which.
            if (caret < at.Start || caret > at.End) continue;
            if (RoleOf(node) is not { } role || !IsArgument(role.Role)) continue;

            // The innermost, because an argument can hold constructs with arguments of their own.
            if (best is null || node.Ancestors().Count() > best.Ancestors().Count()) best = node;
        }
        return best;
    }

    /// <summary>Roles a construct gives to something written as its argument.</summary>
    private static bool IsArgument(string role) =>
        role is "superscript" or "subscript" or "numerator" or "denominator"
             or "degree" or "radicand" or "over" or "under";

    /// <summary>
    /// Whether this piece is a braced group — asked of the parse tree, which said so when it read the
    /// braces, rather than of the characters either side of where the piece was drawn.
    /// <para>
    /// The one thing written in a group counts as the group, the same promotion <see cref="RoleOf"/>
    /// makes: a typesetter names the contents of <c>{a+b}</c> where the part that <em>is</em> the
    /// argument is the whole of it, so the piece pointed at is often the one inside.
    /// </para>
    /// </summary>
    private bool IsBraced(ILayoutNode node) =>
        Innermost(node) is { } part
        && (part.Kind == TexKind.Group
            || (part.Parent is { Kind: TexKind.Group } group && ReferenceEquals(Alone(group), part)));

    /// <summary>
    /// Whether <paramref name="content"/> is one token, and so can stand as an argument bare. A single
    /// character is; so is a whole control word, which is why <c>x^\alpha</c> needs no braces.
    /// </summary>
    private static bool IsOneToken(string content) =>
        content.Length == 1
        || (content.Length > 1 && content[0] == '\\' && content.Skip(1).All(char.IsLetter));

    /// <summary>
    /// The one thing immediately before <paramref name="offset"/> — what backspace acts on.
    /// <para>
    /// The deepest piece ending exactly there, which is the symbol the reader is looking at rather than
    /// whatever encloses it. After the 2 of <c>x^2</c> that is the 2, not the script; after the closing
    /// brace of a fraction there is nothing deeper, so it is the fraction.
    /// </para>
    /// </summary>
    public ILayoutNode? SymbolBefore(int offset)
    {
        ILayoutNode? best = null;
        foreach (var node in Root.SelfAndDescendants())
        {
            if (!node.Stands() || node.Sits().End != offset) continue;

            // Never a run of things. A row is not an item — it is however many items, each of which is
            // one — so it is never "the thing before the caret" however exactly it happens to end
            // there. Without this, a caret sitting after something that drew nothing (a thin space,
            // say) found no symbol ending where it stood and climbed until it reached whatever
            // contained them all: in a two-line align block, backspace un-rendered both equations.
            if (IsSequence(node)) continue;

            // And never a box the typesetter made for its own purposes. A piece of layout that no part
            // of the parse tree stands for is not something the reader wrote: it is how the typesetter
            // chose to group what they wrote — the body of an align block, a base and the primes after
            // it, a construct and the space following it. Those are runs too, by a different route, and
            // the same align block is what finds it: its body is one box covering both equations, and
            // nothing in the source is exactly that.
            if (Innermost(node) is null) continue;

            // Deliberately no exception for a node that happens to span the whole formula. There used to
            // be one — "backspace behind the last symbol must not take everything the reader wrote" —
            // and it was aimed at a row, which the line above already refuses. What it actually caught
            // was a formula that *is* one construct: `\frac{1}{1 + \frac{1}{x}}` has a single thing in
            // it, and skipping that left nothing ending at the caret at all. Backspace then fell through
            // to deleting a character, which took the closing brace off a construct the reader could not
            // see the braces of — a keystroke that quietly produced LaTeX that no longer parses. One
            // construct un-renders whether or not it is the only one.
            if (best is null || node.Ancestors().Count() > best.Ancestors().Count()) best = node;
        }

        return best is null ? null : Owning(best);
    }

    /// <summary>
    /// The thing a piece belongs to, when it is not a thing in its own right.
    /// <para>
    /// A delimiter is drawn by the fence that holds it rather than being a part of it — the same
    /// category as a fraction's bar, and it names no role for the same reason: it is not a place
    /// content goes. But unlike a bar it is not decoration either. A bracket carries meaning only as a
    /// pair; one without its partner cannot be read at all, so nothing may point at, take, carry or
    /// delete it alone. Pointing at one means the group.
    /// </para>
    /// </summary>
    public ILayoutNode Owning(ILayoutNode node)
    {
        var owner = node;

        // Only ever into something that is a construct and claims parts of its own. Climbing on the
        // strength of the piece naming no role is not enough: where nothing names a role — a tree with
        // no parse behind it — every step would qualify and this would walk to the top and hand back
        // the whole formula.
        while (RoleOf(owner) is null && owner.Parent is { } parent
               && IsComposite(parent) && !IsSequence(parent))
            owner = parent;

        return owner;
    }

    /// <summary>
    /// Whether this piece is made of parts — a construct rather than a symbol.
    /// <para>
    /// The question backspace turns on. A construct can be taken back to the source it was written as,
    /// because there is source to go back to: <c>\frac{a}{b}</c> is a command and three braces the
    /// reader typed and can no longer see. A symbol has nothing hidden behind it — an α is an α however
    /// many letters spelled it — so backspace simply takes it, which is what pressing backspace over
    /// one thing has always meant.
    /// </para>
    /// </summary>
    public bool IsComposite(ILayoutNode node) =>
        Innermost(node) is { } part && part.Parts.Any();

    /// <summary>
    /// Whether this piece is a run of things rather than one thing — see <see cref="LatexNode.IsRun"/>,
    /// which is the same question asked of the part a piece was drawn from.
    /// </summary>
    private bool IsSequence(ILayoutNode node) =>
        Innermost(node) is { } part && LatexNode.IsRun(part);

    /// <summary>
    /// The innermost part of the parse tree standing for exactly what this piece of layout was drawn
    /// from, or null if it was drawn from nothing anybody wrote.
    /// <para>
    /// Innermost, because a part and what holds it can stand for the same characters — a formula that is
    /// one fraction is both a run of one thing and a fraction — and the question is always about the
    /// nearer of the two. Reading it as the run would make backspace refuse to un-render a formula
    /// consisting of a single construct, on the grounds that a run is never one thing.
    /// </para>
    /// <para>
    /// Which is settled by asking the piece, because the builder told it. This used to turn the piece's
    /// offsets back into a part by searching the reading for what stood at them — a round trip out of a
    /// part into two numbers and back, when the part was on the piece the whole time.
    /// </para>
    /// </summary>
    private static TexPart? Innermost(ILayoutNode node) =>
        node.Part is TexSourcePart { Length: > 0 } part ? part.Of : null;
}
