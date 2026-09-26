using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Nomnoml;

/// <summary>
/// Reads a <c>nomnoml</c> block into a tree, every character kept.
///
/// <para>
/// A node is written in brackets and a bar divides it into compartments: <c>[Name|a field|a method()]</c>, the first
/// compartment its name. What it is is written in angle brackets before that — <c>[&lt;abstract&gt;Shape]</c> — and a
/// compartment holding nodes of its own is a group, its own box drawn round them. Two nodes are joined by an operator
/// between them, with how many of each written either side of it: <c>[Order] 1 -&gt; 0..n [Line]</c>. The operator is read
/// as the three things nomnoml builds one from — an end, a line and another end — each a piece of its own.
/// </para>
/// <para>
/// A group whose brackets are opened on one line and closed on another is gathered with the lines between them
/// (<see cref="NomnomlKinds.Group"/>), so the tree holds what is inside what; a group inside it is gathered inside it the
/// same way. A directive is a hash, a name and a value; a comment is two slashes. What will not read is held as written,
/// with the reason.
/// </para>
/// </summary>
public static class NomnomlParser
{
    public const string Opens = "[";
    public const string Closes = "]";
    public const string Angled = "<";
    public const string Shuts = ">";
    public const string Bar = "|";
    public const string Hash = "#";
    public const string Slashes = "//";
    public const string Semicolon = ";";
    public const string Colon = ":";

    /// <summary>
    /// What takes the meaning out of the character after it. Nomnoml's own escape: a bracket, a bar or a semicolon
    /// written in a name or a member would end it, and written after this it is only itself.
    /// </summary>
    public const char Escape = '\\';

    /// <summary>The directive that lays the diagram out.</summary>
    public const string DirectionWord = "direction";

    /// <summary>
    /// The left end of an association, longest first — a longer one must never be read as the shorter one it starts
    /// with. Empty is one of them: most associations mark only their right end.
    /// </summary>
    public static readonly string[] Heads = ["(o", "o<", "<:", "(", "o", "+", "<", ""];

    /// <summary>The line drawn between the two ends. Anything but a single dash is drawn dashed.</summary>
    public static readonly string[] Lines = ["--", "-/-", "-"];

    /// <summary>The right end, longest first.</summary>
    public static readonly string[] Tails = [">o", "o)", ":>", "o", ">", ")", "+", ""];

    /// <summary>Every association nomnoml's own documentation names, which is what the tests hold this to.</summary>
    public static readonly string[] Operators =
    [
        "-", "->", "<->", "-->", "<-->", "-:>", "<:-", "--:>", "<:--",
        "+-", "+->", "o-", "o->", "-o)", "o<-)", "->o", "--", "-/-",
    ];

    private const string NodeShape = "A nomnoml node is written in brackets: [Name], [Name|a field|a method()] or [<abstract>Name].";
    private const string DirectiveShape = "A nomnoml directive is a hash, a name and a value: #direction: right.";
    private const string NeverClosed = "This nomnoml node is never closed with ].";
    private const string NeverEnded = "This nomnoml group is never closed with ].";
    private const string Beyond = "Nothing else is read on the line this ends on.";

