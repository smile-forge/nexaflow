using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Requirement;

/// <summary>
/// Reads a requirement diagram — a <c>requirementDiagram</c> block: the requirements written in it, the elements that meet them, the
/// fields inside each, what holds between them, and the lines that lay it out and style it — and says how what is written into one is
/// spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>). The header is optional, since a <c>requirementDiagram</c> fence already
/// says what the block is.
/// </para>
/// <para>
/// The rules are SysML's, as Mermaid writes them. A requirement is a kind, a name and its fields between braces — the line opening
/// them takes every line under it until the <c>}</c> closing them, one field to a line; a relation is two names with the word for what
/// holds between them written on the line joining them, either way round. A statement ends where its line does, except inside quotes:
/// a name in quotes runs to the quote closing it, across line endings, as Mermaid's lexer reads a string.
/// </para>
/// <para>
/// <strong>A name ends at the punctuation a relation is drawn with</strong>, so one holding a dash, a brace or a colon is
/// written in quotes — which is how Mermaid writes a name holding anything at all. One written without them may hold spaces,
/// <c>customer portal</c>, as Mermaid's names do. A <c>#</c> starting a line starts a comment.
/// </para>
/// </summary>
public sealed class RequirementParser : ITranspile
{
    private RequirementParser()
    {
    }

    /// <summary>The language a requirement diagram is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "requirementDiagram";

    /// <summary>The words a requirement diagram's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

    /// <summary>The kinds of requirement SysML names, each drawn in guillemets over the name.</summary>
    public static readonly string[] Requirements =
    [
        "requirement", "functionalRequirement", "interfaceRequirement",
        "performanceRequirement", "physicalRequirement", "designConstraint",
    ];

    /// <summary>What a thing the requirements are about is written as.</summary>
    public const string ElementWord = "element";

    public const string DirectionWord = "direction";

    /// <summary>What opens the fields of a requirement or an element, and what closes them.</summary>
    public const string Opening = "{";
    public const string Closing = "}";

    /// <summary>What gives something a class where it is named.</summary>
    public const string Given = ":::";

    /// <summary>The line a relation is drawn with, and the arrow at the end it reaches.</summary>
    public const string Joins = "-";
    public const string Onward = "->";

    /// <summary>The arrow of a relation written the other way round, with the end it reaches named first.</summary>
    public const string Backward = "<-";

    /// <summary>What may hold between two of them, drawn on the line joining them.</summary>
    public static readonly string[] Holdings =
        ["contains", "copies", "derives", "satisfies", "verifies", "refines", "traces"];

    /// <summary>The one of them drawn as a whole and its parts rather than as an arrow.</summary>
    public const string Holding = "contains";

    /// <summary>How much of a risk it is that a requirement is not met.</summary>
    public static readonly string[] Risks = ["low", "medium", "high"];

    /// <summary>How it is shown that one is met.</summary>
    public static readonly string[] Methods = ["analysis", "inspection", "test", "demonstration"];

    /// <summary>The ways a diagram is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>The fields a requirement holds.</summary>
    public const string IdKey = "id";
    public const string TextKey = "text";
    public const string RiskKey = "risk";
    public const string MethodKey = "verifymethod";

    /// <summary>And the fields an element holds.</summary>
    public const string TypeKey = "type";
    public const string RefKey = "docref";

    public static readonly string[] Keys = [IdKey, TextKey, RiskKey, MethodKey, TypeKey, RefKey];

    /// <summary>
    /// The characters a bare name ends at: the colon a field and a class are written after, the punctuation a relation is drawn
    /// with, the braces fields are written between, the comma between names, the semicolon closing a line, and the quotes a name
    /// holding any of them is written in.
    /// </summary>
    public const string Stops = ":;,-{}<>\" \t\r\n";

    /// <summary>The <c>classDef</c>, <c>class</c> and <c>style</c> lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, RequirementRoles.Id, RequirementRoles.Class, "requirement");

    /// <summary>The words a block may open with: the kinds of requirement, and the element.</summary>
    private static readonly string[] Boxes = [.. Requirements, ElementWord];

    /// <summary>Every word a line may open with: what a block is, and the lines that lay the diagram out and style it.</summary>
    private static readonly string[] Openers =
    [
        .. Boxes, DirectionWord,
        MermaidStyling.ClassDefWord, MermaidStyling.ClassWord, MermaidStyling.StyleWord,
    ];

