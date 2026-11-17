using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Venn;

/// <summary>
/// Reads a Venn diagram — a <c>venn-beta</c> block: a <c>title</c>, the <c>set</c>s, the <c>union</c>s where they overlap, the
/// <c>text</c> items written inside either, and the <c>style</c>s of all three — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds
/// round any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between
/// lines — is read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in
/// Mermaid's syntax rather than about a Venn diagram. The header is optional, since a <c>venn</c> fence already says what the
/// block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A name is a word — letters, digits, <c>_</c> and <c>-</c> — or words in quotes; a label is written in
/// brackets after it, <c>["Alpha"]</c> or <c>[Alpha]</c>; a size after a colon. A union is the overlap of the sets its names
/// list, a comma between each. A <c>text</c> line indented under a set or a union is an item in it, and one written at the
/// start of a line names its region first: <c>text A,B AB1["OpenAPI"]</c>. A style sets <c>fill</c>, <c>color</c>,
/// <c>stroke</c>, <c>stroke-width</c> and <c>fill-opacity</c>. Anything in quotes runs to its closing quote wherever that stands
/// — across a line ending too, as Mermaid's own lexer takes it — and a quote nothing closes is a character like any other, so
/// one typed by mistake costs the line it is on rather than everything under it. A title runs to the end of its line.
/// </para>
/// <para>
/// Which of that a line means where it stands — whether a union's sets were written above it, which region an indented item
/// sits in — is not in the line's own characters, so it is the stages' (<see cref="Stages.GroupRegions"/>,
/// <see cref="Stages.ResolveRegions"/>, <see cref="Stages.ResolveStyles"/>), which a Venn diagram's language offers the engine.
/// This reads what each line says, through <see cref="MermaidLine"/>, and holds what it cannot read as written with the reason.
/// </para>
/// </summary>
public sealed class VennParser : ITranspile
{
    private VennParser()
    {
    }

    /// <summary>The language a Venn diagram is written in: what a fence names it by, and what a <c>mermaid</c> block headed by it is held as.</summary>
    public const string Language = "venn";

    /// <summary>The words a Venn diagram's header is written with — Mermaid's own, and the language's name.</summary>
    public static readonly string[] Keywords = ["venn-beta", Language];

    public const string Set = "set";
    public const string Union = "union";
    public const string Text = "text";
    public const string Style = "style";

    /// <summary>What a <c>style</c> line can set.</summary>
    public static readonly IReadOnlyList<string> Styles = ["fill", "color", "stroke", "stroke-width", "fill-opacity"];

    /// <summary>What opens a comment running to the end of its row, outside quotes.</summary>
    private const string Comment = "%%";

    private const string HeaderShape = "Nothing follows venn-beta on its line: a title is written on a line of its own.";

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

    /// <summary>
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a name's quotes or after the
    /// comma before it, and between a label's brackets or its quotes. A size gets none, being drawn as how much room a region
    /// takes rather than as anything to write in.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Name or MermaidKinds.Label
        || (node.Kind == MermaidKinds.Quoted && holder?.Kind == MermaidKinds.Label);

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a Venn diagram writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    /// <summary>Whether a name can be written without quotes — as a set's name, where <paramref name="set"/> says it is one.</summary>
    internal static bool Bare(string name, bool set) =>
        name.Length > 0
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
        && (!set || char.IsAsciiLetter(name[0]) || name[0] == '_');

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/>, and where the one after it starts.</summary>
    /// <param name="headed">Whether the first line saying anything has been read: only it is the header.</param>
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

        // A binding standing on a line of its own after the header is lines supplied by whatever the diagram is shown against.
        // Held as written; read into its place before the diagram is worked over.
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

