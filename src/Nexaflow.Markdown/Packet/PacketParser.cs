using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Packet;

/// <summary>
/// Reads a packet: the fields of a network packet or a register, each its bits and a label — <c>0-15: "Source Port"</c>, one
/// bit as <c>106: "URG"</c>, or as many bits as a count says, starting where the field before it ended, as <c>+16: "Length"</c>.
///
/// <para>
/// A packet is a language of its own: a <c>packet</c> fence names it, and a <c>mermaid</c> block whose header is <c>packet</c> is
/// handed to it whole (<see cref="MermaidFenceParser"/>). It is written Mermaid's way, so what every such block holds round what
/// it says — front matter, <c>%%</c> comments, directives, <c>accTitle</c> and <c>accDescr</c>, the space between lines — is
/// read with Mermaid's own pieces (<see cref="MermaidParser"/>), and what is read comes out in the shapes every one of them
/// shares, so the block's title and front matter are found the same way. The header is optional here, since the fence already
/// says what the block is; written, it is <c>packet</c> or <c>packet-beta</c> and may carry the title after it.
/// </para>
/// <para>
/// What the lines say is this parser's own, and is read from the characters rather than line by line, because a label is a
/// string as Mermaid's newer grammars write one: in double quotes or single, a backslash keeping the character after it — a
/// quote that would end it, a backslash, a letter naming a control character — and running across a line ending wherever its
/// closing quote stands. A <c>%%</c> inside it is part of it rather than a comment. A quote nothing closes is a character like
/// any other, so one typed by mistake costs the line it is on rather than everything under it.
/// </para>
/// <para>
/// This reads what is written and nothing of what it means: a bit number is a whole number as written, with no noughts before
/// it, and whether a field starts where the one before it ended, ends after it starts or takes any bits at all is its stages'
/// to say (<see cref="Stages.PlaceFields"/>).
/// </para>
/// </summary>
public sealed class PacketParser : ITranspile
{
    private PacketParser()
    {
    }

    /// <summary>The language a packet is written in: what a fence names it by, and what a <c>mermaid</c> block headed by it is held as.</summary>
    public const string Language = "packet";

    /// <summary>The words a packet's header is written with — Mermaid's own, and the one it wrote while the diagram was new.</summary>
    public static readonly string[] Keywords = ["packet-beta", Language];

    /// <summary>What a field counting its bits starts with.</summary>
    public const string Plus = "+";

    /// <summary>What stands between the bit a field starts at and the bit it ends at.</summary>
    public const string Dash = "-";

    /// <summary>What stands between a field's bits and its label.</summary>
    public const string Colon = ":";

    /// <summary>What keeps the character after it inside a label.</summary>
    public const char Backslash = '\\';

    /// <summary>What opens a comment running to the end of its line, outside a label.</summary>
    private const string Comment = "%%";

    private const string FieldShape = "A field is its bits, a colon and its label in quotes: 0-15: \"Source Port\", 106: \"URG\" or +16: \"Length\".";
    private const string ColonShape = "A field's label follows a colon after its bits: 0-15: \"Source Port\".";
    private const string LabelShape = "A field's label is written in quotes: 0-15: \"Source Port\".";
    private const string HeaderShape = "A packet's header takes only its title after it: packet title TCP Packet.";
    private const string NeverClosed = "This label is never closed with the quote it opens with.";

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
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a label's quotes, and after a
    /// field's colon where its label is still to write. Never in a bit number, which is not written in where it is drawn.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is PacketKinds.Label;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The next line of the block, read and added to <paramref name="lines"/> — or the lines it takes — and where the one after it starts.</summary>
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

