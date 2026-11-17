using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Cynefin;

/// <summary>
/// Reads a Cynefin framework — a <c>cynefin-beta</c> block: a <c>title</c>, the five sense-making domains, the items placed in each,
/// and the transitions between them — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a Cynefin framework. The header is optional, since a <c>cynefin</c> fence already says what the block is.
/// </para>
/// <para>
/// A domain is opened by its one word on a line of its own — <c>clear</c>, <c>complicated</c>, <c>complex</c>, <c>chaotic</c>,
/// <c>confusion</c> — and every line under it is an item sitting in it. A line starting with a domain's word and saying more than it
/// is an item too, so <c>clear enough</c> says what it says; only <c>--&gt;</c> after the word makes the line a transition:
/// <c>complex --&gt; complicated : "Pattern found"</c>, its label optional.
/// </para>
/// <para>
/// What an item or a label says is a string as Mermaid's grammar writes one: in double quotes or single, a backslash keeping the
/// character after it, and running across a line ending to its closing quote — so an item, or a transition, is as much of the
/// source as its string takes. Beyond Mermaid, an item may be written bare to the end of its line, where a quote is a character
/// like any other: a string opens only where Mermaid's grammar puts one, at the start of an item or after a transition's colon.
/// </para>
/// <para>
/// An item sits in the domain opened above it, which is the order the lines are written in; one written before any domain is opened
/// sits in none, which is worked out over the block by a stage the language offers the engine (<see cref="Stages.ResolveDomains"/>).
/// </para>
/// </summary>
public sealed class CynefinParser : ITranspile
{
    private CynefinParser()
    {
    }

    /// <summary>The language a Cynefin framework is written in: what a fence names it by.</summary>
    public const string Language = "cynefin";

    /// <summary>The words a Cynefin framework's header is written with — Mermaid's own, and the language's name.</summary>
    public static readonly string[] Keywords = ["cynefin-beta", Language];

    /// <summary>What goes between the two domains of a transition.</summary>
    public const string Arrow = "-->";

    public const string Clear = "clear";
    public const string Complicated = "complicated";
    public const string Complex = "complex";
    public const string Chaotic = "chaotic";
    public const string Confusion = "confusion";

    /// <summary>The word opening each domain — the only five there are.</summary>
    public static readonly IReadOnlyList<string> Domains = [Clear, Complicated, Complex, Chaotic, Confusion];

    /// <summary>What keeps the character after it inside a string.</summary>
    public const char Backslash = '\\';

    /// <summary>What opens a comment running to the end of its row, outside a string.</summary>
    private const string Comment = "%%";

    private const string Moving = "A transition goes from one domain to another: complex --> complicated : \"Pattern found\".";
    private const string Saying = "An item is what it says, in quotes: \"Investigate root cause\".";
    private const string NeverClosed = "This is never closed with the quote it opens with.";

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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where what an item or a transition says is still to write, between its quotes.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind == CynefinKinds.Text;

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

        // A binding standing on a line of its own after the header is lines supplied by whatever the framework is shown against.
        // Held as written; read into its place before the framework is worked over.
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