    private const string BlockShape = "A requirement is written requirement Foo {, its fields, then the } closing them.";
    private const string FieldShape = "A field is written id: 1, text: what it asks for, risk: high or verifymethod: test.";
    private const string RelationShape = "A relation joins two of them: A - satisfies -> B, or B <- satisfies - A.";
    private const string DirectionShape = "A direction line lays the diagram out: direction LR.";
    private const string StrayShape = "A } closes the fields a requirement opened with {.";

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

    /// <inheritdoc/>
    /// <remarks>Spelled as Mermaid spells it: nothing a requirement diagram writes is spelled a way of its own.</remarks>
    public static ContentChange? Rewrite(ContentChange change) => MermaidParser.Rewrite(change);

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

        // A # starts a comment running to the end of its row, as Mermaid's lexer reads one where a line starts — but for an entity code.
        if (text[0] == '#' && MermaidLine.EntityLength(text, 0) == 0)
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
            if (Heading(text, from) is { } header)
            {
                lines.Add(MermaidParser.Line(source, row.Start, from, [header], to, row));
                return row.Stop;
            }
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which is as much of the source as it takes rather than as much as the row holds — a name in quotes may carry
        // it past its row.
        var end = Ended(source, from);

        // A requirement or an element opening its fields takes everything after its brace until the brace closing them.
        if (Begun(source, from, end) is var (opened, after)) return Body(source, row, from, after, opened, lines);

