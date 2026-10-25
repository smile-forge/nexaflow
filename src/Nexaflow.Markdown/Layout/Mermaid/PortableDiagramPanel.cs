using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The panel a two-axis diagram is drawn inside, and what the whole of it takes up: the upright axis's numbers on the
/// left with its title turned up beyond them, the flat axis's underneath, and half the last number along the foot
/// hanging off the right.
/// </summary>
internal sealed record PortableDiagramPanel(LayoutRect Plot, double Wide, double Tall)
{
    public const double Smallest = 40;

    /// <summary>The room the axes themselves need. A diagram adds its own — a key, a title band, a caption.</summary>
    public static DiagramEdges Room(IReadOnlyList<PortableDiagramTick> upright, IReadOnlyList<PortableDiagramTick> flat,
                                    PortableDiagramWords? uprightTitle, PortableDiagramWords? flatTitle, double gap,
                                    double uprightTick = PortableDiagramAxis.TickLength,
                                    double flatTick = PortableDiagramAxis.TickLength) => new(
        Left: PortableDiagramAxis.Room(upright, upright: true, uprightTick)
              + (uprightTitle is null ? 0 : uprightTitle.Height + gap),
        Top: 0,
        Right: Math.Max(gap * 2, (flat.LastOrDefault()?.Words?.Width / 2) ?? 0),
        Bottom: PortableDiagramAxis.Room(flat, upright: false, flatTick) + (flatTitle is null ? 0 : gap + flatTitle.Height));

    /// <param name="aspect">A shape to hold the panel to. The room left over is not used: a matrix asked for square
    /// cells is not a matrix drawn oblong.</param>
    /// <param name="shrink">Give back the size of what was drawn — room left over stands a key away from its panel.</param>
    public static PortableDiagramPanel Round(double wide, double tall, DiagramEdges edges,
                                             double? aspect = null, bool shrink = false)
    {
        var panel = new LayoutSize(Math.Max(Smallest, wide - edges.Left - edges.Right),
                                   Math.Max(Smallest, tall - edges.Top - edges.Bottom));

        if (aspect is { } shape and > 0)
            panel = panel.Width / panel.Height > shape
                ? new LayoutSize(panel.Height * shape, panel.Height)
                : new LayoutSize(panel.Width, panel.Width / shape);

        return new PortableDiagramPanel(new LayoutRect(edges.Left, edges.Top, panel.Width, panel.Height),
                                        shrink ? edges.Left + panel.Width + edges.Right : wide,
                                        shrink ? edges.Top + panel.Height + edges.Bottom : tall);
    }

    /// <param name="below">How far under the panel the flat title sits — the room its numbers took.</param>
    public void Titles(PortableBuilder build, string kind, PortableDiagramWords? upright, PortableDiagramWords? flat, double below)
    {
        // Turned a quarter turn, it is anchored at its foot and reaches up by however wide its words are.
        upright?.Set(build, new LayoutPoint(0, this.Plot.Top + ((this.Plot.Height + upright.Width) / 2)), kind, degrees: -90);

        flat?.Set(build, new LayoutPoint(this.Plot.Left + Math.Max(0, (this.Plot.Width - flat.Width) / 2),
                                         this.Plot.Bottom + below), kind);
    }
}