        // The statement, which is as much of the source as it takes rather than as much as the row holds — a label runs to its
        // closing quote, where a title is the rest of its row whatever it holds. As much of that as is read is what was written,
        // and the rest is the line's.
        var end = MermaidLine.Keyword(text, MermaidLine.TitleWord) is not null ? row.End : Ended(source, from);
        var said = Statement(source[from..end], from);
        var last = MermaidParser.Row.Containing(source, Math.Max(from, from + said.Width - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from, [said], from + said.Width, last));
        return last.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending outside a label first
    /// ends, or the end of the row a comment is written on — what is after <c>%%</c> is the comment's, quotes and all.
    /// </summary>
    private static int Ended(string source, int from)
    {
        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n') return MermaidParser.Row.Containing(source, at).End;
            if (character == '%' && at + 1 < source.Length && source[at + 1] == '%') return MermaidParser.Row.Containing(source, at).End;
            if (character is not ('"' or '\'')) continue;

            var close = Closing(source, at);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>
    /// Where the label opening with the quote at <paramref name="open"/> closes — past every character a backslash keeps — or
    /// -1 where nothing closes it.
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
    /// The header — <c>packet</c> or <c>packet-beta</c>, a title after it where one is written — or null where the line says
    /// something else, which a packet fence needs no header before.
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
            pieces.Add(said.StartsWith(Comment, StringComparison.Ordinal) ? ContentNode.Leaf(Kinds.Comment, said, Roles.Trivia, offset: after)
                       : Titled(said, after) ?? ContentNode.Shown(said, HeaderShape, offset: after));
        }

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>A title, or a field — which is what everything else is meant to be.</summary>
    private static ContentNode Statement(string text, int at) =>
        Titled(text, at) ?? Field(text, at);

    /// <summary>
    /// A title: its word and what it says to the end of the line, or to a comment — or still nothing, being written. Null where
    /// the line does not start with the word.
    /// </summary>
    private static ContentNode? Titled(string text, int at)
    {
        var line = MermaidLine.Of(text, at);
        if (!line.Word(MermaidLine.TitleWord)) return null;

        line.Space();
        if (!line.Done) line.Words(MermaidRoles.Title);

        return line.Read(MermaidKinds.Title);
    }

    /// <summary>
    /// A field: <c>0-15: "Source Port"</c>, <c>106: "URG"</c> or <c>+16: "Length"</c> — space allowed between any two of its
    /// pieces — and a comment after it. A bit number still to write is no complaint, and neither is a label: a hole stands where
    /// each goes. Anything that is not the shape of one is held as written, with the shape it should have.
    /// </summary>
    private static ContentNode Field(string text, int at)
    {
        var line = MermaidLine.Of(text, at, comments: false);

        if (!(char.IsAsciiDigit(line.Next) || line.Next is '+' or '-' or ':')) return line.Shown(FieldShape);

        if (line.Token(Plus))
        {
            line.Space();
            line.Amount(PacketRoles.Bits, Whole, until: Colon);
        }
        else
        {
            line.Amount(PacketRoles.Start, Whole, until: Dash + Colon);
            line.Space();

            if (line.Token(Dash))
            {
                line.Space();
                line.Amount(PacketRoles.End, Whole, until: Colon);
            }
        }

        line.Space();
        if (!line.Token(Colon)) return line.Shown(ColonShape);

        line.Room();
        if (!Labelled(line)) return line.Shown(LabelShape);

        line.Space();
        if (line.Sees(Comment)) line.Add(ContentNode.Leaf(Kinds.Comment, line.Rest, Roles.Trivia, offset: line.Begins));

        return line.Read(PacketKinds.Field);
    }

    /// <summary>
    /// What is wrong with a bit number as it is written — null where it is a whole number with no noughts before it, as Mermaid
    /// writes one.
    /// </summary>
    private static string? Whole(string number) =>
        !number.All(char.IsAsciiDigit) ? $"A bit is numbered with a whole number, such as 0, 7 or 31: {number} is not one."
        : number.Length > 1 && number[0] == '0' ? $"A bit number is written with no noughts before it: {Bare(number)}, not {number}."
        : !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? $"Bit {number} is past the last bit a packet can number."
        : null;

    /// <summary>A bit number without the noughts written before it — nought, where it is nothing else.</summary>
    private static string Bare(string number) => number.TrimStart('0') is { Length: > 0 } bare ? bare : "0";

