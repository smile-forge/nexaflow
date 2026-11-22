using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Quadrant;

/// <summary>
/// Reads a quadrant chart — a <c>quadrantChart</c> block: a <c>title</c>, the <c>x-axis</c> and <c>y-axis</c> ends, the
/// <c>quadrant-1</c>…<c>quadrant-4</c> captions, the points, and the <c>classDef</c>s that style them — and says how what is
/// written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds
/// round any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between
/// lines — is read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in
/// Mermaid's syntax rather than about a quadrant chart. The header is optional, since a <c>quadrant</c> fence already says what
/// the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. An axis is what its low end says, then <c>--&gt;</c> and what its high end says: <c>x-axis Low
/// Reach --&gt; High Reach</c>. A point is its name, a colon, and where it stands from 0 to 1 across and up —
/// <c>Campaign A: [0.3, 0.6]</c> — its class after <c>:::</c> before the colon, and its style after its position:
/// <c>radius</c>, <c>color</c>, <c>stroke-color</c> and <c>stroke-width</c>. Text runs bare to where it ends, or is written in
/// quotes, and what is in quotes runs to its closing quote wherever that stands — across a line ending too, as Mermaid's own
/// lexer takes it. A quote nothing closes is a character like any other, so one typed by mistake costs the line it is on rather
/// than everything under it. A title runs to the end of its line.
/// </para>
/// <para>
/// Where each point stands and what it is drawn with — its class, wherever that is written — is a fact about the block, not the
/// point's line, so it is a stage's (<see cref="Stages.ResolvePoints"/>), which a quadrant chart's language offers the engine.
/// This reads what each line says, through <see cref="MermaidLine"/>, and holds what it cannot read as written with the reason.
/// </para>
/// </summary>
public sealed class QuadrantParser : ITranspile
{
    private QuadrantParser()
    {
    }

    /// <summary>The language a quadrant chart is written in: what a fence names it by, and what a <c>mermaid</c> block headed by it is held as.</summary>
    public const string Language = "quadrant";

    /// <summary>The words a quadrant chart's header is written with — Mermaid's own, and the language's name.</summary>
    public static readonly string[] Keywords = ["quadrantChart", Language];

    public const string XAxis = "x-axis";
    public const string YAxis = "y-axis";
    public const string ClassDef = "classDef";
    public const string Arrow = "-->";
    public const string ClassMark = ":::";

    /// <summary>The words starting a quadrant's line, the first quadrant's first.</summary>
    public static readonly IReadOnlyList<string> Regions = ["quadrant-1", "quadrant-2", "quadrant-3", "quadrant-4"];

    /// <summary>What a point's or a class's style can set.</summary>
    public static readonly IReadOnlyList<string> Styles = ["radius", "color", "stroke-color", "stroke-width"];

    /// <summary>What opens a comment running to the end of its row, outside quotes.</summary>
    private const string Comment = "%%";

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
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where text or a class's name is still to
    /// write — an axis end, a caption, a point's name, between quotes.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is QuadrantKinds.Text or MermaidKinds.Name;

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a quadrant chart writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

    /// <summary>Whether a class's name can be written without quotes.</summary>
    internal static bool Bare(string name) => name.Length > 0 && name.All(Letter);

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

        // A binding standing on a line of its own after the header is lines supplied by whatever the chart is shown against.
        // Held as written; read into its place before the chart is worked over.
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
            // The header may be followed on its row by the chart's first statement — quadrantChart title Reach — as Mermaid reads it.
            var head = text[..Headed(text)];
            if (Heading(head, from) is { } header) return MermaidParser.StatementLine(source, row.Start, from, header, from + head.Length, lines);
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which is as much of the source as it takes rather than as much as the row holds — what is in quotes runs
        // to its closing quote, where a title is the rest of its row whatever it holds. As much of it as is read is what was
        // written, and the rest is the line's; a statement read as nothing yet — a title with nothing after it — is held whole.
        // A ; ends a statement, as Mermaid reads one, so what follows it on its row is another — but a title takes the rest of its row.
        if (text[0] == ';') return MermaidParser.StatementLine(source, row.Start, from, ContentNode.Leaf(Kinds.Token, ";", Roles.Separator, offset: from), from + 1, lines);

