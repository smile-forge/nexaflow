using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Radar;

/// <summary>
/// Reads a radar chart — a <c>radar-beta</c> block: a <c>title</c>, the <c>axis</c> lines naming its spokes, the <c>curve</c> lines
/// drawn over them, and the options <c>max</c>, <c>min</c>, <c>ticks</c>, <c>graticule</c> and <c>showLegend</c> — and says how what
/// is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a radar chart. The header is optional, since a <c>radar</c> fence already says what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. A name is a word — letters, digits, <c>_</c> and <c>-</c>, starting with a letter or an underscore — and
/// a label is written in brackets after it: <c>m["Math"]</c>. A label in quotes is a string as Mermaid's newer grammars write one: in
/// double quotes or single, a backslash keeping the character after it, and running across a line ending wherever its closing quote
/// stands. A line names several axes or several curves, a comma between each. A curve's values follow its name and label in braces,
/// a comma between each: numbers, one per axis in the order the axes are written — <c>{85, 90, 80}</c> — or each naming the axis it
/// is for — <c>{ m: 85, s: 90 }</c> — and the braces may run over several lines, a comment on any of them. Options may share a line,
/// a comment between each: <c>max 100, min 0</c>. So a line is as much of the source as its strings and its braces take.
/// </para>
/// <para>
/// Beyond Mermaid, a name may be written in quotes — which is how one renamed to hold a space still reads — and a label without
/// them. A quote nothing closes is a character like any other, and braces nothing closes before the next curve opens its own end
/// with their line, so what is typed by mistake costs the line it is on rather than everything under it. Which axis each value is
/// for, and whether a curve gives each axis one, is a fact about every axis the block writes rather than about the curve's own line,
/// so it is worked out by the stages a radar chart's language offers the engine (<see cref="Stages.ResolveCurves"/>,
/// <see cref="Stages.ResolveOptions"/>). What is written half way — an axis still to name, a value still to come, braces not yet
/// closed — is read as far as it goes.
/// </para>
/// </summary>
public sealed class RadarParser : ITranspile
{
    private RadarParser()
    {
    }

    /// <summary>The language a radar chart is written in: what a fence names it by.</summary>
    public const string Language = "radar";

    /// <summary>The words a radar chart's header is written with — Mermaid's own, and the language's name.</summary>
    public static readonly string[] Keywords = ["radar-beta", Language];

    public const string Axis = "axis";
    public const string Curve = "curve";
    public const string Max = "max";
    public const string Min = "min";
    public const string Ticks = "ticks";
    public const string Graticule = "graticule";
    public const string ShowLegend = "showLegend";

    /// <summary>What a graticule is drawn as.</summary>
    public const string Circle = "circle";
    public const string Polygon = "polygon";

    /// <summary>What keeps the character after it inside a label.</summary>
    public const char Backslash = '\\';

    /// <summary>What opens a comment running to the end of its row, outside a label.</summary>
    private const string Comment = "%%";

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
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: where an axis's or a curve's name is still
    /// to write, and between a label's brackets or its quotes. A value gets none: it is drawn as how far a curve reaches, not as
    /// anything to write in.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Name or MermaidKinds.Label
        || (node.Kind == MermaidKinds.Quoted && holder?.Kind == MermaidKinds.Label);

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

        // A title is the rest of its row whatever it holds; a title with nothing written after it yet is held whole.
        if (MermaidLine.Keyword(text, MermaidLine.TitleWord) is not null)
        {
            var titled = MermaidLine.Of(source[from..row.End], from).Title();
            lines.Add(MermaidParser.Line(source, row.Start, from,
                                         [titled ?? ContentNode.Leaf(MermaidKinds.Statement, text, offset: from)], titled is null ? to : from + titled.Width, row));
            return row.Stop;
        }