    /// <summary>
    /// A label: the quote it opens with, what it says, and the same quote closing it — or, where nothing is written after the
    /// colon yet, a label still to write. False where anything else is written there, with the reason where a label was opened.
    /// </summary>
    private static bool Labelled(MermaidLine line)
    {
        var at = line.Begins;

        if (line.Done)
        {
            line.Add(ContentNode.Branch(PacketKinds.Label, [ContentNode.Leaf(Kinds.Words, string.Empty, PacketRoles.Label, offset: at)], offset: at));
            return true;
        }

        if (line.Next is not ('"' or '\'')) return false;

        var written = line.Rest;
        var close = Closing(written, 0);
        if (close < 0) return line.Fail(NeverClosed);

        var quote = written[..1];
        line.Open();
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Open, offset: at));
        line.Add(Said(written[1..close], at + 1));
        line.Add(ContentNode.Leaf(Kinds.Token, quote, Roles.Close, offset: at + close));
        line.Close(PacketKinds.Label);

        return true;
    }

    /// <summary>
    /// What is between a label's quotes, as the words it is written with — and, where a backslash keeps a character or the label
    /// runs across a line ending, those as pieces of their own between them, each a <see cref="PacketKinds.Escape"/> or a
    /// <see cref="PacketKinds.Break"/>, so whatever says what the label says finds them without looking at the characters.
    /// </summary>
    private static ContentNode Said(string text, int at)
    {
        if (text.AsSpan().IndexOfAny(Backslash, '\n', '\r') < 0) return ContentNode.Leaf(Kinds.Words, text, PacketRoles.Label, offset: at);

        var pieces = new List<ContentNode>();
        var from = 0;

        for (var place = 0; place < text.Length;)
        {
            var (kind, length) = text[place] switch
            {
                Backslash => (PacketKinds.Escape, 2),
                '\r' when place + 1 < text.Length && text[place + 1] == '\n' => (PacketKinds.Break, 2),
                '\r' or '\n' => (PacketKinds.Break, 1),
                _ => (null, 0),
            };

            if (kind is null)
            {
                place++;
                continue;
            }

            if (place > from) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..place], PacketRoles.Label, offset: at + from));
            pieces.Add(ContentNode.Leaf(kind, text.Substring(place, length), offset: at + place));
            from = place += length;
        }

        if (from < text.Length) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..], PacketRoles.Label, offset: at + from));

        return ContentNode.Branch(Kinds.Words, pieces, PacketRoles.Label, at);
    }

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>
    /// A change as a packet spells it: into a label, with what would end it kept by a backslash; anywhere else, as every block
    /// written Mermaid's way spells it.
    /// </summary>
    private static string? Spelled(ContentPart part, string text) =>
        LabelOf(part) is { } label ? Opened(label) is { } quote ? Escaped(text, quote) : Opening + Escaped(text, Opening) + Opening

        : MermaidParser.Spelled(part, text);

    /// <summary>The quote a label still to write is put in once something is written into it.</summary>
    public const char Opening = '"';

    /// <summary>The label <paramref name="part"/> is in — its words, a piece of them, or the hole in it — or null where it is in none.</summary>
    public static ContentPart? LabelOf(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == PacketKinds.Label) return at;

        return null;
    }

    /// <summary>The quote <paramref name="label"/> is written in, or null for a label still to write, which has none yet.</summary>
    public static char? Opened(ContentPart label) => label.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text[0];

    /// <summary>
    /// <paramref name="text"/> as a label in <paramref name="quote"/> holds it: a backslash and that quote kept by a backslash,
    /// and a line ending as the space Mermaid draws one as — a label is one line wherever it is drawn.
    /// </summary>
    public static string Escaped(string text, char quote) =>
        text.Replace(Backslash.ToString(), @"\\", StringComparison.Ordinal)
            .Replace(quote.ToString(), $"{Backslash}{quote}", StringComparison.Ordinal)
            .ReplaceLineEndings(" ");
}
