using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Nomnoml;

/// <summary>
/// What a <c>nomnoml</c> block says. Unlike a Mermaid block, nothing in it names its type — the fence's language does, so
/// <see cref="MermaidParser.Parse"/> is handed this grammar and every line is a statement.
///
/// <para>
/// A node is written in brackets and a bar divides it into compartments: <c>[Name|a field|a method()]</c>, the first
/// compartment its name. What it is is written in angle brackets before that — <c>[&lt;abstract&gt;Shape]</c> — and a
/// compartment holding nodes of its own is a group, its own box drawn round them. Two nodes are joined by an operator
/// between them, with how many of each written either side of it: <c>[Order] 1 -&gt; 0..n [Line]</c>.
/// </para>
///
/// <para>
/// A directive is a hash, a name and a value; a comment is two slashes. Both are kept as written — the only one drawn is
/// <c>#direction</c>, which lays the diagram out.
/// </para>
/// </summary>
public sealed class NomnomlGrammar : IMermaidGrammar
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

    /// <summary>The characters that have to be escaped to be written.</summary>
    public const string Escapes = "[]|;\\";

    /// <summary>The directive that lays the diagram out, and what it may be set to.</summary>
    public const string DirectionWord = "direction";

    public static readonly string[] Ways = ["down", "up", "right", "left"];

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
    private const string JoinShape = "Two nomnoml nodes are joined by an operator between them: [A] -> [B], [A] -:> [B], [A] +- [B].";
    private const string DirectiveShape = "A nomnoml directive is a hash, a name and a value: #direction: right.";
    private const string NeverClosed = "This nomnoml node is never closed with ].";
    private const string NeverEnded = "This nomnoml group is never closed with ].";

    /// <inheritdoc/>
    /// <remarks>A nomnoml block has no header — its fence's language is what names it — so there are no arguments to read.</remarks>
    public ContentNode? Header(string arguments) => null;

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text, comments: false);
        line.Space();

        if (line.Sees(Slashes))
        {
            line.Words(Roles.Trivia);
            return line.Read(NomnomlKinds.Comment, Roles.Trivia);
        }

        return line.Sees(Hash) ? Directive(line) : Chain(line, line.Written.Length);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A group opened on one line and closed on another. The brackets are counted rather than matched a line at a time,
    /// so a group inside a group ends where its own bracket does; <see cref="Stretches"/> is read afresh for each
    /// statement, which is what lets the count belong to this one.
    /// </remarks>
    public IEnumerable<MermaidStretch> Stretches
    {
        get
        {
            var depth = 0;
            yield return new MermaidStretch(
                NomnomlKinds.Group,
                Opens: text => (depth = Depth(text)) > 0 ? Opened(text) : null,
                Ends: text => (depth += Depth(text)) <= 0,
                Inside: Said,
                Ended: Closing,
                Unclosed: NeverEnded);
        }
    }

    /// <inheritdoc/>
    /// <remarks>A node with nothing in it yet, the caret between its brackets.</remarks>
    public (string Text, int Caret)? Blank(ContentNode? above) => ("[]", 1);

    /// <inheritdoc/>
    /// <remarks>
    /// A bracket, a bar or a semicolon would end the name or the member it was typed into, so it is written after a
    /// backslash, which is how nomnoml writes one itself.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        text.Length == 1 && Escapes.Contains(text[0], StringComparison.Ordinal)
            ? new MermaidWriting(caret, caret, Escape + text, caret + 2)
            : null;

    /// <inheritdoc/>
    /// <remarks>Nothing is worked out after the fact: every part of a nomnoml block is written in its own characters.</remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [];

    /// <inheritdoc/>
    /// <remarks>In a node's name, in a member, and in what is written along an association.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) => node.Kind is MermaidKinds.Words;

    // ── Lines ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A setting: <c>#direction: right</c>, <c>#.abstract: fill=#8f8</c>. The name is whatever stands before the colon,
    /// so a character typed into one is part of the name rather than the end of it.
    /// </summary>
    private static ContentNode Directive(MermaidLine line)
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
    /// The line a group opens on: its bracket, what it says it is, its name, and the bar the nodes inside it follow. What
    /// closes it is on a line of its own, so there is nothing here to close.
    /// </summary>
    private static ContentNode? Opened(string text)
    {
        var line = MermaidLine.Of(text, comments: false);
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

    /// <summary>A line inside a group: whatever it says on its own.</summary>
    private ContentNode Said(string text) =>
        Statement(text) ?? ContentNode.Leaf(MermaidKinds.Statement, text);

    /// <summary>The line a group closes on, which is usually its bracket and nothing else.</summary>
    private ContentNode Closing(string text)
    {
        var line = MermaidLine.Of(text, comments: false);
        return line.Token(Closes, Roles.Close) && line.Done ? line.Read(NomnomlKinds.Node) : Said(text);
    }

    /// <summary>
    /// One or more nodes joined end to end — <c>[A] -&gt; [B] -&gt; [C]</c> — with what is written either side of each
    /// operator, and what the whole line says after a colon at the end of it.
    /// </summary>
    private static ContentNode Chain(MermaidLine line, int stop)
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
    /// The nodes and operators themselves, read into whatever group is open — as far as they go.
    ///
    /// <para>
    /// Where the line stops making sense the reading stops with it, and what is left is the line's own. Holding the whole
    /// line back instead would take what had already been read off the page as soon as its end was half typed.
    /// </para>
    /// </summary>
    private static bool Chained(MermaidLine line, int stop)
    {
        if (!Node(line, stop)) return false;

        while (true)
        {
            var mark = line.Save();
            line.Space();

            if (line.At >= stop || line.Sees(Colon)) { line.Restore(mark); return true; }

            Side(line, stop, NomnomlRoles.Near);
            line.Space();

            if (Ahead(line) is not { } operation) { line.Restore(mark); return true; }

            line.Token(operation, NomnomlRoles.Operator);
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
    private static void Side(MermaidLine line, int stop, string role)
    {
        var end = Found(line.Written, line.At, stop);
        if (end > line.At) line.Words(role, end);
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

    /// <summary>The association written where the line stands, or null where none is.</summary>
    private static string? Ahead(MermaidLine line) => Joined(line.Written, line.At);

    /// <summary>
    /// The whole association at <paramref name="at"/>, or null where none starts there.
    ///
    /// <para>
    /// Nomnoml builds one from three pieces — an end, a line, and another end — so the ways two nodes can be joined are
    /// every combination of those rather than a list somebody has to keep up to date. <c>--:&gt;</c> is a dashed line
    /// with an arrowhead of its own, and so is every other spelling that falls out of the same three lists.
    /// </para>
    /// </summary>
    private static string? Joined(string written, int at)
    {
        foreach (var head in Heads)
        {
            if (!Sees(written, at, head)) continue;

            foreach (var drawn in Lines)
            {
                if (!Sees(written, at + head.Length, drawn)) continue;

                foreach (var tail in Tails)
                    if (Sees(written, at + head.Length + drawn.Length, tail))
                        return head + drawn + tail;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="token"/> stands at <paramref name="at"/>. An empty token always does.</summary>
    private static bool Sees(string written, int at, string token) =>
        at + token.Length <= written.Length && string.CompareOrdinal(written, at, token, 0, token.Length) == 0;

    // ── Nodes ───────────────────────────────────────────────────────────────

    /// <summary>One node in brackets: what it is, what it is called, and the compartments past its name.</summary>
    private static bool Node(MermaidLine line, int stop)
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
    private static void Classified(MermaidLine line, int end)
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
    private static void Named(MermaidLine line, int end)
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
    private static void Compartment(MermaidLine line, int stop)
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
}
