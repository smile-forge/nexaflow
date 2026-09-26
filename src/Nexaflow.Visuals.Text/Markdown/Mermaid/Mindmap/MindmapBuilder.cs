using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Mindmap;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Mindmap;

/// <summary>The pieces a mindmap's layout is made of — its layers, and what is in them.</summary>
public static class MindmapPiece
{
    /// <summary>The lines from each node to its children, one standing for the child it reaches.</summary>
    public const string Branches = "Branches";
    public const string Branch = "Branch";

    /// <summary>The nodes, each standing for its line, with what it says in it.</summary>
    public const string Nodes = "Nodes";
    public const string Node = "Node";
    public const string Title = "Title";
}

/// <summary>
/// Draws a <c>mindmap</c> block as a tidy tree: the root in the middle, its children taking turns either side of it, and every
/// node beside its parent with its own subtree given the room it needs (<see cref="DiagramTree"/>). Each node takes the shape its
/// brackets ask for — a square, a pill, a circle, a cloud, a bang, a hexagon, or a softly rounded box where none are written —
/// washed and edged in its branch's colour, and each branch off the root sweeps out in that colour from side to side, thinning
/// as it goes further out.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A node stands for its line and a branch for the node it
/// reaches, and a title is the characters written, typed into where it is drawn, line by line where it wraps.
/// </para>
/// <para>
/// Mermaid's own mindmap layout is a force simulation (<c>cose-bilkent</c>), which settles somewhere different every time it is
/// run; this is its <c>tidy-tree</c> layout, which is the same every time.
/// </para>
/// </summary>
internal sealed class MindmapBuilder : MermaidBuilder
{
    private const double TextSize = 13;
    private const double MaxNodeWidth = 200;
    private const double Padding = 10;

    /// <summary>How thick a branch is drawn at the root, how much thinner each level further out is, and the thinnest it gets.</summary>
    private const double Thickest = 4;
    private const double Thinner = 1;
    private const double Thinnest = 1.5;

    /// <summary>How strongly a node is washed in its branch's colour, and the root in the accent.</summary>
    private const double Wash = 0.2;
    private const double RootWash = 0.35;

    internal MindmapBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    /// <summary>How many branches the root's children are shared between before the colours come round again.</summary>
    private const int Branches = 11;

    /// <summary>The brackets a node's title is written in, which say its shape.</summary>
    private enum Brackets { None, Square, Rounded, Circle, Cloud, Bang, Hexagon }

    /// <summary>One node: the line it is written on — what pressing it means — what its title says, and the nodes hanging off it.</summary>
    /// <param name="title">Its title in brackets, or its bare id — or null where it is still to write.</param>
    /// <param name="depth">How far from the root it is: nought for the root itself.</param>
    /// <param name="branch">Which of the root's children it hangs off, counted round the root — or -1 for the root.</param>
    private sealed class Node(ContentPart part, ContentPart? title, ContentPart? hole, Brackets shape, int depth, int branch)
    {
        public ContentPart Part { get; } = part;

        public ContentPart? Title { get; } = title;

        public ContentPart? Hole { get; } = hole;

        public Brackets Shape { get; } = shape;

        public int Depth { get; } = depth;

        public int Branch { get; } = branch;

        /// <summary>The nodes hanging off it, in the order they are written.</summary>
        public List<Node> Children { get; } = [];

        /// <summary>It and every node under it, itself first.</summary>
        public IEnumerable<Node> All() => [this, .. Children.SelectMany(child => child.All())];
    }

    /// <summary>
    /// The root, with everything hanging off it as Mermaid nests them — or null where nothing is written. The first node is the
    /// root; every later one hangs off the nearest node before it indented less, so indentation that is unclear — deeper than an
    /// uncle but shallower than a sibling — still hangs it off the nearest shallower node, as Mermaid does. A node hanging off
    /// nothing is not drawn, and its stage says it is wrong.
    /// </summary>
    private Node? Read()
    {
        var lines = Reading.Root.SelfAndDescendants()
            .Where(part => part.Kind == MindmapKinds.Node && part.Trouble is null)
            .Select(part => (part.Indent(), part))
            .ToList();

        var nested = MermaidOutline.Nested(lines);
        var nodes = new Node?[nested.Count];

        for (var at = 0; at < nested.Count; at++)
        {
            var (part, parent) = (nested[at].Item, nested[at].Parent);

            if (at == 0)
            {
                nodes[at] = new Node(part, Title(part), Hole(part), Shape(part), 0, -1);
            }
            else if (parent is { } over && nodes[over] is { } under)
            {
                nodes[at] = new Node(part, Title(part), Hole(part), Shape(part), under.Depth + 1,
                                     under.Depth == 0 ? under.Children.Count % Branches : under.Branch);
                under.Children.Add(nodes[at]!);
            }
        }

        return nodes.FirstOrDefault();
    }

    /// <summary>A node's title: the words in its brackets, or else its bare id.</summary>
    private static ContentPart? Title(ContentPart node) =>
        node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Words() is { Length: > 0 } label ? label
        : node.Children.Any(child => child.Kind == MermaidKinds.Label) ? null
        : node.Children.FirstOrDefault(child => child is { Kind: MermaidKinds.Name, Role: MindmapRoles.Id }).Words() is { Length: > 0 } id ? id
        : null;

