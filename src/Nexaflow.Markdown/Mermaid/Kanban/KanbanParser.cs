using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>
/// Reads a kanban board — a <c>kanban</c> block: its columns and cards, each a node, and the <c>::icon(…)</c> and <c>:::class</c>
/// lines decorating the node above them — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a board. The header is optional, since a <c>kanban</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A node is read as every outline diagram's is (<see cref="MermaidOutline"/>), and then its metadata,
/// <c>@{ assigned: knsv, ticket: MC-2037, priority: 'High' }</c>. Every statement is its row, but for a title written in quotes hard
/// inside its brackets, which runs to the quote closing it, and metadata, which runs from its <c>@{</c> to the <c>}</c> closing it —
/// each across as many lines as it takes, as Mermaid's lexer reads them. The first node's indentation is a column's; a node indented
/// further is a card in the column above it. Which a node is, is a fact about the block rather than its line, so it is worked out by
/// a stage the language offers the engine (<see cref="Stages.ResolveColumns"/>).
/// </para>
/// </summary>
public sealed class KanbanParser : ITranspile
{
    private KanbanParser()
    {
    }

    /// <summary>The language a kanban board is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "kanban";

    /// <summary>The words a kanban board's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    public const string DataMark = "@{";

    /// <summary>A card's priorities, the most urgent first.</summary>
    public static readonly IReadOnlyList<string> Priorities = ["Very High", "High", "Medium", "Low", "Very Low"];

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

    private const string Shape = "A column or a card is its id, its title in brackets, or both, and any metadata after: id3[Update the database]@{ assigned: knsv }.";
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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where a title is still to write, between its quotes.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind == MermaidKinds.Quoted;

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a kanban board writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    // ── Lines ───────────────────────────────────────────────────────────────

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

        // A binding standing on a line of its own after the header is lines supplied by whatever the board is shown against. Held
        // as written; read into its place before the board is worked over.
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

        // The statement, which is its row but for what runs on to the mark closing it. As much of it as is read is what was
        // written, and the rest is the line's.
        var end = Ended(source, from);
        var said = Statement(source[from..end], from);
        var last = MermaidParser.Row.Containing(source, Math.Max(from, from + said.Width - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from, [said], from + said.Width, last));
        return last.Stop;
    }

    /// <summary>
    /// The header — <c>kanban</c>, and anything after it held as written — or null where the line says something else, which a
    /// <c>kanban</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(ContentNode.Leaf(Kinds.Verbatim, rest[gap..], MermaidRoles.Arguments, offset: after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>Whether a title written in quotes opens at <paramref name="at"/>: a quote hard after the bracket opening a node's title.</summary>
    private static bool Quoting(string source, int at) => source[at] == '"' && at > 0 && source[at - 1] is '[' or '(' or ')' or '{';

    /// <summary>
    /// Where the title in quotes opening at <paramref name="at"/> closes — the next quote, where the bracket closing the title follows it
    /// — or -1.
    /// </summary>
    private static int Unquoting(string source, int at)
    {
        var close = source.IndexOf('"', at + 1);
        return close >= 0 && close + 1 < source.Length && source[close + 1] is ']' or ')' or '}' or '(' ? close : -1;
    }

    /// <summary>Where the metadata opening at <paramref name="at"/> closes — the next <c>}</c>, where no other metadata opens first — or -1.</summary>
    private static int Undata(string source, int at)
    {
        var close = source.IndexOf('}', at + DataMark.Length);
        var next = source.IndexOf(DataMark, at + DataMark.Length, StringComparison.Ordinal);
        return close >= 0 && (next < 0 || close < next) ? close : -1;
    }
    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending first ends, or the end of the row
    /// a comment is written on — but a title written in quotes runs to the quote closing it, and metadata to the brace closing it. A
    /// quote or a brace nothing closes is a character like any other, and so is a quote whose next quote has no bracket after it to
    /// close the title, or metadata whose brace comes only after the next card's: each is still being written.
    /// </summary>
    private static int Ended(string source, int from)
    {
        string? closing = null;
        var closed = false;

        for (var at = from; at < source.Length; at++)
        {
            if (source[at] == '\n')
            {
                // A title still open runs across the line ending to its own closing bracket, as Mermaid's lexer reads one.
                if (closing is not null && MermaidOutline.Runs(source, at, closing)) continue;
                return MermaidParser.Row.Containing(source, at).End;
            }

            // A title in quotes runs to the quote closing it hard against its bracket; one still being written ends with its row.
            if (Quoting(source, at))
            {
                var close = Unquoting(source, at);
                if (close < 0) return MermaidParser.Row.Containing(source, at).End;

                at = close;
                continue;
            }

            if (closing is not null)
            {
                if (source.AsSpan(at).StartsWith(closing, StringComparison.Ordinal))
                {
                    at += closing.Length - 1;
                    closing = null;
                    closed = true;
                }

                continue;
            }

            if (source.AsSpan(at).StartsWith(DataMark, StringComparison.Ordinal) && Undata(source, at) is var shut and >= 0)
            {
                at = shut;
                closed = true;
                continue;
            }

            // A %% after the brackets is a comment; one in an id written bare is the id's, as Mermaid's lexer reads it.
            if (closed && source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal)) return MermaidParser.Row.Containing(source, at).End;

            if (MermaidOutline.OpenedAt(source, at) is { } bracket)
            {
                closing = bracket.Close;
                at += bracket.Open.Length - 1;
            }
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>A column or a card, or a line decorating the node above it — or the line held as written, with the reason.</summary>
    private static ContentNode Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at, MermaidOutline.CommentAt);

        if (line.Sees(MermaidOutline.ClassMark)) return MermaidOutline.Decoration(line, MermaidOutline.ClassMark, null, KanbanRoles.Class, KanbanKinds.Class);
        if (line.Sees(MermaidOutline.IconMark)) return MermaidOutline.Decoration(line, MermaidOutline.IconMark, ")", KanbanRoles.Icon, KanbanKinds.Icon);
        return Node(line);
    }

    /// <summary>A column or a card: <c>id3[Update the database]@{ assigned: knsv }</c>, <c>Todo</c>, <c>[In progress]</c>.</summary>
    private static ContentNode Node(MermaidLine line)
    {
        if (!MermaidOutline.Node(line, KanbanRoles.Id, KanbanRoles.Title, "([){}@", out var titled, quotesInWords: true)) return line.Shown(Shape);

        if (line.Sees(DataMark)) Data(line);
        line.Space();

        return line.Done && (titled || line.Rest.Length == 0) ? line.Read(KanbanKinds.Node) : line.Shown(Shape);
    }

    /// <summary>A node's metadata: <c>@{ ticket: MC-2037, assigned: 'knsv' }</c> — held with the reason where it will not read, or is never closed.</summary>
    private static void Data(MermaidLine line)
    {
        const string Written = "Metadata is written key: value, a comma between each: @{ assigned: knsv, priority: 'High' }.";

        line.Open();
        line.Token(DataMark, Roles.Open);
        line.Space();

        var read = line.Done || line.Sees("}") || line.Properties(ends: '}', what: "Metadata");
        if (!read) line.Words(KanbanRoles.Data, Written, until: "}");

        line.Space();
        var closed = line.Token("}", Roles.Close);
        line.Close(KanbanKinds.Data, trouble: closed ? null : "Metadata is closed with }.");
    }
}
