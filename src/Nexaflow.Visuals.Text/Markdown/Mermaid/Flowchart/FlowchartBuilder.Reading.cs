using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

/// <summary>What a flowchart draws, read down its tree in the order it is written.</summary>
internal partial class FlowchartBuilder
{
    /// <summary>
    /// One node: where it was first written, what it is called, what is drawn on it, the shape it is drawn as, the subgraph it
    /// belongs to and what it is styled with.
    /// </summary>
    /// <param name="Part">The whole node as it was first written, which is what a press on it means.</param>
    /// <param name="Id">What it is called, which is what a link, a <c>class</c>, a <c>style</c> and a <c>click</c> name it by.</param>
    protected sealed class Node(ContentPart part, string id)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        /// <summary>The words drawn on it: its label, or what it is called where nothing else says anything.</summary>
        public ContentPart? Said { get; set; }

        /// <summary>The hole standing where its label goes, where holes were asked for and nothing is written there yet.</summary>
        public ContentPart? SaidHole { get; set; }

        public MermaidShape Shape { get; set; }

        /// <summary>The key of the subgraph it was first written in, or null for one written outside them all.</summary>
        public string? Group { get; set; }

        public MermaidStyle Style { get; set; } = MermaidStyle.None;

        /// <summary>Where it leads, as the chart's own reading of itself says — a <c>click</c> line's address, on the node it named.</summary>
        public string? Href { get; init; }

        /// <summary>What a <c>click</c> line says it says while pointed at.</summary>
        public string? Tip { get; set; }

