using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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
/// brackets ask for — a square, a rounded square, a circle, a cloud, a bang, a hexagon, or no border at all with an underline —
/// and a branch off the root is drawn in its own colour, thinning as it goes further out, as Mermaid colours and thickens them.
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
internal sealed class MindmapBuilder : MermaidBuilder<MindmapTree>
{
    private const double TextSize = 13;
    private const double MaxNodeWidth = 200;
    private const double Padding = 10;

    /// <summary>How thick a branch is drawn at the root, and how much thinner each level further out is.</summary>
    private const double Thickest = 11;
    private const double Thinner = 3;

    private MindmapBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new MindmapBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <inheritdoc/>
    protected override MindmapTree Of(MermaidBlock block) => MindmapTree.Of(block);

    protected override Size Draw(MindmapTree map, LayoutBuilder build)
    {
        // A mindmap with no root is the source.
        if (map.Root is not { } root) return AsWritten(build);

        var padding = map.Config.Padding ?? Padding;
        var widest = map.Config.MaxNodeWidth ?? MaxNodeWidth;

        // Every node's words first, since what a node says is what says how big it is.
        var said = new Dictionary<MindmapNode, (IReadOnlyList<DiagramWords> Words, Size Size, DiagramShape Shape)>();
        foreach (var node in map.Nodes)
        {
            var shape = Shaped(node.Shape);
            var pad = Room(node.Shape, padding);
            var lines = Wrapped(node.Title, node.Hole, TextSize, Words(map, node), Math.Max(20, widest - (pad * 2)));
            var words = new Size(lines.Max(line => line.Width), lines.Sum(line => line.Height));

            said[node] = (lines, DiagramShapes.Around(shape, words, pad), shape);
        }

        var placed = DiagramTree.Lay(root, node => node.Children, node => said[node].Size);

        var room = new DiagramRoom();
        foreach (var (_, rect) in placed) room.Reach(rect);

        // The branches under the nodes, so a press near where they meet means the node.
        build.Open(MindmapPiece.Branches, part: null, stops: Stops.None);
        foreach (var node in map.Nodes)
            foreach (var child in node.Children)
            {
                var (from, to) = (room.At(placed[node]), room.At(placed[child]));
                var start = DiagramShapes.Edge(said[node].Shape, from, Middle(to));
                var end = DiagramShapes.Edge(said[child].Shape, to, Middle(from));
                var bend = (start.X + end.X) / 2;

                DiagramConnector.Draw(build, MindmapPiece.Branch, child.Part,
                                      [start, new Point(bend, start.Y), new Point(bend, end.Y), end],
                                      new DiagramStroke(Branch(map, child), Math.Max(2, Thickest - (Thinner * (child.Depth - 1)))),
                                      end: DiagramHead.None, curved: true);
            }

        build.Close();

        build.Open(MindmapPiece.Nodes, part: null, stops: Stops.None);
        foreach (var node in map.Nodes)
        {
            var (lines, _, shape) = said[node];
            var bounds = room.At(placed[node]);
            var fill = Fill(map, node);
            var words = DiagramWords.Stack(lines, DiagramShapes.Inside(shape, bounds))
                .Select(line => (line.Words, line.At, MindmapPiece.Title))
                .ToList();

            // A node with no border of its own is underlined instead, as Mermaid draws one.
            var under = node.Shape != MindmapShape.Plain ? null : Line(bounds);
            DiagramShapes.Draw(build, MindmapPiece.Node, node.Part, shape, bounds, fill, null, words, under);

            if (under is null) continue;
            build.Open(MermaidPiece.Line, node.Part, stops: Stops.None);
            build.Draw(new GeometryMark(under, null, Ink.Written(map.Config.ScaleInverse.GetValueOrDefault(node.Branch + 1)) ?? Ink.Over(fill), 3));
            build.Close();
        }

        build.Close();

        return room.Size;
    }

    /// <summary>The line under a node with no border of its own.</summary>
    private static Geometry Line(Rect bounds)
    {
        var line = new LineGeometry(new Point(bounds.Left, bounds.Bottom), new Point(bounds.Right, bounds.Bottom));
        line.Freeze();
        return line;
    }

    private static Point Middle(Rect rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    /// <summary>The kit's shape a mindmap's brackets ask for.</summary>
    private static DiagramShape Shaped(MindmapShape shape) => shape switch
    {
        MindmapShape.Rounded => DiagramShape.Rounded,
        MindmapShape.Circle => DiagramShape.Circle,
        MindmapShape.Cloud => DiagramShape.Cloud,
        MindmapShape.Bang => DiagramShape.Bang,
        MindmapShape.Hexagon => DiagramShape.Hexagon,
        _ => DiagramShape.Rectangle,
    };

    /// <summary>How much clear air a shape holds its words in — twice as much for the shapes Mermaid pads twice over.</summary>
    private static double Room(MindmapShape shape, double padding) => shape switch
    {
        MindmapShape.Rounded or MindmapShape.Hexagon => padding * 2,
        MindmapShape.Cloud or MindmapShape.Bang => padding + 2,
        _ => padding,
    };

    /// <summary>A node's fill: the root's own, or its branch's colour.</summary>
    private Brush Fill(MindmapTree map, MindmapNode node) =>
        node.Depth == 0
            ? Ink.Written(map.Config.RootFill) ?? Palette.Accent
            : Ink.Written(map.Config.Scale.GetValueOrDefault(node.Branch + 1)) ?? Ink.Series(node.Branch);

    /// <summary>The ink a node's words are set in: what the theme writes for its branch, or whatever reads over its fill.</summary>
    private Brush Words(MindmapTree map, MindmapNode node) =>
        node.Depth == 0
            ? Ink.Written(map.Config.RootTextFill) ?? Ink.Over(Fill(map, node))
            : Ink.Written(map.Config.ScaleLabel.GetValueOrDefault(node.Branch + 1)) ?? Ink.Over(Fill(map, node));

    /// <summary>The ink a branch is drawn in: its own colour, as its nodes take.</summary>
    private Brush Branch(MindmapTree map, MindmapNode child) =>
        Ink.Written(map.Config.Scale.GetValueOrDefault(child.Branch + 1)) ?? Ink.Series(child.Branch);
}
