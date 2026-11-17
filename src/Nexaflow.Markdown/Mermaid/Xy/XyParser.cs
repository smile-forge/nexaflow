using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Xy;

/// <summary>
/// Reads an XY chart — an <c>xychart</c> block: which way it runs after the keyword, a <c>title</c>, its <c>x-axis</c> and
/// <c>y-axis</c>, and its <c>bar</c> and <c>line</c> series — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a chart. The header is optional, since an <c>xychart</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. Text is a word, or words in double quotes, and what is in quotes runs to the quote that closes it —
/// across a line ending, where that is where it stands, and past a <c>%%</c>, which inside quotes is what the text says. An x-axis
/// is its title where it has one, then its categories in brackets — <c>[jan, "feb 2"]</c> — or a range, <c>0 --&gt; 100</c>; a
/// y-axis is its title and a range, either left out for the chart to work out. A series is its name where it has one, then its
/// values in brackets, and a value of a line may carry a label in quotes after it: <c>line [540 "PaLM", 65]</c>. A number may be
/// signed, and start at its point.
/// </para>
/// <para>
/// Nothing a line says depends on another, so the only stage a chart's language offers the engine is what its front matter asks for:
/// which category a value stands over is its place in the order, which the builder reads. What is written half way — a list not yet
/// closed, a value still to come — is read as far as it goes.
/// </para>
/// </summary>
public sealed class XyParser : ITranspile
{
    private XyParser()
    {
    }

    /// <summary>The language an XY chart is written in: what a fence names it by.</summary>
    public const string Language = "xychart";

    /// <summary>The words an XY chart's header is written with — Mermaid's both.</summary>
    public static readonly string[] Keywords = ["xychart-beta", Language];

    public const string XAxis = "x-axis";
    public const string YAxis = "y-axis";
    public const string Bar = "bar";
    public const string Line = "line";
    public const string Horizontal = "horizontal";
    public const string Vertical = "vertical";

    /// <summary>The arrow between a range's ends.</summary>
    public const string Arrow = "-->";

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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where a category, a title or a name is still to write, and between the quotes of a title, a name or a label.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Name or MermaidKinds.Quoted;

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing an XY chart writes is spelled a way of its own.</remarks>
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

        // A binding standing on a line of its own after the header is lines supplied by whatever the chart is shown against. Held as
        // written; read into its place before the chart is worked over.
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
            // The header may be followed on its row by the chart's first statement, as Mermaid reads it.
            var head = text[..Headed(text)];
            if (Heading(head, from) is { } header) return MermaidParser.StatementLine(source, row.Start, from, header, from + head.Length, lines);
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // A ; ends a statement, as Mermaid reads one, so what follows it on its row is another.
        if (text[0] == ';') return MermaidParser.StatementLine(source, row.Start, from, ContentNode.Leaf(Kinds.Token, ";", Roles.Separator, offset: from), from + 1, lines);

