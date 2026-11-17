using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid.Block.Stages;

namespace Nexaflow.Markdown.Mermaid.Block;

/// <summary>
/// Reads a block diagram — a <c>block-beta</c> block: how many columns its grid is laid out in, the blocks themselves — as many to a
/// line as are written there — the composites they nest in, the links between them, and the <c>classDef</c>, <c>class</c> and
/// <c>style</c> lines that colour them — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>). The header is optional, since a <c>block</c> fence already says what
/// the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A block is an id, with a label in the brackets that say its shape
/// (<see cref="MermaidShapes"/>) and <c>:2</c> where it takes more than one column; <c>space</c> leaves cells empty;
/// <c>block:</c>…<c>end</c> nests a grid of its own inside a cell (<see cref="ResolveBlocks"/>); and a link joins the blocks
/// written either side of it. <strong>Where a block is drawn is what its author wrote</strong>, which is the whole point of
/// the type — nothing here works out a position, and nothing moves.
/// </para>
/// <para>
/// Mermaid reads a block diagram without caring where its lines end: several statements may share a row — <c>columns 3 a b c</c> —
/// and each keyword starts a statement of its own wherever it stands (<see cref="Ended(string, int)"/>). A label in quotes runs to
/// the quote closing it, across line endings, as Mermaid's lexer reads a string. A link written across two rows is not read as one:
/// what a link joins is written on one row, as every example in the documentation writes it.
/// </para>
/// <para>
/// Which composite each line is in, whether a link has blocks to join, and what styles each block are facts about the whole block,
/// worked out by the stages its language offers the engine (<see cref="ResolveBlocks"/>, <see cref="ResolveLinks"/>,
/// <see cref="ResolveStyles"/>).
/// </para>
/// </summary>
public sealed class BlockParser : ITranspile
{
    private BlockParser()
    {
    }

    /// <summary>The language a block diagram is written in: what a fence names it by, and a word its header is written with.</summary>
    public const string Language = "block";

    /// <summary>The words a block diagram's header is written with — Mermaid's both.</summary>
    public static readonly string[] Keywords = ["block-beta", Language];

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

    public const string ColumnsWord = "columns";
    public const string AutoWord = "auto";
    public const string BlockWord = "block";
    public const string EndWord = "end";
    public const string SpaceWord = "space";

    /// <summary>The classDef, class and style lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, BlockRoles.Id, BlockRoles.Class, "block");

    /// <summary>
    /// The characters a bare id ends at: everything a label's brackets open or close with, the characters a link is made of, the
    /// comma between the ids a class or a style names, and the rest of what Mermaid's ids exclude.
    /// </summary>
    public const string Stops = "([{<>)}]:=-, \t\"";

    /// <summary>Where a block arrow says what it says.</summary>
    public const string ArrowOpen = "<[";
    public const string ArrowClose = "]>";

    /// <summary>The directions a block arrow points: the four, and <c>x</c> and <c>y</c> for a pair of opposites.</summary>
    public static readonly string[] Directions = ["right", "left", "up", "down", "x", "y"];

    private const string ItemShape = "A block is an id, with a label in brackets where it has one: db, A[\"A wide one\"], id:2.";
    private const string OpenShape = "A composite is opened by block, or by block:ID, and ended by end.";
    private const string ColumnShape = "A grid is laid out in columns 3, or in columns auto.";
    private const string ArrowShape = "A block arrow says what it says and where it points: id<[\"Label\"]>(right).";
    private const string LinkShape = "A link joins the blocks either side of it: A --> B, or A -- \"X\" --> B.";

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        var lines = new List<ContentNode>();
        var at = 0;
        var headed = false;

