using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// One lane of a layered layout laid out in lanes: a band that work runs through, with its own name at the near end of it. The cells
/// in it say so with <see cref="DiagramCell.Lane"/>; a lane is not laid out itself — it is where what is in it is laid out.
/// </summary>
/// <param name="least">How far across it runs at the least, which is the room its own name takes.</param>
/// <param name="pad">The clear air it keeps between its edge and what is in it.</param>
/// <param name="heading">The room kept at the near end of it, the way the layout runs, for its own name.</param>
internal sealed class DiagramLane(double least, double pad, double heading)
{
    public double Least { get; } = least;

    public double Pad { get; } = pad;

    public double Heading { get; } = heading;

    /// <summary>Where it ended up: the whole length of the layout, and its own stretch across it.</summary>
    public Rect Bounds { get; internal set; }

    /// <summary>The strip at the near end of <see cref="Bounds"/>, where its own name is written.</summary>
    public Rect Strip { get; internal set; }
}

/// <summary>
/// Laying a layered layout out in lanes, which is what a swimlane is. <see cref="DiagramLayers"/> turns the cycles, ranks, orders,
/// spreads and routes exactly as it does without lanes; this is everything the lanes change:
///
/// <list type="bullet">
/// <item>no two cells of one lane share a rank, so every step of a lane's own work is a rank of its own;</item>
/// <item>a link handed from one lane to another leaves what it reaches where that lane's own work has got to, rather than holding it a
/// rank further on — unless a handoff is asked to count like any other link;</item>
/// <item>every cell keeps to its lane's own stretch across the layout, the lanes set across in the order they are given, and each lane
/// keeps room at the near end of it for its own name.</item>
/// </list>
/// </summary>
/// <param name="lanes">The lanes, in the order they are set across the layout; a cell names one by its place in this, counted from one.</param>
/// <param name="across">Whether a link handed from one lane to another holds what it reaches a rank further on, as a link inside one lane does.</param>
internal sealed class DiagramLanes(IReadOnlyList<DiagramLane> lanes, bool across = false)
{
    /// <summary>The rank each lane's own work has reached, so its cells come one to a rank.</summary>
    private readonly int[] free = new int[lanes.Count + 1];

    /// <summary>Where each lane starts across the layout, and how far across it runs — worked out by <see cref="Held"/>.</summary>
    private double[] start = new double[lanes.Count + 1];

    private double[] room = new double[lanes.Count + 1];

    /// <summary>
    /// The room the lanes keep at the near end of them, which is where the first rank starts: the strip for each one's own name, and
    /// the same clear air past it that the lane keeps round what it holds — so the first thing in a lane does not stand against its name.
    /// </summary>
    public double Heading => lanes.Select(lane => lane.Heading + lane.Pad).DefaultIfEmpty(0).Max();

    /// <summary>
    /// How far a link holds apart what it joins: as far as it was written long inside one lane, and not at all where it is handed from
    /// one lane to another — a handoff goes across rather than on, which is what keeps the lanes in step with each other.
    /// </summary>
    public int Apart(DiagramCell from, DiagramCell to, int span) =>
        across || from.Lane == 0 || to.Lane == 0 || from.Lane == to.Lane ? span : 0;

    /// <summary>The rank a cell settles in: past whatever rank its own lane has reached, so a lane's cells come one to a rank.</summary>
    public int Ranked(DiagramCell cell, int wanted)
    {
        if (cell.Lane <= 0 || cell.Lane > lanes.Count) return wanted;

        var rank = Math.Max(wanted, this.free[cell.Lane]);
        this.free[cell.Lane] = rank + 1;

        return rank;
    }

    /// <summary>
    /// Holds every cell to its lane: a lane is as far across as the widest rank of what it holds, and what it holds in each rank is set
    /// in the middle of the lane — so a lane runs straight through the layout and nothing in it strays into the next one. Hands back
    /// how far across the lanes came to, all told.
    /// </summary>
    public double Held(IReadOnlyList<List<DiagramLayers.Place>> rows, double between)
    {
        var wide = new double[lanes.Count + 1];

        foreach (var row in rows)
            foreach (var lane in row.GroupBy(place => place.Lane))
                wide[lane.Key] = Math.Max(wide[lane.Key], Reach(lane).Room);

        this.room = new double[lanes.Count + 1];
        this.room[0] = wide[0];

        for (var lane = 1; lane <= lanes.Count; lane++)
            this.room[lane] = Math.Max(lanes[lane - 1].Least, wide[lane] + (lanes[lane - 1].Pad * 2));

        this.start = new double[lanes.Count + 1];
        var running = 0.0;

        for (var lane = 0; lane <= lanes.Count; lane++)
        {
            if (this.room[lane] <= 0) continue;

            this.start[lane] = running;
            running += this.room[lane] + between;
        }

        foreach (var row in rows)
            foreach (var lane in row.GroupBy(place => place.Lane))
            {
                var (from, taken) = Reach(lane);
                var shift = this.start[lane.Key] + ((this.room[lane.Key] - taken) / 2) - from;

                foreach (var place in lane) place.At += shift;
            }

        return Math.Max(0, running - between);

        static (double From, double Room) Reach(IEnumerable<DiagramLayers.Place> lane)
        {
            var least = lane.Min(place => place.At - (place.Size / 2));

            return (least, lane.Max(place => place.At + (place.Size / 2)) - least);
        }
    }

    /// <summary>
    /// Hands each lane what it turned out to be: the whole length of the layout, its own stretch across it, and the strip at the near
    /// end of that where its name is written — across the top of it where the layout runs down the page, and up the near side of it
    /// where the layout runs across.
    /// </summary>
    public void Settled(DiagramWay way, Size whole)
    {
        var down = way is DiagramWay.Down or DiagramWay.Up;

        for (var lane = 1; lane <= lanes.Count; lane++)
        {
            if (this.room[lane] <= 0) continue;

            var size = down ? new Size(this.room[lane], whole.Height) : new Size(whole.Width, this.room[lane]);
            var bounds = DiagramLayers.Placed(0, this.start[lane], size, way, whole);
            var deep = Math.Min(lanes[lane - 1].Heading, down ? bounds.Height : bounds.Width);

            lanes[lane - 1].Bounds = bounds;
            lanes[lane - 1].Strip = way switch
            {
                DiagramWay.Down => new Rect(bounds.X, bounds.Y, bounds.Width, deep),
                DiagramWay.Up => new Rect(bounds.X, bounds.Bottom - deep, bounds.Width, deep),
                DiagramWay.Right => new Rect(bounds.X, bounds.Y, deep, bounds.Height),
                _ => new Rect(bounds.Right - deep, bounds.Y, deep, bounds.Height),
            };
        }
    }
}
