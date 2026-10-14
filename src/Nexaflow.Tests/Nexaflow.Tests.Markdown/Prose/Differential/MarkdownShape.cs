using System.Globalization;
using System.Text;

using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Figures;
using Markdig.Extensions.Footers;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using SourceSpan = Markdig.Syntax.SourceSpan;

namespace Nexaflow.Tests.Markdown.Prose.Differential;

/// <summary>
/// What a reading of a document says, put so that two readings of different shapes can be compared: the set of
/// constructs it found, each a kind, where its first and last characters stand, and the facts it carries.
///
/// <para>
/// Nesting is in the ranges, so neither tree's shape has to agree with the other's — only what each says is there. The
/// kinds are the reading's own (<see cref="MarkdownKinds"/>) wherever one kind is one construct, and a name of their own
/// where one of ours is two: code is a block and a span, a formula sits in a sentence or on its own.
/// </para>
/// <para>
/// <strong>Markdig's types are mapped here, not by the reader's own mapping</strong>, because the mapping is part of what
/// is being tested. This table is the specification a reader of our own is written to.
/// </para>
/// <para>
/// A range leaves out the space and line endings at either end of it, and a line break is the line ending it stands on —
/// the two places Markdig's spans and the reading's pieces measure the same characters differently. Every other allowance
/// is named where it is made, with why.
/// </para>
/// </summary>
internal static class MarkdownShape
{
    public const string CodeSpan = "code-span";
    public const string IndentedCode = "code-indented";
    public const string Fence = "fence";
    public const string HtmlBlock = "html-block";
    public const string HtmlInline = "html-inline";
    public const string Formula = "formula";
    public const string DisplayedFormula = "formula-display";
    public const string AbbreviationDefinition = "abbreviation-definition";

    /// <summary>A stretch the reading could not read and shows as it was typed — which Markdig never does, so it always diverges.</summary>
    public const string ShownAsWritten = "shown-as-written";

    /// <summary>A block of a kind the reading has no reader for.</summary>
    public const string Unread = "unread";

    /// <summary>The kinds that are one construct in both readings, by the reading's own name.</summary>
    private static readonly HashSet<string> Same =
    [
        MarkdownKinds.Paragraph, MarkdownKinds.Heading, MarkdownKinds.Rule, MarkdownKinds.Quote, MarkdownKinds.Alert,
        MarkdownKinds.List, MarkdownKinds.Item, MarkdownKinds.Task, MarkdownKinds.Table, MarkdownKinds.Row, MarkdownKinds.Cell,
        MarkdownKinds.Definition, MarkdownKinds.Term, MarkdownKinds.FrontMatter, MarkdownKinds.Reference,
        MarkdownKinds.Emphasis, MarkdownKinds.Strong, MarkdownKinds.Strike, MarkdownKinds.Mark, MarkdownKinds.Insert,
        MarkdownKinds.Sub, MarkdownKinds.Sup, MarkdownKinds.Citation, MarkdownKinds.Image, MarkdownKinds.Entity,
        MarkdownKinds.Break, MarkdownKinds.Abbreviation, MarkdownKinds.Figure, MarkdownKinds.Caption, MarkdownKinds.Footer,
        Kinds.Link,
    ];

    /// <summary>The kinds whose insides are not looked into: what is in them is no construct of markdown's.</summary>
    private static readonly HashSet<string> Closed =
    [
        MarkdownKinds.Rule, MarkdownKinds.Task, MarkdownKinds.FrontMatter, MarkdownKinds.Reference, MarkdownKinds.Entity,
        MarkdownKinds.Break, MarkdownKinds.Abbreviation, CodeSpan, IndentedCode, Fence, HtmlBlock, HtmlInline, Formula,
        DisplayedFormula, AbbreviationDefinition, ShownAsWritten, Unread,
    ];

    // ── The reading ─────────────────────────────────────────────────────────