        // Any other statement: as much of it as is read is what was written, and the rest is the line's.
        return MermaidParser.StatementLine(source, row.Start, from, Statement(source[from..end], from), end, lines);
    }

    /// <summary>
    /// A requirement's or an element's fields: the line opening them, everything after its brace — one field to a line, the last on the
    /// closing brace's line or on one of its own — and the brace closing them, read as one statement and made one line of the tree. A
    /// text in quotes runs to the quote closing it, across line endings, as Mermaid's lexer reads a string. Where nothing closes them,
    /// the line opening them is a statement on its own, said to be never closed, and the lines under it are read as they would be
    /// without it.
    /// </summary>
    /// <param name="after">Where what follows the opening brace starts.</param>
    private static int Body(string source, MermaidParser.Row row, int from, int after, ContentNode opened, List<ContentNode> lines)
    {
        var pieces = new List<ContentNode> { opened };
        var end = after;

        for (var at = after; at < source.Length;)
        {
            var stop = at;
            while (stop < source.Length && source[stop] is not ('\n' or '}'))
            {
                if (source[stop] == '"' && source.IndexOf('"', stop + 1) is var close and >= 0) stop = close;
                stop++;
            }

            if (stop >= source.Length) break;

            var (start, finish) = (at, stop);
            while (start < finish && char.IsWhiteSpace(source[start])) start++;
            while (finish > start && char.IsWhiteSpace(source[finish - 1])) finish--;

            if (finish > start)
            {
                if (start > end) pieces.Add(MermaidParser.Space(source[end..start], end));
                pieces.Add(Within(source[start..finish], start));
                end = finish;
            }

            if (source[stop] == '}')
            {
                var shut = stop + 1 < source.Length && source[stop + 1] == ';' ? stop + 2 : stop + 1;
                if (stop > end) pieces.Add(MermaidParser.Space(source[end..stop], end));
                pieces.Add(Shutter(source[stop..shut], stop));

                return MermaidParser.StatementLine(source, row.Start, from, ContentNode.Branch(RequirementKinds.Block, pieces, offset: from), shut, lines);
            }

            at = stop + 1;
        }

        // The line opening them ends on the row its brace is on, which a name in quotes may make a later one.
        var opening = MermaidParser.Row.Containing(source, Math.Max(from, after - 1));
        lines.Add(MermaidParser.Line(source, row.Start, from,
            [ContentNode.Branch(RequirementKinds.Block, [opened], offset: from).Saying("These fields are never closed: } closes them.")], after, opening));
        return opening.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending or a comment is found on, outside
    /// a quoted name, and the end of the block where neither follows. A quote nothing closes is a quote like any other character, so
    /// one typed by mistake costs the line it is on rather than everything written under it.
    /// </summary>
    private static int Ended(string source, int from)
    {
        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n' || source.AsSpan(at).StartsWith(Comment, StringComparison.Ordinal)) return MermaidParser.Row.Containing(source, at).End;
            if (character != '"') continue;

            var close = source.IndexOf('"', at + 1);
            if (close >= 0) at = close;
        }

        return MermaidParser.Row.Containing(source, source.Length).End;
    }

    /// <summary>The header — <c>requirementDiagram</c>, with nothing after it — where the first line saying anything is one.</summary>
    private static ContentNode? Heading(string text, int at)
    {
        if (MermaidLine.Keyword(text, Keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Arguments(rest[gap..], after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }

    /// <summary>What follows the keyword, which is nothing: a requirement diagram is laid out by a <c>direction</c> line of its own.</summary>
    private static ContentNode Arguments(string arguments, int at) =>
        MermaidLine.Of(arguments, at).Done
            ? ContentNode.Leaf(Kinds.Verbatim, arguments, MermaidRoles.Arguments, offset: at)
            : ContentNode.Shown(arguments, "Nothing follows requirementDiagram — direction LR lays it out.", MermaidRoles.Arguments, at);

    /// <summary>A statement written on its own: a relation, a name on a line of its own, a direction, or a styling line.</summary>
    private static ContentNode Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        if (line.Written.Trim() is Closing or Closing + ";") return line.Shown(StrayShape);

        return MermaidLine.Keyword(line.Written, Bare, Openers) switch
        {
            null => Related(line),
            DirectionWord => Towards(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, RequirementKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, RequirementKinds.CssClass),
            MermaidStyling.StyleWord => Styling.Styled(line, RequirementKinds.Style),
            var word => Declared(line, word),
        };
    }

    /// <summary>
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a name's quotes, and where a field's
    /// value is still to come after its colon. A name with nothing in it stands for a box of its own while it is written, so the hole in
    /// it is where the writing goes.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or MermaidKinds.Name or RequirementKinds.Value;

    /// <summary>Whether a character carries a bare name or a class name on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>The line opening a requirement or an element: what kind of thing it is, what it is called, and the brace.</summary>
    private static ContentNode Declared(MermaidLine line, string word)
    {
        line.Word(word, MermaidKinds.Key, RequirementRoles.Kind, Bare);
        line.Room();

        if (!Named(line)) return line.Shown(BlockShape);

        line.Room();
        if (!line.Token(Opening, Roles.Open)) return line.Shown(BlockShape);

        line.Space();
        return line.Done ? line.Read(RequirementKinds.Opens) : line.Shown(BlockShape);
    }

    /// <summary>A relation between two of them, written either way round.</summary>
    private static ContentNode Related(MermaidLine line)
    {
        line.Room();
        if (!Named(line)) return line.Shown(RelationShape);

        line.Space();

        // A name on a line of its own writes the box, which is where ::: gives one a class.
        if (line.Done || line.Sees(";")) return line.Closed(RequirementKinds.Naming, RelationShape);

        // Written the other way round, the end the relation reaches is named first — which is what the <- says.
        var back = line.Sees(Backward);
        if (!line.Token(back ? Backward : Joins, RequirementRoles.Arrow)) return line.Shown(RelationShape);

        line.Room();
        line.Setting(RequirementRoles.Says, Between, until: Stops);
        line.Room();

        if (!line.Token(back ? Joins : Onward, RequirementRoles.Arrow)) return line.Shown(RelationShape);

        line.Room();
        if (!Named(line)) return line.Shown(RelationShape);

        return line.Closed(RequirementKinds.Relation, RelationShape);
    }

    /// <summary>The way the diagram is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(RequirementRoles.Towards, Wayward, until: Stops);

        return line.Closed(RequirementKinds.Direction, DirectionShape);
    }

    // ── The fields between the braces ───────────────────────────────────────

    /// <summary>
    /// The line opening a requirement's or an element's fields — <c>requirement r1 {</c>, its name in quotes running on across rows where
    /// it does — and where what follows the brace starts; or null where the line opens none.
    /// </summary>
    private static (ContentNode Opened, int After)? Begun(string source, int from, int end)
    {
        var text = source[from..end];
        if (MermaidLine.Keyword(text, Bare, Boxes) is not { } word) return null;

        var brace = -1;
        for (var at = 0; at < text.Length && brace < 0; at++)
        {
            if (text[at] == '{') brace = at;
            else if (text[at] == '"' && text.IndexOf('"', at + 1) is var close and >= 0) at = close;
        }

        if (brace < 0) return null;

        return Declared(MermaidLine.Of(text[..(brace + 1)], from), word) is { Kind: RequirementKinds.Opens } opened ? (opened, from + brace + 1) : null;
    }

    /// <summary>A line between the braces: one of the fields a requirement or an element holds, and what it is set to.</summary>
    private static ContentNode Within(string text, int at)
    {
        var line = MermaidLine.Of(text, at);
        line.Room();

        // A line with nothing written on it yet is one a field is still to be written on.
        if (line.Done) return line.Read(RequirementKinds.Field);

        if (MermaidLine.Keyword(line.Rest, Bare, Keys) is not { } key) return line.Shown(FieldShape);

        line.Word(key, MermaidKinds.Key, RequirementRoles.Key, Bare);
        line.Space();

        if (!line.Token(":", Roles.Open)) return line.Shown(FieldShape);

        line.Open();
        line.Room();
        Valued(line, key);
        line.Close(RequirementKinds.Value, RequirementRoles.Value);

        return line.Done ? line.Read(RequirementKinds.Field) : line.Shown(FieldShape);
    }

    private static ContentNode Shutter(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        line.Room();
        line.Token(Closing, Roles.Close);

        return line.Closed(RequirementKinds.Shut, StrayShape);
    }

    /// <summary>
    /// What a field is set to: one of the few words a risk and a verification method may be, and otherwise anything at all — in
    /// quotes, where it is written in them, which is how Mermaid writes one with markdown in it.
    /// </summary>
    private static void Valued(MermaidLine line, string key)
    {
        if (string.Equals(key, RiskKey, StringComparison.OrdinalIgnoreCase))
        {
            line.Setting(RequirementRoles.Value, Risked);
            return;
        }

        if (string.Equals(key, MethodKey, StringComparison.OrdinalIgnoreCase))
        {
            line.Setting(RequirementRoles.Value, Verified);
            return;
        }

        if (line.Next == '"')
        {
            var mark = line.Save();
            if (line.Quoted(RequirementRoles.Value)) return;

            line.Restore(mark);
        }

        line.Words(RequirementRoles.Value);
    }

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One requirement where it is named: what it is called, and the class <c>:::</c> gives it.</summary>
    private static bool Named(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!line.Name(RequirementRoles.Id, Worded)) return line.Undo(mark);

        // ::: gives it a class, or several with a comma between them.
        if (line.Sees(Given))
        {
            line.Token(Given);

            do
            {
                if (!line.Name(RequirementRoles.Class, Bare)) return line.Undo(mark);
            }
            while (line.Token(","));
        }

        line.Close(RequirementKinds.Named, RequirementRoles.Id);
        return true;
    }

    /// <summary>
    /// Where a name written without quotes at <paramref name="at"/> ends: Mermaid's rule, a word character and then anything but the
    /// punctuation a line is read by — so a name holds spaces, <c>customer portal</c> — less the space at its end.
    /// </summary>
    private static int Worded(string text, int at)
    {
        if (at >= text.Length || !(char.IsLetterOrDigit(text[at]) || text[at] == '_')) return at;

        var end = at;
        while (end < text.Length && text[end] is not (':' or ',' or ';' or '\r' or '\n' or '{' or '<' or '>' or '-' or '=' or '"')) end++;
        while (end > at && char.IsWhiteSpace(text[end - 1])) end--;

        return end;
    }

    private static string? Between(string said) =>
        Holdings.Contains(said, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"What holds between two requirements is {string.Join(", ", Holdings.SkipLast(1))} or {Holdings[^1]}.";

    private static string? Risked(string said) =>
        Risks.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A risk is low, medium or high.";

    private static string? Verified(string said) =>
        Methods.Contains(said, StringComparer.OrdinalIgnoreCase)
            ? null
            : "A requirement is verified by analysis, inspection, test or demonstration.";

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A requirement diagram is laid out TB, TD, BT, RL or LR.";
}
