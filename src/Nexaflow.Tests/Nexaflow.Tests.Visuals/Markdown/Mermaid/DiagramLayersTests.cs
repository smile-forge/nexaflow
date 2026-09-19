using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The layered layout every diagram whose nodes are joined by lines is drawn with: the ranks the links put the cells in, the way
/// round it all runs, the boxes laid out in their own space first, and where every line ends up running.
/// </summary>
[TestClass]
[CoversNode("mermaid-diagram-kit")]
public class DiagramLayersTests
{
    private static readonly Size Box = new(40, 20);

    [TestMethod]
    public void ALinkPutsWhatItReachesInTheRankBeyondWhatItLeaves()
    {
        var (a, b, c) = (Cell(), Cell(), Cell());
        var size = DiagramLayers.Lay([a, b, c], [Join(a, b), Join(b, c)], DiagramWay.Down, 10, 30);

        Assert.AreEqual(0, a.Bounds.Top, 0.01);
        Assert.AreEqual(50, b.Bounds.Top, 0.01, "one rank on: the first rank's depth and the air after it");
        Assert.AreEqual(100, c.Bounds.Top, 0.01);
        Assert.AreEqual(120, size.Height, 0.01, "three ranks and the air between them");
    }

    [TestMethod]
    public void NothingReachedByALinkStartsAtTheFirstRank()
    {
        var (a, b) = (Cell(), Cell());
        DiagramLayers.Lay([a, b], [], DiagramWay.Down, 10, 30);

        Assert.AreEqual(a.Bounds.Top, b.Bounds.Top, 0.01, "with no link between them they share a rank");
        Assert.IsTrue(b.Bounds.Left >= a.Bounds.Right + 10 || a.Bounds.Left >= b.Bounds.Right + 10, "and stand apart");
    }

    [TestMethod]
    public void ALinkWrittenLongerHoldsWhatItJoinsFurtherApart()
    {
        var (a, b, c) = (Cell(), Cell(), Cell());
        DiagramLayers.Lay([a, b, c], [Join(a, b), Join(a, c, span: 3)], DiagramWay.Down, 10, 30);

        Assert.AreEqual(50, b.Bounds.Top, 0.01, "one rank on");
        Assert.AreEqual(130, c.Bounds.Top, 0.01, "and three — the rank it bends in holding nothing, so taking only the air");
    }

    [TestMethod]
    public void ALinkReachingOverNoRankAtAllHoldsWhatItJoinsSideBySide()
    {
        var (a, b, c) = (Cell(), Cell(), Cell());
        DiagramLayers.Lay([a, b, c], [Join(a, b), Join(a, c, span: 0)], DiagramWay.Down, 10, 30);

        Assert.AreEqual(a.Bounds.Top, c.Bounds.Top, 0.01, "nought ranks on is the rank it leaves — a note beside what it is about");
        Assert.IsTrue(c.Bounds.Left >= a.Bounds.Right + 10 || a.Bounds.Left >= c.Bounds.Right + 10, "and it stands beside it");
        Assert.IsTrue(b.Bounds.Top > a.Bounds.Bottom, "while a link of its own still reaches the next rank");
    }

    [TestMethod]
    public void ALinkReachingOverMoreThanOneRankBends()
    {
        var (a, b, c) = (Cell(), Cell(), Cell());
        var over = Join(a, c, span: 2);
        DiagramLayers.Lay([a, b, c], [Join(a, b), Join(b, c), over], DiagramWay.Down, 10, 30);

        Assert.IsTrue(over.Route.Count > 2, "it goes round what is in the way rather than through it");
        Assert.IsTrue(over.Route.Skip(1).SkipLast(1).Any(at => at.Y > a.Bounds.Bottom && at.Y < c.Bounds.Top),
                      "and bends in the rank between them");
    }

    [TestMethod]
    public void EachWayRoundPutsTheNextRankWhereItSays()
    {
        foreach (var (way, further) in new (DiagramWay Way, string Further)[]
                 {
                     (DiagramWay.Down, "below"),
                     (DiagramWay.Up, "above"),
                     (DiagramWay.Right, "right"),
                     (DiagramWay.Left, "left"),
                 })
        {
            var (a, b) = (Cell(), Cell());
            DiagramLayers.Lay([a, b], [Join(a, b)], way, 10, 30);

            var moved = further switch
            {
                "below" => b.Bounds.Top > a.Bounds.Bottom,
                "above" => b.Bounds.Bottom < a.Bounds.Top,
                "right" => b.Bounds.Left > a.Bounds.Right,
                _ => b.Bounds.Right < a.Bounds.Left,
            };

            Assert.IsTrue(moved, $"{way}: b is {further} a — {a.Bounds} then {b.Bounds}");
        }
    }