    /// <summary>What the reading of <paramref name="source"/> says is in it.</summary>
    public static List<MarkdownConstruct> Of(ContentNode reading, string source)
    {
        var shape = new List<MarkdownConstruct>();
        Walk(reading, reading.Offset ?? 0, words: false);
        return shape;

        void Walk(ContentNode node, int stands, bool words)
        {
            if (node.IsDerived) return;

            // A definition is trivia between blocks, and still a construct: it is what Markdig took out of the text.
            if (node.Role == Roles.Trivia && node.Kind != MarkdownKinds.Reference) return;

            if (!ReferenceEquals(node, reading) && Ours(node, words) is { } kind)
            {
                var (start, end) = Extent(node, stands, source, firstLine: kind is MarkdownKinds.Row or MarkdownKinds.Cell);
                shape.Add(Construct(kind, start, end, OurFacts(node, kind), source));
                if (Closed.Contains(kind)) return;
            }

            var at = stands;
            var inside = words || node.Kind == Kinds.Words;

            foreach (var child in node.Children)
            {
                var begins = child.Offset ?? at;
                Walk(child, begins, inside);
                at = begins + child.Width;
            }
        }
    }

    /// <summary>
    /// Where a piece's characters start and end: from its first to its last — or, for a row or a cell, to its last on the line it
    /// starts on (<see cref="Construct(string, int, int, string, string)"/>). Not its own offset and width: a grid table's cell is
    /// its slices of lines that hold other cells too, so it is no one stretch.
    /// </summary>
    private static (int Start, int End) Extent(ContentNode node, int stands, string source, bool firstLine)
    {
        var pieces = new List<(int Start, int End)>();
        var at = stands;

        Reach(node);

        // Nothing in it takes up any characters — a break between two slices of a cell's lines: it stands where it says.
        var stood = node.Offset ?? stands;
        if (pieces.Count == 0) return (stood, stood + node.Width);

        var start = pieces.Min(piece => piece.Start);
        var ending = firstLine && source.IndexOf('\n', start) is var feed and >= 0 ? feed : int.MaxValue;

        return (start, pieces.Where(piece => piece.Start <= ending).Max(piece => piece.End));

        void Reach(ContentNode piece)
        {
            if (piece.IsDerived) return;
            if (piece.Offset is { } offset) at = offset;

            if (piece.IsLeaf)
            {
                if (piece.Width > 0) pieces.Add((at, at + piece.Width));

                at += piece.Width;
                return;
            }

            foreach (var child in piece.Children) Reach(child);
        }
    }

    private static string? Ours(ContentNode node, bool words) => node switch
    {
        BlockNode { Language: MarkdownParser.Maths } => words ? Formula : DisplayedFormula,
        BlockNode => Fence,
        { Kind: Kinds.Verbatim, IsLeaf: true, Role: Roles.Element } => ShownAsWritten,
        { Kind: Kinds.Verbatim, IsLeaf: false } => Unread,
        { Kind: MarkdownKinds.Code } => words ? CodeSpan : node.Part(Roles.Open) is null ? IndentedCode : Fence,
        { Kind: MarkdownKinds.Html } => words ? HtmlInline : HtmlBlock,
        { Kind: MarkdownKinds.Formula } => Formula,
        { Kind: MarkdownKinds.Reference } when Said(node, MarkdownKinds.Abbreviation) is not null => AbbreviationDefinition,
        _ => Same.Contains(node.Kind) ? node.Kind : null,
    };

    private static string OurFacts(ContentNode node, string kind) => kind switch
    {
        MarkdownKinds.Heading => Facts(("rank", Said(node, MarkdownKinds.Rank)), ("id", Said(node, MarkdownKinds.Anchor))),
        MarkdownKinds.List => Facts(("counts", (Said(node, MarkdownKinds.Numbering) ?? (node.Part(Roles.Body) is { } items ? Said(items, MarkdownKinds.Numbering) : null))
                                         is MarkdownNumbering n ? Counted(n.Bullet, n.Start) : null),
                                    ("loose", (Said(node, MarkdownKinds.Loose) ?? (node.Part(Roles.Body) is { } held ? Said(held, MarkdownKinds.Loose) : null)) is true)),
        MarkdownKinds.Task => Facts(("done", node.Children.Any(child => child.Role == MarkdownRoles.Done))),
        MarkdownKinds.Row => Facts(("head", node.Role == MarkdownRoles.Head)),
        MarkdownKinds.Cell => Facts(("align", Said(node, MarkdownKinds.Aligned)),
                                    ("spans", Said(node, MarkdownKinds.Spans) is MarkdownSpans s ? $"{s.Across}x{s.Down}" : null)),
        MarkdownKinds.Alert => Facts(("name", node.SelfAndDescendants().FirstOrDefault(part => part.Role == Roles.Name && part.IsLeaf)?.Text)),
        Kinds.Link or MarkdownKinds.Image => Facts(("to", MarkdownLinks.Goes(node) ?? string.Empty)),
        MarkdownKinds.Break => Facts(("hard", node.Role == MarkdownRoles.Hard)),
        MarkdownKinds.Abbreviation => Facts(("means", Said(node, MarkdownKinds.Abbreviation))),
        MarkdownKinds.Reference => Facts(("to", Said(node, MarkdownKinds.Reference))),
        AbbreviationDefinition => Facts(("means", Said(node, MarkdownKinds.Abbreviation))),
        Fence => Facts(("language", node is BlockNode block ? block.Language : string.Empty)),
        _ => string.Empty,
    };

