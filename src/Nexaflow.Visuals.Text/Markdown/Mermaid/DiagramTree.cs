using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A tree laid out tidily: every node beside its parent rather than under it, each subtree given the room it needs and no
/// more, and a parent set against the middle of its children — how a mindmap is drawn.
///
/// <para>
/// The root stands in the middle and its children take turns either side of it, the first to its left, as Mermaid's tidy-tree
/// layout shares them out; each side then grows away from the root, level by level.
/// </para>
/// </summary>
internal static class DiagramTree
{
    /// <summary>The clear air between two nodes beside each other, and between one level and the next.</summary>
    public const double Between = 20;
    public const double Across = 40;

    /// <summary>How far the first level stands from the root.</summary>
    public const double Round = 30;

    /// <summary>
    /// Where every node of the tree under <paramref name="root"/> goes, the root's middle at nought: each node's size from
    /// <paramref name="size"/>, its children from <paramref name="children"/>.
    /// </summary>
    /// <param name="sided">Whether the root's children take turns either side of it, rather than all growing right.</param>
    public static Dictionary<T, Rect> Lay<T>(T root, Func<T, IReadOnlyList<T>> children, Func<T, Size> size, bool sided = true)
        where T : notnull
    {
        var deep = new Dictionary<T, double>();
        var placed = new Dictionary<T, Rect>();

        var middle = size(root);
        placed[root] = new Rect(-middle.Width / 2, -middle.Height / 2, middle.Width, middle.Height);

        var kids = children(root);
        foreach (var side in sided ? (int[])[-1, 1] : [1])
        {
            var mine = kids.Where((_, index) => !sided || (index % 2 == 0) == (side < 0)).ToList();
            if (mine.Count == 0) continue;

            var span = mine.Sum(child => Room(child, children, size, deep)) + (Between * (mine.Count - 1));
            var top = -span / 2;
            var from = side > 0 ? middle.Width / 2 + Round : -(middle.Width / 2) - Round;

            foreach (var child in mine)
            {
                Place(child, children, size, deep, placed, from, top, side);
                top += Room(child, children, size, deep) + Between;
            }
        }

        return placed;
    }

    /// <summary>Puts a node with its near side at <paramref name="from"/>, its subtree filling the room from <paramref name="top"/>.</summary>
    private static void Place<T>(T node, Func<T, IReadOnlyList<T>> children, Func<T, Size> size, Dictionary<T, double> deep,
                                 Dictionary<T, Rect> placed, double from, double top, int side)
        where T : notnull
    {
        var own = size(node);
        var room = Room(node, children, size, deep);
        var centre = top + (room / 2);

        placed[node] = new Rect(side > 0 ? from : from - own.Width, centre - (own.Height / 2), own.Width, own.Height);

        var kids = children(node);
        if (kids.Count == 0) return;

        var span = kids.Sum(child => Room(child, children, size, deep)) + (Between * (kids.Count - 1));
        var next = centre - (span / 2);
        var beyond = side > 0 ? from + own.Width + Across : from - own.Width - Across;

        foreach (var child in kids)
        {
            Place(child, children, size, deep, placed, beyond, next, side);
            next += Room(child, children, size, deep) + Between;
        }
    }

    /// <summary>How much room a node's subtree needs across the page: its own size, or all its children together.</summary>
    private static double Room<T>(T node, Func<T, IReadOnlyList<T>> children, Func<T, Size> size, Dictionary<T, double> deep)
        where T : notnull
    {
        if (deep.TryGetValue(node, out var known)) return known;

        var kids = children(node);
        var room = kids.Count == 0
            ? size(node).Height
            : Math.Max(size(node).Height, kids.Sum(child => Room(child, children, size, deep)) + (Between * (kids.Count - 1)));

        deep[node] = room;
        return room;
    }
}
