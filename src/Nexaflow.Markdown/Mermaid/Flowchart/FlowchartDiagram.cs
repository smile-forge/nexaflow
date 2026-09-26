using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart;

/// <summary>Which way a chart, or a subgraph in it, is laid out: where a link written left to right points.</summary>
public enum FlowchartWay
{
    /// <summary><c>TB</c> and <c>TD</c>.</summary>
    Down,

    /// <summary><c>BT</c>.</summary>
    Up,

    /// <summary><c>LR</c>.</summary>
    Right,

    /// <summary><c>RL</c>.</summary>
    Left,
}

/// <summary>
/// One node, read: where it was written, what it is called, what is drawn on it, the shape it is drawn as, the subgraph it
/// belongs to and what it is styled with.
/// </summary>
/// <param name="Part">The whole node as it was written, which is what a press on it means.</param>
/// <param name="Id">What it is called, which is what a link, a <c>class</c>, a <c>style</c> and a <c>click</c> name it by.</param>
/// <param name="Said">The words drawn on it: its label, or what it is called where nothing else says anything.</param>
/// <param name="Group">The subgraph it was first written in, or null for one written outside them all.</param>
/// <param name="Order">Where it comes among the nodes, in the order they are first written.</param>
public sealed record FlowchartNode(
    ContentPart Part,
    string Id,
    ContentPart? Said,
    MermaidShape Shape,
    string? Group,
    MermaidStyle Style,
    int Order)
{
    /// <summary>The hole standing where its label goes, where holes were asked for and nothing is written there yet.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>
    /// What an <c>id@{ label: … }</c> line says it is drawn with, which is worked out from the metadata rather than typed into
    /// where it is drawn — the quotes round it belong to the metadata rather than to the label.
    /// </summary>
    public string? Worked { get; init; }

    /// <summary>The metadata <see cref="Worked"/> was read from, which is what a press on those words means.</summary>
    public ContentPart? WorkedPart { get; init; }

    /// <summary>Where a <c>click</c> line says pressing it leads, which nothing here follows.</summary>
    public string? Href { get; init; }

    /// <summary>What a <c>click</c> line says it says while pointed at.</summary>
    public string? Tip { get; init; }

    /// <summary>The picture or icon it is drawn as, where its metadata names one — null for a node drawn as its shape.</summary>
    public FlowchartPicture? Picture { get; init; }
}

/// <summary>
/// What a node drawn as a picture is drawn with — an <c>@{ img: … }</c> or an <c>@{ icon: … }</c> — and how: the frame an
/// icon stands in, whether its label goes above it, and the size it is asked for.
/// </summary>
/// <param name="Written">The <c>img:</c> or <c>icon:</c> line it was named on, which a found picture is hung on.</param>
/// <param name="Icon">The icon named, pack and all — or null for a picture.</param>
/// <param name="Form">What an icon stands in — <c>square</c>, <c>circle</c>, <c>rounded</c> — or null for nothing.</param>
/// <param name="Above">Whether the label goes above it, as <c>pos: t</c> asks, rather than below.</param>
/// <param name="Keeps">Whether a picture keeps its shape inside the size it is asked for, as <c>constraint: on</c> asks.</param>
public sealed record FlowchartPicture(ContentPart Written, string? Icon, string? Form, bool Above, double? Width, double? Height, bool Keeps);

/// <summary>
/// One link, read: the nodes it joins, what is written on it, what it draws at each end, how its line is drawn, and how far apart
/// it holds what it joins.
/// </summary>
/// <param name="Part">The link as it was written, which is what a press on it means.</param>
/// <param name="Span">How many ranks it reaches over, which is how many the author wrote it long.</param>
/// <param name="Order">Where it comes among the links, which is the number a <c>linkStyle</c> line calls it by.</param>
public sealed record FlowchartLink(
    ContentPart Part,
    string From,
    string To,
    ContentPart? Said,
    MermaidHead Start,
    MermaidHead End,
    MermaidLineStyle Style,
    int Span,
    int Order)
{
    /// <summary>The hole standing where its label goes, where holes were asked for and nothing is written there yet.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>What a <c>linkStyle</c> line asks for it, over what its own characters say.</summary>
    public MermaidStyle Written { get; init; } = MermaidStyle.None;

    /// <summary>The id it was given where it was written, which a later line names it by.</summary>
    public string? Name { get; init; }

    /// <summary>The curve an <c>id@{ curve: … }</c> line asks for it, over whatever the front matter asks for every link.</summary>
    public string? Curve { get; init; }

    /// <summary>Whether it is drawn at all: a link of tildes only holds what it joins apart.</summary>
    public bool Drawn => Style != MermaidLineStyle.Invisible;
}

