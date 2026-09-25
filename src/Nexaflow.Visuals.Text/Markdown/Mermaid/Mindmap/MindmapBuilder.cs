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
internal sealed class MindmapBuilder : MermaidBuilder<MindmapTree>
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

    internal MindmapBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override MindmapTree Of(MermaidBlock block) => MindmapTree.Of(block);

    protected override Size Draw(MindmapTree map, LayoutBuilder build)
    {
        // A mindmap with no root is the source.
        if (map.Root is not { } root) return AsWritten(build);

        var padding = map.Config.Padding ?? Padding;
        var widest = map.Config.MaxNodeWidth ?? MaxNodeWidth;

        // Every node's words first, since what a node says is what says how big it is.
        var said = new Dictionary<MindmapNode, (IReadOnlyList<DiagramWords> Words, Size Size, DiagramShape Shape, Painted Paint)>();
        foreach (var node in map.Nodes)
        {
            var shape = Shaped(node.Shape);
            var pad = Room(node.Shape, padding);
            var paint = Paint(map, node);
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
        foreach (var node in map.Nodes)
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
                                      new DiagramStroke(Branch(map, child), Math.Max(Thinnest, Thickest - (Thinner * (child.Depth - 1)))),
                                      end: DiagramHead.None, curved: true);
            }

        build.Close();

        build.Open(MindmapPiece.Nodes, part: null, stops: Stops.None);
        foreach (var node in map.Nodes)
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
    private static DiagramShape Shaped(MindmapShape shape) => shape switch
    {
        MindmapShape.Square => DiagramShape.Rectangle,
        MindmapShape.Rounded => DiagramShape.Stadium,
        MindmapShape.Circle => DiagramShape.Circle,
        MindmapShape.Cloud => DiagramShape.Cloud,
        MindmapShape.Bang => DiagramShape.Bang,
        MindmapShape.Hexagon => DiagramShape.Hexagon,
        _ => DiagramShape.Rounded,
    };

    /// <summary>How much clear air a shape holds its words in — twice as much for a hexagon, whose points take the rest.</summary>
    private static double Room(MindmapShape shape, double padding) => shape switch
    {
        MindmapShape.Hexagon => padding * 2,
        MindmapShape.Cloud or MindmapShape.Bang => padding + 2,
        _ => padding,
    };

    /// <summary>How a node is painted: its fill, the edge round it, and the ink its words are set in.</summary>
    private readonly record struct Painted(Brush Fill, DiagramStroke? Edge, Brush Words);

    /// <summary>
    /// How a node is painted. Where the theme colours its branch (or the root), it is filled solid in that colour, as Mermaid
    /// fills it; otherwise it is washed in its branch's colour and edged in it, its words in the diagram's own ink.
    /// </summary>
    private Painted Paint(MindmapTree map, MindmapNode node)
    {
        var root = node.Depth == 0;
        var written = Ink.Written(root ? map.Config.RootFill : map.Config.Scale.GetValueOrDefault(node.Branch + 1));
        var ink = Ink.Written(root ? map.Config.RootTextFill : map.Config.ScaleLabel.GetValueOrDefault(node.Branch + 1));
        if (written is not null) return new Painted(written, null, ink ?? Ink.Over(written));

        var colour = root ? Palette.Accent : Ink.Series(node.Branch);
        return new Painted(DiagramInk.Faded(colour, root ? RootWash : Wash), new DiagramStroke(colour, root ? 2 : 1.5), ink ?? Palette.Text);
    }

    /// <summary>The ink a branch is drawn in: its own colour, as its nodes take.</summary>
    private Brush Branch(MindmapTree map, MindmapNode child) =>
        Ink.Written(map.Config.Scale.GetValueOrDefault(child.Branch + 1)) ?? Ink.Series(child.Branch);
}