        // The statement, which is as much of the source as it takes rather than as much as the row holds — what is in quotes runs to
        // its closing quote. As much of it as is read is what was written, and the rest is the line's; a statement read as nothing
        // yet — a title with nothing after it — is held whole.
        var end = Ended(source, from);
        var said = Statement(source[from..end], from) ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from);

        return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending outside quotes first ends, or
    /// the end of the row a comment outside them is written on — or sooner, as Mermaid reads statements: before a ;, and after a
    /// bracket closing with another statement's word after it on the row, <c>x-axis [a, b] bar [1, 2]</c>. A quote nothing closes is a
    /// character like any other.
    /// </summary>
    private static int Ended(string source, int from)
    {
        var row = MermaidParser.Row.Containing(source, from).End;
        var depth = 0;

        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n') return MermaidParser.Row.Containing(source, at).End;
            if (character == '%' && at + 1 < source.Length && source[at + 1] == '%') return MermaidParser.Row.Containing(source, at).End;

            if (character == '"')
            {
                var close = source.IndexOf('"', at + 1);
                if (close >= 0) at = close;
                continue;
            }

            if (character == ';')
            {
                while (at > from && char.IsWhiteSpace(source[at - 1])) at--;
                return at;
            }

            if (character == '[') depth++;
            else if (character == ']' && depth > 0 && --depth == 0)
            {
                var next = at + 1;
                while (next < row && source[next] is ' ' or '\t') next++;

                if (next < row && MermaidLine.Keyword(source[next..row], MermaidLine.TitleWord, XAxis, YAxis, Bar, Line) is not null) return at + 1;
            }
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>
    /// Where the header written at the start of <paramref name="text"/> ends: after the keyword and the way it runs where that follows —
    /// or before a ; or another statement's word after them, which Mermaid reads as the chart's first statement.
    /// </summary>
    private static int Headed(string text)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return text.Length;

        var at = Spaced(text, keyword.Length);
        if (MermaidLine.Keyword(text[at..], Horizontal, Vertical) is { } way) at = Spaced(text, at + way.Length);

        if (at < text.Length && (text[at] == ';' || MermaidLine.Keyword(text[at..], MermaidLine.TitleWord, XAxis, YAxis, Bar, Line) is not null))
        {
            while (at > 0 && char.IsWhiteSpace(text[at - 1])) at--;
            return at;
        }

        return text.Length;
    }

    /// <summary>Where the space starting at <paramref name="at"/> ends.</summary>
    private static int Spaced(string text, int at)
    {
        while (at < text.Length && text[at] is ' ' or '\t') at++;
        return at;
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header — <c>xychart</c> or <c>xychart-beta</c>, and which way it runs where that follows — or null where the line says
    /// something else, which an <c>xychart</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Orientation(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>Which way the chart runs, after the keyword — a comment after it its own — and anything else there held as written, with the reason.</summary>
    private static ContentNode Orientation(string arguments, int at)
    {
        var line = MermaidLine.Of(arguments, at);
        if (line.Done) return line.Read(XyKinds.Options, MermaidRoles.Arguments);

        return (line.Word(Horizontal, XyKinds.Orientation) || line.Word(Vertical, XyKinds.Orientation)) && line.Done
            ? line.Read(XyKinds.Options, MermaidRoles.Arguments)
            : ContentNode.Shown(arguments, "An xychart runs horizontal or vertical, and nothing else follows it: xychart horizontal.", MermaidRoles.Arguments, at);
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>
    /// One statement: a title, an axis or a series — or the text held as written, with the reason. Null for a title with nothing
    /// written after it yet, which is held whole.
    /// </summary>
    private static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        return MermaidLine.Keyword(line.Written, MermaidLine.TitleWord, XAxis, YAxis, Bar, Line) switch
        {
            MermaidLine.TitleWord => line.Title(quotes: true),
            XAxis => Axis(line, XAxis),
            YAxis => Axis(line, YAxis),
            Bar => Series(line, Bar),
            Line => Series(line, Line),
            _ => line.Shown("An xychart line is a title, an axis or a series: x-axis [jan, feb], y-axis \"Revenue\" 0 --> 100, bar [20, 35]."),
        };
    }

    /// <summary>
    /// An axis: <c>x-axis "Month" [jan, feb]</c>, <c>x-axis 0 --&gt; 10</c>, <c>y-axis "Revenue" 0 --&gt; 100</c> — its title and
    /// its range each left out where the chart is to work them out.
    /// </summary>
    private static ContentNode Axis(MermaidLine line, string word)
    {
        var shape = word == XAxis
            ? "An x-axis is its title where it has one, then its categories in brackets or a range: x-axis \"Month\" [jan, feb], or x-axis 0 --> 10."
            : "A y-axis is its title where it has one, then its range where it has one: y-axis \"Revenue\" 0 --> 100.";

        // The bracket may be written hard against the word, as Mermaid reads it: x-axis[jan, feb].
        line.Word(word);
        if (!Spaced(line) && line.Next != '[') return line.Done ? line.Read(XyKinds.Axis) : line.Shown(shape);
        if (line.Done) return line.Read(XyKinds.Axis);

        // A title comes first — unless what is written first is where a range starts.
        if (line.Next != '[' && !Ranged(line.Rest))
        {
            if (!Titled(line)) return line.Shown(shape);
            line.Space();
        }

        if (line.Done) return line.Read(XyKinds.Axis);

        if (line.Next == '[')
        {
            if (word == YAxis) return line.Shown("A y-axis is a range of numbers, not categories: y-axis 0 --> 100.");
            if (!Categories(line)) return line.Shown(shape);
        }
        else if (!Range(line))
        {
            return line.Shown(shape);
        }

        return line.Done ? line.Read(XyKinds.Axis) : line.Shown(shape);
    }

    /// <summary>A series: <c>bar "Revenue" [20, 35]</c>, or <c>line [540 "PaLM", 65]</c>.</summary>
    private static ContentNode Series(MermaidLine line, string word)
    {
        const string Shape = "A series is its name where it has one, then its values in brackets: bar \"Revenue\" [20, 35, 30].";

        // The bracket may be written hard against the word, as Mermaid reads it: bar[20, 35].
        line.Word(word);
        if ((!Spaced(line) && line.Next != '[') || line.Done) return line.Shown(Shape);

        if (line.Next != '[')
        {
            if (!Titled(line)) return line.Shown(Shape);
            line.Space();
        }

        if (line.Next != '[') return line.Shown(Shape);

        Values(line);
        return line.Done ? line.Read(XyKinds.Series) : line.Shown(Shape);
    }

    /// <summary>An axis's categories in their brackets. A comma with nothing after it yet is followed by a category still to write.</summary>
    private static bool Categories(MermaidLine line)
    {
        line.Open();
        line.Token("[", Roles.Open);
        line.Space();

        if (line.Next != ']' && !line.Names(Category, Roles.Element, XyRoles.Category, ends: ']')) return false;

        line.Space();
        var closed = line.Token("]", Roles.Close);
        line.Close(XyKinds.Categories, trouble: closed ? null : "These categories are never closed with ].");
        return true;
    }

    /// <summary>A range: where it starts, the arrow, and where it ends — the end still to come where nothing is written after the arrow.</summary>
    private static bool Range(MermaidLine line)
    {
        line.Open();
        line.Amount(XyRoles.Min, Number, stop: Arrow);
        line.Space();
        if (!line.Token(Arrow)) return false;

        line.Room();
        line.Amount(XyRoles.Max, Number);
        line.Close(XyKinds.Range);
        return true;
    }

    /// <summary>A series' values in their brackets. Brackets never closed are still read, with the reason on them.</summary>
    private static void Values(MermaidLine line)
    {
        line.Open();
        line.Token("[", Roles.Open);
        line.Space();

        if (!line.Done && line.Next != ']')
        {
            while (true)
            {
                Point(line);
                line.Space();
                if (!line.Token(",")) break;
                line.Room();
            }
        }

        var closed = line.Token("]", Roles.Close);
        line.Close(XyKinds.Values, trouble: closed ? null : "These values are never closed with ].");
    }

    /// <summary>One value, and the label in quotes after it where one is written.</summary>
    private static void Point(MermaidLine line)
    {
        line.Open();
        line.Amount(XyRoles.Value, Number, until: ",]\"");

        if (line.Past == '"')
        {
            line.Space();
            line.Quoted(XyRoles.Label, what: "label");
        }

        line.Close(XyKinds.Point);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A title or a name: in quotes, or words without them — as many as are written before the series' values or the axis's range,
    /// <c>bar Sales 2024 [1, 2]</c>, <c>y-axis Revenue in USD 0 --&gt; 100</c>, as Mermaid reads them.
    /// </summary>
    private static bool Titled(MermaidLine line) =>
        line.Next == '"' ? line.Quoted(XyRoles.Title, what: "title") : line.Name(XyRoles.Title, TitleEnds);

    /// <summary>Where a title written without quotes at <paramref name="at"/> ends: before the values' bracket, or before the word a range starts with.</summary>
    private static int TitleEnds(string text, int at)
    {
        var end = text.IndexOf('[', at);
        if (end < 0) end = text.Length;

        var arrow = text.IndexOf(Arrow, at, StringComparison.Ordinal);
        if (arrow >= 0 && arrow < end)
        {
            end = arrow;
            while (end > at && char.IsWhiteSpace(text[end - 1])) end--;
            while (end > at && !char.IsWhiteSpace(text[end - 1])) end--;
        }

        while (end > at && char.IsWhiteSpace(text[end - 1])) end--;
        return end;
    }

    /// <summary>Where a category written without quotes at <paramref name="at"/> ends: before the comma or the bracket after it, its words and all — <c>Jan 2024</c>.</summary>
    private static int CategoryEnds(string text, int at)
    {
        var end = at;
        while (end < text.Length && text[end] is not (',' or ']' or '"')) end++;
        while (end > at && char.IsWhiteSpace(text[end - 1])) end--;

        return end;
    }

    private static bool Category(MermaidLine line) => line.Name(XyRoles.Category, CategoryEnds);

    /// <summary>What a bare word is made of: anything but space, a comma, a bracket, a quote or the arrow's head.</summary>
    internal static bool Letter(char character) => character > ' ' && character is not (',' or '[' or ']' or '"' or '>');

    /// <summary>Whether what is written starts with where a range starts: a single word, then the arrow.</summary>
    private static bool Ranged(string rest)
    {
        var arrow = rest.IndexOf(Arrow, StringComparison.Ordinal);
        return arrow >= 0 && rest[0] != '"' && !rest[..arrow].TrimEnd().Contains(' ');
    }

    /// <summary>Any number: Mermaid's may be signed, and start at its point.</summary>
    private static readonly Func<string, string?> Number = MermaidNumber.Where(_ => true, string.Empty);

    /// <summary>Takes the space after a line's word — and, where nothing more is written, the space left for what follows — or says there is none.</summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }
}