/// <summary>
/// One subgraph, read: the box drawn round every node first written inside it, what is written at the top of it, the subgraph it
/// is itself inside, and the way the nodes in it are laid out where a <c>direction</c> line says one of its own.
/// </summary>
/// <param name="Key">What the nesting calls it, which is what a node says it is inside.</param>
/// <param name="Id">What it is called, which a link and a <c>style</c> line name it by.</param>
public sealed record FlowchartGroup(
    ContentPart Part,
    string Key,
    string Id,
    ContentPart? Said,
    string? Parent,
    FlowchartWay? Way,
    MermaidStyle Style,
    int Order)
{
    /// <summary>The hole standing where what is written on it goes.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>
    /// The whole of it as it was written, from the line that opened it through the <c>end</c> that closed it — the opening line
    /// alone, where nothing closed it. What is drawn for a subgraph that holds the whole of its work, a swimlane's lane, stands for
    /// this, so a press on the lane means the lane and everything in it sits inside it.
    /// </summary>
    public ISourcePart Whole { get; init; } = default(SourceSpan);
}

/// <summary>
/// A <c>flowchart</c> block, read: the nodes written in it, the links between them, the subgraphs they are gathered into, and the
/// way it is all laid out. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// A node written twice is one node: the second writing of an id says more about the node the first one made — the shape and the
/// words it is drawn with — rather than making another, which is what lets a link name the nodes a line above wrote. A node
/// belongs to the subgraph it was first written in, as Mermaid gathers them.
/// </para>
/// <para>
/// A link joins the nodes written either side of it, every node on the left to every node on the right where <c>&amp;</c> joins
/// several, and the next link on the line carries on from the nodes this one reached.
/// </para>
/// </summary>
public sealed class FlowchartDiagram
{
    private FlowchartDiagram(MermaidBlock block, FlowchartConfig config, FlowchartWay way, IReadOnlyList<FlowchartNode> nodes,
                             IReadOnlyList<FlowchartLink> links, IReadOnlyList<FlowchartGroup> groups)
    {
        Block = block;
        Config = config;
        Way = way;
        Nodes = nodes;
        Links = links;
        Groups = groups;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static FlowchartDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public FlowchartConfig Config { get; }

    /// <summary>The way the whole chart is laid out.</summary>
    public FlowchartWay Way { get; }

    /// <summary>The nodes, in the order they are first written.</summary>
    public IReadOnlyList<FlowchartNode> Nodes { get; }

    /// <summary>The links, in the order they are written, which is the order a <c>linkStyle</c> line numbers them in.</summary>
    public IReadOnlyList<FlowchartLink> Links { get; }

    /// <summary>The subgraphs, each before the ones nested in it.</summary>
    public IReadOnlyList<FlowchartGroup> Groups { get; }

    /// <summary>The node an id names, or null where nothing is called that.</summary>
    public FlowchartNode? Find(string id) =>
        id.Length == 0 ? null : Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    /// <summary>The nodes written inside a subgraph — those written outside them all, for null.</summary>
    public IEnumerable<FlowchartNode> Inside(string? group) =>
        Nodes.Where(node => string.Equals(node.Group, group, StringComparison.Ordinal));

    /// <summary>The subgraphs opened inside a subgraph — the outermost ones, for null.</summary>
    public IEnumerable<FlowchartGroup> Within(string? group) =>
        Groups.Where(nested => string.Equals(nested.Parent, group, StringComparison.Ordinal));

    /// <summary>
    /// The lanes: the subgraphs written outside them all, which is what a swimlane draws as bands. Every other subgraph is inside one.
    /// </summary>
    public IEnumerable<FlowchartGroup> Lanes => Within(null);

    /// <summary>
    /// The lane a subgraph, or anything written in one, belongs to: the outermost subgraph holding it. Null for something written in no
    /// subgraph at all.
    /// </summary>
    public FlowchartGroup? Lane(string? group)
    {
        var held = Group(group);
        while (held is { Parent: { } parent }) held = Group(parent);

        return held;
    }

    /// <summary>The subgraph a key names, or null where none does.</summary>
    public FlowchartGroup? Group(string? key) =>
        key is null ? null : Groups.FirstOrDefault(group => string.Equals(group.Key, key, StringComparison.Ordinal));

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the flowchart's own stages.</summary>
    public static FlowchartDiagram Of(MermaidBlock block)
    {
        var nodes = new List<Made>();
        var known = new Dictionary<string, Made>(StringComparer.Ordinal);
        var groups = new List<Held>();
        var opened = new Dictionary<string, Held>(StringComparer.Ordinal);
        var open = new Stack<Held>();
        var links = new List<Joined>();
        var named = new Dictionary<string, Joined>(StringComparer.Ordinal);
        var ways = new Dictionary<string, FlowchartWay>(StringComparer.Ordinal);

        var classes = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);
        var taken = new List<(IReadOnlyList<string> Ids, string Class)>();
        var written = new List<(IReadOnlyList<string> Ids, MermaidStyle Style)>();
        var styled = new List<(IReadOnlyList<int> Links, bool Every, MermaidStyle Style)>();
        var said = new List<(ContentPart Stated, string? Group)>();

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;
            var inside = stated.Fact(FlowchartRoles.Inside);

            switch (stated.Kind)
            {
                case FlowchartKinds.Opens:
                        var group = Opens(stated, Keyed(inside), groups.Count);
                        groups.Add(group);
                        opened[group.Key] = group;
                        open.Push(group);
                        break;

                    // Where a subgraph is closed is where the whole of it ends, which is the stretch it was written as.
                    case FlowchartKinds.Ends:
                        if (open.Count > 0) open.Pop().Closed = stated;
                        break;

                case FlowchartKinds.Nodes:
                    Laid(stated, Keyed(inside), nodes, known, links, named);
                    break;

                case FlowchartKinds.Direction:
                    if (Wayward(Setting(stated, FlowchartRoles.Towards)) is { } towards) ways[inside ?? string.Empty] = towards;
                    break;

                case FlowchartKinds.ClassDef:
                    var declared = MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties));
                    foreach (var name in FlowchartGrammar.Styling.Classes(stated)) classes[name] = declared;
                    break;