        /// <summary>The picture or icon it is drawn as, where its metadata names one — null for a node drawn as its shape.</summary>
        public Picture? Picture { get; set; }
    }

    /// <summary>A node drawn as a picture: what its stage said of it, and the <c>img:</c> or <c>icon:</c> line it was named on, which a found picture is hung on.</summary>
    protected sealed record Picture(ContentPart Written, FlowchartPicture Said)
    {
        public string? Icon => Said.Icon;

        public string? Form => Said.Form;

        public bool Above => Said.Above;

        public double? Width => Said.Width;

        public double? Height => Said.Height;

        public bool Keeps => Said.Keeps;
    }

    /// <summary>
    /// One join a link makes: the nodes it joins, what is written on it, what it draws at each end, how its line is drawn, and how
    /// far apart it holds what it joins.
    /// </summary>
    /// <param name="Part">The link as it was written, which is what a press on it means.</param>
    /// <param name="Span">How many ranks it reaches over, which is how many the author wrote it long.</param>
    /// <param name="Order">Where it comes among the joins, which keeps two joins written alike two links.</param>
    protected sealed record Link(ContentPart Part, string From, string To, ContentPart? Said, MermaidHead Start, MermaidHead End,
                                 MermaidLineStyle Style, int Span, int Order)
    {
        public ContentPart? SaidHole { get; init; }

        /// <summary>What a <c>linkStyle</c> line asks for it, over what its own characters say.</summary>
        public MermaidStyle Written { get; init; } = MermaidStyle.None;

        /// <summary>The curve an <c>id@{ curve: … }</c> line asks for it, over whatever the front matter asks for every link.</summary>
        public string? Curve { get; init; }

        /// <summary>Whether it is drawn at all: a link of tildes only holds what it joins apart.</summary>
        public bool Drawn => Style != MermaidLineStyle.Invisible;
    }

    /// <summary>
    /// One subgraph: the box drawn round every node first written inside it, what is written at the top of it, the subgraph it is
    /// itself inside, and the way the nodes in it are laid out where a <c>direction</c> line says one of its own.
    /// </summary>
    /// <param name="Key">Where it stands among the subgraphs written, which is what a node says it is inside.</param>
    /// <param name="Id">What it is called, which a link and a <c>style</c> line name it by.</param>
    protected sealed class Group(ContentPart part, string key, string id, string? parent)
    {
        public ContentPart Part { get; } = part;

        public string Key { get; } = key;

        public string Id { get; } = id;

        public string? Parent { get; } = parent;

        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        public DiagramWay? Way { get; set; }

        public MermaidStyle Style { get; set; } = MermaidStyle.None;

        /// <summary>
        /// The whole of it as it was written, from the line that opened it through the <c>end</c> that closed it — the opening line
        /// alone, where nothing closed it. What is drawn for a subgraph that holds the whole of its work, a swimlane's lane, stands
        /// for this, so a press on the lane means the lane and everything in it sits inside it.
        /// </summary>
        public ISourcePart Whole { get; init; } = default(SourceSpan);
    }

    /// <summary>
    /// What the block writes, read down the tree in the order it is written.
    ///
    /// <para>
    /// A node written twice is one node: the second writing of an id says more about the node the first one made — the shape and
    /// the words it is drawn with — rather than making another, which is what lets a link name the nodes a line above wrote. A node
    /// belongs to the subgraph it was first written in, as Mermaid gathers them — or, written outside them all first, to the first
    /// one it is written in after.
    /// </para>
    /// </summary>
    protected sealed class Diagram
    {
        /// <summary>Every mention of each node, by what it is called, in the order they are written.</summary>
        private readonly Dictionary<string, List<ContentPart>> mentions = new(StringComparer.Ordinal);

        /// <summary>The <c>id@{ … }</c> line about each node, by the node it is about.</summary>
        private readonly Dictionary<string, ContentPart> meant = new(StringComparer.Ordinal);

        /// <summary>Every link written, in the order they are written, which is what a connection says drew it.</summary>
        private readonly List<ContentPart> drawn = [];

        private Diagram(FlowchartConfig config, DiagramWay way) => (Config, Way) = (config, way);

        /// <summary>What the front matter asks for.</summary>
        public FlowchartConfig Config { get; }

        /// <summary>The way the whole chart is laid out, as its header says.</summary>
        public DiagramWay Way { get; }

        /// <summary>The nodes, in the order they are first written.</summary>
        public List<Node> Nodes { get; } = [];

        /// <summary>The links, in the order they are written.</summary>
        public List<Link> Links { get; } = [];

        /// <summary>The subgraphs, each before the ones nested in it.</summary>
        public List<Group> Groups { get; } = [];

        public static Diagram Of(ContentPart root, ContentPart? header, FlowchartConfig config)
        {
            var towards = header?.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Setting && part.Role == FlowchartRoles.Towards);
            var diagram = new Diagram(config, Wayward(towards?.Text) ?? DiagramWay.Down);

            diagram.Read(root, null);
            diagram.Took(root);
            diagram.Styled(root);

            return diagram;
        }

        /// <summary>The node an id names, or null where nothing is called that.</summary>
        public Node? Find(string id) =>
            id.Length == 0 ? null : Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

        /// <summary>The nodes written inside a subgraph — those written outside them all, for null.</summary>
        public IEnumerable<Node> Inside(string? group) =>
            Nodes.Where(node => string.Equals(node.Group, group, StringComparison.Ordinal));

        /// <summary>The subgraphs opened inside a subgraph — the outermost ones, for null.</summary>
        public IEnumerable<Group> Within(string? group) =>
            Groups.Where(nested => string.Equals(nested.Parent, group, StringComparison.Ordinal));

        /// <summary>
        /// The lanes: the subgraphs written outside them all, which is what a swimlane draws as bands. Every other subgraph is inside one.
        /// </summary>
        public IEnumerable<Group> Lanes => Within(null);

        /// <summary>
        /// The lane a subgraph, or anything written in one, belongs to: the outermost subgraph holding it. Null for something written in
        /// no subgraph at all.
        /// </summary>
        public Group? Lane(string? group)
        {
            var held = Keyed(group);
            while (held is { Parent: { } parent }) held = Keyed(parent);

            return held;
        }

        /// <summary>The subgraph a key names, or null where none does.</summary>
        public Group? Keyed(string? key) =>
            key is null ? null : Groups.FirstOrDefault(group => string.Equals(group.Key, key, StringComparison.Ordinal));

        /// <summary>
        /// Where everything is written: the subgraphs, every mention of each node, every link drawn, and the metadata about each — read
        /// down one part of the block, the whole of it or one subgraph, inside the subgraph given.
        /// </summary>
        private void Read(ContentPart holder, string? inside)
        {
            foreach (var part in holder.Children)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    if (Opened(part, inside) is { } group) Read(part, group.Key);
                    continue;
                }

                if (part.Stated() is not { } stated) continue;

                if (stated.Node is FlowchartMetadataNode { About: FlowchartSaid.Node or FlowchartSaid.New }
                    && Words(stated, FlowchartRoles.Id) is { Length: > 0 } about)
                    this.meant.TryAdd(about, stated);

        // A direction line lays out the subgraph it is written in; one written outside them all lays out nothing.
                if (stated.Kind == FlowchartKinds.Direction && Keyed(inside) is { } laid)
                    laid.Way = Wayward(stated.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == MermaidKinds.Setting && inner.Role == FlowchartRoles.Towards)?.Text) ?? laid.Way;

                if (stated.Kind != FlowchartKinds.Nodes) continue;

                foreach (var piece in stated.Children)
                {
                    if (piece.Kind == FlowchartKinds.Link) this.drawn.Add(piece);

                    // A node naming a subgraph is that subgraph, which a link joins as the box it is rather than as a node.
                    if (piece.Kind != FlowchartKinds.Node || piece.Node is GroupReferenceNode) continue;
                    if (piece.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text is not { Length: > 0 } id) continue;

                    if (!this.mentions.TryGetValue(id, out var written)) this.mentions[id] = written = [];

                    written.Add(piece);
                }
            }
        }

        /// <summary>
        /// The chart as its stage read it: every node once and every connection between them, each found where it was written.
        ///
        /// <para>
        /// What the chart amounts to is settled before ever reaching here — which lines write one node, what its words are, the shape it
        /// is drawn as, the subgraph it is in, and where it leads. This finds the characters each of those stands for, because a stage has
        /// no positions and a press, a selection and a caret are all positions.
        /// </para>
        /// </summary>
        private void Took(ContentPart root)
        {
            if (root.SelfAndDescendants().FirstOrDefault(part => part.Kind == FlowchartKinds.Graph) is not { } graph) return;

            foreach (var entry in graph.SelfAndDescendants())
            {
                if (entry.Node is FlowchartGraphNode node) Nodes.Add(Made(entry, node));
                if (entry.Node is FlowchartGraphLink link && link.Drawn < this.drawn.Count) Links.Add(Joined(entry, link, this.drawn[link.Drawn]));
            }
        }

        /// <summary>
        /// One node: the whole of its first mention, which is what a press on it means, and the words drawn read from the mention its
        /// stage named — the last one to give it any.
        /// </summary>
        private Node Made(ContentPart entry, FlowchartGraphNode said)
        {
            var written = this.mentions.GetValueOrDefault(said.Id) ?? [];
            var drawn = said.Mention is { } at && at < written.Count ? written[at] : null;
            var meant = this.meant.GetValueOrDefault(said.Id);
            var link = entry.Children.FirstOrDefault(child => child.Kind == Kinds.Link);

            var node = new Node(written.FirstOrDefault() ?? meant ?? entry, said.Id)
            {
                Shape = said.Shape,
                Group = said.Group,
                Href = link is null ? null : MarkdownLinks.Goes(link.Node),
                Tip = link?.Part(Roles.Tip)?.Text,
            };

            // The words drawn are the characters a reader wrote them as, wherever those stand — between a node's brackets, or
            // between the quotes of the label an id@{ … } line gives it. Either way they are drawn as themselves and typed into
            // where they stand, which is all a run of words in a document ever is.
            if (said.Meant && Labelled(meant)?.Words() is { } worked)
            {
                node.Said = worked;
                node.SaidHole = worked.Hole();
            }
            else if (drawn is not null)
            {
                var label = drawn.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
                var name = drawn.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

                node.Said = label.Words() ?? name.Words();
                node.SaidHole = (label ?? name)?.Hole();
            }
            else if (meant is not null)
            {
                // A node no line wrote, which its metadata made: the id it names there is the whole of what is written of it.
                node.Said = meant.SelfAndDescendants().FirstOrDefault(part => part.Kind == Kinds.Words && part.Role == FlowchartRoles.Id);
                node.SaidHole = node.Said.Hole();
            }



            if (meant?.Node is FlowchartMetadataNode { Picture: { } picture } && Pictured(meant) is { } shown)
                node.Picture = new Picture(shown, picture);

            return node;
        }

        /// <summary>What the <c>label:</c> — or the <c>title:</c> — of an <c>id@{ … }</c> line is set to, where it sets one.</summary>
        private ContentPart? Labelled(ContentPart? meant) =>
            meant?.Inner(MermaidKinds.Properties) is { } properties ? Set(properties, "label") ?? Set(properties, "title") : null;

        /// <summary>One connection: drawn as the link that wrote it, and styled as the <c>linkStyle</c> lines numbering it asked.</summary>
        private Link Joined(ContentPart entry, FlowchartGraphLink said, ContentPart written)
        {
            var says = written.Children.FirstOrDefault(child => child.Kind is MermaidKinds.Label or MermaidKinds.Quoted or FlowchartKinds.Saying);
            var drawn = MermaidLinks.Of(written.Children.FirstOrDefault(child => child.Role == FlowchartRoles.Arrow)?.Text);

            return new Link(written,
                            entry.Part(FlowchartRoles.From)?.Text ?? string.Empty,
                            entry.Part(FlowchartRoles.To)?.Text ?? string.Empty,
                            says.Words(), drawn.Start, drawn.End, drawn.Style, drawn.Span, Links.Count)
            {
                SaidHole = says?.Hole(),
                Written = said.Style,
                Curve = said.Curve,
            };
        }

        /// <summary>The <c>img:</c> or <c>icon:</c> line a node is named a picture on, which a found picture is hung on.</summary>
        private static ContentPart? Pictured(ContentPart meant)
        {
            var properties = meant.Inner(MermaidKinds.Properties);

            if ((Set(properties, "img") ?? Set(properties, "icon")) is not { } named) return null;

            return named.Parent is { Kind: MermaidKinds.Icon } icon ? icon.Parent : named.Parent;
        }

        /// <summary>A subgraph, and the box it makes: what it is called, and what is written at the top of it.</summary>
        private Group? Opened(ContentPart holder, string? inside)
        {
            if (holder.Children.FirstOrDefault()?.Stated() is not { } opening) return null;

            var node = opening.Inner(FlowchartKinds.Node);
            var name = node?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
            var label = node?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);

            // Where a subgraph is closed is where the whole of it ends, which is the stretch it was written as.
            var closing = holder.Children[^1].Stated() is { Kind: FlowchartKinds.Ends } ends ? ends : opening;

            var group = new Group(opening, Groups.Count.ToString(CultureInfo.InvariantCulture), name.Words()?.Text ?? string.Empty, inside)
            {
                // A subgraph given only a title is called by it, which is how Mermaid names one written that way.
                Said = label.Words() ?? name.Words(),
                SaidHole = (label ?? name)?.Hole(),
                Whole = new SourceSpan(opening.Start, closing.End - opening.Start),
            };

            Groups.Add(group);
            return group;
        }

        /// <summary>What each node and each subgraph is styled with, as the stages said on the name first writing it.</summary>
        private void Styled(ContentPart root)
        {
            var styles = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);
            foreach (var part in root.SelfAndDescendants())
                if (part.Node is StyledNode && part.Words()?.Text is { Length: > 0 } id)
                    styles.TryAdd(id, StyleOf(part));

            foreach (var node in Nodes) node.Style = styles.GetValueOrDefault(node.Id, MermaidStyle.None);
            foreach (var group in Groups) group.Style = styles.GetValueOrDefault(group.Id, MermaidStyle.None);
        }

        /// <summary>What a property of some metadata is set to — an icon's name as written, inside what says it names one.</summary>
        private static ContentPart? Set(ContentPart? properties, string name) =>
            properties?.Children
                .Where(property => property.Kind == MermaidKinds.Property
                                   && string.Equals(property.Part(Roles.Name)?.Text, name, StringComparison.OrdinalIgnoreCase))
                .Select(property => property.Part(MermaidRoles.Value) is { Kind: MermaidKinds.Icon } icon ? icon.Part(MermaidRoles.Value) : property.Part(MermaidRoles.Value))
                .FirstOrDefault(value => value is { Length: > 0 });

        private static string? Words(ContentPart stated, string role) =>
            stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == Kinds.Words && part.Role == role)?.Text;

        /// <summary>The way a word says something is laid out, or null where it says nothing this reads.</summary>
        private static DiagramWay? Wayward(string? said) => (said ?? string.Empty).ToUpperInvariant() switch
        {
            "TB" or "TD" => DiagramWay.Down,
            "BT" => DiagramWay.Up,
            "LR" => DiagramWay.Right,
            "RL" => DiagramWay.Left,
            _ => null,
        };
    }
}
