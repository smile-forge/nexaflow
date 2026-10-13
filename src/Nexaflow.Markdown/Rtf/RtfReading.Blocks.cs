using System.Runtime.InteropServices;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Rtf;

/// <summary>What ends a paragraph: a paragraph mark, a cell, or a row.</summary>
internal enum RtfEnd
{
    Paragraph,
    Cell,
    Row,
}

/// <summary>
/// Building the blocks: the paragraph being read, piece by piece, and the tables it lands in when it ends.
///
/// <para>
/// <strong>Every piece is placed in the order it was read</strong>, so every branch is one unbroken stretch of the
/// source: what went before a paragraph's first word is in that paragraph, what went between two blocks is trivia between
/// them, and a table is exactly its rows.
/// </para>
/// </summary>
internal sealed partial class RtfReading
{
    /// <summary>One piece of the paragraph being read: what it is, whether it is drawn, how, and the link it leads along.</summary>
    private readonly record struct Inline(ContentNode Node, bool Drawn, TextLook? Look, int Link);

    /// <summary>The paragraph being read.</summary>
    private readonly List<Inline> _line = [];

    /// <summary>How many paragraphs have been read, so a group can tell whether the one it began in is still being read.</summary>
    private int _lines;

    /// <summary>The document's blocks, as they are finished.</summary>
    private readonly List<ContentNode> _blocks = [];

    // ── The run of codes being read ─────────────────────────────────────────
    //
    // Codes are decoded a run at a time rather than one by one, because a character of a double-byte code page is two bytes
    // and a writer is free to break the line between them.

    private readonly List<ContentNode> _codes = [];
    private readonly List<byte> _bytes = [];
    private readonly StringBuilder _decoded = new();
    private RtfFont _codedIn = RtfFont.Undeclared;
    private TextLook _codedLook = TextLook.Plain;
    private int _codedLink;

    // ── Pieces ──────────────────────────────────────────────────────────────

    /// <summary>Machinery: written, never drawn, and the end of any run of codes before it.</summary>
    private void Machinery(ContentNode node)
    {
        Decoded();
        _line.Add(new Inline(node, Drawn: false, Look: null, _group.Link));
    }

    /// <summary>
    /// Something written that stands for nothing at all — which, met in the middle of a run of codes, is part of that run
    /// rather than the end of it.
    /// </summary>
    private void Quiet(ContentNode node)
    {
        if (_codes.Count > 0) _codes.Add(node);
        else _line.Add(new Inline(node, Drawn: false, Look: null, _group.Link));
    }

    /// <summary>Words drawn as written.</summary>
    private void Drawn(ContentNode node)
    {
        Decoded();
        _line.Add(new Inline(node, Drawn: true, Look(), _group.Link));
    }

    /// <summary>A tab, which takes the rest of the line on to the paragraph's next stop.</summary>
    private void Tab(int start, int length)
    {
        Decoded();
        _line.Add(new Inline(ContentNode.Leaf(MarkdownKinds.Tab, _source.Substring(start, length), Roles.Element, offset: start),
                             Drawn: true, Look(), _group.Link));
    }

    /// <summary>A line broken where the writer broke it, inside the paragraph.</summary>
    private void Broken(RtfToken token)
    {
        Decoded();
        _line.Add(new Inline(Leaf(token, MarkdownKinds.Break, MarkdownRoles.Hard), Drawn: true, Look: null, _group.Link));
    }

    // ── Codes ───────────────────────────────────────────────────────────────

    /// <summary>A byte spelled in hex.</summary>
    private void Coded(RtfToken token, byte value)
    {
        Coding(Leaf(token, RtfKinds.Hex, Roles.Element));
        _bytes.Add(value);
    }

    /// <summary>Bytes written as themselves, each a character of the font's code page.</summary>
    private void Coded(int start, int length)
    {
        Coding(ContentNode.Leaf(RtfKinds.Data, _source.Substring(start, length), Roles.Element, offset: start));

        foreach (var c in _source.AsSpan(start, length)) _bytes.Add((byte)c);
    }

    /// <summary>A control word or symbol standing for <paramref name="means"/>.</summary>
    private void Coded(RtfToken token, string means)
    {
        Coding(Leaf(token, token.Kind == RtfTokenKind.Word ? RtfKinds.Control : RtfKinds.Symbol, Roles.Element));
        Bytes();
        _decoded.Append(means);
    }

    /// <summary>
    /// <paramref name="leaf"/> as part of the run of codes being read — or the first of a new run, where the font, the look
    /// or the link it is read in is not the run's.
    /// </summary>
    private void Coding(ContentNode leaf)
    {
        var font = Font();
        var look = Look();
        var link = _group.Link;

        if (_codes.Count > 0 && (!_codedIn.Equals(font) || !_codedLook.Equals(look) || _codedLink != link)) Decoded();

        if (_codes.Count == 0) (_codedIn, _codedLook, _codedLink) = (font, look, link);

        _codes.Add(leaf);
    }