                case FlowchartKinds.Class:
                    foreach (var given in FlowchartGrammar.Styling.Givens(stated))
                        taken.Add((FlowchartGrammar.Styling.Ids(stated), given));
                    break;

                case FlowchartKinds.Style:
                    written.Add((FlowchartGrammar.Styling.Ids(stated), MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties))));
                    break;

                case FlowchartKinds.LinkStyle:
                    styled.Add((Numbered(stated), Every(stated), MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties))));
                    break;

                case FlowchartKinds.Click:
                    Clicked(stated, known);
                    break;

                case FlowchartKinds.Said:
                    said.Add((stated, Keyed(inside)));
                    break;
            }
        }

        // The metadata lines last: one may name a node or a link written below it, as Mermaid lets it — and one naming
        // neither makes the node it names, as Mermaid's does.
        foreach (var (metadata, group) in said) Says(metadata, group, nodes, known, named);

        // An id that names a subgraph is that subgraph rather than a node of its own, whichever was written first: a link
        // between two of them joins the boxes, which is how Mermaid reads it.
        var boxed = groups.Select(group => group.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        nodes.RemoveAll(node => boxed.Contains(node.Id));
        foreach (var id in boxed) known.Remove(id);

        foreach (var node in nodes) foreach (var name in node.Classes) taken.Add((new[] { node.Id }, name));

        var styles = MermaidStyling.Styles(known.Keys.Concat(groups.Select(group => group.Id)).Where(id => id.Length > 0),
                                           classes, taken, written);

        return new FlowchartDiagram(block, FlowchartConfig.Read(block.Config),
                                    Wayward(Setting(block.Header, FlowchartRoles.Towards)) ?? FlowchartWay.Down,
                                    [.. nodes.Select(node => Frozen(node, styles))],
                                    [.. links.Select(link => Frozen(link, styled))],
                                    [.. groups.Select(group => Frozen(group, styles, ways))]);
    }

    // ── Reading the lines ───────────────────────────────────────────────────

    /// <summary>
    /// The nodes a line writes and the links between them. A link joins every node written on its left to every node on its
    /// right, and the link after it carries on from those — a semicolon starting the line's next chain afresh.
    /// </summary>
    private static void Laid(ContentPart stated, string? group, List<Made> nodes, Dictionary<string, Made> known,
                             List<Joined> links, Dictionary<string, Joined> named)
    {
        var sides = new List<List<string>> { new() };
        var joins = new List<ContentPart?>();

        foreach (var piece in stated.Children)
        {
            switch (piece.Kind)
            {
                case FlowchartKinds.Node:
                    sides[^1].Add(Gathered(piece, group, nodes, known));
                    break;

                case FlowchartKinds.Link:
                    joins.Add(piece);
                    sides.Add([]);
                    break;

                default:
                    // A semicolon ends what was written before it; what follows it starts again.
                    if (piece.Role != Roles.Separator || piece.Text != ";") continue;

                    joins.Add(null);
                    sides.Add([]);
                    break;
            }
        }

        for (var at = 0; at < joins.Count; at++)
        {
            if (joins[at] is not { } link || sides[at].Count == 0 || sides[at + 1].Count == 0) continue;

            foreach (var from in sides[at])
                foreach (var to in sides[at + 1])
                {
                    var joined = Linked(link, from, to, links.Count);
                    links.Add(joined);
                    if (joined.Name is { Length: > 0 } name) named[name] = joined;
                }
        }
    }

    /// <summary>
    /// Takes a node into the chart — unless its id is already written, in which case this is that same node said again: what it
    /// says now is kept, and no second node is made for it. Hands back the id, which is what a link joins.
    /// </summary>
    private static string Gathered(ContentPart piece, string? group, List<Made> nodes, Dictionary<string, Made> known)
    {
        var name = piece.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        var label = piece.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
        var id = name.Words()?.Text ?? string.Empty;
        var classes = piece.SelfAndDescendants()
            .Where(child => child.Kind == MermaidKinds.Words && child.Role == FlowchartRoles.Class)
            .Select(child => child.Text)
            .Where(taken => taken.Length > 0);

        if (id.Length > 0 && known.TryGetValue(id, out var already))
        {
            if (label.Words() is not null)
            {
                already.Said = label.Words();
                already.SaidHole = label.Hole();
            }

            if (MermaidShapes.Of(piece) != MermaidShape.None) already.Shape = MermaidShapes.Of(piece);
            already.Classes.AddRange(classes);

            // A node written outside every subgraph and again inside one goes into that one — which is how Mermaid reads it, and how
            // a node linked to before its subgraph is opened is put in one at all. One already in a subgraph stays in the first.
            already.Group ??= group;
            return id;
        }

        var made = new Made(piece, id, nodes.Count)
        {
            Group = group,
            Shape = MermaidShapes.Of(piece),
            Said = label.Words() ?? name.Words(),
            SaidHole = (label ?? name)?.Hole(),
        };

        made.Classes.AddRange(classes);

        if (id.Length > 0) known[id] = made;
        nodes.Add(made);

        return id;
    }

    /// <summary>A subgraph as it was written: what it is called, and what is written at the top of it.</summary>
    private static Held Opens(ContentPart stated, string? parent, int order)
    {
        var node = stated.Inner(FlowchartKinds.Node);
        var name = node?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        var label = node?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);

        return new Held(stated, stated.Fact(FlowchartRoles.Opened) ?? string.Empty, name.Words()?.Text ?? string.Empty, order)
        {
            Parent = parent,

            // A subgraph given only a title is called by it, which is how Mermaid names one written that way.
            Said = label.Words() ?? name.Words(),
            SaidHole = (label ?? name)?.Hole(),
        };
    }

    private static Joined Linked(ContentPart part, string from, string to, int order)
    {
        var said = part.Children.FirstOrDefault(child => child.Kind is MermaidKinds.Label or MermaidKinds.Quoted
                                                         or FlowchartKinds.Saying);

        var drawn = MermaidLinks.Of(part.Children.FirstOrDefault(child => child.Role == FlowchartRoles.Arrow)?.Text);

        return new Joined(part, from, to, order)
        {
            Said = said.Words(),
            SaidHole = said?.Hole(),
            Drawn = drawn,
            Name = part.Children.FirstOrDefault(child => child.Role == FlowchartRoles.Link)?.Text,
        };
    }

    /// <summary>What a <c>click</c> line says about the node it names.</summary>
    private static void Clicked(ContentPart stated, IReadOnlyDictionary<string, Made> known)
    {
        if (Words(stated, FlowchartRoles.Id) is not { Length: > 0 } id || !known.TryGetValue(id, out var node)) return;

        node.Href = Words(stated, FlowchartRoles.Href) ?? node.Href;
        node.Tip = Words(stated, FlowchartRoles.Tip) ?? node.Tip;
    }

    /// <summary>What an <c>id@{ … }</c> line says about the node or the link it names.</summary>
    private static void Says(ContentPart stated, string? group, List<Made> nodes, Dictionary<string, Made> known, IReadOnlyDictionary<string, Joined> named)
    {
        if (Words(stated, FlowchartRoles.Id) is not { Length: > 0 } id) return;

        var properties = stated.Inner(MermaidKinds.Properties);

        if (!known.ContainsKey(id) && !named.ContainsKey(id))
        {
            var name = stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == FlowchartRoles.Id);
            var made = new Made(stated, id, nodes.Count) { Group = group, Said = name, SaidHole = name?.Hole() };
            known[id] = made;
            nodes.Add(made);
        }

        if (known.TryGetValue(id, out var node))
        {
            if (Set(properties, "shape") is { } shape) node.Shape = MermaidShapes.Named(shape.Text) ?? MermaidShape.Rectangle;
            if (Pictured(properties) is { } picture) node.Picture = picture;

            if ((Set(properties, "label") ?? Set(properties, "title")) is { } written)
            {
                node.Worked = MermaidText.Decode(MermaidText.Bare(written.Text));
                node.WorkedPart = written;
            }
        }

        if (named.TryGetValue(id, out var link) && Set(properties, "curve") is { } curve) link.Curve = curve.Text;
    }

    /// <summary>The picture or icon a node's metadata names, and how it is asked to be drawn — or null where it names neither.</summary>
    private static FlowchartPicture? Pictured(ContentPart? properties)
    {
        var named = Set(properties, "img") ?? Set(properties, "icon");
        if ((named?.Parent is { Kind: MermaidKinds.Icon } icon ? icon.Parent : named?.Parent) is not { } written) return null;

        var iconed = string.Equals(written.Part(Roles.Name)?.Text, "icon", StringComparison.OrdinalIgnoreCase) ? Bared(named) : null;

        return new FlowchartPicture(written, iconed, Bared(Set(properties, "form")) is { Length: > 0 } form ? form.ToLowerInvariant() : null,
                                    string.Equals(Bared(Set(properties, "pos")), "t", StringComparison.OrdinalIgnoreCase),
                                    Measured(Set(properties, "w")), Measured(Set(properties, "h")),
                                    string.Equals(Bared(Set(properties, "constraint")), "on", StringComparison.OrdinalIgnoreCase));

        static string? Bared(ContentPart? value) => value is null ? null : MermaidText.Bare(value.Text).Trim();

        static double? Measured(ContentPart? value) => MermaidNumber.Read(Bared(value)) is { } size && size > 0 ? size : null;
    }

    /// <summary>What a property of some metadata is set to, or null where the metadata does not set it — an icon's name as written, inside what says it names one.</summary>
    private static ContentPart? Set(ContentPart? properties, string name) =>
        properties?.Children
            .Where(property => property.Kind == MermaidKinds.Property
                               && string.Equals(property.Part(Roles.Name)?.Text, name, StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Part(MermaidRoles.Value) is { Kind: MermaidKinds.Icon } icon ? icon.Part(MermaidRoles.Value) : property.Part(MermaidRoles.Value))
            .FirstOrDefault(value => value is { Length: > 0 });

    /// <summary>The links a <c>linkStyle</c> line numbers.</summary>
    private static IReadOnlyList<int> Numbered(ContentPart stated) =>
        [.. stated.SelfAndDescendants()
              .Where(part => part.Kind == MermaidKinds.Words && part.Role == FlowchartRoles.Index)
              .Select(part => MermaidNumber.Read(part.Text))
              .OfType<double>()
              .Where(at => at >= 0 && at == Math.Floor(at))
              .Select(at => (int)at)];

    /// <summary>Whether a <c>linkStyle</c> line styles every link, which is what <c>default</c> asks for.</summary>
    private static bool Every(ContentPart stated) =>
        stated.Children.Any(child => child.Kind == MermaidKinds.Key && child.Role == FlowchartRoles.Index);

    private static string? Words(ContentPart stated, string role) =>
        stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Words && part.Role == role)?.Text;

    private static string? Setting(ContentPart? stated, string role) =>
        stated?.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Setting && part.Role == role)?.Text;

    private static string? Keyed(string? fact) => fact is null or MermaidNesting.Outermost ? null : fact;

    /// <summary>The way a word says something is laid out, or null where it says nothing this reads.</summary>
    public static FlowchartWay? Wayward(string? said) => (said ?? string.Empty).ToUpperInvariant() switch
    {
        "TB" or "TD" => FlowchartWay.Down,
        "BT" => FlowchartWay.Up,
        "LR" => FlowchartWay.Right,
        "RL" => FlowchartWay.Left,
        _ => null,
    };

    // ── What it comes to ────────────────────────────────────────────────────

    private static FlowchartNode Frozen(Made made, IReadOnlyDictionary<string, MermaidStyle> styles) =>
        new(made.Part!, made.Id, made.Said, made.Shape, made.Group, styles.GetValueOrDefault(made.Id, MermaidStyle.None), made.Order)
        {
            SaidHole = made.SaidHole,
            Worked = made.Worked,
            WorkedPart = made.WorkedPart,
            Href = made.Href,
            Tip = made.Tip,
            Picture = made.Picture,
        };

    private static FlowchartLink Frozen(Joined joined, IReadOnlyList<(IReadOnlyList<int> Links, bool Every, MermaidStyle Style)> styled)
    {
        var style = MermaidStyle.None;
        foreach (var (links, every, written) in styled)
            if (every || links.Contains(joined.Order))
                style = written.Over(style);

        return new FlowchartLink(joined.Part, joined.From, joined.To, joined.Said, joined.Drawn.Start, joined.Drawn.End,
                                 joined.Drawn.Style, joined.Drawn.Span, joined.Order)
        {
            SaidHole = joined.SaidHole,
            Written = style,
            Name = joined.Name,
            Curve = joined.Curve,
        };
    }

    private static FlowchartGroup Frozen(Held held, IReadOnlyDictionary<string, MermaidStyle> styles,
                                         IReadOnlyDictionary<string, FlowchartWay> ways) =>
        new(held.Part, held.Key, held.Id, held.Said, held.Parent, ways.TryGetValue(held.Key, out var way) ? way : null,
            styles.GetValueOrDefault(held.Id, MermaidStyle.None), held.Order)
        {
            SaidHole = held.SaidHole,
            Whole = new SourceSpan(held.Part.Start, (held.Closed?.End ?? held.Part.End) - held.Part.Start),
        };

    /// <summary>A node being read: what it is called, and everything the lines say about it as they are read.</summary>
    private sealed class Made(ContentPart part, string id, int order)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        public int Order { get; } = order;

        public string? Group { get; set; }

        public MermaidShape Shape { get; set; }

        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        public string? Worked { get; set; }

        public ContentPart? WorkedPart { get; set; }

        public string? Href { get; set; }

        public string? Tip { get; set; }

        public FlowchartPicture? Picture { get; set; }

        public List<string> Classes { get; } = [];
    }

    /// <summary>A link being read.</summary>
    private sealed class Joined(ContentPart part, string from, string to, int order)
    {
        public ContentPart Part { get; } = part;

        public string From { get; } = from;

        public string To { get; } = to;

        public int Order { get; } = order;

        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        public MermaidLinks.Drawn Drawn { get; init; }

        public string? Name { get; init; }

        /// <summary>The curve its own metadata asks for, which nothing but its name can give it.</summary>
        public string? Curve { get; set; }
    }

    /// <summary>A subgraph being read.</summary>
    private sealed class Held(ContentPart part, string key, string id, int order)
    {
        public ContentPart Part { get; } = part;

        public string Key { get; } = key;

        public string Id { get; } = id;

        public int Order { get; } = order;

        public string? Parent { get; init; }

        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        /// <summary>The <c>end</c> that closed it, or null for one nothing ends.</summary>
        public ContentPart? Closed { get; set; }
    }
}