    /// <summary>What a stage or the reader hung on a piece as <paramref name="kind"/>, where anything was.</summary>
    private static object? Said(ContentNode node, string kind)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsDerived || child.Kind != kind) continue;
            if (child.Held is { } held) return held;

            foreach (var inner in child.Children) return inner.Held ?? inner.Text;
        }

        return null;
    }

    // ── Markdig's ───────────────────────────────────────────────────────────

    /// <summary>What Markdig's reading of <paramref name="source"/> says is in it.</summary>
    public static List<MarkdownConstruct> Of(MarkdownDocument document, string source)
    {
        var shape = new List<MarkdownConstruct>();
        var defined = document.Descendants<LinkReferenceDefinition>().Select(definition => definition.Span).ToList();

        foreach (var block in document) Block(block);

        // Markdig keeps an abbreviation's definition beside the document rather than in it.
        if (document.GetAbbreviations() is { } abbreviations)
            foreach (var abbreviation in abbreviations.Values)
                shape.Add(Construct(AbbreviationDefinition, abbreviation.Span, Facts(("means", abbreviation.Text.ToString().Trim())), source));

        return shape;

        void Block(Block block)
        {
            if (Theirs(block) is { } kind && Span(block, source, defined) is { } span)
            {
                shape.Add(Construct(kind, span.Start, span.End, TheirFacts(block, kind), source));
                if (Closed.Contains(kind)) return;
            }

            if (block is ContainerBlock container)
                foreach (var inner in container) Block(inner);
            else if (block is LeafBlock { Inline: { } words })
            {
                // The line ending a block's last line ends the block, and is no break inside it.
                var last = Span(block, source, defined) is { } whole ? whole.End : block.Span.End + 1;
                while (last > 0 && char.IsWhiteSpace(source[Math.Min(last, source.Length) - 1])) last--;

                foreach (var piece in words) Inline(piece, last);
            }
        }

        void Inline(Inline inline, int last)
        {
            if (Theirs(inline) is { } kind)
            {
                var construct = Construct(kind, inline.Span, TheirFacts(inline, kind), source);
                if (kind == MarkdownKinds.Break && construct.Start >= last) return;

                shape.Add(construct);
                if (Closed.Contains(kind)) return;
            }

            if (inline is ContainerInline container)
                foreach (var inner in container) Inline(inner, last);
        }
    }

    private static string? Theirs(Block block) => block switch
    {
        HeadingBlock => MarkdownKinds.Heading,
        ThematicBreakBlock => MarkdownKinds.Rule,
        AlertBlock => MarkdownKinds.Alert,
        QuoteBlock => MarkdownKinds.Quote,
        ListBlock => MarkdownKinds.List,
        ListItemBlock => MarkdownKinds.Item,
        Table => MarkdownKinds.Table,
        TableRow => MarkdownKinds.Row,
        TableCell => MarkdownKinds.Cell,
        YamlFrontMatterBlock => MarkdownKinds.FrontMatter,
        DefinitionList => MarkdownKinds.Definition,
        DefinitionTerm => MarkdownKinds.Term,
        Figure => MarkdownKinds.Figure,
        FigureCaption => MarkdownKinds.Caption,
        FooterBlock => MarkdownKinds.Footer,
        Markdig.Syntax.HtmlBlock => HtmlBlock,
        MathBlock => DisplayedFormula,
        FencedCodeBlock => Fence,
        CodeBlock => IndentedCode,
        LinkReferenceDefinition => MarkdownKinds.Reference,
        // A cell holding one paragraph holds a run of words, which is how the reading holds it (MarkdownTable.Blocked).
        ParagraphBlock { Parent: TableCell { Count: 1 } } => null,
        ParagraphBlock paragraph => Displayed(paragraph) ? DisplayedFormula : MarkdownKinds.Paragraph,

        // How Markdig groups a term with what it means. The reading groups them itself, after it has read them
        // (MarkdownGroups), so only the terms and the blocks are compared, never either side's grouping.
        DefinitionItem => null,
        _ => null,
    };

    private static string? Theirs(Inline inline) => inline switch
    {
        EmphasisInline emphasis => Emphasised(emphasis.DelimiterChar, emphasis.DelimiterCount),
        CodeInline => CodeSpan,
        LinkInline { IsImage: true } => MarkdownKinds.Image,
        LinkInline or AutolinkInline => Kinds.Link,
        LineBreakInline => MarkdownKinds.Break,
        HtmlEntityInline => MarkdownKinds.Entity,
        Markdig.Syntax.Inlines.HtmlInline => HtmlInline,
        TaskList => MarkdownKinds.Task,
        AbbreviationInline => MarkdownKinds.Abbreviation,
        MathInline => Formula,
        _ => null,
    };

    private static string TheirFacts(Block block, string kind) => block switch
    {
        HeadingBlock heading => Facts(("rank", heading.Level.ToString(CultureInfo.InvariantCulture)), ("id", heading.TryGetAttributes()?.Id)),
        ListBlock list => Facts(("counts", list.IsOrdered ? Counted(list.BulletType, Started(list.BulletType, list.OrderedStart)) : null),
                                ("loose", list.IsLoose)),
        TableRow row => Facts(("head", row.IsHeader)),
        TableCell cell => Facts(("align", Aligned(cell)), ("spans", cell.ColumnSpan > 1 || cell.RowSpan > 1 ? $"{Math.Max(cell.ColumnSpan, 1)}x{Math.Max(cell.RowSpan, 1)}" : null)),
        AlertBlock alert => Facts(("name", alert.Kind.ToString())),
        LinkReferenceDefinition definition => Facts(("to", definition.Url)),
        MathBlock => string.Empty,
        FencedCodeBlock fence when kind == Fence => Facts(("language", fence.Info ?? string.Empty)),
        _ => string.Empty,
    };

    private static string TheirFacts(Inline inline, string kind) => inline switch
    {
        TaskList task => Facts(("done", task.Checked)),
        LinkInline link => Facts(("to", Written(link))),
        AutolinkInline link => Facts(("to", link.Url)),
        LineBreakInline ending => Facts(("hard", ending.IsHard)),
        AbbreviationInline abbreviation => Facts(("means", abbreviation.Abbreviation.Text.ToString().Trim())),
        _ => string.Empty,
    };

    /// <summary>
    /// Where a block is, from its first character to one past its last — or nowhere, where it stands for none.
    ///
    /// <para>
    /// Three allowances, each for something Markdig measures by what it read rather than by what was written:
    /// </para>
    /// <list type="bullet">
    /// <item><b>A block reaches as far as its own lines.</b> A fence nobody closed is given a span as long as its opening
    /// marks, because there was no closing fence to measure to; its lines say where it really ends.</item>
    /// <item><b>A table's cell is the cell's words.</b> A container handed one paragraph need not carry a span of its own,
    /// and where it does not, the paragraph inside it says where the cell is.</item>
    /// <item><b>An alert's marker is no part of the paragraph under it</b>, and nor is an item's box. Markdig keeps
    /// <c>[!NOTE]</c> out of the paragraph's words but not out of its span, and reads <c>[x]</c> as the paragraph's first
    /// words; the reading gives each to what it belongs to. The paragraph starts where its words do, and is nothing where it
    /// has none.</item>
    /// <item><b>A block ends where the next one begins.</b> A pipe table written straight under a line of words is cut out
    /// of the paragraph it interrupted, and the paragraph's span is left reaching over the table.</item>
    /// <item><b>A definition's term is its own line</b>, not the paragraph Markdig read every term out of.</item>
    /// <item><b>A paragraph starts after the link definitions at its head.</b> Markdig takes them out of its words but not
    /// out of its span — nor out of a heading's, where an underline made the paragraph one.</item>
    /// </list>
    /// </summary>
