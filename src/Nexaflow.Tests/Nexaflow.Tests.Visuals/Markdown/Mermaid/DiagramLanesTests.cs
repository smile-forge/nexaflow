using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Laying the layered layout out in lanes, which is what a swimlane is: a lane's cells one to a rank, a handoff between two lanes
/// going across rather than on, every cell held to its own lane's band, and each band running the whole length of the layout with
/// room at the near end of it for its own name.
/// </summary>
[TestClass]
[CoversNode("mermaid-diagram-kit")]
public class DiagramLanesTests
{
    private static readonly Size Box = new(40, 20);

    [TestMethod]
    public void ALanesCellsComeOneToARank()
    {
        var (one, two) = (Cell(1), Cell(1));
        DiagramLayers.Lay([one, two], [], DiagramWay.Down, 10, 30, Lanes(Lane(), Lane()));

        Assert.AreNotEqual(one.Bounds.Top, two.Bounds.Top,
                           "two cells of one lane never share a rank: every step of a lane's own work is a rank of its own");
        Assert.AreEqual(50, two.Bounds.Top - one.Bounds.Top, 0.01, "one rank on: the rank's depth and the air after it");
    }

    [TestMethod]
    public void AHandoffBetweenLanesGoesAcrossRatherThanOn()
    {
        var (one, two) = (Cell(1), Cell(2));
        DiagramLayers.Lay([one, two], [Join(one, two)], DiagramWay.Down, 10, 30, Lanes(Lane(), Lane()));

        Assert.AreEqual(one.Bounds.Top, two.Bounds.Top, 0.01,
                        "the lane it is handed to carries on where its own work has got to, which is the first rank");
        Assert.IsTrue(two.Bounds.Left > one.Bounds.Right, "and it is the next lane across");
    }

