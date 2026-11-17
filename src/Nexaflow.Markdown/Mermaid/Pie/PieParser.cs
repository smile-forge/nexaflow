using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>
/// Reads a pie chart — a <c>pie</c> block: <c>showData</c> and a title after the keyword, a <c>title</c> of its own, and a slice per
/// line, a label in quotes, a colon and a number — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a pie. The header is optional, since a <c>pie</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A slice's label is a string as its newer grammars write one: in double quotes or single, a backslash
/// keeping the character after it — a quote that would end it, a backslash, a letter naming a control character — and running
/// across a line ending wherever its closing quote stands. So a slice is as much of the source as its label takes, and a <c>%%</c>
/// inside the label is the label's. A quote nothing closes is a character like any other, so one typed by mistake costs the line it
/// is on rather than everything under it.
/// </para>
/// <para>
/// A value is a number of nought or more, and slices are drawn clockwise in the order they are written. A slice worth nought is a
/// slice like any other that takes none of the chart. A value that is not a number, or is less than nought, is still read as a
/// slice, with the reason on the number — the label is what the reader is looking at, and hiding the whole line because its value
/// is wrong would leave nothing to fix. A value not yet written is no complaint at all: it is a slice still being written. What a
/// slice is drawn in, and its share of the whole, are facts about every slice the block writes and its front matter, so they are
/// worked out by the stages a pie's language offers the engine (<see cref="Stages.ResolveSlices"/>, <see cref="Stages.ResolveShares"/>).
/// </para>
/// </summary>
public sealed class PieParser : ITranspile
{
    private PieParser()
    {
    }

    /// <summary>The language a pie is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "pie";

    /// <summary>The words a pie's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    /// <summary>The word after <c>pie</c> that puts each slice's value in the legend.</summary>
    public const string ShowData = "showData";

    /// <summary>What keeps the character after it inside a label.</summary>
    public const char Backslash = '\\';

    /// <summary>What opens a comment running to the end of its row, outside a label.</summary>
    private const string Comment = "%%";

    private const string SliceShape = "A pie slice is a label in quotes, a colon and a number: \"Calcium\" : 42.96.";
    private const string ColonShape = "A pie slice needs a colon between its label and its value.";
    private const string NeverClosed = "This label is never closed with the quote it opens with.";
    private const string HeaderShape = "A pie takes showData and a title after it: pie showData title Key elements.";

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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a label's quotes, and after a slice's colon.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Quoted or MermaidKinds.Amount;

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

        // A binding standing on a line of its own after the header is slices supplied by whatever the pie is shown against. Held as
        // written; read into its place before the pie is worked over.
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