private static (int Start, int End)? Span(Block block, string source, IReadOnlyList<SourceSpan> defined)
{
    if (block is TableCell { Span.Length: <= 0, Count: > 0 } cell) return Span(cell[0], source, defined);

    // A grid table's cell is measured by its first line, and Markdig's span for one grows over every line it reads into it —
    // past the cells beside it on the line it starts on — so its first line is the first line of what it holds. A row is its own
    // cells, measured so: Markdig's span for it reaches back to the first cell coming down into it from the row above, which
    // is that row's.
    if (block is TableCell { Parent.Parent: Table { Parser: GridTableParser } } gridded) return FirstLine(gridded);

    if (block is TableRow { Count: > 0, Parent: Table { Parser: GridTableParser } } row)
    {
        var cells = row.OfType<TableCell>().Select(FirstLine).Where(cell => cell.End > cell.Start).ToList();
        if (cells.Count > 0) return (cells.Min(cell => cell.Start), cells.Max(cell => cell.End));
    }

    // Every term of a definition is given the span of the whole paragraph its lines were; its own line says where it is.
    if (block is DefinitionTerm { Inline: { FirstChild: { } first, LastChild: { } last } }) return (first.Span.Start, last.Span.End + 1);

        // A row or a cell with nothing in it is given no span at all, and an empty head row not even a line: where it is, is
        // the line it was read from, or else the line the first thing holding it with a span starts on — a table's head row
        // is its first.
        if (block.Span.Length <= 0)
        {
            var read = block.Line;
            for (var up = block.Parent; read == 0 && up is not null; up = up.Parent)
                if (up.Span.Length > 0) read = up.Line;

            var empty = LineStart(source, read);
            return (empty, empty);
        }

        var start = block.Span.Start;

        if (block is ParagraphBlock { Parent: AlertBlock alert } marked && alert.IndexOf(marked) == 0)
        {
            if (marked.Inline?.FirstChild is not { } words) return null;
            start = words.Span.Start;
        }

        if (block is ParagraphBlock { Inline.FirstChild: TaskList box })
        {
            if (box.NextSibling is null) return null;
            start = box.Span.End + 1;
        }

        if (block is ParagraphBlock or HeadingBlock)
            foreach (var definition in defined)
                if (definition.Start >= start && definition.End <= block.Span.End) start = Math.Max(start, definition.End + 1);

        var end = block.Span.End + 1;

        if (block is LeafBlock { Lines.Count: > 0 } leaf)
            end = Math.Max(end, leaf.Lines.Lines[leaf.Lines.Count - 1].Slice.End + 1);

        // A fence with nothing under its opening line yet still holds the whole of that line.
        if (block is FencedCodeBlock && source.IndexOf('\n', Math.Clamp(block.Span.Start, 0, source.Length)) is var line)
            end = Math.Max(end, line < 0 ? source.Length : line);

        if (block.Parent is { } parent && parent.IndexOf(block) is var at and >= 0)
            for (var next = at + 1; next < parent.Count; next++)
            {
                if (parent[next] is LinkReferenceDefinitionGroup) continue;
                if (parent[next].Span.Start > start) end = Math.Min(end, parent[next].Span.Start);
                break;
            }

        return (start, end);
    }

