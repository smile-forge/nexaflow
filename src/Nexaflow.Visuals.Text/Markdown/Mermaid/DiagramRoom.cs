using System;
using System.Collections.Generic;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The room a diagram takes: everything it means to draw, gathered as it works out where each piece goes, and the shift that
/// brings all of it inside the box it is drawn in.
///
/// <para>
/// A diagram works its layout out in whatever frame suits it — a chart from its plot's corner, a mindmap from its root, a
/// fishbone from its head — and words, arrows and shapes reach past that frame's edges. Rather than every diagram keeping its own
/// running bounds and remembering to move every coordinate by the same vector, it reaches with each piece and then draws through
/// <see cref="At(Rect)"/> and <see cref="At(Point)"/>. <see cref="Size"/> is what the block then takes.
/// </para>
/// </summary>
/// <param name="padding">Clear air to leave round everything drawn — a diagram's own <c>diagramPadding</c>.</param>
internal sealed class DiagramRoom(double padding = 0)
{
    private Rect _reached = Rect.Empty;
    private readonly List<Rect> _each = [];

    /// <summary>Everything reached so far, in the frame the diagram worked its layout out in.</summary>
    public Rect Reached => _reached;

    /// <summary>What moves the frame's own coordinates into the box the diagram is drawn in.</summary>
    public Vector Shift => new(padding - _reached.X, padding - _reached.Y);

    /// <summary>How big the block is: everything reached, and the clear air round it.</summary>
    public Size Size => _reached.IsEmpty ? new Size(padding * 2, padding * 2) : new Size(_reached.Width + (padding * 2), _reached.Height + (padding * 2));

    /// <summary>How many pieces have been reached, so a diagram can ask where what it drew since reaches.</summary>
    public int Count => _each.Count;

    /// <summary>Takes in what a piece reaches.</summary>
    public void Reach(Rect what)
    {
        _each.Add(what);
        _reached.Union(what);
    }

    /// <summary>Takes in the stretch between two points — an arrow, a bone, a line.</summary>
    public void Reach(Point from, Point to) => Reach(new Rect(from, to));

    /// <summary>Takes in words set at a point.</summary>
    public void Reach(DiagramWords words, Point at) => Reach(new Rect(at, new Size(words.Width, words.Height)));

    /// <summary>
    /// The room a diagram of cells joined by lines takes: the whole of what the layout laid out, every cell in it, and every line
    /// with the words written over the middle of it. A diagram reaches whatever else it draws — a lane's band, a note pinned
    /// beside something — into the room this hands back.
    /// </summary>
    public static DiagramRoom Round(double padding, Size laid, IEnumerable<DiagramCell> cells,
                                    IEnumerable<(DiagramJoin Join, IReadOnlyList<DiagramWords> Said)> joins)
    {
        var room = new DiagramRoom(padding);

        room.Reach(new Rect(default, laid));
        foreach (var cell in cells) room.Reach(cell.Bounds);

        foreach (var (join, said) in joins)
        {
            foreach (var at in join.Route) room.Reach(new Rect(at, at));

            if (said.Count > 0) room.Reach(DiagramConnector.Room(join.Route, said));
        }

        return room;
    }

    /// <summary>How far left what was reached since <paramref name="from"/> pieces goes.</summary>
    public double Left(int from)
    {
        var left = 0d;
        for (var at = from; at < _each.Count; at++) left = Math.Min(left, _each[at].Left);

        return left;
    }

    /// <summary>Where a rectangle of the diagram's own frame is drawn.</summary>
    public Rect At(Rect what) => Rect.Offset(what, Shift);

    /// <summary>Where a point of the diagram's own frame is drawn.</summary>
    public Point At(Point what) => what + Shift;
}