        // The statement, which is as much of the source as its string takes, where it has one; a title is the rest of its row. As
        // much of it as is read is what was written, and the rest is the line's, unless another statement starts on its row; a title
        // with nothing after it yet is held whole.
        var end = MermaidLine.Keyword(text, MermaidLine.TitleWord) is not null ? row.End : Ended(source, from, row);
        var said = Statement(source[from..end], from);
        if (said is not null) return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);

        var last = MermaidParser.Row.Containing(source, Math.Max(from, end - 1));
        lines.Add(MermaidParser.Line(source, row.Start, from, [ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from)], end, last));
        return last.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: past the string it holds, which may run on over line endings, or
    /// at the end of its row. Mermaid reads strings one after another, and after a domain's word, as items of that domain — complex
    /// "Probe" "Sense" — so a string, or a domain's word, with a string after it on its row is a statement of its own.
    /// </summary>
    private static int Ended(string source, int from, MermaidParser.Row row)
    {
        var text = source[from..row.End];

        if (text.Length > 0 && Opens(text[0]))
        {
            var shut = Closing(source, from);
            if (shut < 0) return row.End;

            return Followed(source, shut + 1) ? shut + 1 : MermaidParser.Row.Containing(source, shut).End;
        }

        if (MermaidLine.Keyword(text, Letter, [.. Domains]) is { } word && Followed(source, from + word.Length)) return from + word.Length;

        var open = Opening(text);
        if (open < 0 || Commented(text, open)) return row.End;

        var close = Closing(source, from + open);
        return close < 0 ? row.End : MermaidParser.Row.Containing(source, close).End;
    }

    /// <summary>Whether a string opens after <paramref name="at"/> on its row, past the space before it.</summary>
    private static bool Followed(string source, int at)
    {
        while (at < source.Length && source[at] is ' ' or '\t') at++;
        return at < source.Length && Opens(source[at]);
    }

    /// <summary>
    /// Where the string a statement holds opens, in <paramref name="text"/> — at the start of an item, or after a transition's colon —
    /// or -1 where it holds none.
    /// </summary>
    private static int Opening(string text)
    {
        if (text.Length > 0 && Opens(text[0])) return 0;
        if (MermaidLine.Keyword(text, Letter, [.. Domains]) is null) return -1;

        var arrow = text.IndexOf(Arrow, StringComparison.Ordinal);
        var colon = arrow < 0 ? -1 : text.IndexOf(':', arrow + Arrow.Length);
        if (colon < 0) return -1;

        var at = colon + 1;
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        return at < text.Length && Opens(text[at]) ? at : -1;
    }

    /// <summary>Whether a comment opens in <paramref name="text"/> before <paramref name="end"/>.</summary>
    private static bool Commented(string text, int end) => text.AsSpan(0, end).IndexOf(Comment, StringComparison.Ordinal) >= 0;

    /// <summary>
    /// Where a comment closing a statement starts — outside the string it holds, where it holds one — or -1. Handed to
    /// <see cref="MermaidLine"/>, which keeps what follows it as the line's comment.
    /// </summary>
    private static int CommentAt(string text)
    {
        var open = Opening(text);
        var close = open < 0 ? -1 : Closing(text, open);

        if (close < 0) return text.IndexOf(Comment, StringComparison.Ordinal);

        var before = text.IndexOf(Comment, 0, open, StringComparison.Ordinal);
        return before >= 0 ? before : text.IndexOf(Comment, close + 1, StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="character"/> opens a string.</summary>
    private static bool Opens(char character) => character is '"' or '\'';

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
    /// The header — <c>cynefin-beta</c>, and anything after it held as written — or null where the line says something else,
    /// which a <c>cynefin</c> fence needs no header before.
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

    /// <summary>A title, a domain's line, a transition or an item — or null for a title with nothing written after it yet.</summary>
    private static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at, CommentAt);

        if (MermaidLine.Keyword(line.Written, MermaidLine.TitleWord) is not null) return MermaidLine.Of(text, at).Title();
        if (MermaidLine.Keyword(line.Written, Letter, [.. Domains]) is { } word && Opens(line, word) is { } read) return read;

        // Anything else on a line is what it says, in the domain opened above it — the reading starts again, having taken nothing.
        return Item(MermaidLine.Of(text, at, CommentAt));
    }

    /// <summary>
    /// A domain's line, where its word is all that is written, or a transition from that domain — and null where the line says
    /// something else, which makes it an item.
    /// </summary>
    private static ContentNode? Opens(MermaidLine line, string word)
    {
        line.Word(word, letter: Letter);
        line.Space();
        if (line.Done) return line.Read(CynefinKinds.Domain);
        if (!line.Sees(Arrow)) return null;

        line.Token(Arrow);
        line.Room();

        // The domain it goes to is still to write.
        if (line.Done) return line.Read(CynefinKinds.Move);

        if (MermaidLine.Keyword(line.Rest, Letter, [.. Domains]) is not { } to) return line.Shown(Moving);

        line.Word(to, letter: Letter);
        line.Space();
        if (line.Done) return line.Read(CynefinKinds.Move);

        if (!line.Token(":")) return line.Shown(Moving);

        line.Room();
        if (!line.Done && !Text(line, CynefinRoles.Label)) return line.Shown(Moving);

        return line.Done ? line.Read(CynefinKinds.Move) : line.Shown(Moving);
    }

    /// <summary>What carries a domain's word on: no domain's word holds a hyphen, so an arrow hard against it closes it.</summary>
    private static bool Letter(char character) => char.IsLetterOrDigit(character) || character == '_';

    /// <summary>An item: what it says, in quotes or bare to the end of its line.</summary>
    private static ContentNode Item(MermaidLine line)
    {
        if (!Text(line, CynefinRoles.Says)) return line.Shown(Saying);

        line.Space();
        return line.Done ? line.Read(CynefinKinds.Item) : line.Shown(Saying);
    }

    /// <summary>Text as a piece of its own: a string, or bare to where the line ends — false where its quote is never closed.</summary>
    private static bool Text(MermaidLine line, string role)
    {
        line.Open();

        if (Opens(line.Next))
        {
            if (!Quoted(line, role)) return false;
        }
        else
        {
            line.Words(role);
        }

        line.Close(CynefinKinds.Text, role);
        return true;
    }

    /// <summary>A string: the quote it opens with, what it says, and the same quote closing it — or a failure with the reason, where nothing closes it.</summary>
    private static bool Quoted(MermaidLine line, string role)
    {
        var written = line.Rest;
        var close = Closing(written, 0);
        if (close < 0) return line.Fail(NeverClosed);

        var at = line.Begins;
        var quote = written[..1];

        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Open, offset: at));
        line.Add(Said(written[1..close], at + 1, role));
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Close, offset: at + close));

        return true;
    }

    /// <summary>
    /// What is between a string's quotes, as the words it is written with — and, where a backslash keeps a character or the string
    /// runs across a line ending, those as pieces of their own between them, each a <see cref="CynefinKinds.Escape"/> or a
    /// <see cref="CynefinKinds.Break"/>, so whatever says what the string says finds them without looking at the characters
    /// (<see cref="CynefinStandIns"/>).
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
                Backslash => (CynefinKinds.Escape, 2),
                '\r' when place + 1 < text.Length && text[place + 1] == '\n' => (CynefinKinds.Break, 2),
                '\r' or '\n' => (CynefinKinds.Break, 1),
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

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a Cynefin framework spells it: into a string, with what would end it kept; anywhere else, as every block written in Mermaid's syntax spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        StringOf(part) is { } written && Opened(written) is { } quote ? Escaped(text, quote) : MermaidParser.Spelled(part, text);

    /// <summary>What an item or a label says in quotes that <paramref name="part"/> is in — its words, a piece of them, or the hole in it — or null where it is in none.</summary>
    public static ContentPart? StringOf(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == CynefinKinds.Text) return Opened(at) is null ? null : at;

        return null;
    }

    /// <summary>The quote <paramref name="written"/> is in, or null where it is written bare.</summary>
    public static char? Opened(ContentPart written) =>
        written.Children.FirstOrDefault(child => child.Role == Roles.Open && child.Text.Length == 1 && Opens(child.Text[0]))?.Text[0];

    /// <summary>
    /// <paramref name="text"/> as a string in <paramref name="quote"/> holds it: a backslash kept by a backslash, since one starts an
    /// escape; a line break as the mark Mermaid's syntax writes one with; and in double quotes a quote as its entity code, as in any
    /// diagram (<see cref="MermaidParser.Spelled"/>), in single quotes that quote kept by a backslash.
    /// </summary>
    public static string Escaped(string text, char quote)
    {
        var kept = text.Replace(Backslash.ToString(), @"\\", StringComparison.Ordinal).ReplaceLineEndings(MermaidParser.LineBreak);
        return quote == '"' ? MermaidText.Quoted(kept) : kept.Replace(quote.ToString(), $"{Backslash}{quote}", StringComparison.Ordinal);
    }
}
