using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// Reads a structural C4 diagram — a <c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>, <c>C4Dynamic</c> or
/// <c>C4Deployment</c> block — and says how what is written into one, or into a <c>C4Sequence</c>, is spelled. The header is
/// Mermaid's; the body is C4-PlantUML's macro set, which is one shape throughout — a name, its arguments between brackets, and the
/// brace that may open a block after them:
///
/// <code>
/// Person(customer, "Banking Customer", "A customer of the bank", $tags="v1")
/// System_Boundary(c1, "Internet Banking", "System") {
/// Rel(customer, spa, "Submits credentials", "HTTPS", $index=Index())
/// }
/// </code>
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a C4 diagram. The header is optional, since a fence naming the diagram already says what the block is.
/// </para>
/// <para>
/// <strong>This says only what a line is.</strong> A macro, a boundary opening one, the line closing one, a line a pasted diagram
/// brought with it — and which of the arguments name an element, which is what lets a rename carry. A value in quotes runs to the
/// quote that closes it, across a line ending where that is where it stands, as Mermaid's lexer reads one, so a macro is as much of
/// the source as its quotes take; every other line is its row. What a macro <em>means</em> is the stages' its language offers the
/// engine (<see cref="Stages.ResolveMacros"/>), the same for a diagram laid out as a graph and one laid out as a timeline
/// (<see cref="C4SequenceParser"/>), which reads its macros with the pieces here.
/// </para>
/// <para>
/// A C4 element is named inside the brackets of a macro — <c>Person(customer, "Banking Customer")</c> — and every relationship
/// after it reaches the element by that name. A quote, a comma or a bracket written into the name closes the macro early and leaves
/// the rest of the line as something else, so none of them goes in. C4 names its elements in <see cref="SequenceRoles.Id"/>, the
/// role a sequence diagram names participants in, but the two do not agree on what a name may hold: a sequence takes anything that
/// is not one of its few stops, and C4 takes only the letters an identifier is written with.
/// </para>
/// </summary>
public sealed class C4Parser : ITranspile
{
    private C4Parser()
    {
    }

    /// <summary>The language a structural C4 diagram is written in: what a fence names it by.</summary>
    public const string Language = "c4";

    /// <summary>The words a structural C4 diagram's header is written with — Mermaid's five, and the language's name.</summary>
    public static readonly string[] Keywords = ["C4Context", "C4Container", "C4Component", "C4Dynamic", "C4Deployment", Language];

    /// <summary>The header a <c>C4Dynamic</c> is written with, which numbers its relationships without being asked.</summary>
    public const string Dynamic = "C4Dynamic";
    /// <summary>The macros that open a boundary round everything written until the line closing it.</summary>
    public static readonly string[] Boundaries =
        ["Boundary", "Enterprise_Boundary", "System_Boundary", "Container_Boundary", "Deployment_Node",
         "Node", "Node_L", "Node_R"];

    /// <summary>The accessible description C4 writes with a space after it rather than a colon, which only C4's grammar reads.</summary>
    public const string AccessibleDescription = "accDescription";

    /// <summary>And the macro that closes one, which <c>}</c> does as well.</summary>
    public const string Shutter = "Boundary_End";

    /// <summary>What a relationship's two ends are called where they are given by name rather than by position.</summary>
    public static readonly string[] Ends = ["from", "to"];

    /// <summary>And what an element's own name is called.</summary>
    public const string Named = "alias";

    private const string MacroShape = "A macro is written Person(alias, \"Label\"), with its arguments between brackets.";
    private const string ShutShape = "A boundary is closed by } or by Boundary_End().";


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

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between an argument's quotes.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) => node.Kind == MermaidKinds.Quoted;

