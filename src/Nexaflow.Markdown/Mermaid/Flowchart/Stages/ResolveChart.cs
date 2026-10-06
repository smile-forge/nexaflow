using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Reads the chart as the graph it is: every node once, whatever number of lines name it, and every connection between them.
///
/// <para>
/// A graph is not a tree, so it is kept as the next best thing — a list of nodes and a list of connections between them, each
/// connection naming its two ends by what they are called. The written tree stays what it is, because it is also the source:
/// it says where every mention of a node stands, and a reader selects and edits characters there. This says what those
/// characters amount to, and is derived, so it prints as nothing and takes up none.
/// </para>
/// <para>
/// What the written tree cannot say comes out here, which is everything that takes the whole block to settle. A node written on
/// two lines is one node, and one an <c>id@{ … }</c> line is about is a node whether any line wrote it. A node's words, and the
/// shape it is drawn as, are the last ones any line gave it; the subgraph it belongs to the first it was written in. And where a
/// node leads is on the node, written as prose writes a link — <see cref="Kinds.Link"/> holding a <see cref="Roles.Destination"/>
/// and the words shown — so whoever follows a link needs to know nothing about charts.
/// </para>
/// <para>
/// Nothing positional is here, because a stage has no positions: a node says which of its mentions is the one drawn, and a
/// connection which written link drew it, and whoever lays the chart out looks the characters up by that.
/// </para>
/// </summary>
public sealed class ResolveChart : IAstStage
{
    public string Name => "flowchart:chart";

    public ContentNode Run(ContentNode tree)
    {
        var chart = new Chart();

        chart.Read(tree, null);
        chart.Settle();

        return chart.Empty ? tree : tree.With([.. tree.Children, chart.Graph()]);
    }

    /// <summary>The chart as it is read: the nodes in the order they are first written, and the connections in the order they are.</summary>
    private sealed class Chart
    {
        private readonly List<Knows> order = [];
        private readonly Dictionary<string, Knows> known = new(StringComparer.Ordinal);
        private readonly List<ContentNode> links = [];
        private readonly List<(FlowchartMetadataNode Said, string? Group)> meant = [];
        private readonly List<(string Id, string Where, string? Tip)> clicked = [];
        private int opened;
        private int drawn;

        public bool Empty => this.order.Count == 0 && this.links.Count == 0;

        /// <summary>Everything written in one part of the block — the whole of it, or one subgraph — inside the subgraph given.</summary>
        public void Read(ContentNode holder, string? inside)
        {
            foreach (var node in holder.Children)
            {
                if (node.Kind == MermaidKinds.Group)
                {
                    if (node.Children.FirstOrDefault()?.Stated() is not null)
                        Read(node, this.opened++.ToString(CultureInfo.InvariantCulture));

                    continue;
                }

                if (node.Stated() is not { } stated) continue;

                if (stated is FlowchartMetadataNode about) this.meant.Add((about, inside));
                if (stated.Kind == FlowchartKinds.Click) Clicked(stated);
                if (stated.Kind == FlowchartKinds.Nodes) Laid(stated, inside);
            }
        }

        /// <summary>
        /// What the lines about the nodes say, once every line is read, because either may be written above the node it is about. An
        /// <c>id@{ … }</c> line is about a node, or about nothing written, which it makes a node of in the subgraph it is itself in.
        /// A <c>click</c> line is only ever about a node: one naming nothing is about nothing.
        /// </summary>
        public void Settle()
        {
            foreach (var (said, group) in this.meant)
            {
                if (said.About is not (FlowchartSaid.Node or FlowchartSaid.New)) continue;
                if (Said(said, FlowchartRoles.Id) is not { Length: > 0 } id) continue;

                var node = Taken(id, group);

                if (said.Shape is { } shape) node.Shape = shape;

                if (said.Label is { Length: > 0 } worked)
                {
                    node.Says = worked;
                    node.Meant = true;
                }
            }

            foreach (var (id, where, tip) in this.clicked)
                if (this.known.TryGetValue(id, out var node))
                    node.Leads = (where, tip);
        }

        /// <summary>The graph itself, as the one derived part of the block that says what the chart amounts to.</summary>
        public ContentNode Graph() =>
            ContentNode.Branch(FlowchartKinds.Graph,
                               [ContentNode.Branch(FlowchartKinds.GraphNodes, [.. this.order.Select(Made)]),
                                ContentNode.Branch(FlowchartKinds.GraphLinks, this.links)],
                               Roles.Derived);

        /// <summary>The nodes a line writes, and every join its links make as their stage said.</summary>
        private void Laid(ContentNode stated, string? group)
        {
            foreach (var piece in stated.Children)
            {
                // A node naming a subgraph is that subgraph, which a link joins as the box it is rather than as a node.
                if (piece.Kind == FlowchartKinds.Node && piece is not GroupReferenceNode) Gathered(piece, group);

                if (piece.Kind != FlowchartKinds.Link) continue;

                var written = this.drawn++;

                if (piece is FlowchartLinkNode joined)
                    this.links.AddRange(joined.Joins.Select(join => Joined(join, joined.Curve, written)));
            }
        }

