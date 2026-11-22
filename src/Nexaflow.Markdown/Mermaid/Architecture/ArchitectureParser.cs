using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Architecture;

/// <summary>
/// Reads an architecture diagram — an <c>architecture-beta</c> block: the <c>group</c>s, the <c>service</c>s and <c>junction</c>s
/// inside them, the edges between them, and the <c>align</c> lines saying which of them share a row or a column — and says how what
/// is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about an architecture diagram. The header is optional, since an <c>architecture</c> fence already says what the
/// block is.
/// </para>
/// <para>
/// The rules are Mermaid's. Everything is named by an id, and an id holds letters, digits, underscores and dashes; an icon goes in
/// round brackets after it and a title in square ones, either of them where it has one; and <c>in</c> puts it in a group declared
/// above it. A title is words, or a string as Mermaid's newer grammars write one: in double quotes or single, a backslash keeping the
/// character after it, running across a line ending to its closing quote — so a line is as much of the source as its titles take.
/// An edge names the side each end leaves by — <c>db:R --> L:server</c> — and those sides are what say where things sit: the right
/// of one against the left of another puts the second to its right (<c>ArchitectureBuilder</c>). Whether what a line names is
/// declared, and what it is, is a fact about the whole block, worked out by a stage the language offers the engine
/// (<see cref="Stages.ResolveArchitecture"/>).
/// </para>
/// </summary>
public sealed class ArchitectureParser : ITranspile
{
    private ArchitectureParser()
    {
    }

    /// <summary>The language an architecture diagram is written in: what a fence names it by.</summary>
    public const string Language = "architecture";

    /// <summary>The words an architecture diagram's header is written with — Mermaid's own, and the language's name.</summary>
    public static readonly string[] Keywords = ["architecture-beta", Language];

    public const string GroupWord = "group";
    public const string ServiceWord = "service";
    public const string JunctionWord = "junction";
    public const string AlignWord = "align";
    public const string InWord = "in";
    public const string RowWord = "row";
    public const string ColumnWord = "column";

    /// <summary>The <c>{group}</c> written after an id to reach the group it is in rather than the service itself.</summary>
    public const string GroupMark = "{group}";

    /// <summary>The sides an edge may leave by and arrive at.</summary>
    public static readonly string[] Sides = ["L", "R", "T", "B"];

    /// <summary>What keeps the character after it inside a title in quotes.</summary>
    public const char Backslash = '\\';

    /// <summary>What opens a comment running to the end of its row, outside a title.</summary>
    private const string Comment = "%%";

    private const string GroupShape = "A group is an id, with an icon and a title where it has them: group api(cloud)[API] in cloud.";
    private const string ServiceShape = "A service is an id, with an icon and a title where it has them: service db(database)[Database] in api.";
    private const string JunctionShape = "A junction is an id, and the group it is in where it is in one: junction split in api.";
    private const string EdgeShape = "An edge names the side each end leaves by: db:R --> L:server, or db:T -[reads]- B:server.";
    private const string AlignShape = "An align line shares a row or a column between two services or more: align row db1 db2 db3.";
    private const string NeverClosed = "This title is never closed with the quote it opens with.";

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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a title's or an icon's brackets, and where an id is still to be written.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind == MermaidKinds.Quoted || (node.Kind == MermaidKinds.Name && node.Width == 0);

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

