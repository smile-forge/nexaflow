using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Reads the body of a <c>mermaid</c> block into a tree — what every one of Mermaid's diagram types shares.
///
/// <para>
/// One parser for all of them, because they all open the same way: front matter between two <c>---</c> fences if
/// there is any, then <c>%%</c> comments and <c>%%{ … }%%</c> directives, then the line that names the diagram. After
/// that, any diagram may carry an accessible title and description. Those are read here, into their parts; every
/// other line is a <see cref="MermaidKinds.Statement"/>, held whole, because what its characters mean is its
/// diagram's grammar and not this one's.
/// </para>
/// <para>
/// <b>Front matter is read only as far as its lines.</b> A <c>key: value</c> line is its key, colon and value — which
/// is enough to find a title — and anything else in it is held as written. Its <c>config:</c> is YAML, and the
/// diagrams that apply one read it themselves.
/// </para>
/// <para>
/// What will not read is held as written with the reason, never dropped and never repaired: a directive that never
/// closes is not closed for you, because the half-typed block is exactly what an editor holds.
/// </para>
/// </summary>
public static class MermaidParser
{
    /// <summary>What opens and closes front matter.</summary>
    public const string Fence = "---";

    public const string AccessibleTitle = "accTitle";

    public const string AccessibleDescription = "accDescr";

    /// <summary>
    /// Reads a block into its lines, every character kept. The first line that says anything names the diagram, and its
    /// grammar reads every line after it.
    /// </summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;
        var lines = new List<ContentNode>();
        var at = 0;
        var reading = new Reading();

        if (Fences(source) is (var open, var close))
        {
            // Only blank lines come before front matter; that is what makes it front matter.
            for (; at < open.Start; at = Next(source, at, reading, lines)) { }

            lines.Add(FrontMatter(source, open, close));
            at = close.Stop;
        }

        while (at < source.Length) at = Next(source, at, reading, lines);