    private static ContentPart? Hole(ContentPart node) => node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label).Hole();

    /// <summary>The brackets a node's title is written in.</summary>
    private static Brackets Shape(ContentPart node) => MermaidOutline.Opening(node) switch
    {
        "[" => Brackets.Square,
        "(" => Brackets.Rounded,
        "((" => Brackets.Circle,
        ")" => Brackets.Cloud,
        "))" => Brackets.Bang,
        "{{" => Brackets.Hexagon,
        _ => Brackets.None,
    };

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        // A mindmap with no root is the source.
        if (Read() is not { } root) return AsWritten(build);

        var config = Configured(MindmapConfig.Default);
        var padding = config.Padding ?? Padding;
        var widest = config.MaxNodeWidth ?? MaxNodeWidth;
        var nodes = root.All().ToList();

        // Every node's words first, since what a node says is what says how big it is.
        var said = new Dictionary<Node, (IReadOnlyList<DiagramWords> Words, Size Size, DiagramShape Shape, Painted Paint)>();
        foreach (var node in nodes)
        {
            var shape = Shaped(node.Shape);
            var pad = Room(node.Shape, padding);
            var paint = Paint(config, node);
            var lines = Wrapped(node.Title, node.Hole, TextSize, paint.Words, Math.Max(20, widest - (pad * 2)));
            var words = new Size(lines.Max(line => line.Width), lines.Sum(line => line.Height));

            said[node] = (lines, DiagramShapes.Around(shape, words, pad), shape, paint);
        }

        var placed = DiagramTree.Lay(root, node => node.Children, node => said[node].Size);

        var room = new DiagramRoom();
        foreach (var (_, rect) in placed) room.Reach(rect);

        // The branches under the nodes, so a press near where they meet means the node. Each leaves the middle of its parent's
        // side facing the child — anywhere round a round root, which they fan out from — and sweeps into the middle of the
        // child's near side.
        build.Open(MindmapPiece.Branches, part: null, stops: Stops.None);
        foreach (var node in nodes)
            foreach (var child in node.Children)
            {
                var (from, to) = (room.At(placed[node]), room.At(placed[child]));
                var right = Middle(to).X >= Middle(from).X;
                var end = new Point(right ? to.Left : to.Right, Middle(to).Y);
                var start = node.Depth == 0 && said[node].Shape is DiagramShape.Circle or DiagramShape.Cloud or DiagramShape.Bang
                    ? DiagramShapes.Edge(said[node].Shape, from, end)
                    : new Point(right ? from.Right : from.Left, Middle(from).Y);
                var lead = (end.X - start.X) / 2;

                DiagramConnector.Draw(build, MindmapPiece.Branch, child.Part,
                                      DiagramConnector.Curving(start, new Point(start.X + lead, start.Y), new Point(end.X - lead, end.Y), end),
                                      new DiagramStroke(Branch(config, child), Math.Max(Thinnest, Thickest - (Thinner * (child.Depth - 1)))),
                                      end: DiagramHead.None, curved: true);
            }

        build.Close();

        build.Open(MindmapPiece.Nodes, part: null, stops: Stops.None);
        foreach (var node in nodes)
        {
            var (lines, _, shape, paint) = said[node];
            var bounds = room.At(placed[node]);
            var words = DiagramWords.Placed(lines, DiagramShapes.Inside(shape, bounds), MindmapPiece.Title);

            DiagramShapes.Draw(build, MindmapPiece.Node, node.Part, shape, bounds, paint.Fill, paint.Edge, words);
        }

        build.Close();

        return room.Size;
    }

    private static Point Middle(Rect rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    /// <summary>The kit's shape a mindmap's brackets ask for — a node with none a softly rounded box, and <c>(rounded)</c> a pill.</summary>
    private static DiagramShape Shaped(Brackets shape) => shape switch
    {
        Brackets.Square => DiagramShape.Rectangle,
        Brackets.Rounded => DiagramShape.Stadium,
        Brackets.Circle => DiagramShape.Circle,
        Brackets.Cloud => DiagramShape.Cloud,
        Brackets.Bang => DiagramShape.Bang,
        Brackets.Hexagon => DiagramShape.Hexagon,
        _ => DiagramShape.Rounded,
    };

    /// <summary>How much clear air a shape holds its words in — twice as much for a hexagon, whose points take the rest.</summary>
    private static double Room(Brackets shape, double padding) => shape switch
    {
        Brackets.Hexagon => padding * 2,
        Brackets.Cloud or Brackets.Bang => padding + 2,
        _ => padding,
    };

    /// <summary>How a node is painted: its fill, the edge round it, and the ink its words are set in.</summary>
    private readonly record struct Painted(Brush Fill, DiagramStroke? Edge, Brush Words);

    /// <summary>
    /// How a node is painted. Where the theme colours its branch (or the root), it is filled solid in that colour, as Mermaid
    /// fills it; otherwise it is washed in its branch's colour and edged in it, its words in the diagram's own ink.
    /// </summary>
    private Painted Paint(MindmapConfig config, Node node)
    {
        var root = node.Depth == 0;
        var written = Ink.Written(root ? config.RootFill : config.Scale.GetValueOrDefault(node.Branch + 1));
        var ink = Ink.Written(root ? config.RootTextFill : config.ScaleLabel.GetValueOrDefault(node.Branch + 1));
        if (written is not null) return new Painted(written, null, ink ?? Ink.Over(written));

        var colour = root ? Palette.Accent : Ink.Series(node.Branch);
        return new Painted(DiagramInk.Faded(colour, root ? RootWash : Wash), new DiagramStroke(colour, root ? 2 : 1.5), ink ?? Palette.Text);
    }

    /// <summary>The ink a branch is drawn in: its own colour, as its nodes take.</summary>
    private Brush Branch(MindmapConfig config, Node child) =>
        Ink.Written(config.Scale.GetValueOrDefault(child.Branch + 1)) ?? Ink.Series(child.Branch);
}
