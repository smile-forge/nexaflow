using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>A node, or a box holding other cells. A box is laid out in its own space first, then sized to what it holds and its cells moved into place.</summary>
internal sealed class PortableDiagramCell(LayoutSize size)
{
    /// <summary>How much room it takes. For a box, the most of what it was given and what it turned out to hold.</summary>
    public LayoutSize Size { get; set; } = size;

    /// <summary>The box it is in, or null for one at the outermost level.</summary>
    public PortableDiagramCell? Inside { get; init; }

    /// <summary>The way what is inside it runs, where it is a box laid out its own way rather than the chart's.</summary>
    public DiagramWay? Way { get; init; }

    /// <summary>The clear air a box keeps between its edge and what is inside it.</summary>
    public double Pad { get; init; }

    /// <summary>The room a box keeps at the top of it for what is written there.</summary>
    public double Heading { get; init; }

    /// <summary>The lane band it's confined to, numbered from one; 0 for no lane.</summary>
    public int Lane { get; init; }

    /// <summary>
    /// What it is drawn as, where the layout has to know: a line meeting a shape touches its outline, and the outline of
    /// a diamond is nowhere near the box round it. A cell that says nothing is met as its box.
    /// </summary>
    public DiagramShape Shape { get; init; } = DiagramShape.Rectangle;

    /// <summary>Where it ended up — in the space of the box it is in until that box is placed, and absolute after.</summary>
    public LayoutRect Bounds { get; set; }
}

/// <summary>One line a layered layout draws between two cells, and the way it ended up running.</summary>
/// <param name="span">
/// How many ranks it reaches over at the least, which is how far apart it holds what it joins. Nought holds them in the same rank,
/// side by side — a note written beside the thing it is about rather than after it — and nothing need be drawn for it.
/// </param>
internal sealed class PortableDiagramJoin(PortableDiagramCell from, PortableDiagramCell to, int span = 1)
{
    public PortableDiagramCell From { get; } = from;

    public PortableDiagramCell To { get; } = to;

    public int Span { get; } = Math.Max(0, span);

    /// <summary>Where it runs, from the middle of what it leaves to the middle of what it reaches, bending on the way.</summary>
    public IReadOnlyList<LayoutPoint> Route { get; set; } = [];

    /// <summary>
    /// How much room what is written on this line needs, or nothing where a diagram writes nothing on its lines. The
    /// layout holds the two ranks a line runs between far enough apart for it.
    /// </summary>
    public LayoutSize Said { get; init; }

    /// <summary>Where it bends, in the space of the box it was laid out in — <see cref="PortableDiagramLayers"/>' own bookkeeping.</summary>
    internal IReadOnlyList<LayoutPoint> Bends { get; set; } = [];

    /// <summary>The box it was laid out in, which is the space <see cref="Bends"/> are in.</summary>
    internal PortableDiagramCell? Level { get; set; }

    /// <summary>Which way the level it was laid out in runs, which is the way it leaves and arrives.</summary>
    internal DiagramWay Towards { get; set; }
}