    // ── Reading the block ───────────────────────────────────────────────────

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
            if (Heading(text, from, Keywords) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;
        if (MermaidParser.SpacedDescription(source, row, from, to, AccessibleDescription, lines) is { } described) return described;

        // A line of C4's own, as much of the source as it takes; anything else is a line nobody meant to write, since a structural
        // diagram is written in C4's words alone. As much of it as is read is what was written, and the rest is the line's.
        var end = Reach(source, from, row);
        var written = source[from..end];
        var said = Own(written, from) ?? Stray(written, from);
        var took = said is null ? end : from + said.Width;
        var last = MermaidParser.Row.Containing(source, Math.Max(from, took - 1));

        lines.Add(MermaidParser.Line(source, row.Start, from,
                                     [said ?? ContentNode.Leaf(MermaidKinds.Statement, written, offset: from)], took, last));
        return last.Stop;
    }
    /// <summary>
    /// Where the statement starting at <paramref name="from"/> on <paramref name="row"/> ends: for a macro, the end of the row a line
    /// ending outside its quotes first ends, or the row a comment outside them is written on — a quote nothing closes being a
    /// character like any other; for every other line, its row.
    /// </summary>
    internal static int Reach(string source, int from, MermaidParser.Row row)
    {
        if (Heads(source[from..row.End]) is not { } name) return row.End;

        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n' || source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal))
            {
                // A boundary's brace may stand on the line under it, as Mermaid's grammar has it.
                if (character == '\n' && Boundaries.Contains(name, StringComparer.OrdinalIgnoreCase) && BraceBelow(source, at) is { } brace) return brace;

                return MermaidParser.Row.Containing(source, at).End;
            }

            if (character != '"') continue;

            var close = source.IndexOf('"', at + 1);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>Where the row under the line ending at <paramref name="ending"/> ends, where that row is a boundary's brace and nothing else — or null.</summary>
    private static int? BraceBelow(string source, int ending)
    {
        var below = MermaidParser.Row.At(source, ending + 1);
        var (from, to) = below.Text(source);

        return to - from == 1 && source[from] == '{' ? below.End : null;
    }

    /// <summary>
    /// The header — one of <paramref name="keywords"/>, and anything after it held as written — or null where the line says
    /// something else, which a fence naming the diagram needs no header before.
    /// </summary>
    internal static ContentNode? Heading(string text, int at, string[] keywords)
    {
        if (MermaidLine.Keyword(text, keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Arguments(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>What follows the keyword: nothing but a comment, since everything a C4 diagram says, it says on a line of its own.</summary>
    private static ContentNode Arguments(string arguments, int at) =>
        MermaidLine.Of(arguments, at).Done
            ? ContentNode.Leaf(Kinds.Verbatim, arguments, MermaidRoles.Arguments, offset: at)
            : ContentNode.Shown(arguments, "Nothing follows a C4 header — it is written on a line of its own.", MermaidRoles.Arguments, at);

    /// <summary>
    /// A line of C4's own: a PlantUML comment, a wrapper a pasted diagram brought with it, a title, the brace closing a boundary, or
    /// a macro — or null where the line is none of them.
    /// </summary>
    internal static ContentNode? Own(string text, int at)
    {
        var line = MermaidLine.Of(text, at, comments: false);

        // PlantUML's own comment, which an apostrophe opens; a %% comment is every diagram's, and MermaidLine takes it.
        if (line.Written.TrimStart().StartsWith('\'')) return Aside(line, Kinds.Comment, Roles.Trivia);

        // The wrapper lines a diagram pasted from PlantUML brings with it.
        if (line.Written.TrimStart() is ['@', ..] or ['!', ..]) return Aside(line, C4Kinds.Aside, C4Roles.Aside);

        var read = MermaidLine.Of(text, at);

        if (MermaidLine.Keyword(read.Written, MermaidLine.TitleWord) is not null) return read.Title();
        if (read.Written.Trim() is "}" or "})") return Shutting(read);
        return Heads(read.Written) is { } name ? Called(read, name) : null;
    }

    /// <summary>A line that is no line of C4's own, held as written with the shape a macro has — or null for one saying nothing.</summary>
    private static ContentNode? Stray(string text, int at)
    {
        var read = MermaidLine.Of(text, at);
        return read.Done ? null : read.Shown(MacroShape);
    }
    /// <summary>The name of the macro a line calls, or null where it calls none.</summary>
    public static string? Heads(string written)
    {
        var end = 0;
        while (end < written.Length && (char.IsLetterOrDigit(written[end]) || written[end] == '_')) end++;
        if (end == 0) return null;

        var past = end;
        while (past < written.Length && written[past] is ' ' or '\t') past++;

        return past < written.Length && written[past] == '(' ? written[..end] : null;
    }

    /// <summary>
    /// Where the argument written at <paramref name="at"/> ends: the comma or the bracket closing it, outside any quotes and
    /// any brackets of its own — which is what lets <c>"C#, ASP.NET Core"</c> and <c>Index()</c> be one argument each.
    /// </summary>
    public static int Ending(string written, int at)
    {
        var depth = 0;
        var quoted = false;

        for (var index = at; index < written.Length; index++)
        {
            var character = written[index];

            if (character == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (character == '(') depth++;
            else if (character == ')' && depth == 0) return index;
            else if (character == ')') depth--;
            else if (character == ',' && depth == 0) return index;
        }

        return written.Length;
    }

    /// <summary>Whether a character carries an element's name on: the letters an identifier is written with.</summary>
    public static bool Bare(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '.' || character > '\u007F';

    /// <summary>And whether one carries a bare argument on, which is anything that does not close it.</summary>
    internal static bool Argued(char character) => character is not ('"' or ',' or '(' or ')') && !char.IsControl(character);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A line read and drawn as nothing: a PlantUML comment, or a wrapper a pasted diagram brought with it.</summary>
    private static ContentNode Aside(MermaidLine line, string kind, string role)
    {
        line.Room();
        line.Words(role);

        return line.Read(kind);
    }

    /// <summary>The <c>}</c> that closes a boundary, which a <c>)</c> may follow.</summary>
    private static ContentNode Shutting(MermaidLine line)
    {
        line.Room();
        line.Token("}", Roles.Close);
        line.Token(")", Roles.Close);

        return line.Closed(C4Kinds.Ends, ShutShape);
    }

    /// <summary>One macro call: its name, its arguments, and the brace that may open a block after them.</summary>
    private static ContentNode Called(MermaidLine line, string name)
    {
        var kind = Boundaries.Contains(name, StringComparer.OrdinalIgnoreCase) ? C4Kinds.Boundary
                 : string.Equals(name, Shutter, StringComparison.OrdinalIgnoreCase) ? C4Kinds.Ends
                 : C4Kinds.Macro;

        var aliases = Aliased(name);

        line.Room();
        line.Word(name, MermaidKinds.Key, C4Roles.Macro, Bare);
        line.Space();

        if (!line.Token("(", Roles.Open)) return line.Shown(MacroShape);

        line.Room();
        line.Open();

        var at = 0;
        while (!line.Done && line.Next != ')')
        {
            if (Argued(line, aliases, at)) at++;

            line.Room();
            if (line.Next != ',') break;

            line.Token(",");
            line.Room();
        }

        line.Close(MermaidKinds.Properties);

        if (!line.Token(")", Roles.Close)) return line.Shown(MacroShape);

        line.Space();
        line.Token("{", Roles.Open);

        return line.Closed(kind, MacroShape);
    }

    /// <summary>One argument: given by name where a <c>$</c> opens it, and by position otherwise.</summary>
    private static bool Argued(MermaidLine line, Aliases aliases, int at)
    {
        line.Open();

        if (line.Next != '$')
        {
            Valued(line, aliases.At.Contains(at));
            line.Close(MermaidKinds.Property);

            return true;
        }

        line.Token("$");

        var mark = line.At;
        if (!line.Name(C4Roles.Key, MermaidLine.Letter))
        {
            line.Close(MermaidKinds.Property);
            return false;
        }

        var key = line.Written[mark..line.At];

        line.Space();
        line.Token("=");
        line.Room();
        Valued(line, aliases.Keys.Contains(key, StringComparer.OrdinalIgnoreCase));
        line.Close(MermaidKinds.Property);

        return false;
    }

    /// <summary>What an argument is: the name of an element, or a value in quotes or bare.</summary>
    private static void Valued(MermaidLine line, bool alias)
    {
        if (alias)
        {
            if (line.Next == '"')
            {
                line.Name(SequenceRoles.Id, Bare);
                return;
            }

            // A name with nothing written in it yet is one still to be written, so it is read as an empty one.
            line.Open();
            if (line.At <= line.Written.Length) line.Words(SequenceRoles.Id, Ending(line.Written, line.At));
            line.Close(MermaidKinds.Name, SequenceRoles.Id);

            return;
        }

        if (line.Next == '"')
        {
            line.Quoted(C4Roles.Value, what: "value");
            return;
        }

        line.Words(C4Roles.Value, Ending(line.Written, line.At));
    }

    /// <summary>Which of a macro's arguments name an element — by position, and by the name they may be given under.</summary>
    private static Aliases Aliased(string name)
    {
        var word = name.ToLowerInvariant();

        if (word.StartsWith("relindex", StringComparison.Ordinal)) return new([1, 2], Ends);
        if (word.StartsWith("rel", StringComparison.Ordinal) || word.StartsWith("birel", StringComparison.Ordinal))
            return new([0, 1], Ends);

        if (word is "updaterelstyle") return new([0, 1], Ends);
        if (Boundaries.Contains(name, StringComparer.OrdinalIgnoreCase)) return new([], []);

        return Elemental(word) ? new([0], [Named]) : new([], []);
    }

    /// <summary>
    /// Whether a macro declares an element. The names are systematic —
    /// <c>{Person|System|Container|Component}{Db|Queue}?{_Ext}?</c> — so this reads them apart rather than listing every one.
    /// </summary>
    public static bool Elemental(string name)
    {
        var word = name.ToLowerInvariant();
        if (word.EndsWith("_ext", StringComparison.Ordinal)) word = word[..^4];

        foreach (var kind in new[] { "person", "system", "container", "component" })
        {
            if (!word.StartsWith(kind, StringComparison.Ordinal)) continue;

            return word[kind.Length..] is "" or "db" or "queue";
        }

        return false;
    }

    /// <summary>Which arguments of one macro name an element.</summary>
    private readonly record struct Aliases(IReadOnlyList<int> At, IReadOnlyList<string> Keys);

    // ── Writing back ────────────────────────────────────────────────────────

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>A change as a C4 diagram spells it.</summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role == SequenceRoles.Id
            ? text.All(Bare) ? text : null
            : MermaidParser.Spelled(part, text);
}
