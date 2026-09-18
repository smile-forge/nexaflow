using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Flowchart.Stages;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a <c>flowchart</c> block says — or a <c>graph</c> one, which Mermaid reads the same way, and a block whose header has
/// not been typed yet: the way it is laid out, the nodes and the links written between them, the subgraphs those are gathered
/// into, and the lines that style, number and point them somewhere.
///
/// <para>
/// The rules are Mermaid's. A node is an id with a label in the brackets that say its shape (<see cref="MermaidShapes"/>), and a
/// link joins the nodes written either side of it (<see cref="MermaidLinks"/>) — as many to a line as are written there, each
/// link carrying on from where the one before it reached, and <c>&amp;</c> joining several nodes at once. A node written again is
/// the same node: the second writing says more about it rather than making another, which is what lets a link name nodes a line
/// above laid out.
/// </para>
/// <para>
/// <strong>An id ends where a link starts.</strong> A dash carries an id on — a node may be called <c>left-hand</c> — unless
/// what is written there is a link, which is the rule Mermaid reads ids by and the reason <see cref="Ends"/> asks
/// <see cref="MermaidLinks"/> rather than looking at the character alone.
/// </para>
/// </summary>
public sealed class FlowchartGrammar : IMermaidGrammar
{
    public const string SubgraphWord = "subgraph";
    public const string EndWord = "end";
    public const string DirectionWord = "direction";
    public const string LinkStyleWord = "linkStyle";
    public const string ClickWord = "click";
    public const string DefaultWord = "default";
    public const string InterpolateWord = "interpolate";
    public const string HrefWord = "href";
    public const string CallWord = "call";

    /// <summary>The ways a chart, or a subgraph in it, is laid out.</summary>
    public static readonly string[] Ways = ["TB", "TD", "BT", "RL", "LR"];

    /// <summary>Where the link a <c>click</c> line writes opens.</summary>
    public static readonly string[] Targets = ["_self", "_blank", "_parent", "_top"];

    /// <summary>What an <c>id@{ … }</c> line may say about the node or the link it names.</summary>
    public static readonly string[] Metadata =
        ["shape", "label", "icon", "form", "pos", "w", "h", "img", "constraint", "curve", "animate", "animation", "view"];

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

    /// <inheritdoc/>
    /// <remarks>The way the chart is laid out, which every node in it is placed by.</remarks>
    public ContentNode? Header(string arguments)
    {
        var line = MermaidLine.Of(arguments);
        if (line.Done) return null;

        line.Setting(FlowchartRoles.Towards, Wayward, until: Stops);
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done
            ? line.Read(FlowchartKinds.Way, MermaidRoles.Arguments)
            : ContentNode.Shown(arguments, "A flowchart is laid out TB, TD, BT, RL or LR, and nothing else follows it: flowchart LR.",
                                MermaidRoles.Arguments);
    }

    /// <inheritdoc/>
    public ContentNode? Statement(string text)
    {
        var line = MermaidLine.Of(text);

        return MermaidLine.Keyword(line.Written, Bare,
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

    /// <inheritdoc/>
    /// <remarks>
    /// A label is put in quotes to hold a quote, a bracket closing it or a comment; what is written on a link is quoted to hold
    /// whatever would close the link early. An id, a class and a way are written bare and cannot be quoted at all, so what they
    /// cannot hold is dropped, and nothing but a digit goes where a link is numbered.
    /// </remarks>
    public MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is { Kind: FlowchartKinds.Saying } saying) return Quoted(saying, part, caret, text);
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is FlowchartRoles.Id or FlowchartRoles.Class or FlowchartRoles.Link) return Bared(part, caret, text);
        if (part.Role is FlowchartRoles.Target or FlowchartRoles.Curve or FlowchartRoles.Call)
            return MermaidWriting.Only(caret, text, Bare);