    /// <summary>Decodes the bytes read so far into the run's characters.</summary>
    private void Bytes()
    {
        if (_bytes.Count == 0) return;

        _decoded.Append(RtfCodePages.Decode(CollectionsMarshal.AsSpan(_bytes), CodePageOf(_codedIn), _codedIn.Symbol));
        _bytes.Clear();
    }

    /// <summary>Ends the run of codes being read, if one is, as a piece of the paragraph drawn as what it decodes to.</summary>
    private void Decoded()
    {
        if (_codes.Count == 0) return;

        Bytes();

        var node = ContentNode.Branch(MarkdownKinds.Encoded,
            [.. _codes, Fact(MarkdownKinds.Decoded, MarkdownRoles.Decoded, _decoded.ToString())],
            Roles.Element, Begins(_codes));

        _line.Add(new Inline(node, Drawn: true, _codedLook, _codedLink));

        _codes.Clear();
        _decoded.Clear();
    }

    // ── Paragraphs ──────────────────────────────────────────────────────────

    /// <summary>
    /// The paragraph being read ends at <paramref name="token"/> — a paragraph mark, the end of a cell, or the end of a row at
    /// <paramref name="depth"/> tables deep — and is put where it belongs.
    /// </summary>
    private void Ended(RtfToken token, RtfEnd end, int depth)
    {
        Decoded();

        var mark = Leaf(token, token.Kind == RtfTokenKind.Word ? RtfKinds.Control : RtfKinds.Symbol, Roles.Name);

        switch (end)
        {
            case RtfEnd.Paragraph:
            {
                var within = _group.Depth;
                var paragraph = Paragraphed(mark);

                Tabled(within);

                if (within > 0) CellAt(within).Blocks.Add(paragraph);
                else _blocks.Add(paragraph);

                break;
            }

            case RtfEnd.Cell:
            {
                Tabled(depth);

                var cell = CellAt(depth);

                if (_line.Any(piece => piece.Drawn)) cell.Blocks.Add(Paragraphed(mark));
                else cell.Blocks.Add(Between([.. _line.Select(piece => piece.Node), mark]));

                var row = _tables[depth - 1].Row!;
                row.Cells.Add(cell);
                row.Cell = null;

                break;
            }

            case RtfEnd.Row:
            {
                Tabled(depth);

                var table = _tables[depth - 1];
                var row = table.Row ?? new RtfRowBuild();

                if (row.Cell is { } open) row.Cells.Add(open);

                row.Cell = null;
                row.Trailing.AddRange(_line.Select(piece => piece.Node));
                row.Trailing.Add(mark);
                row.Defined = Defined(depth);

                table.Rows.Add(row);
                table.Row = null;

                break;
            }
        }

        _line.Clear();
        _lines++;
    }

    /// <summary>The paragraph being read, ended by <paramref name="mark"/> where it has one, set as the group says.</summary>
    private ContentNode Paragraphed(ContentNode? mark)
    {
        var words = Grouped(_line);
        var body = ContentNode.Branch(Kinds.Words, words, Roles.Body, Begins(words));

        List<ContentNode> parts = [body];
        if (mark is not null) parts.Add(mark);
        parts.Add(Fact(MarkdownKinds.Typeset, MarkdownRoles.Typeset, Paragraph()));

        return ContentNode.Branch(MarkdownKinds.Paragraph, parts, Roles.Element, Begins(parts));
    }

    /// <summary>
    /// A paragraph's pieces as its words: every stretch leading along one link gathered under that link, and every run of
    /// drawn pieces set one way gathered under that look.
    /// </summary>
    private List<ContentNode> Grouped(List<Inline> line)
    {
        var words = new List<ContentNode>(line.Count);

        for (var at = 0; at < line.Count;)
        {
            var link = line[at].Link;
            var stop = at + 1;

            while (stop < line.Count && line[stop].Link == link) stop++;

            var span = CollectionsMarshal.AsSpan(line)[at..stop];

            if (link != 0 && _links.TryGetValue(link, out var destination))
            {
                var inside = Styled(span);

                words.Add(ContentNode.Branch(Kinds.Link,
                [
                    ContentNode.Branch(Kinds.Words, inside, Roles.Body, Begins(inside)),
                    ContentNode.Branch(Kinds.Link, [ContentNode.Holding(Kinds.Link, Roles.Destination, destination)], Roles.Derived),
                ], Roles.Element, Begins(inside)));
            }
            else
            {
                words.AddRange(Styled(span));
            }

            at = stop;
        }

        return words;
    }

