using System;
using System.Collections.Generic;

using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid.Flowchart;

/// <summary>
/// Reads a flowchart — a <c>flowchart</c> or a <c>graph</c> block: the way it is laid out, the nodes and the links written between
/// them, the subgraphs those are gathered into, and the lines that style, number and point them somewhere — and says how what is
/// written into one is spelled. A swimlane is the same syntax laid out in lanes, and reads its lines with the pieces here
/// (<see cref="Swimlane.SwimlaneParser"/>).
///
/// <para>
/// A diagram is a language of its own, written in Mermaid's syntax — a <c>mermaid</c> fence says only which
/// (<see cref="MermaidFenceParser"/>) — so this reads its own source rather than being handed rows of it. What a fence holds round
/// any diagram — the front matter, an <c>accTitle</c>, a <c>%%</c> comment, a directive, a binding, the space between lines — is
/// read with the shared pieces (<see cref="MermaidParser"/>), because those are facts about a block written in Mermaid's syntax
/// rather than about a chart. The header is optional, since a <c>flowchart</c> fence already says what the block is; a
/// <c>mermaid</c> fence holding no header at all is handed here.
/// </para>
/// <para>
/// What the lines say is read from the characters rather than line by line. A line is where most statements end, but it is not
/// where every token does: a label written between quotes runs to its closing quote wherever that stands, so a string written
/// across two lines is one string rather than two halves with a boundary through it.
/// </para>
/// <para>
/// The rules are Mermaid's. A node is an id with a label in the brackets that say its shape (<see cref="MermaidShapes"/>), and a
/// link joins the nodes written either side of it (<see cref="MermaidLinks"/>) — as many to a line as are written there, each link
/// carrying on from where the one before it reached, and <c>&amp;</c> joining several nodes at once. A node written again is the
/// same node: the second writing says more about it rather than making another, which is what lets a link name nodes a line above
/// laid out. <strong>An id ends where a link starts.</strong> A dash carries an id on — a node may be called <c>left-hand</c> —
/// unless what is written there is a link, which is the rule Mermaid reads ids by and the reason <see cref="Ends"/> asks
/// <see cref="MermaidLinks"/> rather than looking at the character alone.
/// </para>
/// <para>
/// Which subgraph each line is in, which nodes name a subgraph, what each link joins and is styled with, what each <c>id@{ … }</c>
/// line means, what styles each node and subgraph, whether a shape named is one and what the chart amounts to as a graph are facts
/// about the whole block, worked out by the stages its language offers the engine (<see cref="Stages.ResolveSubgraphs"/>,
/// <see cref="Stages.ResolveChart"/> among them).
/// </para>
/// </summary>
public sealed class FlowchartParser : ITranspile
{
    private FlowchartParser()
    {
    }

    /// <summary>The language a flowchart is written in: what a fence names it by, and a word its header is written with.</summary>
    public const string Language = "flowchart";

    /// <summary>The words a flowchart's header is written with — Mermaid's, the one laying it out with ELK among them.</summary>
    public static readonly string[] Keywords = ["flowchart-elk", Language, "graph"];

    /// <summary>What opens a comment running to the end of its row.</summary>
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

    /// <inheritdoc cref="ITranspile.Rewrite"/>
    public static ContentChange? Rewrite(ContentChange change) => Transpiles.Spelling(change, Spelled);

    /// <summary>
    /// A change as a chart spells it.
    ///
    /// <para>
    /// What a node is called is the one thing a chart spells differently from the rest of Mermaid: a link, a <c>class</c>, a
    /// <c>style</c> and a <c>click</c> line all name a node by it, and the characters a name may hold are the whole of what tells
    /// one name from the next. A character that would end a name is not written at all, because writing it would leave every line
    /// that named the node joining something that is not there.
    /// </para>
    /// </summary>
    private static string? Spelled(ContentPart part, string text) =>
        part.Role == FlowchartRoles.Id ? Fits(part, text) ? text : null
        : Quoted(part) ? MermaidText.Quoted(text.ReplaceLineEndings(MermaidParser.LineBreak))
        : MermaidParser.Spelled(part, text);

    /// <summary>
    /// Whether <paramref name="text"/> can go into what something is called.
    ///
    /// <para>
    /// A node's name ends at the first character that is not part of one, so none of those may go in at all. A subgraph's name is
    /// written in words, spaces and all — <c>subgraph Sales team</c> is one name — so a space is a letter of that one.
    /// </para>
    /// </summary>
    private static bool Fits(ContentPart part, string text) =>
        Subgraphed(part)
            ? text.All(letter => Bare(letter) || letter is ' ' or '\t' || Titling(letter))
            : text.All(Bare);

