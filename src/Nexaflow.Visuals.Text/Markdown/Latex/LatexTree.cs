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
/// A formula, as the things that are only true of LaTeX see it: the source, the parse it was read into,
/// and the <see cref="Editing.Laid"/> the builder made from the two.
///
/// <para>
/// <strong>What is here is what a tune and a barcode have no answer to.</strong> Which construct a piece
/// is a part of and what part it is; which cell of a matrix a pointer is in and what dropping a column
/// there would mean; whether the thing before the caret is a command that can be taken back to the
/// characters that spelled it. Every one of those is a question about a parse tree, and a parse tree is
/// the one thing a builder does not hand on.
/// </para>
/// <para>
/// <strong>Everything else is asked of the <see cref="Laid"/>.</strong> Where a caret goes and how tall
/// it is, what a press means, what a drag took, which rectangles a selection washes, where an arrow key
/// lands — all of it is <see cref="LayoutQuery"/> over <see cref="Piece"/>, shared with every other kind
/// of content and exercised against a hand-built tree. This class used to forward seventeen of them, and
/// that is precisely why a formula could not be hosted by the element that hosts a score: the rules
/// looked like LaTeX's because they were reached through a type called LatexTree.
/// </para>
/// <para>
/// It is deliberately free of the typesetter. Producing the tree needs fonts, and fonts need WPF and a
/// desktop; asking it questions needs neither.
/// </para>
/// </summary>
public sealed class LatexTree
{
    /// <param name="latex">The source it was built from.</param>
    /// <param name="laid">What the builder made: the pieces, their size, and what could not be read.</param>
    /// <param name="draws">
    /// Whether the typesetter has a drawing for a named command — <see cref="LatexBuilder.Draws"/>. Handed
    /// in rather than asked for, because asking needs the engine and the engine needs fonts, and every
    /// question below this line is answerable without a desktop.
    /// </param>
    public LatexTree(string latex, Laid laid, Func<string, bool> draws,
                     RawZone? shownAsWritten = null, bool placeholders = false)
    {
        Latex = latex ?? string.Empty;
        Laid = laid;
        _draws = draws;
        _shownAsWritten = shownAsWritten;
        _showHoles = placeholders;
    }

    private readonly Func<string, bool> _draws;
    private readonly RawZone? _shownAsWritten;
    private readonly bool _showHoles;
    private TexReading? _reading;

    /// <summary>The source this tree was built from.</summary>
    public string Latex { get; }