    [TestMethod]
    public void ARingOfCellsStillLaysOut()
    {
        var (a, b, c) = (Cell(), Cell(), Cell());
        var back = Join(c, a);
        DiagramLayers.Lay([a, b, c], [Join(a, b), Join(b, c), back], DiagramWay.Down, 10, 30);

        Assert.AreEqual(0, a.Bounds.Top, 0.01, "the link back to the start is turned round to be ranked");
        Assert.AreEqual(100, c.Bounds.Top, 0.01);
        Assert.IsTrue(back.Route.Count >= 2, "and it is still drawn the way it was written");
    }

    [TestMethod]
    public void ABoxIsTheSizeOfWhatItHolds_AndHoldsItInside()
    {
        var box = new DiagramCell(default) { Pad = 10, Heading = 16 };
        var (a, b) = (Cell(box), Cell(box));

        var size = DiagramLayers.Lay([box, a, b], [Join(a, b)], DiagramWay.Down, 10, 30);

        Assert.AreEqual(60, box.Size.Width, 0.01, "as wide as the widest of them, and the air either side");
        Assert.AreEqual(106, box.Size.Height, 0.01, "two ranks, the air between them, the air round them and the heading");
        Assert.AreEqual(box.Size, size, "the box is all there is at the outermost level");

        foreach (var cell in new[] { a, b })
        {
            Assert.IsTrue(cell.Bounds.Left >= box.Bounds.Left && cell.Bounds.Right <= box.Bounds.Right, $"{cell.Bounds} is inside {box.Bounds}");
            Assert.IsTrue(cell.Bounds.Top >= box.Bounds.Top + 16 && cell.Bounds.Bottom <= box.Bounds.Bottom,
                          "and below the room kept for what is written at the top");
        }
    }

    [TestMethod]
    public void ABoxRunsWhatItHoldsItsOwnWay()
    {
        var box = new DiagramCell(default) { Pad = 4, Way = DiagramWay.Down };
        var (a, b) = (Cell(box), Cell(box));
        var c = Cell();

        DiagramLayers.Lay([box, a, b, c], [Join(a, b), Join(c, box)], DiagramWay.Right, 10, 30);

        Assert.IsTrue(b.Bounds.Top > a.Bounds.Bottom, $"inside the box it runs down: {a.Bounds} then {b.Bounds}");
        Assert.IsTrue(box.Bounds.Left > c.Bounds.Right, $"and the level holding it still runs right: {c.Bounds} then {box.Bounds}");
    }

    [TestMethod]
    public void ALinkBetweenCellsInDifferentBoxesIsArrangedBetweenTheBoxes()
    {
        var one = new DiagramCell(default) { Pad = 4 };
        var two = new DiagramCell(default) { Pad = 4 };
        var (a, b) = (Cell(one), Cell(two));
        var across = Join(a, b);

        DiagramLayers.Lay([one, two, a, b], [across], DiagramWay.Down, 10, 30);

        Assert.IsTrue(two.Bounds.Top > one.Bounds.Bottom, $"the boxes are a rank apart: {one.Bounds} then {two.Bounds}");
        Assert.AreEqual(2, across.Route.Count, "and the line runs straight between the cells it really joins");
        Assert.IsTrue(across.Route[0].Y > one.Bounds.Top && across.Route[^1].Y < two.Bounds.Bottom);
    }

    [TestMethod]
    public void ALinkBackToTheCellItLeavesRunsBesideIt()
    {
        var a = Cell();
        var itself = Join(a, a);

        DiagramLayers.Lay([a], [itself], DiagramWay.Down, 10, 30);

        Assert.AreEqual(4, itself.Route.Count);
        Assert.IsTrue(itself.Route.All(at => at.X >= a.Bounds.Right - 0.01), "out beside it and back again");
    }

    [TestMethod]
    public void NothingToLayOutTakesNoRoom() =>
        Assert.AreEqual(default, DiagramLayers.Lay([], [], DiagramWay.Down, 10, 30));

    private static DiagramCell Cell(DiagramCell? inside = null) => new(Box) { Inside = inside };

    private static DiagramJoin Join(DiagramCell from, DiagramCell to, int span = 1) => new(from, to, span);
}