    /// <summary>
    /// Whether what is written here stands between quotes — which is what makes a quote in it the end of it, and a line ending the
    /// end of the line it was written on. Asked of what holds it as well as of itself, because the words between a pair of quotes
    /// are a run of their own inside them.
    /// </summary>
    private static bool Quoted(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Quoted) return true;

        return false;
    }

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

        // A binding standing on a line of its own after the header is lines of the chart supplied by whatever it is shown
        // against. Held as written; read into its place before the chart is worked over.
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

            // A header may end at a ; with the chart's first statement after it on its row, as Mermaid lets a chart be written on one line.
            var cut = text.IndexOf(';');
            var head = cut < 0 ? text : text[..(cut + 1)];

            if (Heading(head, from, Keywords) is { } header)
                return MermaidParser.StatementLine(source, row.Start, from, header, from + head.Length, lines);
        }

        if (MermaidParser.Accessibility(source, row, from, to, lines) is { } stop) return stop;

        // The statement, which is as much of the source as it takes rather than as much as the row holds — a quoted label, or
        // metadata in braces, carries it past its row, and a ; ends it with the next one after it on the row.
        var end = Ended(source, from);
        var said = Statement(source[from..end], from) ?? ContentNode.Leaf(MermaidKinds.Statement, source[from..end], offset: from);

        return MermaidParser.StatementLine(source, row.Start, from, said, end, lines);
    }

    /// <summary>
    /// Where the statement starting at <paramref name="from"/> ends: the first line ending outside a quoted run and outside metadata
    /// in braces, or just past a ; standing outside a label — and the end of the block where neither follows. The space after it
    /// belongs to the line rather than to the statement.
    ///
    /// <para>
    /// A line ending is where a statement ends, and a quote, or the braces of <c>id@{ … }</c>, is where that stops being true — which
    /// is the whole of why a chart reads its own source. A ; is where Mermaid ends a statement too, so a chart may be written on one
    /// line. A quote nothing closes is a quote like any other character, so one typed by mistake costs the line it is on rather than
    /// everything written under it; so do braces closed only after another opens, which are metadata still being written. What
    /// follows a <c>%%</c> outside a label is a comment, whatever it holds. A link, or the
    /// <c>&amp;</c> joining nodes, takes the space either side of it, line endings and all, as Mermaid's lexer reads one — so a link
    /// written at the end of a row, or at the start of the next, joins what stands either side of the line ending.
    /// </para>
    /// </summary>
    internal static int Ended(string source, int from)
    {
        var end = source.Length;
        var depth = 0;
        var piped = false;

        for (var at = from; at < source.Length; at++)
        {
            var character = source[at];

            if (character == '\n')
            {
                if (depth == 0 && !piped && Continued(source, from, at)) continue;

                end = at;
                break;
            }

            // A %% in a label is the label's — A[CPU 100%%] — as Mermaid's lexer reads it there.
            if (character == '%' && depth == 0 && !piped && at + 1 < source.Length && source[at + 1] == '%')
            {
                var row = source.IndexOf('\n', at);
                end = row < 0 ? source.Length : row;
                break;
            }

            if (character == '"')
            {
                var close = source.IndexOf('"', at + 1);
                if (close >= 0) at = close;
                continue;
            }

            if (character == '@' && at + 1 < source.Length && source[at + 1] == '{' && Braced(source, at + 2) is { } shut)
            {
                at = shut;
                continue;
            }

            // An asymmetric node's label opens with a > written hard against its id — id>label] — where a link's head follows a dash or an equals sign.
            if (character == '|') piped = !piped;
            else if (character is '[' or '(' or '{' || character == '>' && at > from && Bare(source[at - 1]) && source[at - 1] is not ('-' or '=')) depth++;
            else if (character is ']' or ')' or '}') depth = Math.Max(0, depth - 1);
            else if (character == ';' && depth == 0 && !piped) { end = at + 1; break; }
        }

        while (end > from && char.IsWhiteSpace(source[end - 1])) end--;

        return end;
    }

    /// <summary>
    /// Whether the statement starting at <paramref name="from"/> runs on past the line ending at <paramref name="at"/>: where its row
    /// ends with a link — its label between bars and all — or an <c>&amp;</c>, or the next row saying anything starts with one.
    /// </summary>
    private static bool Continued(string source, int from, int at)
    {
        var before = source[from..at].TrimEnd();

        // A link joins nodes: a line that opens with a keyword — end, a subgraph, a style — takes none from the row under it.
        if (MermaidLine.ExactKeyword(before, Bare, [SubgraphWord, EndWord, DirectionWord, LinkStyleWord, ClickWord, .. MermaidStyling.Words]) is not null) return false;
        if (before.EndsWith('|') && before.LastIndexOf('|', before.Length - 2) is var bar and >= 0) before = before[..bar].TrimEnd();
        if (before.EndsWith('&') || MermaidLinks.Ending(before)) return true;

        var next = at + 1;
        while (next < source.Length && char.IsWhiteSpace(source[next])) next++;

        return next < source.Length && (source[next] == '&' || MermaidLinks.At(source, next) is not null);
    }

    /// <summary>
    /// Where a comment closing a statement starts — a <c>%%</c> outside quotes, a label's brackets and the bars a link's label stands
    /// between — or -1. Handed to <see cref="MermaidLine"/>, which keeps what follows it as the line's comment.
    /// </summary>
    private static int CommentAt(string text)
    {
        var depth = 0;
        var piped = false;

        for (var at = 0; at + 1 < text.Length; at++)
        {
            var character = text[at];

            if (character == '"')
            {
                var close = text.IndexOf('"', at + 1);
                if (close >= 0) at = close;
                continue;
            }

            if (character == '|') piped = !piped;
            else if (character is '[' or '(' or '{' || character == '>' && at > 0 && Bare(text[at - 1]) && text[at - 1] is not ('-' or '=')) depth++;
            else if (character is ']' or ')' or '}') depth = Math.Max(0, depth - 1);
            else if (character == '%' && text[at + 1] == '%' && depth == 0 && !piped) return at;
        }

        return -1;
    }

    /// <summary>
    /// Where the braces of metadata opened just before <paramref name="from"/> close — across line endings, as Mermaid reads it — or null
    /// where nothing closes them before another opens, which is metadata still being written.
    /// </summary>
    private static int? Braced(string source, int from)
    {
        var close = source.IndexOf('}', from);
        if (close < 0) return null;

        var open = source.IndexOf('{', from);
        return open >= 0 && open < close ? null : close;
    }

    /// <summary>
    /// The header — one of <paramref name="keywords"/>, and the way the chart is laid out where that follows it — or null where the line
    /// says something else, which a fence naming the diagram needs no header before. A swimlane's header is read the same way.
    /// </summary>
    internal static ContentNode? Heading(string text, int at, string[] keywords)
    {
        if (MermaidLine.Keyword(text, keywords) is not { } keyword) return null;

        var pieces = new List<ContentNode> { ContentNode.Leaf(MermaidKinds.Keyword, text[..keyword.Length], Roles.Name, offset: at) };

        var rest = text[keyword.Length..];
        var gap = MermaidParser.Leading(rest);
        var after = at + keyword.Length + gap;

        if (gap > 0) pieces.Add(MermaidParser.Space(rest[..gap], at + keyword.Length));
        if (gap < rest.Length) pieces.Add(Way(rest[gap..], after) ?? ContentNode.Leaf(Kinds.Verbatim, rest[gap..], MermaidRoles.Arguments, offset: after));

        return ContentNode.Branch(MermaidKinds.Header, pieces, offset: at);
    }
    // ── The chart's statements ──────────────────────────────────────────────

    public const string SubgraphWord = "subgraph";
    public const string EndWord = "end";
    public const string DirectionWord = "direction";
    public const string LinkStyleWord = "linkStyle";
    public const string ClickWord = "click";
    public const string DefaultWord = "default";
    public const string InterpolateWord = "interpolate";
    public const string HrefWord = "href";
    public const string CallWord = "call";

    public const string IconWord = "icon";

    /// <summary>The ways a chart, or a subgraph in it, is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>The ways a chart's header may say it runs with an arrow, as Mermaid reads them: right, left, up and down.</summary>
    public static readonly string[] Pointed = [">", "<", "^", "v"];

    /// <summary>Where the link a <c>click</c> line writes opens.</summary>
    public static readonly string[] Targets = ["_self", "_blank", "_parent", "_top"];

    /// <summary>
    /// The characters a bare id ends at: everything a label's brackets open or close with, the bar a link's label is written
    /// between, the ampersand between nodes joined together, the comma between ids, the colon of a class, the at of metadata and
    /// the tilde a link drawn as nothing is written with. A dash is not one of them — <see cref="Ends"/> reads those.
    /// </summary>
    public const string Stops = "([{<>)}]|&,;:@ \t\r\n\"~";

    /// <summary>The <c>classDef</c>, <c>class</c> and <c>style</c> lines, which every diagram with them reads the one way.</summary>
    public static readonly MermaidStyling Styling = new(Bare, FlowchartRoles.Id, FlowchartRoles.Class, "node");

    private const string NodeShape = "A node is an id, with a label in the brackets that say its shape: A, B[\"Wide\"], C{Decide}.";
    private const string LinkShape = "A link joins the nodes either side of it: A --> B, or A -- yes --> B.";
    private const string SubgraphShape = "A subgraph is opened by subgraph, by subgraph Title, or by subgraph id [Title], and ended by end.";
    private const string DirectionShape = "A direction line lays out the subgraph it is in: direction LR.";
    private const string LinkStyleShape = "A linkStyle numbers the links it styles, or says default: linkStyle 0,2 stroke:#f00.";
    private const string ClickShape = "A click line says where a node leads: click A \"https://example.com\" \"Tooltip\".";
    private const string SaidShape = "Metadata says more about what it names: A@{ shape: cyl, label: \"Store\" }.";

    /// <summary>What follows the keyword: the way the chart is laid out, which every node in it is placed by — or null where nothing does.</summary>
    private static ContentNode? Way(string arguments, int at)
    {
        var line = MermaidLine.Of(arguments, at);
        if (line.Done) return null;

        line.Setting(FlowchartRoles.Towards, Headed, until: " \t;");
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done
            ? line.Read(FlowchartKinds.Way, MermaidRoles.Arguments)
            : ContentNode.Shown(arguments, "A chart is laid out TB, TD, BT, RL or LR, and nothing else follows the keyword: flowchart LR.",
                                MermaidRoles.Arguments, at);
    }

    /// <summary>One statement of a chart, read to where it ends — or the line held as written, with the reason.</summary>
    internal static ContentNode? Statement(string text, int at)
    {
        var line = MermaidLine.Of(text, at, CommentAt);

        // Mermaid's chart reads its words case and all: a node called End is a node, and only end closes a subgraph.
        return MermaidLine.ExactKeyword(line.Written, Bare,
                                        [SubgraphWord, EndWord, DirectionWord, LinkStyleWord, ClickWord, .. MermaidStyling.Words]) switch
        {
            SubgraphWord => Opens(line),
            EndWord => Ended(line),
            DirectionWord => Towards(line),
            LinkStyleWord => Linked(line),
            ClickWord => Clicked(line),
            MermaidStyling.ClassDefWord => Styling.Defined(line, FlowchartKinds.ClassDef),
            MermaidStyling.ClassWord => Styling.Applied(line, FlowchartKinds.Class),
            MermaidStyling.StyleWord => Styling.Styled(line, FlowchartKinds.Style),
            _ => Said(line) ?? Items(line),
        };
    }

    /// <summary>Whether a hole stands in <paramref name="node"/> while somebody writes in the block: between a label's quotes or brackets, and where a link's number is still to be written.</summary>
    public static bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or MermaidKinds.Label or MermaidKinds.Amount;

    /// <summary>Whether a character carries a bare id, a class or a way on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    /// <summary>
    /// Where the id written at <paramref name="at"/> ends. Mermaid's rule, and the reason a link may be written hard against an
    /// id: everything a bare id may hold carries it on, and a dash, a dot or an equals sign carries it on as well — unless a
    /// link starts there, which is what ends it. A colon and a comma carry it on too, as Mermaid's ids hold them — <c>svc:api</c> —
    /// but for the <c>:::</c> giving it a class.
    /// </summary>
    public static int Ends(string text, int at)
    {
        while (at < text.Length && (Bare(text[at]) || Carried(text, at)))
        {
            if (text[at] is '-' or '.' or '=' && MermaidLinks.At(text, at) is not null) break;
            at++;
        }

        return at;
    }

    /// <summary>Whether the colon or the comma at <paramref name="at"/> carries an id on, which a colon opening <c>:::</c> does not.</summary>
    private static bool Carried(string text, int at) =>
        text[at] == ',' || text[at] == ':' && !text.AsSpan(at).StartsWith(MermaidOutline.ClassMark, StringComparison.Ordinal);

    /// <summary>Whether a subgraph's name, written in words, holds <paramref name="character"/> though a bare id does not: a colon, an ampersand or a comma, as Mermaid's titles do.</summary>
    private static bool Titling(char character) => character is ':' or '&' or ',';

    /// <summary>
    /// Where a subgraph's own name ends: Mermaid lets it be written in words, spaces and all — <c>subgraph Sales team</c>, whose name is
    /// its title too — so it runs on through a space, a colon, an ampersand and a comma, and stops where its label opens, where a link starts, or at the end of what a name
    /// may hold. The space at the end of it belongs to the line rather than to the name.
    /// </summary>
    public static int Titled(string text, int at)
    {
        var end = at;

        while (at < text.Length)
        {
            if (text[at] is ' ' or '\t')
            {
                at++;
                continue;
            }

            if (!Bare(text[at]) && !(Titling(text[at]) && !text.AsSpan(at).StartsWith(MermaidOutline.ClassMark, StringComparison.Ordinal))) break;
            if (text[at] is '-' or '.' or '=' && MermaidLinks.At(text, at) is not null) break;

            end = ++at;
        }

        return end;
    }

    /// <summary>
    /// Whether a name is a subgraph's own, which is the one name in a chart written in words — <c>subgraph Sales team</c> — where a
    /// node's id, a class and a link's own name are written bare and hold no space. Asked by whatever writes a name, so the rule is
    /// stated once: the parser spelling a name it is handed, and the handler letting one be typed a character at a time.
    /// </summary>
    public static bool Subgraphed(ContentPart part)
    {
        for (var over = part; over is not null; over = over.Parent)
            if (over.Kind == FlowchartKinds.Opens) return true;

        return false;
    }

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A subgraph opening: <c>subgraph</c>, <c>subgraph Title</c>, <c>subgraph "Title"</c>, or <c>subgraph id [Title]</c>.</summary>
    private static ContentNode Opens(MermaidLine line)
    {
        line.Word(SubgraphWord, letter: Bare);

        var mark = line.Save();
        line.Room();

        if (line.Next == '"')
        {
            // A title in quotes, which Mermaid takes as the subgraph's name and its title both.
            line.Open();
            line.Open();

            if (line.Quoted(FlowchartRoles.Id, what: "title"))
            {
                line.Close(MermaidKinds.Name, FlowchartRoles.Id);
                line.Close(FlowchartKinds.Node);
            }
            else
            {
                line.Restore(mark);
            }
        }
        else if (!line.Done && !Node(line, FlowchartKinds.Node, Titled))
        {
            line.Restore(mark);
        }

        line.Space();
        line.Token(";");
        line.Space();

        return line.Done ? line.Read(FlowchartKinds.Opens) : line.Shown(SubgraphShape);
    }

    private static ContentNode Ended(MermaidLine line)
    {
        line.Word(EndWord, letter: Bare);
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done ? line.Read(FlowchartKinds.Ends) : line.Shown("A subgraph is ended by end, with nothing after it.");
    }

    /// <summary>The way the subgraph this line is in is laid out: <c>direction LR</c>.</summary>
    private static ContentNode Towards(MermaidLine line)
    {
        line.Word(DirectionWord, letter: Bare);
        line.Room();
        line.Setting(FlowchartRoles.Towards, Wayward, until: Stops);
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done ? line.Read(FlowchartKinds.Direction) : line.Shown(DirectionShape);
    }

    /// <summary>The nodes and the links written on one line, in the order they are written.</summary>
    private static ContentNode Items(MermaidLine line)
    {
        while (!line.Done)
        {
            if (!Link(line) && !Fan(line) && !Closes(line) && !Inline(line) && !Node(line, FlowchartKinds.Node))
            {
                line.Held(line.Reason ?? NodeShape);
                break;
            }

            line.Space();
        }

        return line.Read(FlowchartKinds.Nodes);
    }

    /// <summary>
    /// Metadata written on a node where it stands among nodes and links — <c>A@{ shape: cyl } --&gt; B</c> — which is about the node
    /// written just before it, as Mermaid reads it.
    /// </summary>
    private static bool Inline(MermaidLine line)
    {
        if (!line.Sees("@{")) return false;

        var mark = line.Save();
        line.Open();
        line.Token("@", Roles.Open);
        line.Token("{", Roles.Open);
        line.Space();

        if (!line.Done && line.Next != '}' && !Settings(line)) return line.Undo(mark, SaidShape);

        line.Space();
        if (!line.Token("}", Roles.Close)) return line.Undo(mark, SaidShape);

        line.Close(FlowchartKinds.Said);
        return true;
    }

    /// <summary>The links this line numbers, and the style they are drawn with: <c>linkStyle 0,2 stroke:#f00</c>.</summary>
    private static ContentNode Linked(MermaidLine line)
    {
        line.Word(LinkStyleWord, letter: Bare);
        line.Room();

        if (!line.Word(DefaultWord, MermaidKinds.Key, FlowchartRoles.Index, Bare) && !line.Names(Numbered, FlowchartRoles.Index, "link"))
            return line.Shown(LinkStyleShape);

        line.Room();

        if (line.Word(InterpolateWord, letter: Bare))
        {
            line.Room();
            if (!line.Name(FlowchartRoles.Curve, Bare)) return line.Shown(LinkStyleShape);
            line.Room();
        }

        if (!line.Done && !line.Properties(ends: ';')) return line.Shown(LinkStyleShape);

        return line.Closed(FlowchartKinds.LinkStyle, LinkStyleShape);
    }

    /// <summary>Where pressing a node leads, and what it says while pointed at: <c>click A "https://example.com" "Tooltip"</c>.</summary>
    private static ContentNode Clicked(MermaidLine line)
    {
        line.Word(ClickWord, letter: Bare);
        line.Room();

        if (!Named(line)) return line.Shown(ClickShape);

        line.Room();

        if (line.Word(HrefWord, letter: Bare))
        {
            line.Room();
            if (!line.Quoted(Roles.Destination, what: "link")) return line.Shown(ClickShape);
        }
        else if (line.Word(CallWord, letter: Bare))
        {
            line.Room();
            if (!Call(line)) return line.Shown(ClickShape);
        }
        else if (line.Next == '"')
        {
            if (!line.Quoted(Roles.Destination, what: "link")) return line.Shown(ClickShape);
        }
        else if (!Call(line))
        {
            return line.Shown(ClickShape);
        }

        line.Room();
        if (line.Next == '"' && !line.Quoted(FlowchartRoles.Tip, what: "tooltip")) return line.Shown(ClickShape);

        line.Room();
        if (!line.Done && !line.Sees(";") && !line.Name(FlowchartRoles.Target, Bare, Targeted)) return line.Shown(ClickShape);

        return line.Closed(FlowchartKinds.Click, ClickShape);
    }

    /// <summary>
    /// What an <c>id@{ … }</c> line says about the node or the link it names — the shape and the label a node is drawn with, the
    /// curve a link takes. Null where the line is not written that way, which leaves it to be read as nodes and links.
    /// </summary>
    private static ContentNode? Said(MermaidLine line)
    {
        var end = Ends(line.Written, line.At);
        if (end <= line.At || end + 1 >= line.Written.Length) return null;
        if (line.Written[end] != '@' || line.Written[end + 1] != '{') return null;

        var mark = line.Save();
        Named(line);
        line.Token("@", Roles.Open);
        line.Token("{", Roles.Open);
        line.Space();

        if (!line.Done && line.Next != '}' && !Settings(line)) return line.Shown(SaidShape);

        line.Space();
        line.Token("}", Roles.Close);
        line.Space();
        line.Token(";");
        line.Space();

        if (line.Done) return line.Read(FlowchartKinds.Said);

        // More written after it — a link on from the node it names — makes it a line of nodes with metadata on one of them.
        line.Restore(mark);
        return null;
    }

    /// <summary>
    /// What an <c>id@{ … }</c> line sets, read so that a value written between quotes is the quotes and the run of words they hold,
    /// rather than one leaf holding both.
    ///
    /// <para>
    /// A label is drawn from that run and a reader types into it where it stands, which is what a label written in a node's
    /// brackets already does. A value read as one leaf leaves the characters on the screen standing for nothing there is a caret
    /// position in, so it can only be drawn from a copy worked out elsewhere and can never be written in.
    /// </para>
    /// </summary>
    private static bool Settings(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        while (true)
        {
            var colon = line.At;
            while (colon < line.Written.Length && line.Written[colon] is not (':' or ',' or '}')) colon++;

            if (colon >= line.Written.Length || line.Written[colon] != ':') return Unread(line, mark);

            var name = line.Written[line.At..colon].TrimEnd();
            if (name.Length == 0) return Unread(line, mark);

            line.Open();
            // A key nothing reads is read and kept, and sets nothing, as Mermaid's metadata is.
            line.Add(ContentNode.Leaf(MermaidKinds.Key, name, Roles.Name, offset: line.Begins));
            line.Space();
            line.Token(":");
            line.Space();

            if (!Valued(line, name)) return Unread(line, mark);

            line.Space();
            line.Close(MermaidKinds.Property);

            if (line.Done || line.Next == '}') break;

            // A comma between them, or — metadata written as YAML on the lines inside its braces — the line ending before the next.
            line.Token(",");
            line.Space();

            if (line.Done || line.Next == '}') return Unread(line, mark);
        }

        line.Close(MermaidKinds.Properties);
        return true;
    }

    /// <summary>What one of them is set to: the words between its quotes, or the characters up to whatever ends it.</summary>
    private static bool Valued(MermaidLine line, string name)
    {
        var icon = name.Equals(IconWord, StringComparison.OrdinalIgnoreCase);
        if (icon) line.Open();

        if (line.Next == '"')
        {
            if (Shut(line.Written, line.At) is not { } close) return false;

            line.Open();
            line.Token("\"", Roles.Open);
            line.Add(ContentNode.Leaf(Kinds.Words, line.Written[line.At..close], FlowchartRoles.Label, offset: line.Begins));
            line.Token("\"", Roles.Close);
            line.Close(MermaidKinds.Quoted, MermaidRoles.Value);
        }
        else
        {
            var end = line.At;
            while (end < line.Written.Length && line.Written[end] is not (',' or '}' or '\n')) end++;

            var value = line.Written[line.At..end].TrimEnd();
            if (value.Length == 0) return false;

            line.Add(ContentNode.Leaf(MermaidKinds.Setting, value, MermaidRoles.Value, MermaidStyle.Trouble(name, value), line.Begins));
        }

        if (icon) line.Close(MermaidKinds.Icon, MermaidRoles.Value);

        return true;
    }

    /// <summary>Where the quote opening at <paramref name="at"/> is closed, or null where nothing closes it.</summary>
    private static int? Shut(string written, int at)
    {
        for (var next = at + 1; next < written.Length; next++)
            if (written[next] == '"') return next;

        return null;
    }

    /// <summary>Puts the line back as it was and says the metadata could not be read.</summary>
    private static bool Unread(MermaidLine line, MermaidLine.Mark mark)
    {
        line.Restore(mark);
        return false;
    }

    // ── What a line of nodes is made of ─────────────────────────────────────

    /// <summary>
    /// One node: its id, the label in the brackets that say its shape, and the classes written on it. <paramref name="ends"/> says where
    /// its id stops, which is a rule of the line's own — a subgraph's name runs on through a space where a node's id does not.
    /// </summary>
    private static bool Node(MermaidLine line, string kind, Func<string, int, int>? ends = null)
    {
        var mark = line.Save();
        line.Open();

        if (!MermaidOutline.Node(line, FlowchartRoles.Id, FlowchartRoles.Label, Stops, out var titled,
                                 MermaidShapes.Brackets, spaced: false, ends: ends ?? Ends))
            return line.Undo(mark);

        if (!titled && line.At == mark.At) return line.Undo(mark, NodeShape);

        Classed(line);
        line.Close(kind);
        return true;
    }

    /// <summary>The class a node is given where it is written: the <c>:::chosen</c> after it.</summary>
    private static void Classed(MermaidLine line)
    {
        if (!line.Sees(MermaidOutline.ClassMark)) return;

        line.Token(MermaidOutline.ClassMark);
        Named(line, FlowchartRoles.Class);
    }

    /// <summary>The ampersand between nodes a link leaves from, or reaches, together: <c>A --&gt; B &amp; C</c>.</summary>
    private static bool Fan(MermaidLine line) => line.Sees("&") && line.Token("&");

    /// <summary>The semicolon that may close what is written on a line, and start the next thing written on it.</summary>
    private static bool Closes(MermaidLine line) => line.Sees(";") && line.Token(";");

    /// <summary>
    /// A link, with what is written on it: after it between bars — <c>--&gt;|yes|</c> — or between its opening and the link
    /// closing it — <c>-- yes --&gt;</c>. An id of its own may be written before it, so a later line can style it.
    /// </summary>
    private static bool Link(MermaidLine line)
    {
        var mark = line.Save();
        line.Open();

        Identified(line);

        if (MermaidLinks.At(line.Written, line.At) is not { } link) return line.Undo(mark);

        var opening = Drawn(line, link);

        if (link.Whole)
        {
            line.Token(opening, FlowchartRoles.Arrow);
            Bars(line);
            line.Close(FlowchartKinds.Link);
            return true;
        }

        line.Token(opening, Roles.Open);
        line.Space();

        if (!Saying(line, opening)) return line.Undo(mark, LinkShape);

        line.Space();
        if (MermaidLinks.At(line.Written, line.At) is not { Whole: true } closing) return line.Undo(mark, LinkShape);

        line.Token(Drawn(line, closing), FlowchartRoles.Arrow);
        line.Close(FlowchartKinds.Link);
        return true;
    }

    /// <summary>An id given to the link itself, so a later line can style or curve it: the <c>e1</c> of <c>e1@--&gt;</c>.</summary>
    private static void Identified(MermaidLine line)
    {
        var end = Ends(line.Written, line.At);
        if (end <= line.At || end >= line.Written.Length || line.Written[end] != '@') return;
        if (end + 1 < line.Written.Length && line.Written[end + 1] is '{' or '"') return;

        line.Words(FlowchartRoles.Link, end);
        line.Token("@", Roles.Open);
    }

    /// <summary>What is written on a link after it, between bars, space before them or not: <c>--&gt;|yes|</c>, <c>--&gt; |yes|</c>.</summary>
    private static void Bars(MermaidLine line)
    {
        var mark = line.Save();
        line.Space();

        if (line.Sees("|")) line.Label("|", "|", FlowchartRoles.Label);
        else line.Restore(mark);
    }

    /// <summary>
    /// What is written on a link between its opening and the link closing it — <c>-- yes --&gt;</c> — in quotes or bare. Bare
    /// text runs to where the closing starts, which is how Mermaid ends it: two dashes, two equals signs, or a dot and a dash.
    /// </summary>
    private static bool Saying(MermaidLine line, string opening)
    {
        if (line.Next == '"') return line.Quoted(FlowchartRoles.Label, what: "label");

        var end = line.Written.IndexOf(Closing(opening), line.At, StringComparison.Ordinal);
        if (end <= line.At) return false;

        line.Open();
        line.Words(FlowchartRoles.Label, end);
        line.Close(FlowchartKinds.Saying, FlowchartRoles.Label);
        return true;
    }

    /// <summary>What closes a link opened the way <paramref name="opening"/> was written.</summary>
    private static string Closing(string opening) =>
        opening.Contains('=') ? "==" : opening.Contains('.') ? ".-" : "--";

    /// <summary>A bare name — a node's id, a class — read as far as Mermaid's rule carries it.</summary>
    private static bool Named(MermaidLine line, string role = FlowchartRoles.Id)
    {
        var end = Ends(line.Written, line.At);
        if (end <= line.At) return line.Fail(NodeShape);

        line.Open();
        line.Words(role, end);
        line.Close(MermaidKinds.Name, role);
        return true;
    }

    private static bool Numbered(MermaidLine line) => line.Name(FlowchartRoles.Index, char.IsAsciiDigit, Counted);

    /// <summary>What is wrong with the number a <c>linkStyle</c> line gives a link: links are counted from nought, in whole ones.</summary>
    private static readonly Func<string, string?> Counted =
        MermaidNumber.Where(at => at >= 0 && at == Math.Floor(at),
                            "A linkStyle numbers the links it styles, counting from nought: linkStyle 0,2 stroke:#f00.");

    private static string? Wayward(string said) =>
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A chart is laid out TB, TD, BT, RL or LR.";

    /// <summary>What is wrong with the way a header says the chart runs, which may be one of its arrows as well as its words.</summary>
    private static string? Headed(string said) => Pointed.Contains(said, StringComparer.Ordinal) ? null : Wayward(said);

    private static string? Targeted(string said) =>
        Targets.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A link opens _self, _blank, _parent or _top.";

    /// <summary>
    /// What a click line calls: a name, and the arguments in the brackets after it, which Mermaid takes as written up to the bracket
    /// closing them — <c>callback("x", 2)</c>.
    /// </summary>
    private static bool Call(MermaidLine line)
    {
        var end = line.At;
        while (end < line.Written.Length && Bare(line.Written[end])) end++;
        if (end == line.At) return line.Fail(ClickShape);

        if (end < line.Written.Length && line.Written[end] == '(')
        {
            var close = line.Written.IndexOf(')', end + 1);
            if (close < 0) return line.Fail("A call's arguments are closed with ).");

            end = close + 1;
        }

        line.Open();
        line.Words(FlowchartRoles.Call, end);
        line.Close(MermaidKinds.Name, FlowchartRoles.Call);
        return true;
    }

    private static string Drawn(MermaidLine line, MermaidLinks.Joined link) => line.Written.Substring(line.At, link.Length);
}