        if (part.Role is FlowchartRoles.Towards) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);
        if (part.Role is FlowchartRoles.Index) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

        return null;
    }

    /// <summary>
    /// What is typed into a bare name — an id, a class — which cannot be quoted at all: what such a name may hold, and only so long
    /// as none of it starts a link. A dash carries an id on, and is dropped where what is written after it would read as the link
    /// instead.
    /// </summary>
    private static MermaidWriting? Bared(ContentPart part, int caret, string text)
    {
        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var after = said[at..] + Following(part);
        var kept = said[..at];
        var written = string.Empty;

        foreach (var character in text.Where(Bare))
        {
            var tried = kept + character;
            if (Ends(tried + after, 0) < tried.Length) continue;

            kept = tried;
            written += character;
        }

        return written == text ? null : new MermaidWriting(caret, caret, written, caret + written.Length);
    }

    /// <summary>What is written after a part, which is what a character typed at the end of it would run into.</summary>
    private static string Following(ContentPart part)
    {
        var top = part;
        while (top.Parent is { } holder) top = holder;

        // Print, not Text: a branch holds no text of its own, and what is written after a part is the source it stands in.
        var written = top.Print();
        var at = part.End - top.Start;

        return at >= 0 && at <= written.Length ? written[at..] : string.Empty;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A node is declared where it is first written and used wherever it is written again — the ends of a link, a <c>class</c>
    /// line, a <c>style</c> line, a <c>click</c> line — because every one of those is the same id.
    /// </remarks>
    public IReadOnlyList<MermaidName> Names(ContentPart block)
    {
        var said = new Dictionary<string, List<ContentPart>>(StringComparer.Ordinal);

        foreach (var name in block.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Name))
        {
            if (name.Words() is not { Role: FlowchartRoles.Id, Length: > 0 } words) continue;

            if (!said.TryGetValue(words.Text, out var places)) said[words.Text] = places = [];
            places.Add(name);
        }

        return [.. said.Select(name => new MermaidName(name.Key, name.Value[0], [.. name.Value.Skip(1)]))];
    }

    /// <inheritdoc/>
    /// <remarks>An id is written bare, so what an id cannot hold is dropped.</remarks>
    public string Naming(string name) => new([.. name.Where(Bare)]);

    /// <inheritdoc/>
    /// <remarks>
    /// Which subgraph each line is in (<see cref="ResolveSubgraphs"/>), whether a link has nodes to join
    /// (<see cref="ResolveLinks"/>), and whether what is styled is written at all (<see cref="ResolveStyles"/>).
    /// </remarks>
    public IEnumerable<IAstStage> Stages(MermaidBlock block) => [new ResolveSubgraphs(), new ResolveLinks(), new ResolveStyles()];

    /// <inheritdoc/>
    /// <remarks>Between a label's quotes or brackets, and where a link's number is still to be written.</remarks>
    public bool Holds(ContentNode? holder, ContentNode node) =>
        node.Kind is MermaidKinds.Quoted or MermaidKinds.Label or MermaidKinds.Amount;

    /// <summary>Whether a character carries a bare id, a class or a way on.</summary>
    public static bool Bare(char character) => !Stops.Contains(character, StringComparison.Ordinal);

    /// <summary>
    /// Where the id written at <paramref name="at"/> ends. Mermaid's rule, and the reason a link may be written hard against an
    /// id: everything a bare id may hold carries it on, and a dash, a dot or an equals sign carries it on as well — unless a
    /// link starts there, which is what ends it.
    /// </summary>
    public static int Ends(string text, int at)
    {
        while (at < text.Length && Bare(text[at]))
        {
            if (text[at] is '-' or '.' or '=' && MermaidLinks.At(text, at) is not null) break;
            at++;
        }

        return at;
    }

    // ── The lines ───────────────────────────────────────────────────────────

    /// <summary>A subgraph opening: <c>subgraph</c>, <c>subgraph Title</c>, or <c>subgraph id [Title]</c>.</summary>
    private static ContentNode Opens(MermaidLine line)
    {
        line.Word(SubgraphWord, letter: Bare);

        var mark = line.Save();
        line.Room();

        if (!line.Done && !Node(line, FlowchartKinds.Node)) line.Restore(mark);

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
            if (!Link(line) && !Fan(line) && !Closes(line) && !Node(line, FlowchartKinds.Node))
            {
                line.Held(line.Reason ?? NodeShape);
                break;
            }

            line.Space();
        }

        return line.Read(FlowchartKinds.Nodes);
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

        return MermaidStyling.Closed(line, FlowchartKinds.LinkStyle, LinkStyleShape);
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
            if (!line.Quoted(FlowchartRoles.Href, what: "link")) return line.Shown(ClickShape);
        }
        else if (line.Word(CallWord, letter: Bare))
        {
            line.Room();
            if (!line.Name(FlowchartRoles.Call, Called)) return line.Shown(ClickShape);
        }
        else if (line.Next == '"')
        {
            if (!line.Quoted(FlowchartRoles.Href, what: "link")) return line.Shown(ClickShape);
        }
        else if (!line.Name(FlowchartRoles.Call, Called))
        {
            return line.Shown(ClickShape);
        }

        line.Room();
        if (line.Next == '"' && !line.Quoted(FlowchartRoles.Tip, what: "tooltip")) return line.Shown(ClickShape);

        line.Room();
        if (!line.Done && !line.Sees(";") && !line.Name(FlowchartRoles.Target, Bare, Targeted)) return line.Shown(ClickShape);

        return MermaidStyling.Closed(line, FlowchartKinds.Click, ClickShape);
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

        Named(line);
        line.Token("@", Roles.Open);
        line.Token("{", Roles.Open);
        line.Space();

        if (!line.Done && line.Next != '}' && !line.Properties(Metadata, ends: '}', what: "Metadata"))
            return line.Shown(SaidShape);

        line.Space();
        line.Token("}", Roles.Close);
        line.Space();
        line.Token(";");
        line.Space();

        return line.Done ? line.Read(FlowchartKinds.Said) : line.Shown(SaidShape);
    }

    // ── What a line of nodes is made of ─────────────────────────────────────

    /// <summary>One node: <c>A</c>, <c>B["Wide"]</c>, <c>C{Decide}:::chosen</c>.</summary>
    private static bool Node(MermaidLine line, string kind)
    {
        var mark = line.Save();
        line.Open();

        if (!MermaidOutline.Node(line, FlowchartRoles.Id, FlowchartRoles.Label, Stops, out var titled,
                                 MermaidShapes.Brackets, spaced: false, ends: Ends))
            return Back(line, mark);

        if (!titled && line.At == mark.At) return Back(line, mark, NodeShape);

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

        if (MermaidLinks.At(line.Written, line.At) is not { } link) return Back(line, mark);

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

        if (!Saying(line, opening)) return Back(line, mark, LinkShape);

        line.Space();
        if (MermaidLinks.At(line.Written, line.At) is not { Whole: true } closing) return Back(line, mark, LinkShape);

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

    /// <summary>What is written on a link after it, between bars: <c>--&gt;|yes|</c>.</summary>
    private static void Bars(MermaidLine line)
    {
        if (line.Sees("|")) line.Label("|", "|", FlowchartRoles.Label);
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

    /// <summary>
    /// What is typed into what is written on a link, put in quotes where it would otherwise close the link early — which is how
    /// Mermaid holds those characters there too.
    /// </summary>
    private static MermaidWriting? Quoted(ContentPart saying, ContentPart part, int caret, string text)
    {
        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);
        var whole = before + after;

        var closes = whole.Contains("--", StringComparison.Ordinal) || whole.Contains("==", StringComparison.Ordinal)
                     || whole.Contains(".-", StringComparison.Ordinal) || whole.Contains("%%", StringComparison.Ordinal)
                     || whole.Contains('"');

        return closes ? MermaidWriting.Quoting(saying.Start, saying.End, before, after) : null;
    }

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
        Ways.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A flowchart is laid out TB, TD, BT, RL or LR.";

    private static string? Targeted(string said) =>
        Targets.Contains(said, StringComparer.OrdinalIgnoreCase) ? null : "A link opens _self, _blank, _parent or _top.";

    /// <summary>Whether a character carries on what a click line calls, which may be written with the brackets of a call.</summary>
    private static bool Called(char character) => Bare(character) || character is '(' or ')';

    private static string Drawn(MermaidLine line, MermaidLinks.Joined link) => line.Written.Substring(line.At, link.Length);

    /// <summary>Takes nothing, with the reason: a reading that got part of the way and cannot go on.</summary>
    private static bool Back(MermaidLine line, MermaidLine.Mark mark, string? reason = null)
    {
        var why = reason ?? line.Reason;
        line.Restore(mark);
        if (why is not null) line.Fail(why);

        return false;
    }
}