        if (MermaidParser.Fences(source) is (var open, var close))
        {
            // Only blank lines come before front matter; that is what makes it front matter.
            for (; at < open.Start; at = Next(source, at, ref headed, lines)) { }

            lines.Add(MermaidParser.FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, ref headed, lines);

        return new BlockNode(Language, lines, MermaidKinds.Block);
    }

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a block diagram writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    /// <summary>The next line of the block, read and added to <paramref name="lines"/> — or the rows it takes — and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it may be the header.</param>
    private static int Next(string source, int at, ref bool headed, List<ContentNode> lines)
    {
        var row = MermaidParser.Row.At(source, at);
        var (from, to) = row.Text(source);
        var text = source[from..to];

        if (text.Length == 0)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from, [], to, row));
            return row.Stop;
        }

        if (text.StartsWith("%%{", StringComparison.Ordinal)) return MermaidParser.Directive(source, row, from, to, lines);

        if (text.StartsWith(Comment, StringComparison.Ordinal))
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia, offset: from)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is lines supplied by whatever the diagram is shown against. Held
        // as written; read into its place before the diagram is worked over.
        if (BoundText.Path(text) is not null)
        {
            lines.Add(MermaidParser.Line(source, row.Start, from,
                [headed
                    ? ContentNode.Leaf(Kinds.BoundContent, text, offset: from)
                    : ContentNode.Shown(text, "A diagram is not bound whole: its first line names its type, and a binding after it supplies lines of it.", offset: from)],
                to, row));
            return row.Stop;
        }

        if (!headed)
        {
            headed = true;
            if (Heading(text, from) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which is as much of the source as it takes rather than as much as the row holds — a quoted label carries it
        // past its row, and a keyword starts another on it. As much of it as is read is what was written, and the rest is the line's.
        var end = Ended(source, from);
        return MermaidParser.StatementLine(source, row.Start, from, Statement(source[from..end], from), end, lines);
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends. Mermaid reads a block diagram without caring where its lines end,
    /// so a row may hold several statements: <c>columns 3 a b c</c> is the columns and then a line of blocks, and
    /// <c>block:G a b end</c> a composite opened, its blocks and its end. A <c>columns</c> line ends after its count, a composite's
    /// opening after the block it is, an <c>end</c> after the word, and a line of blocks where a keyword starts or the row does. A
    /// label in quotes runs to the quote closing it, across line endings as Mermaid's lexer reads a string; a quote nothing closes is a
    /// quote like any other character, so one typed by mistake costs the line it is on rather than everything written under it.
    /// </summary>
    private static int Ended(string source, int from)
    {
        var limit = source.Length;

        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            // Mermaid's block lexer skips every line ending, and a link takes the space either side of it: a link at the end of a row,
            // or at the start of the next, joins what stands either side of the line ending.
            if (character == '\n' && Continued(source, from, at)) continue;

            if (character == '\n' || source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal))
            {
                limit = MermaidParser.Row.Containing(source, at).End;
                break;
            }

            if (character != '"') continue;

            var close = source.IndexOf('"', at + 1);
            if (close >= 0) at = close;
        }

        var text = source[from..limit];

        var reach = MermaidLine.ExactKeyword(text, Bare, [ColumnsWord, BlockWord, EndWord, .. MermaidStyling.Words]) switch
        {
            ColumnsWord => Token(text, Spaced(text, ColumnsWord.Length)),
            BlockWord => text.Length > BlockWord.Length && text[BlockWord.Length] == ':' ? Token(text, BlockWord.Length + 1) : BlockWord.Length,
            EndWord => EndWord.Length,
            null => Keyed(text),
            _ => text.Length,
        };

        var end = from + reach;
        while (end > from && char.IsWhiteSpace(source[end - 1])) end--;

        return reach >= text.Length ? limit : end;
    }

    /// <summary>
    /// Whether the statement starting at <paramref name="from"/> runs on past the line ending at <paramref name="at"/>: where its row
    /// ends with a link, or the next row saying anything starts with one.
    /// </summary>
    private static bool Continued(string source, int from, int at)
    {
        var before = source[from..at].TrimEnd();

        // A link joins blocks: a line that opens with a keyword — columns, a composite, its end, a style — takes none from the row under it.
        if (MermaidLine.ExactKeyword(before, Bare, [ColumnsWord, BlockWord, EndWord, .. MermaidStyling.Words]) is not null) return false;
        if (MermaidLinks.Ending(before)) return true;

        var next = at + 1;
        while (next < source.Length && char.IsWhiteSpace(source[next])) next++;

        return next < source.Length && MermaidLinks.At(source, next) is not null;
    }

    /// <summary>Where the space starting at <paramref name="at"/> ends.</summary>
    private static int Spaced(string text, int at)
    {
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        return at;
    }

    /// <summary>
    /// Where the token starting at <paramref name="at"/> ends: at the first space outside its brackets and its quotes, so a block written
    /// with its label and its width — <c>A["One two"]:2</c> — is one token.
    /// </summary>
    private static int Token(string text, int at)
    {
        var depth = 0;

        for (; at < text.Length; at++)
        {
            var character = text[at];

            if (character == '"')
            {
                var close = text.IndexOf('"', at + 1);
                if (close < 0) return text.Length;

                at = close;
                continue;
            }

            if (character is '[' or '(' or '{') depth++;
            else if (character is ']' or ')' or '}') depth = Math.Max(0, depth - 1);
            else if (character is ' ' or '\t' && depth == 0) return at;
        }

        return at;
    }

    /// <summary>Where a line of blocks ends: before the first token after it that is a keyword — <c>end</c>, <c>block</c>, <c>columns</c> — or at the end of the row.</summary>
    private static int Keyed(string text)
    {
        var at = Token(text, 0);

        while (at < text.Length)
        {
            var start = Spaced(text, at);
            if (start >= text.Length) break;
            if (MermaidLine.ExactKeyword(text[start..], Bare, ColumnsWord, BlockWord, EndWord) is not null) return start;

            at = Token(text, start);
        }

        return text.Length;
    }

    /// <summary>
    /// The header — <c>block-beta</c> or <c>block</c>, with nothing after it — where the first line saying anything is one. A
    /// <c>block:ID</c> is a composite opening, not a header: the keyword is a header only where nothing but space follows it.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var rest = text[keyword.Length..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0])) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(ContentNode.Leaf(Kinds.Verbatim, rest[gap..], MermaidRoles.Arguments, offset: after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>A statement: a columns line, a composite opening or its end, a styling line, or a line of blocks and links.</summary>
    private static ContentNode Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        // Mermaid's block diagram reads its words case and all: a block called End is a block, and only end closes a composite.
        return MermaidLine.ExactKeyword(line.Written, Bare, [ColumnsWord, EndWord, BlockWord, .. MermaidStyling.Words]) switch
        {
            ColumnsWord => Columns(line),
            EndWord => Ended(line),
            BlockWord => Opens(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, BlockKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, BlockKinds.Class),
            MermaidStyling.StyleWord => Styling.Styled(line, BlockKinds.Style),
            _ => Items(line),
        };
    }

    /// <summary>
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a label's quotes, and where a width
    /// or a column count is still to be written.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Quoted or MermaidKinds.Amount;

    /// <summary>Whether a character carries a bare id, a class or a direction on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>How many columns the grid this line is in is laid out in: <c>columns 3</c>, or <c>columns auto</c>.</summary>
    private static ContentNode Columns(MermaidLine line)
    {
        line.Word(ColumnsWord, letter: Bare);
        line.Room();

        if (!line.Word(AutoWord, MermaidKinds.Key, BlockRoles.Count, Bare))
            line.Amount(BlockRoles.Count, MermaidNumber.Where(count => count >= 1 && count == Math.Floor(count),
                                                              "A grid is laid out in a whole number of columns, one or more."),
                        until: Stops);

        line.Space();
        return line.Done ? line.Read(BlockKinds.Columns) : line.Shown(ColumnShape);
    }

    /// <summary>A composite opening: <c>block</c>, or <c>block:ID</c> — which is a block like any other, label, width and all.</summary>
    private static ContentNode Opens(MermaidLine line)
    {
        line.Word(BlockWord, letter: Bare);

        if (line.Token(":") && !Item(line)) return line.Shown(OpenShape);

        line.Space();
        return line.Done ? line.Read(BlockKinds.Opens) : line.Shown(OpenShape);
    }

    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, letter: Bare);
        line.Space();
        return line.Done ? line.Read(BlockKinds.Ends) : line.Shown("A composite is ended by end, with nothing after it.");
    }

    /// <summary>The blocks, the empty cells, the arrows and the links written on one line, in the order they are written.</summary>
    private static ContentNode Items(MermaidLine line)
    {
        while (!line.Done)
        {
            if (!Link(line) && !Spaces(line) && !Item(line))
            {
                line.Held(line.Reason ?? ItemShape);
                break;
            }

            line.Space();
        }

        return line.Read(BlockKinds.Items);
    }

    // ── What a line of blocks is made of ────────────────────────────────────

    /// <summary>One block: <c>db</c>, <c>A["A wide one"]</c>, <c>id(("DB")):2</c> — or a block arrow, which is a block too.</summary>
    private static bool Item(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!MermaidOutline.Node(line, BlockRoles.Id, BlockRoles.Label, Stops, out var titled, MermaidShapes.Brackets, spaced: false))
            return line.Undo(mark);

        var named = titled || line.At > mark.At;

        switch (Arrow(line))
        {
            case false:
                return line.Undo(mark);

            case true:
                Width(line);
                line.Close(BlockKinds.Arrow);
                return true;
        }

        if (!named) return line.Undo(mark, ItemShape);

        Width(line);
        line.Close(BlockKinds.Item);
        return true;
    }

    /// <summary>
    /// What a block arrow says and where it points: <c>&lt;["Label"]&gt;(x, down)</c>. Null where no arrow is written here, and
    /// false for one written wrong.
    /// </summary>
    private static bool? Arrow(MermaidLine line)
    {
        if (!line.Sees(ArrowOpen)) return null;
        if (!line.Label(ArrowOpen, ArrowClose, BlockRoles.Label)) return false;

        line.Space();
        if (!line.Token("(", Roles.Open)) return line.Fail(ArrowShape);

        line.Room();
        line.Names(Direction, BlockRoles.Direction, "direction", ",", item: null, ends: ')');
        line.Space();

        return line.Token(")", Roles.Close) || line.Fail(ArrowShape);
    }

    /// <summary>Cells left empty: <c>space</c>, or <c>space:3</c>.</summary>
    private static bool Spaces(MermaidLine line)
    {
        if (MermaidLine.ExactKeyword(line.Rest, Bare, SpaceWord) is null) return false;

        line.Open();
        line.Word(SpaceWord, letter: Bare);
        Width(line);
        line.Close(BlockKinds.Space);
        return true;
    }

    /// <summary>How many columns something takes, where it says: the <c>:2</c> after it.</summary>
    private static void Width(MermaidLine line)
    {
        if (!line.Sees(":")) return;

        line.Token(":");
        line.Amount(BlockRoles.Width, MermaidNumber.Where(columns => columns >= 1 && columns == Math.Floor(columns),
                                                          "A block takes a whole number of columns, one or more."),
                    until: Stops);
    }

    /// <summary>A link, with its label between the opening of the link and the link that closes it where it has one.</summary>
    private static bool Link(MermaidLine line)
    {
        if (MermaidLinks.At(line.Written, line.At) is not { } link) return false;

        var mark = line.Save();
        line.Open();

        if (link.Whole)
        {
            line.Token(Drawn(line, link), BlockRoles.Arrow);
            line.Close(BlockKinds.Link);
            return true;
        }

        line.Token(Drawn(line, link), Roles.Open);
        line.Space();

        if (!line.Quoted(BlockRoles.Label, what: "label")) return line.Undo(mark, LinkShape);

        line.Space();
        if (MermaidLinks.At(line.Written, line.At) is not { Whole: true } closing) return line.Undo(mark, LinkShape);

        line.Token(Drawn(line, closing), BlockRoles.Arrow);
        line.Close(BlockKinds.Link);
        return true;
    }

    private static bool Direction(MermaidLine line) =>
        line.Name(BlockRoles.Direction, Bare,
                  said => Directions.Contains(said, StringComparer.OrdinalIgnoreCase)
                      ? null
                      : $"A block arrow points {string.Join(", ", Directions.SkipLast(1))} or {Directions[^1]}.");

    // ── What a link is made of ──────────────────────────────────────────────

    private static string Drawn(MermaidLine line, MermaidLinks.Joined link) => line.Written.Substring(line.At, link.Length);
}