    /// <summary>Every run of drawn pieces set alike, under the look they are set in; everything else as it is.</summary>
    private static List<ContentNode> Styled(ReadOnlySpan<Inline> pieces)
    {
        var styled = new List<ContentNode>(pieces.Length);

        for (var at = 0; at < pieces.Length;)
        {
            var look = pieces[at].Look;

            if (!pieces[at].Drawn || look is null || look == TextLook.Plain)
            {
                styled.Add(pieces[at++].Node);

                continue;
            }

            var stop = at + 1;
            while (stop < pieces.Length && pieces[stop].Drawn && look.Equals(pieces[stop].Look)) stop++;

            var run = new List<ContentNode>(stop - at + 1);
            foreach (var piece in pieces[at..stop]) run.Add(piece.Node);
            run.Add(Fact(MarkdownKinds.Typeset, MarkdownRoles.Typeset, look));

            styled.Add(ContentNode.Branch(MarkdownKinds.Styled, run, Roles.Element, Begins(run)));
            at = stop;
        }

        return styled;
    }

    // ── Tables ──────────────────────────────────────────────────────────────

    /// <summary>The tables open, outermost first.</summary>
    private readonly List<RtfTableBuild> _tables = [];

    /// <summary>The cells each depth of table defined for its next row, and the one being defined.</summary>
    private readonly Dictionary<int, List<RtfCellDefinition>> _defined = [];

    private RtfCellDefinition _defining = new();

    /// <summary>Whether a <c>\trowd</c> has started a row's definition at <paramref name="depth"/>.</summary>
    private List<RtfCellDefinition> Defined(int depth) =>
        _defined.TryGetValue(depth, out var cells) ? [.. cells] : [];

    /// <summary>Opens tables down to <paramref name="depth"/>, and closes any deeper — a paragraph is in exactly that many.</summary>
    private void Tabled(int depth)
    {
        while (_tables.Count > depth) Close();
        while (_tables.Count < depth) _tables.Add(new RtfTableBuild());
    }

    /// <summary>The cell being written at <paramref name="depth"/>, begun where none is.</summary>
    private RtfCellBuild CellAt(int depth)
    {
        var table = _tables[depth - 1];
        table.Row ??= new RtfRowBuild();

        return table.Row.Cell ??= new RtfCellBuild();
    }

    /// <summary>Closes the innermost table, putting it in the cell holding it or among the document's blocks.</summary>
    private void Close()
    {
        var table = _tables[^1];
        _tables.RemoveAt(_tables.Count - 1);

        if (table.Row is { } open)
        {
            if (open.Cell is { } cell) open.Cells.Add(cell);
            open.Cell = null;
            table.Rows.Add(open);
        }

        if (table.Rows.Count == 0) return;

        var node = table.Built();

        if (_tables.Count == 0) _blocks.Add(node);
        else CellAt(_tables.Count).Blocks.Add(node);
    }

    // ── The end ─────────────────────────────────────────────────────────────

    /// <summary>What is left once the tokens run out: a last paragraph with no mark after it, and the braces closing the document.</summary>
    private void Finish()
    {
        Unfinished();
        Tabled(0);

        if (_line.Count > 0) _blocks.Add(Between([.. _line.Select(piece => piece.Node)]));

        _line.Clear();
    }

    /// <summary>The last paragraph, where it was written with no mark after it and has any words — set as the group it is in says.</summary>
    private void Unfinished()
    {
        Decoded();

        if (!_line.Any(piece => piece.Drawn)) return;

        var within = _group.Depth;
        var paragraph = Paragraphed(null);

        Tabled(within);

        if (within > 0) CellAt(within).Blocks.Add(paragraph);
        else _blocks.Add(paragraph);

        _line.Clear();
        _lines++;
    }

    /// <summary>
    /// A page break. The document is read as one continuous page, so it is drawn as the room a few blank lines take, which is
    /// what tells a reader the writer started a new page here. A paragraph written across it goes on after it, as a paragraph
    /// of its own set the same way.
    /// </summary>
    private void Paged(RtfToken token)
    {
        Unfinished();
        Tabled(0);

        var mark = Leaf(token, RtfKinds.Control, Roles.Name);
        List<ContentNode> parts = [.. _line.Select(piece => piece.Node), mark];

        _blocks.Add(ContentNode.Branch(MarkdownKinds.Page, parts, Roles.Element, Begins(parts)));

        _line.Clear();
        _lines++;
    }

    // ── Making nodes ────────────────────────────────────────────────────────

    /// <summary>Machinery standing between blocks: trivia, which nothing draws.</summary>
    private static ContentNode Between(List<ContentNode> pieces) =>
        ContentNode.Branch(RtfKinds.Between, pieces, Roles.Trivia, Begins(pieces));

    /// <summary>What was worked out about a piece, hung under it.</summary>
    private static ContentNode Fact(string kind, string role, object held) =>
        ContentNode.Branch(kind, [ContentNode.Holding(kind, role, held)], Roles.Derived);

    /// <summary>Where the first of <paramref name="pieces"/> that says so begins.</summary>
    internal static int? Begins(IReadOnlyList<ContentNode> pieces)
    {
        foreach (var piece in pieces)
        {
            if (piece.IsDerived) continue;
            if (piece.Offset is { } offset) return offset;
            if (Begins(piece.Children) is { } inner) return inner;
        }

        return null;
    }
}
