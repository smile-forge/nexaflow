using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Er;

/// <summary>
/// Reads an entity relationship diagram — an <c>erDiagram</c> block: the entities written in it, the attributes inside them, how many
/// of each entity the other has at either end of a relationship, the subgraphs they are boxed into, and the lines that lay it out and
/// style it — and says how what is written into one is spelled.
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>). The header is optional, since an <c>erDiagram</c> fence already says
/// what the block is.
/// </para>
/// <para>
/// The rules are Mermaid's. An entity is a name, said again wherever it is used, with its attributes written between braces — the
/// line opening them takes every line under it until the <c>}</c> closing them, one attribute to a line. A relationship is two
/// entities with how many of each the other has written between them, as a pair of symbols or in words. A statement ends where its
/// line does, except inside quotes: a name, a label or what a relationship is called written in quotes runs to the quote closing
/// it, across line endings, as Mermaid's lexer reads a string.
/// </para>
/// <para>
/// <strong>What a name may hold is Mermaid's own rule</strong>: letters and digits, the dash, the dot, the underscore and the
/// star, and anything outside ASCII. A name holding anything else is written in quotes.
/// </para>
/// <para>
/// Which subgraph each line is in, which ends of a relationship name a subgraph, and what styles each entity are facts about the
/// whole block, worked out by the stages its language offers the engine (<see cref="Stages.ResolveGroups"/>,
/// <see cref="Stages.ResolveJoins"/>, <see cref="Stages.ResolveStyles"/>).
/// </para>
/// </summary>
public sealed class ErParser : ITranspile
{
    private ErParser()
    {
    }

    /// <summary>The language an ER diagram is written in: what a fence names it by, and the word its header is written with.</summary>
    public const string Language = "erDiagram";

    /// <summary>The words an ER diagram's header is written with.</summary>
    public static readonly string[] Keywords = [Language];

    /// <summary>What opens a comment running to the end of its row.</summary>
    private const string Comment = "%%";

    public const string SubgraphWord = "subgraph";
    public const string EndWord = "end";
    public const string DirectionWord = "direction";

    /// <summary>What a relationship that identifies what it reaches may be written as, rather than <c>--</c>.</summary>
    public const string ToWord = "to";

    /// <summary>And what one that does not may be written as, rather than <c>..</c>.</summary>
    public const string OptionallyWord = "optionally to";

    /// <summary>What opens an entity's attributes, and what closes them.</summary>
    public const string Opening = "{";
    public const string Closing = "}";

    /// <summary>What gives an entity a class where it is named.</summary>
    public const string Given = ":::";

    /// <summary>
    /// How many of the entity at one end the other has, drawn as crow's feet. Each is written one way round at the near end and
    /// the other way round at the far end, and either is read at either end, as Mermaid's own lexer takes them.
    /// </summary>
    public static readonly string[] Cards = ["|o", "o|", "||", "}o", "o{", "}|", "|{"];

    /// <summary>The line between them: solid where the relationship identifies what it reaches, and dotted where it does not.</summary>
    public static readonly string[] Solid = ["--"];
    public static readonly string[] Dotted = ["..", "-.", ".-"];

    /// <summary>What either end may be written in words instead, longest first so <c>one or more</c> is not read as <c>one</c>.</summary>
    public static readonly string[] Counts =
        [.. new[]
        {
            "zero or one", "one or zero", "zero or more", "zero or many", "one or more", "one or many",
            "many(0)", "many(1)", "only one", "many", "0+", "1+", "one", "1",
        }.OrderByDescending(said => said.Length)];

    /// <summary>The keys an attribute may be.</summary>
    public static readonly string[] Keys = ["PK", "FK", "UK"];

    /// <summary>The ways a diagram is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>The <c>classDef</c>, <c>class</c> and <c>style</c> lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, ErRoles.Id, ErRoles.Class, "entity");