        var end = MermaidLine.Keyword(text, MermaidLine.TitleWord) is not null ? row.End : Ended(source, from);
        var said = Statement(source[from..end], from) ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from);

        return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending outside quotes first ends,
    /// or the end of the row a comment is written on — what is after <c>%%</c> is the comment's, quotes and all — or before a ;, which\n/// ends a statement as Mermaid reads one.
    /// </summary>
    private static int Ended(string source, int from)
    {
        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n') return MermaidParser.Row.Containing(source, at).End;
            if (character == '%' && at + 1 < source.Length && source[at + 1] == '%') return MermaidParser.Row.Containing(source, at).End;
            if (character == ';')
            {
                while (at > from && char.IsWhiteSpace(source[at - 1])) at--;
                return at;
            }

            if (character != '"') continue;

            var close = source.IndexOf('"', at + 1);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>Where the header written at the start of <paramref name="text"/> ends: after the keyword — or before a statement's word after it, which Mermaid reads as the chart's first statement.</summary>
    private static int Headed(string text)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return text.Length;

        var at = keyword.Length;
        while (at < text.Length && text[at] is ' ' or '\t') at++;

        if (at >= text.Length || MermaidLine.Keyword(text[at..], [MermaidLine.TitleWord, XAxis, YAxis, ClassDef, .. Regions]) is null) return text.Length;

        while (at > 0 && char.IsWhiteSpace(text[at - 1])) at--;
        return at;
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header — <c>quadrantChart</c>, whatever follows it on its line held as written, as it always has been — or null where
    /// the line says something else, which a <c>quadrant</c> fence needs no header before.
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
    /// One statement: a title, an axis, a caption, a class or a point — or the text held as written, with the reason. Null for a
    /// title with nothing written after it yet, which is held whole.
    /// </summary>
    private static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        return MermaidLine.Keyword(line.Written, [MermaidLine.TitleWord, XAxis, YAxis, ClassDef, .. Regions]) switch
        {
            MermaidLine.TitleWord => line.Title(),
            XAxis => Axis(line, XAxis),
            YAxis => Axis(line, YAxis),
            ClassDef => Class(line),
            { } region => Region(line, region),
            null => Point(line),
        };
    }

    /// <summary>An axis: <c>x-axis Low Reach --&gt; High Reach</c> — its high end left out where only its low end is named.</summary>
    private static ContentNode Axis(MermaidLine line, string word)
    {
        line.Word(word);
        if (!Spaced(line)) return line.Done ? line.Read(QuadrantKinds.Axis) : line.Shown("An axis names its low end, then --> and its high end: x-axis Low Reach --> High Reach.");

        // The arrow is two dashes or more and a head, as Mermaid reads it: Low ---> High.
        var arrow = Pointing(line.Written, line.At);
        if (!Text(line, QuadrantRoles.Low, end: arrow?.At)) return line.Shown("This text is never closed with a quote.");

        line.Space();
        if (arrow is { } drawn && line.Token(line.Written[drawn.At..(drawn.At + drawn.Length)]))
        {
            line.Room();
            if (!Text(line, QuadrantRoles.High)) return line.Shown("This text is never closed with a quote.");
        }

        return line.Done ? line.Read(QuadrantKinds.Axis) : line.Shown("An axis names its low end, then --> and its high end: x-axis Low Reach --> High Reach.");
    }

    /// <summary>Where an axis's arrow starts, past <paramref name="at"/>, and how long it is: two dashes or more and a head — or null where none is written.</summary>
    private static (int At, int Length)? Pointing(string text, int at)
    {
    if (at >= text.Length) return null;

        for (var start = text.IndexOf("--", at, StringComparison.Ordinal); start >= 0; start = text.IndexOf("--", start + 1, StringComparison.Ordinal))
        {
            var end = start;
            while (end < text.Length && text[end] == '-') end++;

            if (end < text.Length && text[end] == '>') return (start, end + 1 - start);
            start = end - 1;
        }

        return null;
    }

    /// <summary>A quadrant's caption: <c>quadrant-1 We should expand</c>.</summary>
    private static ContentNode Region(MermaidLine line, string word)
    {
        line.Word(word);
        if (!Spaced(line)) return line.Shown($"A quadrant's line is its word and what it says: {word} We should expand.");

        return Text(line, QuadrantRoles.Caption) && line.Done
            ? line.Read(QuadrantKinds.Region)
            : line.Shown($"A quadrant's line is its word and what it says: {word} We should expand.");
    }

    /// <summary>A class: <c>classDef important color: #ff3300, radius: 10</c> — its style still to write where none is yet.</summary>
    private static ContentNode Class(MermaidLine line)
    {
        const string Shape = "A classDef names its class, then its style: classDef important color: #ff3300, radius: 10.";

        line.Word(ClassDef);
        if (!Spaced(line) || line.Done || !Id(line, QuadrantRoles.Class)) return line.Shown(Shape);

        if (!line.Done)
        {
            var at = line.At;
            line.Space();
            if (line.At == at || !line.Properties(Styles)) return line.Shown(Shape);
        }

        return line.Read(QuadrantKinds.Class);
    }

    /// <summary>
    /// A point: <c>Campaign A: [0.3, 0.6]</c>, <c>Point A:::important: [0.9, 0.1] radius: 12</c>. Its position still to come
    /// stands after the space left for it.
    /// </summary>
    private static ContentNode Point(MermaidLine line)
    {
        const string Shape = "A point is its name, a colon and where it stands, from 0 to 1 across and up: Campaign A: [0.3, 0.6].";

        if (!Text(line, QuadrantRoles.Name, until: ":")) return line.Shown(Shape);

        line.Space();
        if (line.Token(ClassMark) && !Id(line, QuadrantRoles.Class)) return line.Shown(Shape);

        line.Space();
        if (!line.Token(":")) return line.Shown(Shape);

        line.Room();
        if (line.Done) return line.Read(QuadrantKinds.Point);
        if (line.Next != '[') return line.Shown(Shape);

        Position(line);

        if (!line.Done)
        {
            var at = line.At;
            line.Space();
            if (line.At == at || !line.Properties(Styles)) return line.Shown(Shape);
        }

        return line.Read(QuadrantKinds.Point);
    }

    /// <summary>Where a point stands: <c>[0.3, 0.6]</c> — read as far as it goes, with the reason where it is not two numbers, or never closed.</summary>
    private static void Position(MermaidLine line)
    {
        line.Open();
        line.Token("[", Roles.Open);
        line.Space();
        line.Amount(QuadrantRoles.X, Coordinate, until: ",]");
        line.Space();

        var pair = line.Token(",");
        if (pair)
        {
            line.Room();
            line.Amount(QuadrantRoles.Y, Coordinate, until: "]");
            line.Space();
        }

        var closed = line.Token("]", Roles.Close);
        line.Close(QuadrantKinds.Position, trouble: !closed ? "This position is never closed with ]."
                                                   : !pair ? "A point stands at two numbers, across and up: [0.3, 0.6]."
                                                   : null);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>Text as a piece of its own: in quotes, or bare up to <paramref name="until"/> or <paramref name="stop"/> — false where its quote is never closed.</summary>
    private static bool Text(MermaidLine line, string role, string? until = null, string? stop = null, int? end = null)
    {
        line.Open();

        if (line.Next == '"')
        {
            if (!line.Quoted(role, kind: null)) return false;
        }
        else
        {
            if (end is { } upto) line.Words(role, upto);
            else line.Words(role, until: until, stop: stop);
        }

        line.Close(QuadrantKinds.Text, role);
        return true;
    }

    /// <summary>A class's name, bare or in quotes.</summary>
    private static bool Id(MermaidLine line, string role) => line.Name(role, Letter);

    private static bool Letter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';

    /// <summary>A position: a number from 0 to 1.</summary>
    private static readonly Func<string, string?> Coordinate = MermaidNumber.Where(number => number is >= 0 and <= 1, "A point stands from 0 to 1 across and up.");

    /// <summary>Takes the space after a line's word — and, where nothing more is written, the space left for what follows — or says there is none.</summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }
}