        // The statement, which is as much of the source as it takes rather than as much as the row holds — a title in quotes runs
        // to its closing quote, where a title line is the rest of its row whatever it holds. As much of it as is read is what was
        // written, and the rest is the line's; a title with nothing after it yet is held whole.
        var end = MermaidLine.Keyword(text, MermaidLine.TitleWord) is not null ? row.End : Ended(source, from);
        var said = Statement(source[from..end], from);
        var took = said is null ? end : from + said.Width;
        var last = MermaidParser.Row.Containing(source, Math.Max(from, took - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from,
                                     [said ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from)], took, last));
        return last.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending outside a title first ends, or
    /// the end of the row a comment outside one is written on. A title in quotes is one only where its closing quote has its
    /// bracket hard after it, as Mermaid's grammar writes one; a quote that is not is a character like any other.
    /// </summary>
    private static int Ended(string source, int from)
    {
        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n' || source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal)) return MermaidParser.Row.Containing(source, at).End;
            if (character != '[' || at + 1 >= source.Length || source[at + 1] is not ('"' or '\'')) continue;

            var close = Closing(source, at + 1);
            if (close >= 0 && close + 1 < source.Length && source[close + 1] == ']') at = close + 1;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>
    /// Where the string opening with the quote at <paramref name="open"/> closes — past every character a backslash keeps — or -1
    /// where nothing closes it.
    /// </summary>
    private static int Closing(string text, int open)
    {
        var quote = text[open];

        for (var at = open + 1; at < text.Length; at++)
        {
            if (text[at] == Backslash) at++;
            else if (text[at] == quote) return at;
        }

        return -1;
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header — <c>architecture-beta</c>, and anything after it held as written — or null where the line says something else,
    /// which an <c>architecture</c> fence needs no header before.
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

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>
    /// One statement: a title, a group, a service, a junction, an align line or an edge — or the line held as written, with the
    /// reason. Null for a title with nothing written after it yet, which is held whole.
    /// </summary>
    private static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        return MermaidLine.Keyword(line.Written, MermaidLine.Letter, MermaidLine.TitleWord, GroupWord, ServiceWord, JunctionWord, AlignWord) switch
        {
            MermaidLine.TitleWord => line.Title(),
            GroupWord => Declared(line, GroupWord, ArchitectureKinds.Group, GroupShape, drawn: true),
            ServiceWord => Declared(line, ServiceWord, ArchitectureKinds.Service, ServiceShape, drawn: true),
            JunctionWord => Declared(line, JunctionWord, ArchitectureKinds.Junction, JunctionShape, drawn: false),
            AlignWord => Aligned(line),
            _ => Edge(line),
        };
    }

    /// <summary>
    /// A group, a service or a junction: its id, the icon and title a drawn one may carry, and the group it is put in.
    /// </summary>
    private static ContentNode Declared(MermaidLine line, string word, string kind, string shape, bool drawn)
    {
        line.Word(word);
        line.Room();
        Id(line, ArchitectureRoles.Id);

        if (drawn)
        {
            line.Space();

            // A service may show words in its icon's place, a string as Mermaid's grammar writes one: service fn "λ"[Lambda].
            if (word == ServiceWord && line.Next is '"' or '\'')
            {
                line.Open();
                var said = Quoted(line, ArchitectureRoles.IconText);
                line.Close(MermaidKinds.Icon);
                if (!said) return line.Shown(shape);
            }
            else if (line.Sees("("))
            {
                line.Open();
                var named = line.Label("(", ")", ArchitectureRoles.Icon);
                line.Close(MermaidKinds.Icon);
                if (!named) return line.Shown(shape);
            }

            line.Space();
            if (line.Sees("[") && !Titled(line, ArchitectureRoles.Title)) return line.Shown(shape);
        }

        line.Space();

        if (line.Word(InWord))
        {
            line.Room();
            Id(line, ArchitectureRoles.In);
            line.Space();
        }

        return line.Done ? line.Read(kind) : line.Shown(shape);
    }

    /// <summary>An edge: each end, the side it leaves by, the heads it draws and what is written on it.</summary>
    private static ContentNode Edge(MermaidLine line)
    {
        Id(line, ArchitectureRoles.Id);
        line.Token(GroupMark, ArchitectureRoles.Group);

        // Space may stand either side of a colon: db : R -- L : server.
        if (!line.Space().Token(":")) return line.Shown(EdgeShape);
        if (!Side(line.Space())) return line.Shown(EdgeShape);

        line.Space();
        line.Token("<", ArchitectureRoles.Head);
        if (!Joined(line)) return line.Shown(EdgeShape);

        line.Token(">", ArchitectureRoles.Head);
        line.Space();

        if (!Side(line)) return line.Shown(EdgeShape);
        if (!line.Space().Token(":")) return line.Shown(EdgeShape);

        line.Space();
        Id(line, ArchitectureRoles.Id);
        line.Token(GroupMark, ArchitectureRoles.Group);
        line.Space();

        return line.Done ? line.Read(ArchitectureKinds.Edge) : line.Shown(EdgeShape);
    }

    /// <summary>The services sharing a row or a column: <c>align row db1 db2 db3</c>.</summary>
    private static ContentNode Aligned(MermaidLine line)
    {
        line.Word(AlignWord);
        line.Room();

        if (!line.Word(RowWord, MermaidKinds.Key, ArchitectureRoles.Axis) && !line.Word(ColumnWord, MermaidKinds.Key, ArchitectureRoles.Axis))
            return line.Shown(AlignShape);

        line.Room();
        line.Open();

        while (!line.Done)
        {
            if (!MermaidLine.Letter(line.Next)) break;

            Id(line, ArchitectureRoles.Id);
            line.Room();
        }

        line.Close(MermaidKinds.Names, ArchitectureRoles.Id);
        return line.Done ? line.Read(ArchitectureKinds.Align) : line.Shown(AlignShape);
    }

    // ── What the lines are made of ──────────────────────────────────────────

    /// <summary>
    /// An id, as far as it is written: letters, digits, underscores and dashes, which is what Mermaid's ids hold. One still to be
    /// written is where a hole stands, so a line being typed reads as the line it is becoming.
    /// </summary>
    private static void Id(MermaidLine line, string role)
    {
        line.Open();
        line.Words(role, until: Stops);
        line.Close(MermaidKinds.Name, role);
    }

    /// <summary>The side an edge leaves by or arrives at.</summary>
    private static bool Side(MermaidLine line)
    {
        foreach (var side in Sides)
            if (line.Word(side, MermaidKinds.Key, ArchitectureRoles.Side, char.IsLetterOrDigit))
                return true;

        return line.Fail("An edge leaves and arrives on a side of what it joins: L, R, T or B.");
    }

    /// <summary>What joins an edge's two ends: <c>--</c>, or <c>-[reads]-</c> with what is written on it between.</summary>
    private static bool Joined(MermaidLine line)
    {
        if (line.Token("--")) return true;
        if (!line.Token("-", Roles.Open)) return line.Fail("An edge joins its two ends with -- , or with -[reads]- to say what it is.");

        return Titled(line, ArchitectureRoles.Title) && line.Token("-", Roles.Close);
    }

    /// <summary>
    /// A title in its square brackets: words — <c>[Database]</c> — or a string hard against the brackets, in double quotes or
    /// single — <c>["Data base"]</c>, <c>['it\'s']</c>.
    /// </summary>
    private static bool Titled(MermaidLine line, string role)
    {
        var after = line.At + 1;
        if (after >= line.Written.Length || line.Written[after] is not ('"' or '\'')) return line.Label("[", "]", role, quotesInWords: true);

        var mark = line.Save();
        line.Open();
        line.Token("[", Roles.Open);

        if (Quoted(line, role) && line.Space().Token("]", Roles.Close))
        {
            line.Close(MermaidKinds.Label);
            return true;
        }

        // A string closing short of the bracket leaves the title its words, quotes and all: ["main" DB] says "main" DB, as Mermaid reads it.
        line.Restore(mark);
        return line.Label("[", "]", role, quotesInWords: true);
    }

    /// <summary>A title's string: the quote it opens with, what it says, and the same quote closing it — or a failure with the reason, where nothing closes it.</summary>
    private static bool Quoted(MermaidLine line, string role)
    {
        var written = line.Rest;
        var close = Closing(written, 0);
        if (close < 0) return line.Fail(NeverClosed);

        var at = line.Begins;
        var quote = written[..1];

        line.Open();
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Open, offset: at));
        line.Add(Said(written[1..close], at + 1, role));
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Close, offset: at + close));
        line.Close(MermaidKinds.Quoted);

        return true;
    }

    /// <summary>
    /// What is between a title's quotes, as the words it is written with — and, where a backslash keeps a character or the title runs
    /// across a line ending, those as pieces of their own between them, each an <see cref="ArchitectureKinds.Escape"/> or an
    /// <see cref="ArchitectureKinds.Break"/>, so whatever says what the title says finds them without looking at the characters
    /// (<see cref="ArchitectureStandIns"/>).
    /// </summary>
    private static ContentNode Said(string text, int at, string role)
    {
        if (text.AsSpan().IndexOfAny(Backslash, '\n', '\r') < 0) return ContentWords.Of(text, Kinds.Words, role, at: at);

        var pieces = new List<ContentNode>();
        var from = 0;

        for (var place = 0; place < text.Length;)
        {
            var (kind, length) = text[place] switch
            {
                // A \n is a line break, as it is in every diagram's words, so it stays in them for whatever breaks their lines to find.
                Backslash when place + 1 < text.Length && text[place + 1] == 'n' => (null, 2),
                Backslash => (ArchitectureKinds.Escape, 2),
                '\r' when place + 1 < text.Length && text[place + 1] == '\n' => (ArchitectureKinds.Break, 2),
                '\r' or '\n' => (ArchitectureKinds.Break, 1),
                _ => (null, 0),
            };

            if (kind is null)
            {
                place += Math.Max(1, length);
                continue;
            }

            if (place > from) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..place], role, offset: at + from));
            pieces.Add(ContentNode.Leaf(kind, text.Substring(place, length), offset: at + place));
            from = place += length;
        }

        if (from == 0) return ContentWords.Of(text, Kinds.Words, role, at: at);
        if (from < text.Length) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..], role, offset: at + from));

        return ContentNode.Branch(Kinds.Words, pieces, role, at);
    }

    /// <summary>
    /// The characters an id ends at: every printable character Mermaid's ids exclude, which is all of them but a letter, a digit, an
    /// underscore and a dash.
    /// </summary>
    private static readonly string Stops =
        new([.. Enumerable.Range(' ', '~' - ' ' + 1).Select(at => (char)at).Where(character => !MermaidLine.Letter(character))]);

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as an architecture diagram spells it: into a title in quotes, with what would end it kept; anywhere else, as every block written in Mermaid's syntax spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        QuotedOf(part) is { } quoted && Opened(quoted) is { } quote ? Escaped(text, quote) : MermaidParser.Spelled(part, text);

    /// <summary>The title in quotes <paramref name="part"/> is in — its words, a piece of them, or the hole in it — or null where it is in none.</summary>
    public static ContentPart? QuotedOf(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Quoted && at.Parent?.Kind == MermaidKinds.Label) return at;

        return null;
    }

    /// <summary>The quote <paramref name="quoted"/> is written in.</summary>
    public static char? Opened(ContentPart quoted) => quoted.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text[0];

    /// <summary>
    /// <paramref name="text"/> as a title in <paramref name="quote"/> holds it: a backslash kept by a backslash, since one starts an
    /// escape; a line break as the mark Mermaid's syntax writes one with; and in double quotes a quote as its entity code, as in any
    /// diagram (<see cref="MermaidParser.Spelled"/>), in single quotes that quote kept by a backslash.
    /// </summary>
    public static string Escaped(string text, char quote)
    {
        var kept = text.Replace(Backslash.ToString(), @"\\", StringComparison.Ordinal).ReplaceLineEndings(MermaidParser.LineBreak);
        return quote == '"' ? MermaidText.Quoted(kept) : kept.Replace(quote.ToString(), $"{Backslash}{quote}", StringComparison.Ordinal);
    }
}