        return new BlockNode(Named(lines), lines, MermaidKinds.Block);
    }

    /// <summary>
    /// The word the diagram <paramref name="lines"/> are is named by: its header's, as written — a flowchart, for a block with no
    /// header yet, and a <c>mermaid</c> block for one whose header names no diagram.
    /// </summary>
    private static string Named(IEnumerable<ContentNode> lines) =>
        lines.SelectMany(line => line.Children).FirstOrDefault(part => part.Kind == MermaidKinds.Header) is not { } header ? "flowchart"
        : header.Part(Roles.Name) is { } keyword && MermaidDiagrams.Named(keyword.Text) != MermaidDiagram.Unknown ? keyword.Text
        : MermaidFenceParser.Language;

    /// <summary>
    /// The header of a block — the line that names its diagram — and where it starts, read no further than it; or null for a block
    /// with none yet.
    /// </summary>
    public static (int At, ContentNode Header)? Heading(string? source)
    {
        source ??= string.Empty;
        var lines = new List<ContentNode>();
        var reading = new Reading();
        var start = Fences(source) is (_, var close) ? close.Stop : 0;

        for (var at = start; at < source.Length && !reading.Headed;) at = Next(source, at, reading, lines);

        foreach (var part in lines.SelectMany(line => line.Children))
        {
            if (part.Kind == MermaidKinds.Header) return (start, part);
            start += part.Width;
        }

        return null;
    }

    /// <summary>
    /// <paramref name="block"/> with what each binding standing on a line of its own after the header supplies read into its place:
    /// after the line holding it, as lines of the block's own that nobody wrote here (<see cref="Roles.Supplied"/>) — so the block
    /// still prints as its author wrote it, and everything that works a diagram over sees the supplied lines as it sees the rest.
    ///
    /// <para>
    /// What a binding supplies is lines of the diagram the header names, and may open with front matter of its own, which is how
    /// what it supplies says which of its nodes have more behind them. A binding nothing supplies is left as written, saying so. A
    /// diagram is never bound whole: the header names its type, so a binding before it is not read as one (<see cref="Next"/>).
    /// </para>
    /// </summary>
    /// <param name="supplied">What the binding naming a path comes to, or null where it comes to nothing.</param>
    public static ContentNode Bind(ContentNode block, Func<string, string?> supplied)
    {
        if (block.Children.SelectMany(line => line.Children).FirstOrDefault(part => part.Kind == MermaidKinds.Header) is not { } heading)
            return block;

        var header = heading.Print();
        List<ContentNode>? lines = null;

        for (var at = 0; at < block.Children.Count; at++)
        {
            var line = block.Children[at];

            if (line.IsDerived || line.Children.FirstOrDefault(part => part.Kind == Kinds.BoundContent) is not { } bound)
            {
                lines?.Add(line);
                continue;
            }

            lines ??= [.. block.Children.Take(at)];

            var path = BoundText.Path(bound.Text)!;
            if (supplied(path) is not { } text)
            {
                lines.Add(line.With([.. line.Children.Select(part => ReferenceEquals(part, bound)
                    ? ContentNode.Leaf(Kinds.BoundContent, bound.Text, bound.Role, $"Nothing is bound to '{path}'.")
                    : part)]));
                continue;
            }

            lines.Add(line);
            lines.AddRange(Supplied(text, header).Select(read => ContentNode.Branch(read.Kind, read.Children, Roles.Supplied)));
        }

        return lines is null ? block : block.With(lines);
    }

    /// <summary>How a line breaks inside what a diagram draws as words.</summary>
    public const string LineBreak = "<br>";

    /// <summary>
    /// What a binding supplied, read as lines of the diagram <paramref name="header"/> names — read after it, so its grammar reads
    /// them — with any front matter they open with read as front matter.
    /// </summary>
    private static IEnumerable<ContentNode> Supplied(string text, string header)
    {
        ContentNode? front = null;
        var body = text;

        if (Fences(text) is (_, var close))
        {
            front = Parse(text[..close.Stop]).Children.FirstOrDefault(part => part.Kind == MermaidKinds.FrontMatter);
            body = text[close.Stop..];
        }

        // The header's own line is not what was supplied: it is there so the lines after it are read as the diagram they belong to.
        var read = Parse(header + "\n" + body).Children.Skip(1);

        return front is null ? read : [front, .. read];
    }

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One line where <paramref name="at"/> starts, read into <paramref name="lines"/> — or, for a directive or a
    /// description that runs on, the lines it takes. Hands back where the next one starts.
    /// </summary>
    /// <param name="headed">Whether the diagram has been named yet: the first line that says anything names it.</param>
    private static int Next(string source, int at, Reading reading, List<ContentNode> lines)
    {
        var row = Row.At(source, at);
        var (from, to) = row.Text(source);
        var text = source[from..to];

        if (text.Length == 0)
        {
            lines.Add(Line(source, row.Start, from, [], to, row));
            return row.Stop;
        }

        if (text.StartsWith("%%{", StringComparison.Ordinal))
            return Directive(source, row, from, to, lines);

        if (text.StartsWith("%%", StringComparison.Ordinal))
        {
            lines.Add(Line(source, row.Start, from, [ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia)], to, row));
            return row.Stop;
        }

        // A binding standing on a line of its own after the header is lines of the diagram supplied by whatever it is shown against.
        // Held as written; read into its place before the diagram is worked over (Bind). Before the header it would be the whole
        // diagram, which is never bound: the header is what names the diagram, and so what reads it.
        if (BoundText.Path(text) is not null)
        {
            lines.Add(Line(source, row.Start, from,
                [reading.Headed
                    ? ContentNode.Leaf(Kinds.BoundContent, text)
                    : ContentNode.Shown(text, "A diagram is not bound whole: its first line names its type, and a binding after it supplies lines of it.")],
                to, row));
            return row.Stop;
        }

        if (!reading.Headed)
        {
            reading.Headed = true;
            lines.Add(Line(source, row.Start, from, [Header(text, reading)], to, row));
            return row.Stop;
        }

        if (Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // A statement written across several lines is found here and read by its grammar, line by line.
        if (reading.Grammar is { } across && Stretched(source, row, from, to, across, lines) is { } spanned) return spanned;

        // What a line says is its diagram's own grammar, handed the line to the end of its row: as much of it as the grammar
        // reads is what was written, and the rest is the line's. A type without one keeps its lines whole.
        if (reading.Grammar?.Statement(source[from..row.End]) is { } said)
            lines.Add(Line(source, row.Start, from, [said], from + said.Width, row));
        else
            lines.Add(Line(source, row.Start, from, [ContentNode.Leaf(MermaidKinds.Statement, text)], to, row));
        return row.Stop;
    }

    /// <summary>
    /// A statement written across several lines, where one starts here: the line that opens it, every line inside it and the line that
    /// ends it, read as one statement and made one line of the tree. Null where no stretch starts here.
    /// </summary>
    private static int? Stretched(string source, Row row, int from, int to, IMermaidGrammar grammar, List<ContentNode> lines)
    {
        foreach (var stretch in grammar.Stretches)
        {
            if (stretch.Opens(source[from..to]) is not { } opened) continue;

            var pieces = new List<ContentNode> { opened };
            var end = to;

            for (var next = Row.At(source, row.Stop); next.Start < source.Length; next = Row.At(source, next.Stop))
            {
                var (start, stop) = next.Text(source);
                var text = source[start..stop];

                pieces.Add(Space(source[end..start]));
                end = stop;

                if (text.Length == 0) continue;

                if (!stretch.Ends(text))
                {
                    pieces.Add(stretch.Inside(text));
                    continue;
                }

                pieces.Add(stretch.Ended(text));
                lines.Add(Line(source, row.Start, from, [ContentNode.Branch(stretch.Kind, pieces)], stop, next));
                return next.Stop;
            }

            // Nothing ends it, so what opened it is a statement on its own, said to be never closed.
            lines.Add(Line(source, row.Start, from, [ContentNode.Branch(stretch.Kind, [opened]).Saying(stretch.Unclosed)], to, row));
            return row.Stop;
        }

        return null;
    }

    /// <summary>
    /// A line: the space before what it says, what it says, anything written after that on the last line it takes,
    /// and the characters that ended that line.
    /// </summary>
    /// <param name="start">Where the line starts.</param>
    /// <param name="from">Where what it says starts.</param>
    /// <param name="content">What it says — exactly the characters from <paramref name="from"/> to <paramref name="to"/>.</param>
    /// <param name="to">Where what it says ends.</param>
    /// <param name="last">The line that ends on — the same line, unless what it says runs on.</param>
    private static ContentNode Line(string source, int start, int from, IReadOnlyList<ContentNode> content, int to, Row last)
    {
        var pieces = new List<ContentNode>();

        if (from > start) pieces.Add(Space(source[start..from]));
        pieces.AddRange(content);
        if (last.End > to) pieces.AddRange(After(source[to..last.End]));
        if (last.Stop > last.End) pieces.Add(Space(source[last.End..last.Stop]));

        return ContentNode.Branch(MermaidKinds.Line, pieces);
    }

    /// <summary>
    /// What follows a construct on the line it ends on. Space is the line's; anything else has no reading, since a
    /// directive or a description closes its line.
    /// </summary>
    private static IEnumerable<ContentNode> After(string text)
    {
        var lead = Leading(text);
        var trail = Trailing(text, lead);

        if (lead > 0) yield return Space(text[..lead]);
        if (lead == text.Length) yield break;

        yield return ContentNode.Shown(text[lead..(text.Length - trail)], "Nothing else is read on the line this ends on.");
        if (trail > 0) yield return Space(text[(text.Length - trail)..]);
    }

    // ── The header ──────────────────────────────────────────────────────────

    /// <summary>
    /// The line naming the diagram: its keyword, then whatever follows. A keyword is a letter and then letters, digits,
    /// hyphens and underscores, which is what lets <c>gitGraph:</c> and <c>graph TD;</c> name their types.
    /// <para>
    /// A line in the header's place that starts with no keyword is still the header — held as written, with the reason —
    /// so a block whose first line names nothing reads as a diagram of no known type rather than as one with no header.
    /// </para>
    /// </summary>
    private static ContentNode Header(string text, Reading reading)
    {
        if (!char.IsAsciiLetter(text[0]))
            return ContentNode.Branch(MermaidKinds.Header, [ContentNode.Shown(text, text == Fence
                ? "`---` opens front matter only as the first line of a block, and needs another `---` to close it."
                : "A Mermaid diagram starts with the name of its type: flowchart, sequenceDiagram, pie….")]);

        var length = 1;
        while (length < text.Length && (char.IsAsciiLetterOrDigit(text[length]) || text[length] is '-' or '_')) length++;

        var keyword = text[..length];
        reading.Grammar = MermaidDiagrams.Grammar(MermaidDiagrams.Named(keyword));

        var trouble = MermaidDiagrams.Named(keyword) == MermaidDiagram.Unknown
            ? $"'{keyword}' is not a Mermaid diagram type."
            : null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, keyword, Roles.Name, trouble) };

        var rest = text[length..];
        var gap = Leading(rest);
        if (gap > 0) pieces.Add(Space(rest[..gap]));
        if (gap < rest.Length)
            pieces.Add(reading.Grammar?.Header(rest[gap..])
                       ?? ContentNode.Leaf(Kinds.Verbatim, rest[gap..], MermaidRoles.Arguments));

        return ContentNode.Branch(MermaidKinds.Header, pieces);
    }

    /// <summary>
    /// How far the read has got: whether the diagram has been named yet, and what its type reads for itself once it has.
    /// </summary>
    private sealed class Reading
    {
        /// <summary>Whether anything but blank lines, comments and directives has been read — the first of those names the diagram.</summary>
        public bool Headed;

        /// <summary>What this diagram type reads beyond the shared lines, where it has a grammar of its own.</summary>
        public IMermaidGrammar? Grammar;
    }

    // ── Directives ──────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>%%{ … }%%</c> directive, which may run over several lines. What is inside is held as written: this renderer
    /// obeys no directive, and the diagrams that take a config take it from front matter.
    /// </summary>
    private static int Directive(string source, Row row, int from, int to, List<ContentNode> lines)
    {
        var close = source.IndexOf("}%%", from + 3, StringComparison.Ordinal);
        if (close < 0)
        {
            lines.Add(Line(source, row.Start, from,
                [ContentNode.Shown(source[from..to], "This directive is never closed with `}%%`.")], to, row));
            return row.Stop;
        }

        var pieces = new List<ContentNode> { ContentNode.Leaf(Kinds.Token, "%%{", Roles.Open) };
        if (close > from + 3) pieces.Add(ContentNode.Leaf(Kinds.Verbatim, source[(from + 3)..close], Roles.Body));
        pieces.Add(ContentNode.Leaf(Kinds.Token, "}%%", Roles.Close));

        var last = Row.Containing(source, close);
        lines.Add(Line(source, row.Start, from, [ContentNode.Branch(MermaidKinds.Directive, pieces)], close + 3, last));
        return last.Stop;
    }

    // ── Accessibility ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>accTitle: …</c>, <c>accDescr: …</c>, or <c>accDescr { … }</c> over as many lines as it takes — read into
    /// <paramref name="lines"/>, handing back where the next line starts. Null where the line is none of those: a
    /// word that only starts the same way is the diagram's own.
    /// </summary>
    private static int? Accessibility(string source, Row row, int from, int to, List<ContentNode> lines)
    {
        var name = Starting(source, from, to, AccessibleTitle) ?? Starting(source, from, to, AccessibleDescription);
        if (name is null) return null;

        var gap = from + name.Length;
        while (gap < to && char.IsWhiteSpace(source[gap])) gap++;
        if (gap >= to) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Key, name, Roles.Name) };
        if (gap > from + name.Length) pieces.Add(Space(source[(from + name.Length)..gap]));

        if (source[gap] == ':')
        {
            pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator));
            pieces.AddRange(Padded(source[(gap + 1)..to]));

            lines.Add(Line(source, row.Start, from, [ContentNode.Branch(MermaidKinds.Accessibility, pieces)], to, row));
            return row.Stop;
        }

        if (source[gap] != '{' || name != AccessibleDescription) return null;

        var close = source.IndexOf('}', gap + 1);
        if (close < 0)
        {
            lines.Add(Line(source, row.Start, from,
                [ContentNode.Shown(source[from..to], "This description is never closed with `}`.")], to, row));
            return row.Stop;
        }

        pieces.Add(ContentNode.Leaf(Kinds.Token, "{", Roles.Open));
        pieces.AddRange(Padded(source[(gap + 1)..close]));
        pieces.Add(ContentNode.Leaf(Kinds.Token, "}", Roles.Close));

        var last = Row.Containing(source, close);
        lines.Add(Line(source, row.Start, from, [ContentNode.Branch(MermaidKinds.Accessibility, pieces)], close + 1, last));
        return last.Stop;
    }

    /// <summary>
    /// <paramref name="name"/>, where the text at <paramref name="from"/> is that word and nothing longer — Mermaid's
    /// keywords are case-sensitive.
    /// </summary>
    private static string? Starting(string source, int from, int to, string name)
    {
        if (to - from < name.Length || string.CompareOrdinal(source, from, name, 0, name.Length) != 0) return null;

        var after = from + name.Length;
        return after == to || !char.IsAsciiLetterOrDigit(source[after]) ? name : null;
    }

    /// <summary>A value with the space either side of it as trivia — and no value at all where it is only space.</summary>
    private static IEnumerable<ContentNode> Padded(string text)
    {
        var lead = Leading(text);
        var trail = Trailing(text, lead);

        if (lead > 0) yield return Space(text[..lead]);
        if (lead < text.Length) yield return ContentNode.Leaf(MermaidKinds.Value, text[lead..(text.Length - trail)], MermaidRoles.Value);
        if (trail > 0) yield return Space(text[(text.Length - trail)..]);
    }

    // ── Front matter ────────────────────────────────────────────────────────

    /// <summary>
    /// The two fences of the front matter, where there is any: the first line that is not blank, if it is <c>---</c>,
    /// and the next line that is. A fence that is never closed opens nothing.
    /// </summary>
    private static (Row Open, Row Close)? Fences(string source)
    {
        var open = Row.At(source, 0);
        while (open.Start < source.Length && open.IsBlank(source)) open = Row.At(source, open.Stop);
        if (open.Start >= source.Length || !open.IsFence(source)) return null;

        for (var close = Row.At(source, open.Stop); close.Start < source.Length; close = Row.At(source, close.Stop))
            if (close.IsFence(source)) return (open, close);

        return null;
    }

    /// <summary>
    /// The front matter, read as the tree its YAML is: every line holds, after its own line ending, the lines written under it — the
    /// ones indented further, up to the next line that is not — so <c>config:</c> holds <c>pie:</c>, and <c>pie:</c> holds
    /// <c>legendPosition:</c>. A blank line goes with the lines round it. Nothing is moved, so it still prints as it was written.
    /// </summary>
    private static ContentNode FrontMatter(string source, Row open, Row close)
    {
        var top = new List<Nested>();
        var under = new List<Nested>();

        for (var row = Row.At(source, open.Stop); row.Start < close.Start; row = Row.At(source, row.Stop))
        {
            var (from, to) = row.Text(source);
            var line = new Nested(from - row.Start, Line(source, row.Start, from, from == to ? [] : [Yaml(source[from..to])], to, row));

            if (from != to)
                while (under.Count > 0 && under[^1].Indent >= line.Indent) under.RemoveAt(under.Count - 1);

            (under.Count > 0 ? under[^1].Under : top).Add(line);
            if (from != to) under.Add(line);
        }

        return ContentNode.Branch(MermaidKinds.FrontMatter,
            [FenceLine(source, open, Roles.Open), .. top.Select(line => line.Built()), FenceLine(source, close, Roles.Close)]);
    }

    /// <summary>A line of front matter while it is read, how far it is indented, and the lines found under it so far.</summary>
    private sealed class Nested(int indent, ContentNode line)
    {
        public int Indent { get; } = indent;

        public List<Nested> Under { get; } = [];

        /// <summary>The line, holding the lines under it after its own.</summary>
        public ContentNode Built() =>
            this.Under.Count == 0 ? line : ContentNode.Branch(line.Kind, [.. line.Children, .. this.Under.Select(nested => nested.Built())], line.Role);
    }

    private static ContentNode FenceLine(string source, Row row, string role)
    {
        var (from, to) = row.Text(source);
        return Line(source, row.Start, from, [ContentNode.Leaf(MermaidKinds.Fence, source[from..to], role)], to, row);
    }

    /// <summary>
    /// A line of front matter: a comment, a <c>key: value</c> field, or — a list item, a line with no colon — held as
    /// written. Its indentation is the line's, so a field nested under <c>config:</c> is the same shape as one that is
    /// not; which it is, is whether its line starts with space.
    /// </summary>
    private static ContentNode Yaml(string text)
    {
        if (text[0] == '#') return ContentNode.Leaf(Kinds.Comment, text, Roles.Trivia);
        if (text[0] == '-' || text.IndexOf(':') is not (var colon and > 0)) return ContentNode.Leaf(MermaidKinds.Yaml, text);

        var key = text[..colon];
        var keyEnd = key.Length - Trailing(key, 0);

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Key, key[..keyEnd], Roles.Name) };
        if (keyEnd < key.Length) pieces.Add(Space(key[keyEnd..]));

        pieces.Add(ContentNode.Leaf(Kinds.Token, ":", Roles.Separator));
        pieces.AddRange(Padded(text[(colon + 1)..]));

        return ContentNode.Branch(MermaidKinds.Field, pieces);
    }

    // ── Characters ──────────────────────────────────────────────────────────

    private static ContentNode Space(string text) => ContentNode.Leaf(Kinds.Space, text, Roles.Trivia);

    private static int Leading(string text)
    {
        var n = 0;
        while (n < text.Length && char.IsWhiteSpace(text[n])) n++;
        return n;
    }

    /// <summary>How much space ends <paramref name="text"/>, never reaching back past <paramref name="floor"/>.</summary>
    private static int Trailing(string text, int floor)
    {
        var n = 0;
        while (text.Length - n > floor && char.IsWhiteSpace(text[text.Length - n - 1])) n++;
        return n;
    }

    /// <summary>
    /// A line of the source: what is on it, from <see cref="Start"/> to <see cref="End"/>, and the characters that
    /// ended it, from <see cref="End"/> to <see cref="Stop"/>.
    /// </summary>
    private readonly record struct Row(int Start, int End, int Stop)
    {
        public static Row At(string source, int start)
        {
            var newline = source.IndexOf('\n', start);
            var stop = newline < 0 ? source.Length : newline + 1;

            var end = newline < 0 ? source.Length : newline;
            if (end > start && source[end - 1] == '\r') end--;

            return new Row(start, end, stop);
        }

        /// <summary>The line <paramref name="offset"/> is on.</summary>
        public static Row Containing(string source, int offset) =>
            At(source, offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1);

        /// <summary>Where what the line says starts and ends, less the space either side of it.</summary>
        public (int From, int To) Text(string source)
        {
            var from = Start;
            while (from < End && char.IsWhiteSpace(source[from])) from++;

            var to = End;
            while (to > from && char.IsWhiteSpace(source[to - 1])) to--;

            return (from, to);
        }

        public bool IsBlank(string source)
        {
            var (from, to) = Text(source);
            return from == to;
        }

        public bool IsFence(string source)
        {
            var (from, to) = Text(source);
            return to - from == Fence.Length && string.CompareOrdinal(source, from, Fence, 0, Fence.Length) == 0;
        }
    }
}