    private const string EntityShape = "An entity is written CUSTOMER, CUSTOMER[\"The customer\"], or CUSTOMER { its attributes }.";
    private const string AttributeShape = "An attribute is written string name, or string name PK, FK \"what it is for\".";
    private const string RelationShape = "A relationship joins two entities: CUSTOMER ||--o{ ORDER : places.";
    private const string SubgraphShape = "A subgraph boxes the entities written between subgraph Name and end.";
    private const string DirectionShape = "A direction line lays the diagram out: direction LR.";
    private const string StrayShape = "A } closes the attributes an entity opened with {.";

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
    /// <remarks>Spelled as Mermaid spells it: nothing an ER diagram writes is spelled a way of its own.</remarks>
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

        // An entity opening its attributes takes everything after its brace until the brace closing them, on its line or under it.
        if (Begun(source, from, to) is var (opened, after)) return Body(source, row, from, after, opened, lines);

        // Any other statement, which is as much of the source as it takes rather than as much as the row holds. As much of it as is
        // read is what was written, and the rest is the line's.
        var end = Ended(source, from);
        return MermaidParser.StatementLine(source, row.Start, from, Statement(source[from..end], from), end, lines);
    }

    /// <summary>
    /// An entity's attributes: the line opening them, everything after its brace — one attribute to a line, the first and the last on
    /// the brace's lines or on lines of their own — and the brace closing them, read as one statement and made one line of the tree, as
    /// Mermaid reads a block: <c>CUSTOMER { string name PK }</c> as well as one written down the page. A comment in quotes runs to the
    /// quote closing it, across line endings. Where nothing closes them, the line opening them is a statement on its own, said to be
    /// never closed, and the lines under it are read as they would be without it.
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

                return MermaidParser.StatementLine(source, row.Start, from, ContentNode.Branch(ErKinds.Block, pieces, offset: from), shut, lines);
            }

            at = stop + 1;
        }

        lines.Add(MermaidParser.Line(source, row.Start, from,
            [ContentNode.Branch(ErKinds.Block, [opened], offset: from).Saying("These attributes are never closed: } closes them.")], after, row));
        return row.Stop;
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the end of the row a line ending or a comment is found on, outside
    /// a quoted run, and the end of the block where neither follows. A quote nothing closes is a quote like any other character, so
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

    /// <summary>The header — <c>erDiagram</c>, with nothing after it — where the first line saying anything is one.</summary>
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

    /// <summary>What follows the keyword, which is nothing: an ER diagram is laid out by a <c>direction</c> line of its own.</summary>
    private static ContentNode Arguments(string arguments, int at) =>
        MermaidLine.Of(arguments, at).Done
            ? ContentNode.Leaf(Kinds.Verbatim, arguments, MermaidRoles.Arguments, offset: at)
            : ContentNode.Shown(arguments, "Nothing follows erDiagram — direction LR lays it out.", MermaidRoles.Arguments, at);

    /// <summary>A statement written on its own: an entity, a relationship, a subgraph and its end, a direction, or a styling line.</summary>
    private static ContentNode Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        if (line.Written.Trim() is Closing or Closing + ";") return line.Shown(StrayShape);

        return MermaidLine.Keyword(line.Written, Bare, [SubgraphWord, EndWord, DirectionWord, .. MermaidStyling.Words]) switch
        {
            SubgraphWord => Opened(line),
            EndWord => Ended(line),
            DirectionWord => Towards(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, ErKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, ErKinds.CssClass),
            MermaidStyling.StyleWord => Styling.Styled(line, ErKinds.Style),
            _ => Joined(line),
        };
    }

    /// <summary>
    /// Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a name's quotes, and where what is
    /// written on a relationship is still to come after its colon.
    /// </summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or MermaidKinds.Name or ErKinds.Said;

    /// <summary>
    /// Whether a character carries a bare name on: Mermaid's own rule — letters and digits, the dash, the dot, the underscore
    /// and the star, and anything outside ASCII, which is what makes a name in any language read.
    /// </summary>
    public static bool Bare(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '-' or '*' or '.' || character > '\u007F';

    /// <summary>Whether a character carries an attribute's type on, which holds the brackets an array or a size is written in.</summary>
    public static bool Typed(char character) =>
        Bare(character) || character is '(' or ')' or '[' or ']' or '?' or '~' or ',';

    /// <summary>Whether a character carries what an attribute is called on, which holds the brackets an array or a call is written with.</summary>
    private static bool Fielded(char character) => Bare(character) || character is '[' or ']' or '(' or ')';

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>An entity on its own, an entity opening its attributes, or a relationship between two of them.</summary>
    private static ContentNode Joined(MermaidLine line)
    {
        line.Room();
        if (!Named(line)) return line.Shown(EntityShape);

        line.Space();

        if (line.Token(Opening, Roles.Open))
        {
            line.Space();
            return line.Done ? line.Read(ErKinds.Opens) : line.Shown(EntityShape);
        }

        if (line.Done || line.Sees(";")) return line.Closed(ErKinds.Entity, EntityShape);

        if (!Counted(line)) return line.Shown(RelationShape);
        line.Room();

        if (!Lined(line)) return line.Shown(RelationShape);
        line.Room();

        if (!Counted(line)) return line.Shown(RelationShape);
        line.Room();

        if (!Named(line)) return line.Shown(RelationShape);

        Said(line);
        return line.Closed(ErKinds.Relation, RelationShape);
    }

    /// <summary>A subgraph, which boxes the entities written until the <c>end</c> closing it.</summary>
    private static ContentNode Opened(MermaidLine line)
    {
        line.Word(SubgraphWord, letter: Bare);
        line.Room();

        // A name in quotes is the name without them, as Mermaid reads one — so a relationship naming the subgraph reaches it.
        line.Open();
        if (line.Next == '"')
        {
            if (!line.Quoted(ErRoles.Space, what: "name")) return line.Shown(SubgraphShape);
        }
        else
        {
            line.Words(ErRoles.Space, until: "[");
        }

        line.Close(MermaidKinds.Name, ErRoles.Space);

        // The name is read less the space before its label, which is the line's own rather than the name's.
        line.Space();
        if (line.Next == '[' && !line.Label("[", "]", ErRoles.Label)) return line.Shown(SubgraphShape);

        return line.Closed(ErKinds.Subgraph, SubgraphShape);
    }

    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, letter: Bare);

        return line.Closed(ErKinds.Ends, "A subgraph is closed by end, with nothing after it.");
    }

    /// <summary>The way the diagram is laid out.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(ErRoles.Towards, Wayward, until: " \t");

        return line.Closed(ErKinds.Direction, DirectionShape);
    }

    // ── The attributes between the braces ───────────────────────────────────

    /// <summary>
    /// The line opening an entity's attributes — <c>CUSTOMER {</c>, whatever follows the brace on its row — and where what follows the
    /// brace starts; or null where the line opens none.
    /// </summary>
    private static (ContentNode Opened, int After)? Begun(string source, int from, int to)
    {
        var text = source[from..to];
        if (MermaidLine.Keyword(text, Bare, [SubgraphWord, EndWord, DirectionWord, .. MermaidStyling.Words]) is not null) return null;

        var brace = -1;
        for (var at = 0; at < text.Length && brace < 0; at++)
        {
            if (text[at] == '{') brace = at;
            else if (text[at] == '"' && text.IndexOf('"', at + 1) is var close and >= 0) at = close;
            // A brace in an entity's label is the label's: p[Person{] { opens its attributes at the second.
            else if (text[at] == '[' && text.IndexOf(']', at + 1) is var shut and >= 0) at = shut;
        }

        if (brace < 0) return null;

        return Joined(MermaidLine.Of(text[..(brace + 1)], from)) is { Kind: ErKinds.Opens } opened ? (opened, from + brace + 1) : null;
    }

    /// <summary>A line between the braces: what the attribute holds, what it is called, the keys it is, and what it is for.</summary>
    private static ContentNode Within(string text, int at)
    {
        var line = MermaidLine.Of(text, at);
        line.Room();

        // A line with nothing written on it yet is one an attribute is still to be written on.
        if (line.Done) return line.Read(ErKinds.Attribute);

        if (!line.Name(ErRoles.Type, Typed)) return line.Shown(AttributeShape);

        line.Space();
        // What an attribute is called may hold brackets — tags[] — or be written between backticks, as Mermaid's names are.
        if (Rest(line) && !(line.Next == '`' ? line.Label("`", "`", ErRoles.Field) : line.Name(ErRoles.Field, Fielded))) return line.Shown(AttributeShape);

        line.Space();
        if (Rest(line) && !line.Names(Keyed, ErRoles.Key, "key")) return line.Shown(AttributeShape);

        line.Space();
        if (!line.Done && !line.Quoted(ErRoles.Comment, what: "comment")) return line.Shown(AttributeShape);

        return line.Closed(ErKinds.Attribute, AttributeShape);
    }

    private static ContentNode Shutter(string text, int at)
    {
        var line = MermaidLine.Of(text, at);

        line.Room();
        line.Token(Closing, Roles.Close);

        return line.Closed(ErKinds.Shut, StrayShape);
    }

    /// <summary>Whether anything but the comment closing the line is still to be read.</summary>
    private static bool Rest(MermaidLine line) => !line.Done && line.Next != '"';

    private static bool Keyed(MermaidLine line) => line.Name(ErRoles.Key, char.IsAsciiLetter, Keying);

    // ── The pieces a line is made of ────────────────────────────────────────

    /// <summary>One entity where it is named: its name, the label drawn instead of it, and the classes <c>:::</c> gives it.</summary>
    private static bool Named(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        if (!line.Name(ErRoles.Id, Bare)) return line.Undo(mark);

        if (line.Next == '[' && !line.Label("[", "]", ErRoles.Label)) return line.Undo(mark);

        if (line.Sees(Given))
        {
            line.Token(Given);
            if (!line.Names(Classed, ErRoles.Class, "class")) return line.Undo(mark);
        }

        line.Close(ErKinds.Named, ErRoles.Id);
        return true;
    }

    private static bool Classed(MermaidLine line) => line.Name(ErRoles.Class, Bare);

    /// <summary>How many of the entity at one end the other has: a pair of symbols, or the same said in words.</summary>
    private static bool Counted(MermaidLine line)
    {
        foreach (var mark in Cards)
            if (line.Token(mark, ErRoles.Card)) return true;

        // A count in words may be written hard against the line it ends — 1--0+ — so a dash or a dot ends one.
        foreach (var said in Counts)
            if (line.Word(said, MermaidKinds.Setting, ErRoles.Card, character => Bare(character) && character is not ('-' or '.'))) return true;

        return line.Fail(RelationShape);
    }

    /// <summary>The line between them, which says whether the relationship identifies what it reaches.</summary>
    private static bool Lined(MermaidLine line)
    {
        foreach (var mark in Solid.Concat(Dotted))
            if (line.Token(mark, ErRoles.Arrow)) return true;

        if (line.Word(OptionallyWord, MermaidKinds.Setting, ErRoles.Arrow, Bare)) return true;
        if (line.Word(ToWord, MermaidKinds.Setting, ErRoles.Arrow, Bare)) return true;

        return line.Fail(RelationShape);
    }

    /// <summary>What a relationship is called, after the colon opening it — whether anything is written there.</summary>
    private static bool Said(MermaidLine line)
    {
        var mark = line.Save();
        line.Space();

        if (!line.Token(":", Roles.Open))
        {
            line.Restore(mark);
            return false;
        }

        line.Open();
        line.Room();

        if (line.Next == '"') line.Quoted(ErRoles.Said, what: "label");
        else line.Words(ErRoles.Said);

        line.Close(ErKinds.Said, ErRoles.Said);
        return true;
    }

    private static string? Keying(string said) =>
        Keys.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "An attribute is a PK, an FK or a UK.";

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "An ER diagram is laid out TB, TD, BT, RL or LR.";
}