        // The statement, which is as much of the source as it takes rather than as much as the row holds — what is in quotes runs
        // to its closing quote, where a title is the rest of its row whatever it holds. As much of it as is read is what was
        // written, and the rest is the line's; a statement read as nothing yet — a title with nothing after it — is held whole.
        var end = MermaidLine.Keyword(text, MermaidLine.TitleWord) is not null ? row.End : Ended(source, from);
        var said = Statement(source[from..end], from);
        var took = said is null ? end : from + said.Width;
        var last = MermaidParser.Row.Containing(source, Math.Max(from, took - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from,
                                     [said ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from)], took, last));
        return last.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending outside quotes first ends,
    /// or the end of the row a comment is written on — what is after <c>%%</c> is the comment's, quotes and all.
    /// </summary>
    private static int Ended(string source, int from)
    {
        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n') return MermaidParser.Row.Containing(source, at).End;
            if (character == '%' && at + 1 < source.Length && source[at + 1] == '%') return MermaidParser.Row.Containing(source, at).End;
            if (character != '"') continue;

            var close = source.IndexOf('"', at + 1);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header — <c>venn-beta</c>, with nothing after it but a comment — or null where the line says something else, which a
    /// <c>venn</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length)
        {
            var said = rest[gap..];
            pieces.Add(said.StartsWith(Comment, StringComparison.Ordinal)
                           ? ContentNode.Leaf(Kinds.Comment, said, Roles.Trivia, offset: after)
                           : ContentNode.Shown(said, HeaderShape, MermaidRoles.Arguments, after));
        }

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>
    /// One statement: a title, a set, a union, a text item or a style — or the text held as written, with the reason. Null for a
    /// title with nothing written after it yet, which is held whole.
    /// </summary>
    private static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, Set, Union, Text, Style) switch
        {
            MermaidLine.TitleWord => line.Title(quotes: true),
            Set => Region(line, Set),
            Union => Region(line, Union),
            Text => Item(line),
            Style => Styled(line),
            _ => line.Shown("A Venn diagram line is a set, a union, a text item, a style or a title: set A[\"Alpha\"]:20."),
        };
    }

    /// <summary>
    /// A <c>set</c> — <c>set A["Alpha"]:20</c> — or a <c>union</c> — <c>union A,B["AB"]:3</c>. A size not yet written stands
    /// after whatever space was left for it after the colon, which is where typing it puts it.
    /// </summary>
    private static ContentNode Region(MermaidLine line, string keyword)
    {
        var shape = keyword == Set
            ? "A set is its name, then its label and its size where they are written: set A[\"Alpha\"]:20."
            : "A union is its sets, then its label and its size where they are written: union A,B[\"AB\"]:3.";

        line.Word(keyword);
        line.Space();

        if (line.Done)
            return line.Shown(keyword == Set
                                  ? "A set is named: set A, or set A[\"Alpha\"]:20."
                                  : "A union names the sets it is the overlap of: union A,B[\"AB\"]:3.");

        var named = keyword == Set
            ? line.Name(VennRoles.Id, Letter, name => char.IsLetter(name[0]) || name[0] == '_'
                                                  ? null
                                                  : "A set's name starts with a letter or an underscore, or is written in quotes.")
            : line.Names(Id, Roles.Element, VennRoles.Id);
        if (!named) return line.Shown(shape);

        line.Space();
        if (line.Next == '[')
        {
            if (!line.Label("[", "]", VennRoles.Label)) return line.Shown(shape);
            line.Space();
        }

        if (line.Token(":"))
        {
            // Nothing after the colon: the size is still to come, and goes after whatever space was left for it.
            line.Room();
            line.Amount(VennRoles.Size, MermaidNumber.Positive("A size is a number greater than nought."));
        }

        return line.Done ? line.Read(keyword == Set ? VennKinds.Set : VennKinds.Union) : line.Shown(shape);
    }

    /// <summary>
    /// A <c>text</c> item: <c>text A1["React"]</c>, sitting in the set or union it is indented under — or, written at the
    /// start of a line, <c>text A,B AB1["OpenAPI"]</c>, naming its region first.
    /// </summary>
    private static ContentNode Item(MermaidLine line)
    {
        const string Shape = "A text item is its name and its label: text A1[\"React\"] — or, naming its region first, text A,B AB1[\"OpenAPI\"].";

        line.Word(Text);
        line.Space();
        if (line.Done) return line.Shown(Shape);

        var start = line.Save();
        if (!line.Names(Id, VennRoles.Region, VennRoles.Id)) return line.Shown(Shape);

        if (line.Spaced && line.Past is not ('\0' or '['))
        {
            // A second name: the first was the region.
            line.Space();
            if (!Id(line)) return line.Shown(Shape);
        }
        else if (line.Since(start) is [{ Children: [var only] }])
        {
            // One name and nothing more: the item's own.
            line.Restore(start);
            line.Add(only);
        }
        else
        {
            return line.Shown(Shape);
        }

        line.Space();
        if (line.Next == '[' && !line.Label("[", "]", VennRoles.Label)) return line.Shown(Shape);

        return line.Done ? line.Read(VennKinds.Text) : line.Shown(Shape);
    }

    /// <summary>A <c>style</c>: what it styles, and <c>name:value</c> properties with a comma between each.</summary>
    private static ContentNode Styled(MermaidLine line)
    {
        const string Shape = "A style names what it styles and what it sets, a comma between each: style A fill:#ff6b6b, stroke:#333.";

        line.Word(Style);
        line.Space();
        if (line.Done || !line.Names(Id, VennRoles.Target, VennRoles.Id)) return line.Shown(Shape);

        var at = line.At;
        line.Space();
        if (line.At == at || line.Done || !line.Properties(Styles)) return line.Shown(Shape);

        return line.Read(VennKinds.Style);
    }

    /// <summary>
    /// A name, in quotes or bare. Only a set's own name is held to Mermaid's rule that a bare one starts with a letter or an
    /// underscore: an item may be called by a number.
    /// </summary>
    private static bool Id(MermaidLine line) => line.Name(VennRoles.Id, Letter);

    /// <summary>What a bare name is made of.</summary>
    private static bool Letter(char character) => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or '+';
}