        // A title is the rest of its row whatever it holds; a title with nothing written after it yet is held whole.
        if (MermaidLine.Keyword(text, MermaidLine.TitleWord) is not null)
        {
            var titled = MermaidLine.Of(source[from..row.End], from).Title();
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [titled ?? ContentNode.Leaf(MermaidKinds.Statement, text, offset: from)], titled is null ? to : from + titled.Width, row));
            return row.Stop;
        }

        // A slice is as much of the source as its label takes. As much of that as is read is what was written, and the rest is the line's.
        var said = Slice(MermaidLine.Of(source[from..Ended(source, from)], from, CommentAt));
        var last = MermaidParser.Row.Containing(source, Math.Max(from, from + said.Width - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from, [said], from + said.Width, last));
        return last.Stop;
    }

    /// <summary>
    /// Where the slice starting at <paramref name="from"/> ends: the end of the row a line ending outside a label first ends, or the
    /// end of the row a comment is written on — what is after <c>%%</c> is the comment's, quotes and all.
    /// </summary>
    private static int Ended(string source, int from)
    {
        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n' || Commenting(source, at)) return MermaidParser.Row.Containing(source, at).End;
            if (character is not ('"' or '\'')) continue;

            var close = Closing(source, at);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>Where a comment closing a slice starts, outside its label — or -1.</summary>
    private static int CommentAt(string text)
    {
        for (var at = 0; at < text.Length; at++)
        {
            if (Commenting(text, at)) return at;
            if (text[at] is not ('"' or '\'')) continue;

            var close = Closing(text, at);
            if (close >= 0) at = close;
        }

        return -1;
    }

    /// <summary>Whether a comment opens at <paramref name="at"/>.</summary>
    private static bool Commenting(string text, int at) => text.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal);

    /// <summary>
    /// Where the label opening with the quote at <paramref name="open"/> closes — past every character a backslash keeps — or -1
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
    /// The header — <c>pie</c>, with <c>showData</c> and a title after it where they are written — or null where the line says
    /// something else, which a <c>pie</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Options(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>What follows the keyword: <c>showData</c>, a title after it — and anything else held as written, with the reason.</summary>
    private static ContentNode Options(string arguments, int at)
    {
        // A comment after them is theirs, and a title stops where one starts, as Mermaid's lexer reads them.
        var line = MermaidLine.Of(arguments, at);
        if (line.Word(ShowData, PieKinds.ShowData)) line.Space();

        if (!line.Done && !line.Then((rest, from) => MermaidLine.Of(rest, from).Title()))
            line.Held(HeaderShape);

        return line.Read(PieKinds.Options, MermaidRoles.Arguments);
    }

    // ── Slices ──────────────────────────────────────────────────────────────

    /// <summary>
    /// One slice: <c>"Calcium" : 42.96</c> — or <c>"Calcium" : </c>, still to be given its value, which stands after the space left
    /// for it: that is where typing it will put it. Anything that is not the shape of one is held as written, with the reason.
    /// </summary>
    private static ContentNode Slice(MermaidLine line)
    {
        if (!Labelled(line)) return line.Shown(SliceShape);

        line.Space();
        if (!line.Token(":")) return line.Shown(ColonShape);

        line.Room();
        line.Amount(PieRoles.Value, MermaidNumber.Where(number => number >= 0, "A pie slice cannot be worth less than nought."));
        return line.Read(PieKinds.Slice);
    }

    /// <summary>
    /// A label: the quote it opens with, what it says, and the same quote closing it. False where no label opens here, with the
    /// reason where nothing closes the one that does.
    /// </summary>
    private static bool Labelled(MermaidLine line)
    {
        if (line.Next is not ('"' or '\'')) return false;

        var written = line.Rest;
        var close = Closing(written, 0);
        if (close < 0) return line.Fail(NeverClosed);

        var at = line.Begins;
        var quote = written[..1];

        line.Open();
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Open, offset: at));
        line.Add(Said(written[1..close], at + 1));
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Close, offset: at + close));
        line.Close(MermaidKinds.Quoted);

        return true;
    }

    /// <summary>
    /// What is between a label's quotes, as the words it is written with — and, where a backslash keeps a character or the label
    /// runs across a line ending, those as pieces of their own between them, each a <see cref="PieKinds.Escape"/> or a
    /// <see cref="PieKinds.Break"/>, so whatever says what the label says finds them without looking at the characters
    /// (<see cref="PieStandIns"/>).
    /// </summary>
    private static ContentNode Said(string text, int at)
    {
        if (text.AsSpan().IndexOfAny(Backslash, '\n', '\r') < 0) return ContentWords.Of(text, Kinds.Words, PieRoles.Label, at: at);

        var pieces = new List<ContentNode>();
        var from = 0;

        for (var place = 0; place < text.Length;)
        {
            var (kind, length) = text[place] switch
            {
                // A \n is a line break, as it is in every diagram's words, so it stays in them for whatever breaks their lines to find.
                Backslash when place + 1 < text.Length && text[place + 1] == 'n' => (null, 2),
                Backslash => (PieKinds.Escape, 2),
                '\r' when place + 1 < text.Length && text[place + 1] == '\n' => (PieKinds.Break, 2),
                '\r' or '\n' => (PieKinds.Break, 1),
                _ => (null, 0),
            };

            if (kind is null)
            {
                place += Math.Max(1, length);
                continue;
            }

            if (place > from) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..place], PieRoles.Label, offset: at + from));
            pieces.Add(ContentNode.Leaf(kind, text.Substring(place, length), offset: at + place));
            from = place += length;
        }

        if (from == 0) return ContentWords.Of(text, Kinds.Words, PieRoles.Label, at: at);
        if (from < text.Length) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..], PieRoles.Label, offset: at + from));

        return ContentNode.Branch(Kinds.Words, pieces, PieRoles.Label, at);
    }

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a pie spells it: into a label, with what would end it kept by a backslash; anywhere else, as every block written in Mermaid's syntax spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        LabelOf(part) is { } label && Opened(label) is { } quote ? Escaped(text, quote) : MermaidParser.Spelled(part, text);

    /// <summary>The label <paramref name="part"/> is in — its words, a piece of them, or the hole in it — or null where it is in none.</summary>
    public static ContentPart? LabelOf(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Quoted) return at;

        return null;
    }

    /// <summary>The quote <paramref name="label"/> is written in.</summary>
    public static char? Opened(ContentPart label) => label.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text[0];

    /// <summary>
    /// <paramref name="text"/> as a label in <paramref name="quote"/> holds it: a backslash kept by a backslash, since one starts an
    /// escape; a line break as the mark Mermaid's syntax writes one with; and in double quotes a quote as its entity code, as in
    /// any diagram (<see cref="MermaidParser.Spelled"/>), in single quotes that quote kept by a backslash.
    /// </summary>
    public static string Escaped(string text, char quote)
    {
        var kept = text.Replace(Backslash.ToString(), @"\\", StringComparison.Ordinal).ReplaceLineEndings(MermaidParser.LineBreak);
        return quote == '"' ? MermaidText.Quoted(kept) : kept.Replace(quote.ToString(), $"{Backslash}{quote}", StringComparison.Ordinal);
    }
}
