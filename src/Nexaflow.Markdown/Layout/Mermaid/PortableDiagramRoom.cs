
namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The room a diagram takes: everything it means to draw, gathered as it works out where each piece goes, and the shift that
/// brings all of it inside the box it is drawn in.
///
/// <para>
/// A diagram works its layout out in whatever frame suits it — a chart from its plot's corner, a mindmap from its root, a
/// fishbone from its head — and words, arrows and shapes reach past that frame's edges. Rather than every diagram keeping its own
/// running bounds and remembering to move every coordinate by the same vector, it reaches with each piece and then draws through
/// <see cref="At(LayoutRect)"/> and <see cref="At(LayoutPoint)"/>. <see cref="Size"/> is what the block then takes.
/// </para>
/// </summary>
/// <param name="padding">Clear air to leave round everything drawn — a diagram's own <c>diagramPadding</c>.</param>
internal sealed class PortableDiagramRoom(double padding = 0)
{
    private LayoutRect _reached = LayoutRect.Empty;
    private readonly List<LayoutRect> _each = [];

    /// <summary>Everything reached so far, in the frame the diagram worked its layout out in.</summary>
    public LayoutRect Reached => _reached;

    /// <summary>What moves the frame's own coordinates into the box the diagram is drawn in.</summary>
    public LayoutVector Shift => new(padding - _reached.X, padding - _reached.Y);

    /// <summary>How big the block is: everything reached, and the clear air round it.</summary>
    public LayoutSize Size => _reached.IsEmpty ? new LayoutSize(padding * 2, padding * 2) : new LayoutSize(_reached.Width + (padding * 2), _reached.Height + (padding * 2));

    /// <summary>How many pieces have been reached, so a diagram can ask where what it drew since reaches.</summary>
    public int Count => _each.Count;

    /// <summary>Takes in what a piece reaches.</summary>
    public void Reach(LayoutRect what)
    {
        _each.Add(what);
        _reached = LayoutRect.Union(_reached, what);
    }

    /// <summary>Takes in the stretch between two points — an arrow, a bone, a line.</summary>
    public void Reach(LayoutPoint from, LayoutPoint to) => Reach(new LayoutRect(from, to));

    /// <summary>Takes in words set at a point.</summary>
    public void Reach(PortableDiagramWords words, LayoutPoint at) => Reach(new LayoutRect(at, new LayoutSize(words.Width, words.Height)));

    /// <summary>
    /// The room a diagram of cells joined by lines takes: the whole of what the layout laid out, every cell in it, and every line
    /// with the words written over the middle of it. A diagram reaches whatever else it draws — a lane's band, a note pinned
    /// beside something — into the room this hands back.
    /// </summary>
    public static PortableDiagramRoom Round(double padding, LayoutSize laid, IEnumerable<PortableDiagramCell> cells,
                                            IEnumerable<(PortableDiagramJoin Join, IReadOnlyList<PortableDiagramWords> Said)> joins)
    {
        var room = new PortableDiagramRoom(padding);

        room.Reach(new LayoutRect(default, laid));
        foreach (var cell in cells) room.Reach(cell.Bounds);

        foreach (var (join, said) in joins)
        {
            foreach (var at in join.Route) room.Reach(new LayoutRect(at, at));

            if (said.Count > 0) room.Reach(PortableDiagramConnector.Room(join.Route, said));
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
    public LayoutRect At(LayoutRect what) => LayoutRect.Offset(what, Shift);

    /// <summary>Where a point of the diagram's own frame is drawn.</summary>
    public LayoutPoint At(LayoutPoint what) => what + Shift;
}