    /// <summary>What the builder made — the tree, its size, and what could not be read.</summary>
    public Laid Laid { get; }

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
    public (Piece Construct, string Role)? RoleOf(Piece node)
    {
        if (node.Part is not { Length: > 0 }) return null;

        // Recovered text was shown, not read. Whatever the parser wrapped it in while carrying on is an
        // artefact of the recovery rather than anything the writer expressed, so it names no part of
        // anything — and copying it can only ever yield the characters.
        if (Laid.IsGuesswork(node)) return null;

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
    /// The formula as a parse tree, with every part place and parent worked out.
    ///
    /// <para>
    /// <b>Not the parser tree.</b> It is what <see cref="TexPipeline.Read"/> hands back: the parse gathered
    /// into the shapes a builder can act on, possibly with holes put in the empty arguments, with undrawable
    /// commands marked, and with whatever is under the caret shown as it was written. Those stages can move
    /// the tree without moving the source or the picture, so this is a third thing worth looking at and not a
    /// restatement of either.
    /// </para>
    /// <para>
    /// Read here rather than kept by the builder, which hands back a <see cref="Editing.Laid"/> and nothing
    /// else. Read once and kept: parts are matched by identity in places, so a second reading of the same
    /// characters is a different tree wearing the same spans.
    /// </para>
    /// <para>
    /// This object is a reading of one string, and changed source is a different <see cref="LatexTree"/>, so
    /// there is no moment at which it could be out of date.
    /// </para>
    /// </summary>
    public TexReading Reading =>
        _reading ??= TexReading.Of(TexPipeline.Read(
            Latex,
            _draws,
            _shownAsWritten is { Length: > 0 } zone ? (zone.Start, zone.Length) : null,
    _showHoles));

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
    private static Piece Drawn(Piece node, TexPart part) =>
        node.Ancestors().FirstOrDefault(
            a => a.Part is TexSourcePart drawn && ReferenceEquals(drawn.Of, part));

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

    /// <summary>
    /// That table as the editor's own model of it, which rewrites LaTeX by character — or null where the
    /// parse tree describes something the model cannot represent as a rectangle: no cells at all, or a
    /// ragged row count that would make cell addresses lie. Nullable because
    /// <see cref="LatexGrid.From"/> says so; declaring it otherwise only moved the null past the compiler
    /// and into whatever touched the grid first.
    /// </summary>
    private LatexGrid? Shaped(TexGrid grid) =>
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
        foreach (var node in Laid.Root.SelfAndDescendants().OrderBy(n => n.Bounds.Width * n.Bounds.Height))
        {
            if (Origin(node) is not { Kind: TexKind.Environment } part) continue;
            if (TexGrid.Read(part.Node, part.Start) is not { } grid) continue;

            // How far the matrix reaches, brackets included. The cells' box stops at the cells: the
            // delimiters are drawn by the fence around them, which is a separate piece of the same
            // construct — so the margin a reader aims at when offering a column to the matrix belongs to
            // the fence, not to the box being asked. Anything drawn from the same part is that same
            // construct drawn in another part, so its extent counts as this one's.
            var reach = node.Bounds;
            foreach (var ancestor in node.Ancestors())
                if (Origin(ancestor) is { } outer && ReferenceEquals(outer, part))
                    reach.Union(ancestor.Bounds);

            if (!reach.Contains(point)) continue;

            // Each cell's extent on the page, taken from the pieces drawn for its node.
            var boxes = new Dictionary<(int, int), Rect>();
            foreach (var cell in grid.Cells)
            {
                if (cell.Node is not { } written) continue;

                var drawn = node.SelfAndDescendants()
                    .Where(piece => piece.Bounds.Width > 0 && Inside(Origin(piece), written))
                    .Select(piece => piece.Bounds)
                    .ToList();
                if (drawn.Count == 0) continue;

                var box = drawn[0];
                foreach (var rect in drawn.Skip(1)) box.Union(rect);
                boxes[(cell.Row, cell.Column)] = box;
            }

            if (boxes.Count == 0) continue;

            // A grid the editor's model cannot represent is skipped for the same reason as one that drew
            // no boxes: there is nothing here to land a drop on, and an enclosing grid may still serve.
            if (Shaped(grid) is not { } shaped) continue;
            return Land(shaped, boxes, point);
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

    // ── Point → source ──────────────────────────────────────────────────────

    // ── Source → geometry ───────────────────────────────────────────────────

    // ── Ranges and stepping ─────────────────────────────────────────────────

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
    /// Which is <see cref="TexEdit.Write"/>'s work, and not this one's: where the caret is and what a
    /// construct may hold are questions about the formula, and the answer is a tree. All this adds is what
    /// the surface needs on top — the source that tree prints as, and where the caret goes in it.
    /// </para>
    /// <para>
    /// Returns null when nothing had to be reshaped to hold the text. Then it is simply inserted, which the
    /// caller does itself: typing a character carries rules of its own — a backslash opens a command that
    /// is shown as it is spelled — and those are about the source, not the structure.
    /// </para>
    /// </summary>
    public LatexWrite? Write(int caret, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var written = TexEdit.Write(Reading, Math.Clamp(caret, 0, Latex.Length), text);

        return written.Reshaped ? Wrote(written.Tree.Print(), written.End, written.Length) : null;
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

        // Read what the cut left behind, rather than shifting this reading's answers into it. A drag can
        // pass over a position whose argument the cut has since taken apart, and the argument of somewhere
        // that is no longer there has nothing to say about where a term lands.
        var written = TexEdit.Write(TexReading.Of(TexPipeline.Read(remainder)), drop, moved);

        return Wrote(written.Tree.Print(), written.End, written.Length);

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

    /// <summary>
    /// A block moved somewhere else in the same matrix.
    /// <para>
    /// Whole columns and whole rows are a reordering of pieces that are already there, so they are done to
    /// the tree: every cell keeps the node it was, and a matrix somebody lined up by hand still looks the
    /// way they left it after a drag somewhere else in it. Anything less than a whole line changes what is
    /// written in the cells rather than which order they are in, and is still rendered.
    /// </para>
    /// </summary>
    private LatexWrite? MovedWithin(LatexGrid grid, GridBlock block, (int Row, int Column) cell)
    {
        if (Table(grid) is { } table)
        {
            if (grid.IsWholeColumns(block))
            {
                var (order, landed) = grid.ColumnOrder(block, cell.Column);
                return Settled(TexEdit.Columns(table, order, landed.Left, landed.Columns));
            }

            if (grid.IsWholeRows(block))
            {
                var (order, landed) = grid.RowOrder(block, cell.Row);
                return Settled(TexEdit.Rows(table, order, landed.Top, landed.Rows));
            }
        }

        return Settled(grid.WithBlockMoved(block, cell.Row, cell.Column));
    }

    /// <summary>The part of the parse tree a table was read from, where the reading still holds one.</summary>
    private TexPart? Table(LatexGrid grid) =>
        Reading.Root.SelfAndDescendants()
            .FirstOrDefault(part => part.Kind == TexKind.Environment && part.Start == grid.Start);

    /// <summary>What an edit to the tree came to, as the surface wants it.</summary>
    private static LatexWrite Settled(TexWrite written) =>
        Wrote(written.Tree.Print(), written.End, written.Length);

    private static LatexWrite? MovedOut(LatexGrid grid, GridBlock block, int to)
    {
        var taken = grid.Extracted(block);
        var (latex, left) = grid.WithBlockTaken(block).Render();

        // The drop was named against the formula as it was. Rewriting the matrix's cells changes the
        // length of everything from the body onwards, so a drop after it moves by the difference.
        var shift = (left.BodyEnd - left.BodyStart) - (grid.BodyEnd - grid.BodyStart);
        var at = Math.Clamp(to <= left.BodyStart ? to : to + shift, 0, latex.Length);

        var written = TexEdit.Write(TexReading.Of(TexPipeline.Read(latex)), at, taken);
        return Wrote(written.Tree.Print(), written.End, written.Length);
    }

    private static LatexWrite Wrote(string latex, int caret, int length) =>
        new(latex, caret, new EditRange(caret - length, length));

    /// <summary>
    /// The one thing immediately before <paramref name="offset"/> — what backspace acts on.
    /// <para>
    /// The deepest piece ending exactly there, which is the symbol the reader is looking at rather than
    /// whatever encloses it. After the 2 of <c>x^2</c> that is the 2, not the script; after the closing
    /// brace of a fraction there is nothing deeper, so it is the fraction.
    /// </para>
    /// </summary>
    public Piece SymbolBefore(int offset)
    {
        var best = default(Piece);
        foreach (var node in Laid.Root.SelfAndDescendants())
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
            if (!best.Exists || node.Depth > best.Depth) best = node;
        }

        return best.Selectable();
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
    public bool IsComposite(Piece node) =>
        Innermost(node) is { } part && part.Parts.Any();

    /// <summary>
    /// Whether this piece is a run of things rather than one thing — see <see cref="LatexCapture.IsRun"/>,
    /// which is the same question asked of the part a piece was drawn from.
    /// </summary>
    private bool IsSequence(Piece node) =>
        Innermost(node) is { } part && LatexCapture.IsRun(part);

    /// <summary>
    /// The part of the parse tree a piece was drawn from, typed — the link back that says what it
    /// <em>is</em> rather than merely where it came from.
    ///
    /// <para>
    /// Several pieces share one: a fraction's box and its bar are one construct drawn in parts. What a
    /// piece is <em>to</em> the thing holding it lives on the parse tree, not on the layout, which is why
    /// this is a reference and not a copy of anything.
    /// </para>
    /// </summary>
    private static TexPart? Origin(Piece piece) => (piece.Part as TexSourcePart)?.Of;

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
    private static TexPart? Innermost(Piece node) =>
        node.Part is TexSourcePart { Length: > 0 } part ? part.Of : null;
}