        // Anything else is as much of the source as its labels and its braces take. As much of that as is read is what was written,
        // and the rest is the line's.
        var end = Ended(source, from);
        return MermaidParser.StatementLine(source, row.Start, from, Statement(MermaidLine.Of(source[from..end], from, CommentAt)), end, lines);
    }

    /// <summary>
    /// Where the line starting at <paramref name="from"/> ends: the end of the row a line ending outside a label and outside braces
    /// first ends, or the end of the row a comment outside them is written on — or sooner, where another statement's word starts on
    /// the row: Mermaid's radar needs nothing between its statements, so <c>axis a, b curve x{1, 2}</c> is two, and an option's word
    /// starts another only after an axis or a curve, since options follow one another on the one line. A
    /// title runs to the end of its row, as Mermaid's does.
    /// </summary>
    private static int Ended(string source, int from)
    {
        var row = MermaidParser.Row.Containing(source, from).End;
        if (MermaidLine.Keyword(source[from..row], MermaidLine.TitleWord) is not null) return row;

        var opting = MermaidLine.Keyword(source[from..row], Max, Min, Ticks, Graticule, ShowLegend) is not null;

        for (var at = from; at < source.Length; at++)
        {
            if (source[at] == '\n' || Commenting(source, at)) return MermaidParser.Row.Containing(source, at).End;

            if (at > from && source[at] is ' ' or '\t' && at < row)
            {
                var next = at;
                while (next < row && source[next] is ' ' or '\t') next++;

                var word = next < row ? MermaidLine.Keyword(source[next..row], MermaidLine.TitleWord, Axis, Curve, Max, Min, Ticks, Graticule, ShowLegend) : null;
                if ((word is MermaidLine.TitleWord or Axis or Curve || word is not null && !opting) && Whole(source[from..at]))
                {
                    while (at > from && char.IsWhiteSpace(source[at - 1])) at--;
                    return at;
                }
            }

            at = Enclosed(source, at);
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>
    /// Whether a statement written as <paramref name="before"/> has gone far enough for a word after it to start the next one — not
    /// where the word stands where a name goes, after a comma or straight after the statement's own word, as in <c>axis axis1, axis</c>.
    /// </summary>
    private static bool Whole(string before)
    {
        var said = before.TrimEnd();
        return !said.EndsWith(',') && MermaidLine.Keyword(said, Axis, Curve, Max, Min, Ticks, Graticule, ShowLegend)?.Length != said.Length;
    }

    /// <summary>
    /// Where the header written at the start of <paramref name="text"/> ends: after the keyword and the colon Mermaid allows after it, a
    /// comment after them its own — or before a statement after them, which Mermaid reads as the chart's first.
    /// </summary>
    private static int Headed(string text)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return text.Length;

        var at = keyword.Length;
        while (at < text.Length && text[at] is ' ' or '\t') at++;

        if (at < text.Length && text[at] == ':')
        {
            at++;
            while (at < text.Length && text[at] is ' ' or '\t') at++;
        }

        if (at >= text.Length || Commenting(text, at)) return text.Length;
        if (MermaidLine.Keyword(text[at..], MermaidLine.TitleWord, Axis, Curve, Max, Min, Ticks, Graticule, ShowLegend) is null) return text.Length;

        while (at > 0 && char.IsWhiteSpace(text[at - 1])) at--;
        return at;
    }

    /// <summary>Where a comment closing a line starts, outside its labels and its braces — or -1.</summary>
    private static int CommentAt(string text)
    {
        for (var at = 0; at < text.Length; at++)
        {
            if (Commenting(text, at)) return at;
            at = Enclosed(text, at);
        }

        return -1;
    }

    /// <summary>
    /// The last character of the label or the braces opening at <paramref name="at"/> — or <paramref name="at"/> itself where
    /// neither opens there, or nothing closes what does.
    /// </summary>
    private static int Enclosed(string text, int at) => text[at] switch
    {
        '"' or '\'' when Closing(text, at) is var close and >= 0 => close,
        '{' when Shut(text, at) is var shut and >= 0 => shut,
        _ => at,
    };

    /// <summary>
    /// Where the braces opening at <paramref name="open"/> close, over as many lines as they take and past any label or comment
    /// inside them — or -1 where another curve's braces open first, or nothing closes them.
    /// </summary>
    private static int Shut(string text, int open)
    {
        for (var at = open + 1; at < text.Length; at++)
        {
            switch (text[at])
            {
                case '}':
                    return at;

                case '{':
                    return -1;

                case '"' or '\'' when Closing(text, at) is var close and >= 0:
                    at = close;
                    break;

                case '%' when Commenting(text, at):
                    var end = text.IndexOf('\n', at);
                    if (end < 0) return -1;
                    at = end;
                    break;
            }
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
    /// The header — <c>radar-beta</c>, and a colon after it where one is written — or null where the line says something else,
    /// which a <c>radar</c> fence needs no header before.
    /// </summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Colon(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>What follows the keyword: Mermaid allows a colon, and a comment, and nothing else.</summary>
    private static ContentNode Colon(string arguments, int at)
    {
        var line = MermaidLine.Of(arguments, at, CommentAt);
        line.Token(":");

        return line.Done
            ? line.Read(RadarKinds.Colon, MermaidRoles.Arguments)
            : ContentNode.Shown(arguments, "Nothing but a colon follows radar-beta on its line: axes, curves and options go on lines of their own.",
                                MermaidRoles.Arguments, at);
    }

    // ── Statements ──────────────────────────────────────────────────────────

    /// <summary>An axis line, a curve line or a line of options — or the line held as written, with the reason.</summary>
    private static ContentNode Statement(MermaidLine line) =>
        MermaidLine.Keyword(line.Written, Axis, Curve, Max, Min, Ticks, Graticule, ShowLegend) switch
        {
            Axis => Axes(line),
            Curve => Curves(line),
            null => line.Shown("A radar line is a title, an axis, a curve or an option: axis m[\"Math\"], curve a[\"Alice\"]{85}, max 100."),
            _ => Options(line),
        };

    /// <summary>
    /// An <c>axis</c> line: <c>axis m["Math"], s["Science"]</c> — or, with nothing yet after its word and the space left for it, an
    /// axis still to name, standing where typing its name puts it.
    /// </summary>
    private static ContentNode Axes(MermaidLine line)
    {
        const string Shape = "An axis line names its axes, a comma between each, each with its label after it where it has one: axis m[\"Math\"], s[\"Science\"].";

        line.Word(Axis);
        if (!Spaced(line)) return line.Shown(Shape);

        return line.Names(Named, Roles.Element, RadarRoles.Id, item: RadarKinds.Axis) && line.Done
            ? line.Read(RadarKinds.Axes)
            : line.Shown(Shape);
    }

    /// <summary>
    /// A <c>curve</c> line: <c>curve a["Alice"]{85, 90}, b{70, 75}</c>. A curve whose values are not written yet is still a curve,
    /// being written.
    /// </summary>
    private static ContentNode Curves(MermaidLine line)
    {
        const string Shape = "A curve line names its curves, a comma between each, each with its label and its values in braces after it: curve a[\"Alice\"]{85, 90, 80}.";

        line.Word(Curve);
        if (!Spaced(line)) return line.Shown(Shape);

        return line.Names(Drawn, Roles.Element, RadarRoles.Id, item: RadarKinds.Curve) && line.Done
            ? line.Read(RadarKinds.Curves)
            : line.Shown(Shape);
    }

    /// <summary>An axis: its name, and its label where one is written.</summary>
    private static bool Named(MermaidLine line) => Id(line) && Labelled(line);

    /// <summary>A curve: its name, its label where one is written, and its values where they are.</summary>
    private static bool Drawn(MermaidLine line)
    {
        if (!Named(line)) return false;
        if (line.Past != '{') return true;

        line.Space();
        Values(line);
        return true;
    }

    /// <summary>
    /// The label in brackets after a name, where one is written: <c>["Math"]</c> or <c>['Math']</c>, space allowed inside the
    /// brackets — or, beyond Mermaid, <c>[Math]</c> without quotes.
    /// </summary>
    private static bool Labelled(MermaidLine line)
    {
        if (line.Past != '[') return true;

        line.Space();
        var mark = line.Save();

        line.Open();
        line.Token("[", Roles.Open);
        line.Space();

        if (line.Next is not ('"' or '\''))
        {
            line.Restore(mark);
            return line.Label("[", "]", RadarRoles.Label);
        }

        if (!Quoted(line)) return line.Undo(mark);

        line.Space();
        if (!line.Token("]", Roles.Close)) return line.Undo(mark, "This label is never closed with ].");

        line.Close(MermaidKinds.Label);
        return true;
    }

    /// <summary>A label in quotes: the quote it opens with, what it says, and the same quote closing it — or a failure with the reason, where nothing closes it.</summary>
    private static bool Quoted(MermaidLine line)
    {
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
    /// What is between a label's quotes, as the words it is written with — and, where a backslash keeps a character or the label runs
    /// across a line ending, those as pieces of their own between them, each a <see cref="RadarKinds.Escape"/> or a
    /// <see cref="RadarKinds.Break"/>, so whatever says what the label says finds them without looking at the characters
    /// (<see cref="RadarStandIns"/>).
    /// </summary>
    private static ContentNode Said(string text, int at)
    {
        if (text.AsSpan().IndexOfAny(Backslash, '\n', '\r') < 0) return ContentWords.Of(text, Kinds.Words, RadarRoles.Label, at: at);

        var pieces = new List<ContentNode>();
        var from = 0;

        for (var place = 0; place < text.Length;)
        {
            var (kind, length) = text[place] switch
            {
                // A \n is a line break, as it is in every diagram's words, so it stays in them for whatever breaks their lines to find.
                Backslash when place + 1 < text.Length && text[place + 1] == 'n' => (null, 2),
                Backslash => (RadarKinds.Escape, 2),
                '\r' when place + 1 < text.Length && text[place + 1] == '\n' => (RadarKinds.Break, 2),
                '\r' or '\n' => (RadarKinds.Break, 1),
                _ => (null, 0),
            };

            if (kind is null)
            {
                place += Math.Max(1, length);
                continue;
            }

            if (place > from) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..place], RadarRoles.Label, offset: at + from));
            pieces.Add(ContentNode.Leaf(kind, text.Substring(place, length), offset: at + place));
            from = place += length;
        }

        if (from == 0) return ContentWords.Of(text, Kinds.Words, RadarRoles.Label, at: at);
        if (from < text.Length) pieces.Add(ContentNode.Leaf(Kinds.Words, text[from..], RadarRoles.Label, offset: at + from));

        return ContentNode.Branch(Kinds.Words, pieces, RadarRoles.Label, at);
    }

    /// <summary>
    /// A curve's values in their braces: <c>{85, 90, 80}</c>, or <c>{ m: 85, s: 90 }</c>, over as many lines as they take. A comma
    /// with nothing after it yet is followed by a value still to come. Braces never closed are still read, with the reason on them,
    /// so a curve being written is drawn as far as it goes.
    /// </summary>
    private static void Values(MermaidLine line)
    {
        line.Open();
        line.Token("{", Roles.Open);
        Gap(line);

        if (!line.Done && line.Next != '}')
        {
            while (true)
            {
                Entry(line);
                Gap(line);
                if (!line.Token(",")) break;
                line.Room();
                Gap(line);
            }
        }

        var closed = line.Token("}", Roles.Close);
        line.Close(RadarKinds.Values, trouble: closed ? null : "These values are never closed with }.");
    }

    /// <summary>One value: a number — or the axis it is for, a colon, and a number.</summary>
    private static void Entry(MermaidLine line)
    {
        line.Open();

        var start = line.Save();
        // The axis a value is for, then a colon — which Mermaid lets be left out: { m 85, s 90 }.
        var keyed = false;

        if (line.Name(RadarRoles.Key, Letter, Trouble))
        {
            line.Space();

            if (line.Token(":"))
            {
                line.Room();
                keyed = true;
            }
            else
            {
                keyed = line.Next is (>= '0' and <= '9') or '-' or '+' or '.';
            }
        }

        if (!keyed) line.Restore(start);

        line.Amount(RadarRoles.Value, MermaidNumber.Where(number => number >= 0, "A value is a number, nought or more."), until: ",}%");
        line.Close(RadarKinds.Entry);
    }

    /// <summary>The space between a curve's values, and any comment in it running to the end of its row.</summary>
    private static void Gap(MermaidLine line)
    {
        while (true)
        {
            line.Space();
            if (!line.Sees(Comment)) return;

            var rest = line.Rest;
            var end = rest.IndexOf('\n');
            var comment = (end < 0 ? rest : rest[..end]).TrimEnd('\r');
            line.Add(ContentNode.Leaf(Kinds.Comment, comment, Roles.Trivia, offset: line.Begins));
        }
    }

    /// <summary>A line of options, a comma between each: <c>max 100, graticule polygon</c>.</summary>
    private static ContentNode Options(MermaidLine line)
    {
        const string Shape = "An option line sets max, min, ticks, graticule or showLegend, a comma between each: max 100, graticule polygon.";

        while (true)
        {
            if (MermaidLine.Keyword(line.Rest, Max, Min, Ticks, Graticule, ShowLegend) is not { } word) return line.Shown(Shape);

            line.Open();
            line.Word(word);
            line.Room();
            Set(line, word);
            line.Close(RadarKinds.Option);

            // A comma between one option and the next, or nothing at all, as Mermaid reads them: max 100 min 0.
            line.Space();
            if (!line.Token(","))
            {
                if (MermaidLine.Keyword(line.Rest, Max, Min, Ticks, Graticule, ShowLegend) is null) break;
                continue;
            }

            line.Room();
            if (line.Done) break;
        }

        return line.Done ? line.Read(RadarKinds.Options) : line.Shown(Shape);
    }

    /// <summary>What an option is set to, up to the comma before the next — with what is wrong with it, where anything is.</summary>
    private static void Set(MermaidLine line, string word)
    {
        const string Until = ", \t";

        switch (word)
        {
            case Max or Min:
                line.Amount(RadarRoles.Value, MermaidNumber.Where(number => number >= 0, $"{word} is a number, nought or more."), Until);
                break;

            case Ticks:
                // Mermaid draws a ring a 1/ticks of the radius further out for each tick and one for a part of one, so a fraction is a ticks too.
                line.Amount(RadarRoles.Value, MermaidNumber.Where(number => number >= 0, "ticks is nought or more: how far apart the graticule's rings stand."), Until);
                break;

            case Graticule:
                line.Setting(RadarRoles.Value, value => Is(value, Circle, Polygon) ? null : "A graticule is a circle or a polygon.", Until);
                break;

            default:
                line.Setting(RadarRoles.Value, value => Is(value, "true", "false") ? null : "showLegend is true or false.", Until);
                break;
        }
    }

    // ── Words ───────────────────────────────────────────────────────────────

    /// <summary>A name, bare or in quotes.</summary>
    private static bool Id(MermaidLine line) => line.Name(RadarRoles.Id, Letter, Trouble);

    /// <summary>What a bare name is made of.</summary>
    private static bool Letter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';

    /// <summary>What is wrong with a bare name, where anything is.</summary>
    private static string? Trouble(string name) =>
        char.IsAsciiLetter(name[0]) || name[0] == '_' ? null : "A name starts with a letter or an underscore, or is written in quotes.";

    /// <summary>Whether a name can be written without quotes.</summary>
    internal static bool Bare(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(Letter);

    private static bool Is(string value, params string[] words) => words.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes the space after a line's word — and, where nothing more is written, the space left for what follows it — or says there
    /// is none, which is a word run into what follows it.
    /// </summary>
    private static bool Spaced(MermaidLine line)
    {
        var at = line.At;
        line.Room();
        return line.At > at;
    }

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a radar chart spells it: into a label in quotes, with what would end it kept by a backslash; anywhere else, as every block written in Mermaid's syntax spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        QuotedOf(part) is { } quoted && Opened(quoted) is { } quote ? Escaped(text, quote) : MermaidParser.Spelled(part, text);

    /// <summary>The label in quotes <paramref name="part"/> is in — its words, a piece of them, or the hole in it — or null where it is in none.</summary>
    public static ContentPart? QuotedOf(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Quoted && at.Parent?.Kind == MermaidKinds.Label) return at;

        return null;
    }

    /// <summary>The quote <paramref name="quoted"/> is written in.</summary>
    public static char? Opened(ContentPart quoted) => quoted.Children.FirstOrDefault(child => child.Role == Roles.Open)?.Text[0];

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
