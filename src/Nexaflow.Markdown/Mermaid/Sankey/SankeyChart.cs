using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Sankey;

/// <summary>
/// A node, read: nothing declares one, so it is the name a flow was written between, and it stands for the first flow that
/// wrote it.
/// </summary>
/// <param name="Said">The name as it was first written, which is what typing beside the node changes.</param>
/// <param name="Order">Where it comes among the nodes, which is the colour it takes and the order it is stacked in.</param>
public sealed record SankeyNode(string Name, ContentPart Said, int Order);

/// <summary>
/// One flow, read: where it comes from, where it goes, and what it is worth.
/// </summary>
/// <param name="Part">The whole row as it was written, which is what a press on the ribbon means.</param>
/// <param name="Worth">What its value comes to, or nought where none is written or it is no number.</param>
public sealed record SankeyFlow(
    ContentPart Part,
    ContentPart? Source,
    ContentPart? Target,
    ContentPart? Value,
    string From,
    string To,
    double Worth)
{
    /// <summary>The hole standing where the value is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? ValueHole { get; init; }

    /// <summary>What is wrong with it, where anything is.</summary>
    public string? Trouble => Value?.Trouble;

    /// <summary>Whether it is worth drawing a ribbon for.</summary>
    public bool Drawn => Worth > 0 && From.Length > 0 && To.Length > 0;
}

/// <summary>
/// A <c>sankey-beta</c> block, read: the flows written in it and the nodes they are written between. Its title is the
/// block's (<see cref="MermaidBlock.Title"/>), Mermaid's sankey having no title line of its own.
///
/// <para>
/// Nothing declares a node. The nodes are the names the flows are written between, in the order they are first written,
/// which is the order they are stacked in and the colour each takes.
/// </para>
/// </summary>
public sealed class SankeyChart
{
    private SankeyChart(MermaidBlock block, SankeyConfig config, IReadOnlyList<SankeyNode> nodes, IReadOnlyList<SankeyFlow> flows)
    {
        Block = block;
        Config = config;
        Nodes = nodes;
        Flows = flows;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    public static SankeyChart Read(string? block) => Of(MermaidParser.Read(block));

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static SankeyChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public SankeyConfig Config { get; }

    /// <summary>The nodes, in the order they are first written.</summary>
    public IReadOnlyList<SankeyNode> Nodes { get; }

    /// <summary>The flows, in the order they are written.</summary>
    public IReadOnlyList<SankeyFlow> Flows { get; }

    /// <summary>What a node is worth: whatever flows into it, or out of it, whichever is the more.</summary>
    public double Worth(SankeyNode node) => Math.Max(Into(node), OutOf(node));

    /// <summary>What flows into a node.</summary>
    public double Into(SankeyNode node) =>
        Flows.Where(flow => flow.Drawn && string.Equals(flow.To, node.Name, StringComparison.Ordinal)).Sum(flow => flow.Worth);

    /// <summary>What flows out of a node.</summary>
    public double OutOf(SankeyNode node) =>
        Flows.Where(flow => flow.Drawn && string.Equals(flow.From, node.Name, StringComparison.Ordinal)).Sum(flow => flow.Worth);

    /// <summary>The node a name names, or null where nothing is called that.</summary>
    public SankeyNode? Node(string name) =>
        Nodes.FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal));

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the sankey's own stages.</summary>
    public static SankeyChart Of(MermaidBlock block)
    {
        var flows = new List<SankeyFlow>();
        var nodes = new List<SankeyNode>();
        var known = new Dictionary<string, SankeyNode>(StringComparer.Ordinal);

        foreach (var part in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == SankeyKinds.Flow))
        {
            var names = part.Children.Where(child => child.Kind == MermaidKinds.Name).ToList();
            var source = names.Count > 0 ? names[0].Words() : null;
            var target = names.Count > 1 ? names[1].Words() : null;
            var value = part.Inner(MermaidKinds.Number);

            var from = Says(source);
            var to = Says(target);

            flows.Add(new SankeyFlow(part, source, target, value, from, to, value.Number() ?? 0)
            {
                ValueHole = part.Inner(MermaidKinds.Amount).Hole(),
            });

            foreach (var (name, said) in new[] { (from, source), (to, target) })
                if (name.Length > 0 && said is not null && !known.ContainsKey(name))
                {
                    known[name] = new SankeyNode(name, said, nodes.Count);
                    nodes.Add(known[name]);
                }
        }

        return new SankeyChart(block, SankeyConfig.Read(block.Config), nodes, flows);
    }

    /// <summary>
    /// What a name says: what is between its quotes where it has them, with a quote written twice standing for one — and
    /// without the space either side of it, as Mermaid reads a field.
    /// </summary>
    private static string Says(ContentPart? words) =>
        words is null ? string.Empty : words.Text.Replace(SankeyGrammar.Quoted, "\"", StringComparison.Ordinal).Trim();
}