/// <summary>
/// Where the first line of what a grid table's cell holds stands: from its first word to the last before the cell's first
/// line break — its whole span, where it holds no words of its own.
/// </summary>
private static (int Start, int End) FirstLine(TableCell cell)
{
    if (cell is not { Count: > 0 } || cell[0] is not LeafBlock { Inline.FirstChild: { } first }) return (cell.Span.Start, cell.Span.End + 1);

    var end = first.Span.End + 1;

    for (var inline = first; inline is not null and not LineBreakInline; inline = inline.NextSibling)
        if (inline.Span.Length > 0) end = Math.Max(end, inline.Span.End + 1);

    return (first.Span.Start, end);
}

    /// <summary>Where the line counted from nought as <paramref name="line"/> starts.</summary>
    private static int LineStart(string source, int line)
    {
        var at = 0;
        for (var count = 0; count < line && at >= 0; count++) at = source.IndexOf('\n', at) is var next and >= 0 ? next + 1 : -1;

        return at < 0 ? source.Length : at;
    }

    /// <summary>
    /// Where a link goes, as its address was written. A bare address names no scheme, and Markdig gives it one —
    /// <c>www.example.org</c> goes to <c>http://www.example.org</c> — where the reading keeps the characters written and
    /// leaves the scheme to whatever follows the link.
    /// </summary>
    private static string Written(LinkInline link) =>
        link is { IsAutoLink: true, Url: { } url } && url.StartsWith("http://", StringComparison.Ordinal)
        && link.FirstChild is LiteralInline { Content: var words } && !words.ToString().StartsWith("http", StringComparison.Ordinal)
            ? url["http://".Length..]
            : link.Url ?? string.Empty;

    /// <summary>
    /// Whether a paragraph is nothing but one formula between double dollars — which the reading takes for the display
    /// formula it is (MarkdownGroups) and Markdig for a paragraph holding a formula.
    /// </summary>
    private static bool Displayed(ParagraphBlock paragraph)
    {
        if (paragraph.Inline is null) return false;

        var formulas = 0;

        foreach (var inline in paragraph.Inline)
            switch (inline)
            {
                case MathInline { DelimiterCount: >= 2 }: formulas++; break;
                case LiteralInline literal when literal.Content.IsEmptyOrWhitespace(): break;
                case LineBreakInline: break;
                default: return false;
            }

        return formulas == 1;
    }

    private static string? Aligned(TableCell cell)
    {
        if (cell.Parent is not TableRow { Parent: Table table } row) return null;

        // Which column a cell stands in is counted along its row, every cell before it as wide as it was written to be.
        var column = 0;
        foreach (var before in row)
        {
            if (ReferenceEquals(before, cell)) break;
            if (before is TableCell earlier) column += Math.Max(earlier.ColumnSpan, 1);
        }

        if (column >= table.ColumnDefinitions.Count) return null;

        return table.ColumnDefinitions[column].Alignment switch
        {
            TableColumnAlign.Left => MarkdownAligns.Left,
            TableColumnAlign.Center => MarkdownAligns.Center,
            TableColumnAlign.Right => MarkdownAligns.Right,
            _ => null,
        };
    }

    // ── Shared ──────────────────────────────────────────────────────────────

    /// <summary>What a run between delimiters is, by the character and how many of them.</summary>
    private static string Emphasised(char delimiter, int count) => (delimiter, count) switch
    {
        ('"', _) => MarkdownKinds.Citation,
        ('~', 2) => MarkdownKinds.Strike,
        ('~', _) => MarkdownKinds.Sub,
        ('^', _) => MarkdownKinds.Sup,
        ('=', _) => MarkdownKinds.Mark,
        ('+', _) => MarkdownKinds.Insert,
        (_, 2) => MarkdownKinds.Strong,
        _ => MarkdownKinds.Emphasis,
    };

    /// <summary>How a list counts: the alphabet it counts in and the number it starts from.</summary>
    private static string Counted(char alphabet, int start) => $"{alphabet}{start.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The number a list's first marker stands for, as the marker reads in the alphabet the list counts in: digits as a
    /// number, a letter as its place in the alphabet, a roman numeral as its value.
    /// </summary>
    private static int Started(char alphabet, string? written)
    {
        var said = written?.Trim() ?? string.Empty;
        if (said.Length == 0) return 1;
        if (int.TryParse(said, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return number;

        if (alphabet is 'i' or 'I')
        {
            var value = 0;
            var last = 0;

            for (var at = said.Length - 1; at >= 0; at--)
            {
                var digit = char.ToLowerInvariant(said[at]) switch { 'i' => 1, 'v' => 5, 'x' => 10, 'l' => 50, 'c' => 100, 'd' => 500, 'm' => 1000, _ => 0 };
                value += digit < last ? -digit : digit;
                last = Math.Max(last, digit);
            }

            return value;
        }

        return char.ToLowerInvariant(said[0]) - 'a' + 1;
    }

    private static string Facts(params (string Name, object? Value)[] facts)
    {
        var said = new StringBuilder();

        foreach (var (name, value) in facts)
        {
            if (value is null) continue;
            if (said.Length > 0) said.Append(';');

            said.Append(name).Append('=').Append(value is bool yes ? (yes ? "yes" : "no") : Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        return said.ToString();
    }

    private static MarkdownConstruct Construct(string kind, SourceSpan span, string facts, string source) =>
        Construct(kind, span.Start, span.End + 1, facts, source);

    private static MarkdownConstruct Construct(string kind, int start, int end, string facts, string source)
    {
        start = Math.Clamp(start, 0, source.Length);
        end = Math.Clamp(end, start, source.Length);

        // A line break is the line ending it stands on, however much of what stands before it either side counts as the break.
        if (kind == MarkdownKinds.Break && source.IndexOf('\n', start) is var ending and >= 0)
            return new MarkdownConstruct(kind, ending, ending + 1, facts);

        // A row and a cell are measured inside the pipes at their edges, which Markdig counts in some places and not in others.
        var edged = kind is MarkdownKinds.Row or MarkdownKinds.Cell;
        var stood = start;

        // And on the line they start on. Markdig measures a grid table's cell written over several lines by its first — but for
        // the last of a row, which it carries on to wherever the row ended — and a row by its cells as they stood when the row
        // was made, part-way down a cell going on into the next. What the cell holds on its later lines is compared all the same.
        if (edged && source.IndexOf('\n', start, end - start) is var first and >= 0) end = first;

        while (start < end && (char.IsWhiteSpace(source[start]) || (edged && source[start] == '|'))) start++;
        while (end > start && (char.IsWhiteSpace(source[end - 1]) || (edged && source[end - 1] == '|'))) end--;

        // A blank line inside a quote is the quote's marks and nothing else; at the end of a range it is no part of what
        // it follows, though Markdig counts it in where a list item goes on over it.
        for (var feed = source.LastIndexOf('\n', Math.Max(end - 1, 0)); feed >= start && end > start; feed = source.LastIndexOf('\n', Math.Max(end - 1, 0)))
        {
            if (!source.AsSpan(feed + 1, end - feed - 1).ContainsAnyExcept("> \t")) end = feed;
            else break;

            while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        }

        // Something with nothing in it stands at the start of its line: where along the line each reading puts an empty
        // cell's nothing is no fact about it.
        if (start == end)
        {
            var line = stood == 0 ? 0 : source.LastIndexOf('\n', stood - 1) + 1;
            return new MarkdownConstruct(kind, line, line, facts);
        }

        // The hashes closing a heading are optional and say nothing; Markdig counts them out and the reading in.
        if (kind == MarkdownKinds.Heading && start < end && source[start] == '#')
        {
            var hashes = end;
            while (hashes > start && source[hashes - 1] == '#') hashes--;

            if (hashes < end && hashes > start && char.IsWhiteSpace(source[hashes - 1]))
            {
                end = hashes;
                while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
            }
        }

        return new MarkdownConstruct(kind, start, end, facts);
    }
}

/// <summary>One thing a reading says is in a document: what it is, where its first and last characters stand, and what it carries.</summary>
internal readonly record struct MarkdownConstruct(string Kind, int Start, int End, string Facts)
{
    private const int Longest = 40;

    /// <summary>
    /// The construct by what it is and the characters it stands for — which, unlike where it stands, survives the
    /// document it is in being written to somewhere else.
    /// </summary>
    public string Named(string source)
    {
        var text = source[this.Start..this.End];
        var shown = (text.Length > Longest ? text[..Longest] + "…" : text).Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");

        return this.Facts.Length > 0 ? $"{this.Kind}[{this.Facts}] \"{shown}\"" : $"{this.Kind} \"{shown}\"";
    }

    /// <summary>Where it is, as lines and columns counted from one.</summary>
    public string Where(string source) => $"{Place(source, this.Start)}-{Place(source, this.End)}";

    private static string Place(string source, int at)
    {
        var line = 1;
        var starts = 0;

        for (var next = source.IndexOf('\n'); next >= 0 && next < at; next = source.IndexOf('\n', next + 1))
        {
            line++;
            starts = next + 1;
        }

        return $"{line}:{at - starts + 1}";
    }
}
