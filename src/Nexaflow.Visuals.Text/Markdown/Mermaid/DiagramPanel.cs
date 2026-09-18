using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The room a panel's furniture takes on each of its four sides.</summary>
internal readonly record struct DiagramEdges(double Left, double Top, double Right, double Bottom)
{
    public static DiagramEdges operator +(DiagramEdges room, DiagramEdges beyond) =>
        new(room.Left + beyond.Left, room.Top + beyond.Top, room.Right + beyond.Right, room.Bottom + beyond.Bottom);
}

/// <summary>
/// The panel a two-axis diagram is drawn inside, and what the whole of it takes up: the upright axis's numbers on the
/// left with its title turned up beyond them, the flat axis's underneath, and half the last number along the foot
/// hanging off the right.
/// </summary>
internal sealed record DiagramPanel(Rect Plot, double Wide, double Tall)
{
    public const double Smallest = 40;

    /// <summary>The room the axes themselves need. A diagram adds its own — a key, a title band, a caption.</summary>
    public static DiagramEdges Room(IReadOnlyList<DiagramTick> upright, IReadOnlyList<DiagramTick> flat,
                                    DiagramWords? uprightTitle, DiagramWords? flatTitle, double gap,
                                    double uprightTick = DiagramAxis.TickLength,
                                    double flatTick = DiagramAxis.TickLength) => new(
        Left: DiagramAxis.Room(upright, upright: true, uprightTick)
              + (uprightTitle is null ? 0 : uprightTitle.Height + gap),
        Top: 0,
        Right: Math.Max(gap * 2, (flat.LastOrDefault()?.Words?.Width / 2) ?? 0),
        Bottom: DiagramAxis.Room(flat, upright: false, flatTick) + (flatTitle is null ? 0 : gap + flatTitle.Height));

    /// <param name="aspect">A shape to hold the panel to. The room left over is not used: a matrix asked for square
    /// cells is not a matrix drawn oblong.</param>
    /// <param name="shrink">Give back the size of what was drawn — room left over stands a key away from its panel.</param>
    public static DiagramPanel Round(double wide, double tall, DiagramEdges edges,
                                     double? aspect = null, bool shrink = false)
    {
        var panel = new Size(Math.Max(Smallest, wide - edges.Left - edges.Right),
                             Math.Max(Smallest, tall - edges.Top - edges.Bottom));

        if (aspect is { } shape and > 0)
            panel = panel.Width / panel.Height > shape
                ? new Size(panel.Height * shape, panel.Height)
                : new Size(panel.Width, panel.Width / shape);

        return new DiagramPanel(new Rect(edges.Left, edges.Top, panel.Width, panel.Height),
                                shrink ? edges.Left + panel.Width + edges.Right : wide,
                                shrink ? edges.Top + panel.Height + edges.Bottom : tall);
    }

    /// <param name="below">How far under the panel the flat title sits — the room its numbers took.</param>
    public void Titles(LayoutBuilder build, string kind, DiagramWords? upright, DiagramWords? flat, double below)
    {
        // Turned a quarter turn, it is anchored at its foot and reaches up by however wide its words are.
        upright?.Set(build, new Point(0, this.Plot.Top + ((this.Plot.Height + upright.Width) / 2)), kind, degrees: -90);

        flat?.Set(build, new Point(this.Plot.Left + Math.Max(0, (this.Plot.Width - flat.Width) / 2),
                                   this.Plot.Bottom + below), kind);
    }
}