        /// <summary>
        /// Takes a node into the chart — or, where its id is already in, keeps what this mention of it says. Mermaid's rule: a node
        /// is written once and named as often as a reader likes, the words and the shape are the last a line gave it, and the
        /// subgraph is the first it was written in.
        /// </summary>
        private void Gathered(ContentNode piece, string? group)
        {
            if (Inside(piece, MermaidKinds.Name) is not { Length: > 0 } id) return;

            var node = Taken(id, group);
            var mention = node.Mentions++;

            node.Mention ??= mention;
            node.Group ??= group;

            var shape = MermaidShapes.Of(Shaped(piece), Closed(piece));
            if (shape != MermaidShape.None) node.Shape = shape;

            if (Inside(piece, MermaidKinds.Label) is { Length: > 0 } label)
            {
                node.Says = label;
                node.Mention = mention;
                node.Meant = false;
            }
        }

        /// <summary>Where a <c>click</c> line says the node it names leads, and what that node says while pointed at.</summary>
        private void Clicked(ContentNode stated)
        {
            if (Said(stated, FlowchartRoles.Id) is not { Length: > 0 } id) return;
            if (Said(stated, Roles.Destination) is not { Length: > 0 } where) return;

            this.clicked.Add((id, where, Said(stated, FlowchartRoles.Tip)));
        }

        /// <summary>The node an id names, put into the chart — in the subgraph given — where nothing has named it yet.</summary>
        private Knows Taken(string id, string? group)
        {
            if (this.known.TryGetValue(id, out var already)) return already;

            var made = new Knows(id) { Group = group };

            this.known[id] = made;
            this.order.Add(made);

            return made;
        }
    }

    /// <summary>What is known of one node while the chart is read.</summary>
    private sealed class Knows(string id)
    {
        public string Id { get; } = id;

        /// <summary>The words drawn on it: its label, its metadata's, or what it is called where nothing says any.</summary>
        public string Says { get; set; } = id;

        /// <summary>How many times it has been written so far, which is what numbers the mention drawn.</summary>
        public int Mentions { get; set; }

        /// <summary>Which of its mentions holds the words drawn, or null where no line wrote it at all.</summary>
        public int? Mention { get; set; }

        /// <summary>Whether its words came from an <c>id@{ … }</c> line rather than from any mention of it.</summary>
        public bool Meant { get; set; }

        public MermaidShape Shape { get; set; }

        /// <summary>The key of the subgraph it was first written in, or null for one written outside them all.</summary>
        public string? Group { get; set; }

        public (string Where, string? Tip)? Leads { get; set; }
    }

    /// <summary>One node: what it is called, and what it says — inside a link, where it leads somewhere.</summary>
    private static ContentNode Made(Knows node)
    {
        var words = ContentNode.Leaf(Kinds.Words, node.Says, Roles.Body);
        var name = ContentNode.Leaf(Kinds.Words, node.Id, Roles.Name);

        if (node.Leads is not { } goes) return Settled(ContentNode.Branch(FlowchartKinds.GraphNode, [name, words]), node);

        var link = new List<ContentNode> { ContentNode.Leaf(Kinds.Words, goes.Where, Roles.Destination), words };
        if (goes.Tip is { Length: > 0 } tip) link.Add(ContentNode.Leaf(Kinds.Words, tip, Roles.Tip));

        return Settled(ContentNode.Branch(FlowchartKinds.GraphNode, [name, ContentNode.Branch(Kinds.Link, link)]), node);
    }

    /// <summary>The node, with everything settled about it that is not words, hung on the piece standing for it.</summary>
    private static ContentNode Settled(ContentNode written, Knows node) =>
        new FlowchartGraphNode(written, node.Id, node.Shape, node.Group, node.Mention, node.Meant);

    /// <summary>One connection, by the names of the two nodes it runs between.</summary>
    private static ContentNode Joined(FlowchartJoin join, string? curve, int drawn) =>
        new FlowchartGraphLink(ContentNode.Branch(FlowchartKinds.GraphLink,
                                                  [ContentNode.Leaf(Kinds.Words, join.From, FlowchartRoles.From),
                                                   ContentNode.Leaf(Kinds.Words, join.To, FlowchartRoles.To)]),
                               join.Style, curve, drawn);

    /// <summary>What a node writes under a part of itself — its name, its label — or null where it writes none.</summary>
    private static string? Inside(ContentNode piece, string kind) =>
        piece.Children.FirstOrDefault(child => child.Kind == kind).Words()?.Text;

    /// <summary>What a line says in a role, wherever inside it that is written.</summary>
    private static string? Said(ContentNode stated, string role) =>
        stated.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == Kinds.Words && inner.Role == role)?.Text;

    /// <summary>The bracket a node's label opens with, which with the one closing it says the shape it is drawn as.</summary>
    private static string? Shaped(ContentNode piece) =>
        piece.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label)?.Children
             .FirstOrDefault(child => child.Role == Roles.Open)?.Text;

    private static string? Closed(ContentNode piece) =>
        piece.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label)?.Children
             .LastOrDefault(child => child.Role == Roles.Close)?.Text;
}