    [TestMethod]
    public void AHandoffRunsStraightAcrossFromOneLaneToTheNext()
    {
        var (one, two) = (Cell(1), Cell(2));
        var join = Join(one, two);
        DiagramLayers.Lay([one, two], [join], DiagramWay.Down, 10, 30, Lanes(Lane(), Lane()));

        CollectionAssert.AreEqual(new[] { Middle(one.Bounds), Middle(two.Bounds) }, join.Route.ToArray(),
                                  "side by side in the same rank, there is no rank between them to bend in — so it runs straight across, "
                                + "which is what lets each end be brought in to the shape it meets");

        static Point Middle(Rect bounds) => new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    [TestMethod]
    public void AHandoffHoldsWhatItReachesOnWhereItIsAskedTo()
    {
        var (one, two) = (Cell(1), Cell(2));
        DiagramLayers.Lay([one, two], [Join(one, two)], DiagramWay.Down, 10, 30, Lanes([Lane(), Lane()], across: true));

        Assert.AreEqual(50, two.Bounds.Top - one.Bounds.Top, 0.01, "asked to count like any other link, it moves it a rank on");
    }

    [TestMethod]
    public void EveryCellKeepsToItsOwnLanesBand()
    {
        var (first, second) = (Lane(), Lane());
        var cells = new[] { Cell(1), Cell(1), Cell(2) };
        DiagramLayers.Lay(cells, [], DiagramWay.Down, 10, 30, Lanes(first, second));

        foreach (var cell in cells.Take(2))
            Assert.IsTrue(first.Bounds.Left <= cell.Bounds.Left && cell.Bounds.Right <= first.Bounds.Right,
                          $"{cell.Bounds} is inside {first.Bounds}");

        Assert.IsTrue(second.Bounds.Left <= cells[2].Bounds.Left && cells[2].Bounds.Right <= second.Bounds.Right,
                      $"{cells[2].Bounds} is inside {second.Bounds}");
        Assert.IsTrue(second.Bounds.Left >= first.Bounds.Right, "and the bands do not overlap");
    }

    [TestMethod]
    public void ALaneRunsTheWholeLengthOfTheLayout()
    {
        var lane = Lane();
        var whole = DiagramLayers.Lay([Cell(1), Cell(1)], [], DiagramWay.Down, 10, 30, Lanes(lane));

        Assert.AreEqual(0, lane.Bounds.Top, 0.01);
        Assert.AreEqual(whole.Height, lane.Bounds.Height, 0.01, "a lane is the whole length of the layout, not the size of its work");
    }

    [TestMethod]
    public void RoomIsKeptAtTheNearEndOfEveryBandForItsOwnName()
    {
        var lane = Lane(heading: 18);
        var one = Cell(1);
        DiagramLayers.Lay([one], [], DiagramWay.Down, 10, 30, Lanes(lane));

        Assert.AreEqual(18, one.Bounds.Top, 0.01, "the first rank starts past the room the lanes keep for their names");
        Assert.AreEqual(new Rect(lane.Bounds.X, 0, lane.Bounds.Width, 18), lane.Strip,
                        "and the strip is that room, across the near end of the band");
    }

    [TestMethod]
    public void TheStripIsAtTheNearEndOfTheBandWhicheverWayItRuns()
    {
        foreach (var way in new[] { DiagramWay.Down, DiagramWay.Up, DiagramWay.Right, DiagramWay.Left })
        {
            var lane = Lane(heading: 18);
            DiagramLayers.Lay([Cell(1), Cell(1)], [], way, 10, 30, Lanes(lane));

            var near = way switch
            {
                DiagramWay.Down => lane.Strip.Top == lane.Bounds.Top,
                DiagramWay.Up => lane.Strip.Bottom == lane.Bounds.Bottom,
                DiagramWay.Right => lane.Strip.Left == lane.Bounds.Left,
                _ => lane.Strip.Right == lane.Bounds.Right,
            };

            Assert.IsTrue(near, $"{way}: the strip {lane.Strip} is at the near end of {lane.Bounds}");
            Assert.AreEqual(18, way is DiagramWay.Down or DiagramWay.Up ? lane.Strip.Height : lane.Strip.Width, 0.01,
                            $"{way}: and it is as deep as the room kept for the name");
        }
    }

    [TestMethod]
    public void ALaneWithNothingInItIsStillABand()
    {
        var (empty, held) = (Lane(least: 30), Lane());
        var whole = DiagramLayers.Lay([Cell(2)], [], DiagramWay.Down, 10, 30, Lanes(empty, held));

        Assert.AreEqual(30, empty.Bounds.Width, 0.01, "a lane nobody has written anything in yet is as wide as its own name");
        Assert.IsTrue(whole.Width >= empty.Bounds.Width + held.Bounds.Width, "and the layout is wide enough to hold it");
    }

    [TestMethod]
    public void ALaneOfNothingAtAllIsStillDrawn()
    {
        var lane = Lane(least: 30, heading: 18);
        var whole = DiagramLayers.Lay([], [], DiagramWay.Down, 10, 30, Lanes(lane));

        Assert.AreEqual(new Rect(0, 0, 30, 18), lane.Bounds, "a swimlane of empty lanes is still lanes to draw");
        Assert.AreEqual(new Size(30, 18), whole);
    }

    [TestMethod]
    public void ALaneIsAtLeastAsWideAsItsOwnName()
    {
        var lane = Lane(least: 200);
        var one = Cell(1);
        DiagramLayers.Lay([one], [], DiagramWay.Down, 10, 30, Lanes(lane));

        Assert.AreEqual(200, lane.Bounds.Width, 0.01, "a lane is as wide as its own name where its work is narrower");
        Assert.AreEqual(lane.Bounds.Left + (lane.Bounds.Width / 2), one.Bounds.Left + (one.Bounds.Width / 2), 0.01,
                        "and what it holds sits in the middle of the band");
    }

    [TestMethod]
    public void WhatIsInNoLaneKeepsOutOfTheLanesBands()
    {
        var lane = Lane();
        var (loose, held) = (Cell(0), Cell(1));
        DiagramLayers.Lay([loose, held], [], DiagramWay.Down, 10, 30, Lanes(lane));

        Assert.IsFalse(lane.Bounds.IntersectsWith(loose.Bounds), "a cell in no lane is in a band of its own before them all");
        Assert.IsTrue(lane.Bounds.Contains(held.Bounds));
    }

    [TestMethod]
    public void ACellInsideASubgraphInsideALaneIsStillHeldToTheBand()
    {
        var lane = Lane();
        var box = new DiagramCell(new Size(0, 0)) { Lane = 1, Pad = 6 };
        var inner = new DiagramCell(Box) { Inside = box };
        var beside = Cell(2);

        DiagramLayers.Lay([box, inner, beside], [], DiagramWay.Down, 10, 30, Lanes(lane, Lane()));

        Assert.IsTrue(lane.Bounds.Contains(box.Bounds), $"the box {box.Bounds} is inside the band {lane.Bounds}");
        Assert.IsTrue(box.Bounds.Contains(inner.Bounds), "and what the box holds is inside the box");
    }

    // ── What it works with ──────────────────────────────────────────────────

    private static DiagramCell Cell(int lane) => new(Box) { Lane = lane };

    private static DiagramLane Lane(double least = 0, double pad = 0, double heading = 0) => new(least, pad, heading);

    private static DiagramLanes Lanes(params DiagramLane[] lanes) => new(lanes);

    private static DiagramLanes Lanes(IReadOnlyList<DiagramLane> lanes, bool across) => new(lanes, across);

    private static DiagramJoin Join(DiagramCell from, DiagramCell to, int span = 1) => new(from, to, span);
}