    /// <summary>Reads a block into its lines, every character kept.</summary>
    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;
        return ContentNode.Branch(NomnomlKinds.Diagram, Read(source, 0, source.Length));
    }

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>The lines starting from <paramref name="at"/> up to <paramref name="end"/>, each group gathered with what is inside it.</summary>
    private static List<ContentNode> Read(string source, int at, int end)
    {
        var lines = new List<ContentNode>();

        while (at < end)
        {
            var row = Row.At(source, at);
            var (from, to) = row.Text(source);
            var text = source[from..to];

            if (text.Length > 0 && Depth(text) > 0 && Opened(text) is { } opened)
            {
                if (Closing(source, row, end, Depth(text)) is { } last)
                {
                    var (start, stop) = last.Text(source);

                    lines.Add(ContentNode.Branch(NomnomlKinds.Group,
                    [
                        Line(source, row, from, opened),
                        .. Read(source, row.Stop, last.Start),
                        Line(source, last, start, source[start..stop] == Closes
                            ? ContentNode.Leaf(Kinds.Token, Closes, Roles.Close)
                            : Statement(source[start..last.End])),
                    ]));

                    at = last.Stop;
                    continue;
                }

                // Nothing closes it, so what opened it is a line on its own, said to be never closed.
                lines.Add(Line(source, row, from, opened.Saying(NeverEnded)));
                at = row.Stop;
                continue;
            }

            lines.Add(Line(source, row, from, text.Length == 0 ? null : Statement(source[from..row.End])));
            at = row.Stop;
        }

        return lines;
    }

    /// <summary>
    /// The line a group opened with <paramref name="depth"/> brackets closes on: the first after it where as many are
    /// closed as were opened, counting those opened and closed on the lines between — or null where none does before
    /// <paramref name="end"/>.
    /// </summary>
    private static Row? Closing(string source, Row opening, int end, int depth)
    {
        for (var row = Row.At(source, opening.Stop); row.Start < end; row = Row.At(source, row.Stop))
        {
            var (from, to) = row.Text(source);
            if (from < to && (depth += Depth(source[from..to])) <= 0) return row;
        }

        return null;
    }

    /// <summary>
    /// A line: the space before what it says, what it says, anything written after that which is not read, and the
    /// characters that ended the line.
    /// </summary>
    private static ContentNode Line(string source, Row row, int from, ContentNode? said)
    {
        var pieces = new List<ContentNode>();
        var to = from + (said?.Width ?? 0);

        if (from > row.Start) pieces.Add(Space(source[row.Start..from]));
        if (said is not null) pieces.Add(said);
        if (row.End > to) pieces.AddRange(After(source[to..row.End]));
        if (row.Stop > row.End) pieces.Add(Space(source[row.End..row.Stop]));

        return ContentNode.Branch(NomnomlKinds.Line, pieces);
    }

    /// <summary>What follows a statement on its line: space, and anything else held as written with the reason.</summary>
    private static IEnumerable<ContentNode> After(string text)
    {
        var lead = 0;
        while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;

        var trail = 0;
        while (text.Length - trail > lead && char.IsWhiteSpace(text[text.Length - trail - 1])) trail++;

        if (lead > 0) yield return Space(text[..lead]);
        if (lead == text.Length) yield break;

        yield return ContentNode.Shown(text[lead..(text.Length - trail)], Beyond);
        if (trail > 0) yield return Space(text[(text.Length - trail)..]);
    }

    /// <summary>What one line says: a comment, a directive, or nodes joined end to end.</summary>
    private static ContentNode Statement(string text)
    {
        var line = new Cursor(text);
        line.Space();

        if (line.Sees(Slashes))
        {
            line.Words(Roles.Trivia);
            return line.Read(NomnomlKinds.Comment, Roles.Trivia);
        }

        return line.Sees(Hash) ? Directive(line) : Chain(line, line.Written.Length);
    }

    /// <summary>
    /// A setting: <c>#direction: right</c>, <c>#.abstract: fill=#8f8</c>. The name is whatever stands before the colon,
    /// so a character typed into one is part of the name rather than the end of it.
    /// </summary>
    private static ContentNode Directive(Cursor line)
    {
        line.Token(Hash, Roles.Open);

        var colon = line.Written.IndexOf(Colon, line.At, StringComparison.Ordinal);
        if (colon < 0) return line.Shown(DirectiveShape);

        if (colon > line.At) line.Words(NomnomlRoles.Key, colon);

        line.Space();
        line.Token(Colon);
        line.Room();
        if (!line.Done) line.Words(NomnomlRoles.Setting);

        return line.Read(NomnomlKinds.Directive);
    }

    /// <summary>
    /// The line a group opens on: its bracket, what it says it is, its name, and the bar the nodes inside it follow — or
    /// null where the line opens no node. What closes it is on a line of its own, so there is nothing here to close.
    /// </summary>
    private static ContentNode? Opened(string text)
    {
        var line = new Cursor(text);
        line.Space();
        if (!line.Sees(Opens)) return null;

        var end = line.Written.Length;
        line.Token(Opens, Roles.Open);
        Classified(line, end);

        var bars = Bars(line.Written, line.At, end);
        var first = bars.Count == 0 ? end : bars[0];

        line.Space();
        if (first > line.At) line.Words(NomnomlRoles.Id, first);

        line.Space();
        if (bars.Count > 0) line.Token(Bar);
        if (!line.Done) line.Words(NomnomlRoles.Member);

        return line.Read(NomnomlKinds.Node);
    }

    /// <summary>
    /// One or more nodes joined end to end — <c>[A] -&gt; [B] -&gt; [C]</c> — with what is written either side of each
    /// operator, and what the whole line says after a colon at the end of it.
    /// </summary>
    private static ContentNode Chain(Cursor line, int stop)
    {
        if (!Chained(line, stop)) return line.Shown(NodeShape);

        var mark = line.Save();
        line.Space();

        if (line.At < stop && line.Token(Colon))
        {
            line.Room();
            if (!line.Done) line.Words(NomnomlRoles.Said);
        }
        else
        {
            line.Restore(mark);
        }

        return line.Read(NomnomlKinds.Association);
    }

    /// <summary>
    /// The nodes and operators themselves — as far as they go.
    ///
    /// <para>
    /// Where the line stops making sense the reading stops with it, and what is left is the line's own. Holding the whole
    /// line back instead would take what had already been read off the page as soon as its end was half typed.
    /// </para>
    /// </summary>
    private static bool Chained(Cursor line, int stop)
    {
        if (!Node(line, stop)) return false;

        while (true)
        {
            var mark = line.Save();
            line.Space();

            if (line.At >= stop || line.Sees(Colon)) { line.Restore(mark); return true; }

            Side(line, stop, NomnomlRoles.Near);
            line.Space();

            if (!Operator(line)) { line.Restore(mark); return true; }
            line.Space();

            // What is written the far side runs to the node itself rather than to the next operator: a count is written
            // beside its node, and a dash typed into one is part of the count, not another way of joining two nodes.
            var next = Bracket(line.Written, line.At, stop);
            if (next > line.At) line.Words(NomnomlRoles.Far, next);
            line.Space();

            if (!Node(line, stop)) { line.Restore(mark); return true; }
        }
    }

    /// <summary>What is written between a node and the operator after it: how many of it there are.</summary>
    private static void Side(Cursor line, int stop, string role)
    {
        var end = Found(line.Written, line.At, stop);
        if (end > line.At) line.Words(role, end);
    }

    /// <summary>
    /// The association written where the line stands, read as the three pieces nomnoml builds one from — an end, a line,
    /// and another end — so the ways two nodes can be joined are every combination of those rather than a list somebody
    /// has to keep up to date. False where none starts there.
    /// </summary>
    private static bool Operator(Cursor line)
    {
        if (Joined(line.Written, line.At) is not var (head, drawn, tail)) return false;

        line.Open();
        if (head.Length > 0) line.Token(head, NomnomlRoles.Head);
        line.Token(drawn, NomnomlRoles.Drawn);
        if (tail.Length > 0) line.Token(tail, NomnomlRoles.Tail);
        line.Close(NomnomlKinds.Operator, NomnomlRoles.Operator);

        return true;
    }

    /// <summary>The end, the line and the other end of the association at <paramref name="at"/>, or null where none starts there.</summary>
    private static (string Head, string Drawn, string Tail)? Joined(string written, int at)
    {
        foreach (var head in Heads)
        {
            if (!Sees(written, at, head)) continue;

            foreach (var drawn in Lines)
            {
                if (!Sees(written, at + head.Length, drawn)) continue;

                foreach (var tail in Tails)
                    if (Sees(written, at + head.Length + drawn.Length, tail))
                        return (head, drawn, tail);
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="token"/> stands at <paramref name="at"/>. An empty token always does.</summary>
    private static bool Sees(string written, int at, string token) =>
        at + token.Length <= written.Length && string.CompareOrdinal(written, at, token, 0, token.Length) == 0;

    // ── Nodes ───────────────────────────────────────────────────────────────

    /// <summary>One node in brackets: what it is, what it is called, and the compartments past its name.</summary>
    private static bool Node(Cursor line, int stop)
    {
        if (!line.Sees(Opens)) return line.Fail(NodeShape);

        var mark = line.Save();
        line.Open();
        line.Token(Opens, Roles.Open);

        var end = Matching(line.Written, line.At, stop);
        if (end < 0) return line.Undo(mark, NeverClosed);

        Classified(line, end);
        Named(line, end);

        line.Token(Closes, Roles.Close);
        line.Close(NomnomlKinds.Node);
        return true;
    }

    /// <summary>What a node says it is, where it says: <c>[&lt;abstract&gt;Shape]</c>.</summary>
    private static void Classified(Cursor line, int end)
    {
        if (!line.Sees(Angled)) return;

        var shut = line.Written.IndexOf(Shuts, line.At, StringComparison.Ordinal);
        if (shut < 0 || shut >= end) return;

        line.Open();
        line.Token(Angled, Roles.Open);
        if (shut > line.At) line.Words(NomnomlRoles.Classifier, shut);
        line.Token(Shuts, Roles.Close);
        line.Close(NomnomlKinds.Classifier);
    }

    /// <summary>A node's name, then a compartment for each bar past it.</summary>
    private static void Named(Cursor line, int end)
    {
        var bars = Bars(line.Written, line.At, end);
        var first = bars.Count == 0 ? end : bars[0];

        line.Space();
        if (first > line.At) line.Words(NomnomlRoles.Id, first);

        for (var at = 0; at < bars.Count; at++)
        {
            line.Space();
            line.Token(Bar);
            Compartment(line, at + 1 < bars.Count ? bars[at + 1] : end);
        }

        line.Space();
    }

    /// <summary>
    /// One compartment past a node's name: the nodes written in it, which make the node a group, or its members, one to
    /// a semicolon.
    /// </summary>
    private static void Compartment(Cursor line, int stop)
    {
        line.Open();

        while (line.At < stop)
        {
            var before = line.At;
            line.Space();
            if (line.At >= stop) break;

            var end = Ending(line.Written, line.At, stop);

            if (line.Sees(Opens)) Chained(line, end);
            else if (end > line.At) line.Words(NomnomlRoles.Member, end);

            line.Space();
            if (line.At < stop) line.Token(Semicolon);
            if (line.At == before) break;
        }

        line.Close(NomnomlKinds.Compartment);
    }

    // ── Finding the ends of things ──────────────────────────────────────────

    /// <summary>Where the bracket opened before <paramref name="at"/> is closed, or -1 where nothing closes it.</summary>
    private static int Matching(string written, int at, int stop)
    {
        var depth = 1;
        for (var index = at; index < stop && index < written.Length; index++)
        {
            if (written[index] == Escape) index++;
            else if (written[index] == '[') depth++;
            else if (written[index] == ']' && --depth == 0) return index;
        }

        return -1;
    }

    /// <summary>Where each bar outside any bracket stands, between <paramref name="at"/> and <paramref name="stop"/>.</summary>
    private static List<int> Bars(string written, int at, int stop)
    {
        var bars = new List<int>();
        var depth = 0;

        for (var index = at; index < stop; index++)
        {
            if (written[index] == Escape) index++;
            else if (written[index] == '[') depth++;
            else if (written[index] == ']') depth--;
            else if (written[index] == '|' && depth == 0) bars.Add(index);
        }

        return bars;
    }

    /// <summary>Where the next node starts, or <paramref name="stop"/> where none does before it.</summary>
    private static int Bracket(string written, int at, int stop)
    {
        for (var index = at; index < stop; index++)
        {
            if (written[index] == Escape) index++;
            else if (written[index] == '[') return index;
        }

        return stop;
    }

    /// <summary>Where the next association or node starts, or <paramref name="stop"/> where neither does before it.</summary>
    private static int Found(string written, int at, int stop)
    {
        for (var index = at; index < stop; index++)
        {
            if (written[index] == Escape) { index++; continue; }
            if (written[index] == '[') return index;
            if (Joined(written, index) is not null) return index;
        }

        return stop;
    }

    /// <summary>Where what is being read in a compartment ends: its semicolon, or the compartment's own end.</summary>
    private static int Ending(string written, int at, int stop)
    {
        var depth = 0;

        for (var index = at; index < stop; index++)
        {
            if (written[index] == Escape) index++;
            else if (written[index] == '[') depth++;
            else if (written[index] == ']') depth--;
            else if (written[index] == ';' && depth == 0) return index;
        }

        return stop;
    }

    /// <summary>How many brackets a line opens past those it closes — what says a group runs on past this line.</summary>
    private static int Depth(string text)
    {
        var depth = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == Escape) index++;
            else if (text[index] == '[') depth++;
            else if (text[index] == ']') depth--;
        }

        return depth;
    }

    private static ContentNode Space(string text) => ContentNode.Leaf(Kinds.Space, text, Roles.Trivia);

    /// <summary>
    /// A line of the source: what is on it, from <see cref="Start"/> to <see cref="End"/>, and the characters that ended
    /// it, from <see cref="End"/> to <see cref="Stop"/>.
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

        /// <summary>Where what the line says starts and ends, less the space either side of it.</summary>
        public (int From, int To) Text(string source)
        {
            var from = Start;
            while (from < End && char.IsWhiteSpace(source[from])) from++;

            var to = End;
            while (to > from && char.IsWhiteSpace(source[to - 1])) to--;

            return (from, to);
        }
    }

    /// <summary>
    /// Where the reading of one line has come to, and the pieces read so far — which is all nomnoml needs to read a line:
    /// space, a token, words to where something ends, and pieces made of pieces.
    /// </summary>
    private sealed class Cursor(string text)
    {
        private readonly List<ContentNode> pieces = [];
        private readonly Stack<int> groups = new();

        /// <summary>What is read: the line less the space after it.</summary>
        public string Written { get; } = text.TrimEnd();

        public int At { get; private set; }

        public bool Done => At >= Written.Length;

        /// <summary>Why the last read that failed did, where that was how something was written — or null.</summary>
        private string? Reason { get; set; }

        public bool Sees(string token) => !Done && Written.AsSpan(At).StartsWith(token, StringComparison.Ordinal);

        /// <summary>Takes <paramref name="token"/> where it is written next.</summary>
        public bool Token(string token, string role = Roles.Separator)
        {
            if (!Sees(token)) return false;

            Add(ContentNode.Leaf(Kinds.Token, token, role));
            return true;
        }

        /// <summary>Takes the space written next, if any.</summary>
        public void Space()
        {
            var past = At;
            while (past < Written.Length && char.IsWhiteSpace(Written[past])) past++;

            if (past > At) Add(ContentNode.Leaf(Kinds.Space, Written[At..past], Roles.Trivia));
        }

        /// <summary>
        /// Takes the space left for something still to be written: the space next — and, where nothing more is written, the
        /// space after the end of the line as well, which is where the reader will type what follows a colon.
        /// </summary>
        public void Room()
        {
            if (!Done)
            {
                Space();
                return;
            }

            var past = At;
            while (past < text.Length && char.IsWhiteSpace(text[past])) past++;

            if (past > At) Add(ContentNode.Leaf(Kinds.Space, text[At..past], Roles.Trivia));
        }

        /// <summary>Takes everything still to be read as words.</summary>
        public void Words(string role) => Add(ContentNode.Leaf(NomnomlKinds.Words, Written[At..], role));

        /// <summary>Takes words up to <paramref name="end"/>, less the space before it.</summary>
        public void Words(string role, int end) =>
            Add(ContentNode.Leaf(NomnomlKinds.Words, Written[At..Math.Clamp(end, At, Written.Length)].TrimEnd(), role));

        /// <summary>Starts a piece holding what is read from here until <see cref="Close"/>.</summary>
        public void Open() => groups.Push(pieces.Count);

        /// <summary>Ends the piece <see cref="Open"/> started, as a <paramref name="kind"/> holding everything read since.</summary>
        public void Close(string kind, string role = Roles.Element)
        {
            var from = groups.Pop();
            var node = ContentNode.Branch(kind, pieces.GetRange(from, pieces.Count - from), role);

            pieces.RemoveRange(from, pieces.Count - from);
            pieces.Add(node);
        }

        public readonly record struct Mark(int At, int Pieces, int Groups);

        public Mark Save() => new(At, pieces.Count, groups.Count);

        /// <summary>Goes back to <paramref name="mark"/>, forgetting everything read since.</summary>
        public void Restore(Mark mark)
        {
            At = mark.At;
            pieces.RemoveRange(mark.Pieces, pieces.Count - mark.Pieces);
            while (groups.Count > mark.Groups) groups.Pop();
            Reason = null;
        }

        /// <summary>Goes back to <paramref name="mark"/> and says why the reading that got there could not go on. Always false.</summary>
        public bool Undo(Mark mark, string reason)
        {
            Restore(mark);
            return Fail(reason);
        }

        /// <summary>Fails a read with the reason. Always false.</summary>
        public bool Fail(string reason)
        {
            Reason = reason;
            return false;
        }

        /// <summary>What was read, as a <paramref name="kind"/>.</summary>
        public ContentNode Read(string kind, string role = Roles.Element) => ContentNode.Branch(kind, [.. pieces], role);

        /// <summary>The line as written, held with why it could not be read.</summary>
        public ContentNode Shown(string shape) => ContentNode.Shown(Written, Reason ?? shape);

        private void Add(ContentNode piece)
        {
            pieces.Add(piece);
            At += piece.Width;
        }
    }
}
