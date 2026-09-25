using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Mindmap;

/// <summary>The shape a node's brackets ask for.</summary>
public enum MindmapShape
{
    /// <summary>A bare id, or a title in brackets Mermaid gives no shape: no border, an underline instead.</summary>
    Plain,

    /// <summary><c>[…]</c></summary>
    Square,

    /// <summary><c>(…)</c></summary>
    Rounded,

    /// <summary><c>((…))</c></summary>
    Circle,

    /// <summary><c>)…(</c></summary>
    Cloud,

    /// <summary><c>))…((</c></summary>
    Bang,

    /// <summary><c>{{…}}</c></summary>
    Hexagon,
}

/// <summary>
/// One node of a mindmap: what its title says, the shape its brackets ask for, and the nodes hanging off it.
/// </summary>
/// <param name="Part">The node as written — what pressing it means.</param>
/// <param name="Title">What its title says — its title in brackets, or its bare id — or null where it is still to write.</param>
/// <param name="Hole">The hole standing where its title is still to write.</param>
/// <param name="Depth">How far from the root it is: nought for the root itself.</param>
/// <param name="Branch">Which of the root's children it hangs off, counted round the root — or -1 for the root.</param>
public sealed record MindmapNode(ContentPart Part, ContentPart? Title, ContentPart? Hole, MindmapShape Shape, int Depth, int Branch)
{
    private readonly List<MindmapNode> _children = [];

    /// <summary>The nodes hanging off it, in the order they are written.</summary>
    public IReadOnlyList<MindmapNode> Children => _children;

    /// <summary>The icon an <c>::icon(…)</c> line gives it, where one does.</summary>
    public string? Icon { get; internal set; }

    /// <summary>The classes a <c>:::…</c> line gives it, where one does.</summary>
    public string? Class { get; internal set; }

    internal void Add(MindmapNode node) => _children.Add(node);
}

/// <summary>
/// A <c>mindmap</c> block, read: its root and everything hanging off it, as Mermaid nests them. The first node written is the
/// root; every later node hangs off the nearest node before it indented less, so indentation that is unclear — deeper than an
/// uncle but shallower than a sibling — still hangs the node off the nearest shallower node, as Mermaid does. A node hanging off
/// nothing is left out, and said to be wrong (<see cref="Stages.ResolveRoot"/>).
/// </summary>
public sealed class MindmapTree
{
    private MindmapTree(MermaidBlock block, MindmapConfig config) => (Block, Config) = (block, config);

    /// <summary>How many branches the root's children are shared between before the colours come round again.</summary>
    public const int Branches = 11;

    

    public static MindmapTree Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    public static MindmapTree Of(MermaidBlock block)
    {
        var map = new MindmapTree(block, MindmapConfig.Read(block.Config));
        var parts = block.Reading.Root.SelfAndDescendants().ToList();

        var lines = parts
            .Where(part => part.Kind == MindmapKinds.Node && part.Trouble is null)
            .Select(part => (part.Indent(), part))
            .ToList();

        // The root first, and every node under the nearest node before it indented less, however unclear the indentation
        // between them; a node indented no further than the root hangs off nothing, and is not drawn.
        var nested = MermaidOutline.Nested(lines);
        var made = new Dictionary<ContentPart, MindmapNode>();
        var nodes = new MindmapNode?[nested.Count];

        for (var at = 0; at < nested.Count; at++)
        {
            var (part, parent) = (nested[at].Item, nested[at].Parent);

            if (at == 0)
            {
                map.Root = new MindmapNode(part, Title(part), Hole(part), Shape(part), 0, -1);
                nodes[at] = map.Root;
            }
            else if (parent is { } over && nodes[over] is { } under)
            {
                nodes[at] = new MindmapNode(part, Title(part), Hole(part), Shape(part), under.Depth + 1,
                                            under.Depth == 0 ? under.Children.Count % Branches : under.Branch);
                under.Add(nodes[at]!);
            }

            if (nodes[at] is { } node) made[part] = node;
        }

        // An icon or a class is the node above its line's.
        MindmapNode? last = null;
        foreach (var part in parts)
            switch (part.Kind)
            {
                case MindmapKinds.Node when made.TryGetValue(part, out var node): last = node; break;
                case MindmapKinds.Icon when last is not null: last.Icon = part.Words()?.Text; break;
                case MindmapKinds.Class when last is not null: last.Class = part.Words()?.Text; break;
            }

        return map;
    }

    public MermaidBlock Block { get; }

    public MindmapConfig Config { get; }

    /// <summary>The root of the mindmap — null where nothing is written.</summary>
    public MindmapNode? Root { get; private set; }

    /// <summary>Every node of the mindmap, the root first.</summary>
    public IEnumerable<MindmapNode> Nodes => Root is null ? [] : Under(Root);

    private static IEnumerable<MindmapNode> Under(MindmapNode node) => [node, .. node.Children.SelectMany(Under)];

    /// <summary>A node's title: the words in its brackets, or else its bare id.</summary>
    private static ContentPart? Title(ContentPart node) =>
        node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Words() is { Length: > 0 } label ? label
        : node.Children.Any(child => child.Kind == MermaidKinds.Label) ? null
        : node.Children.FirstOrDefault(child => child is { Kind: MermaidKinds.Name, Role: MindmapRoles.Id }).Words() is { Length: > 0 } id ? id
        : null;

    private static ContentPart? Hole(ContentPart node) => node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Hole();

    /// <summary>The shape a node's brackets ask for.</summary>
    private static MindmapShape Shape(ContentPart node) => MermaidOutline.Opening(node) switch
    {
        "[" => MindmapShape.Square,
        "(" => MindmapShape.Rounded,
        "((" => MindmapShape.Circle,
        ")" => MindmapShape.Cloud,
        "))" => MindmapShape.Bang,
        "{{" => MindmapShape.Hexagon,
        _ => MindmapShape.Plain,
    };
}
