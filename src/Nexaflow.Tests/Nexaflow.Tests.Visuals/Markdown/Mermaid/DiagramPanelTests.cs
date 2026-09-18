using System.Globalization;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// The panel every two-axis diagram is drawn inside: the room its numbers and titles take on each side, the panel left
/// over, and the titles set along it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramPanelTests
{
    private static DiagramTick[] Ticks(params string[] says) =>
        says.Select((word, index) => new DiagramTick(says.Length == 1 ? 0 : (double)index / (says.Length - 1),
                                                     DiagramShapesTests.Words(word)))
            .ToArray();

    [TestMethod]
    public void TheNumbersAndTheirTitlesAreWhatTheRoomAroundThePanelIsFor() => UiThread.Run(() =>
    {
        var upright = Ticks("0", "500", "1000");
        var flat = Ticks("a", "b", "c");
        var title = DiagramShapesTests.Words("Miles per gallon");

        var bare = DiagramPanel.Room(upright, flat, null, null, gap: 6);
        var titled = DiagramPanel.Room(upright, flat, title, null, gap: 6);

        Assert.AreEqual(DiagramAxis.Room(upright, upright: true), bare.Left,
                        "the upright axis's numbers are read on the left");
        Assert.AreEqual(DiagramAxis.Room(flat, upright: false), bare.Bottom,
                        "and the flat axis's underneath");
        Assert.AreEqual(bare.Left + title.Height + 6, titled.Left,
                        "a title turned to read up the axis takes its own height beyond them");
        Assert.AreEqual(0.0, bare.Top, "nothing of the axes' own stands over the panel");
        Assert.IsTrue(bare.Right >= flat[^1].Words!.Width / 2,
                      "half the last number along the foot would otherwise hang off the right");
    });

    [TestMethod]
    public void WhatADiagramStandsBeyondTheAxesIsAddedToTheirRoom()
    {
        var axes = new DiagramEdges(40, 0, 12, 24);
        var key = new DiagramEdges(0, 30, 80, 0);

        Assert.AreEqual(new DiagramEdges(40, 30, 92, 24), axes + key);
    }

    [TestMethod]
    public void ThePanelIsWhatIsLeftInsideTheRoomItIsGiven()
    {
        var round = DiagramPanel.Round(500, 300, new DiagramEdges(40, 10, 20, 30));

        Assert.AreEqual(new Rect(40, 10, 440, 260), round.Plot);
        Assert.AreEqual(500, round.Wide, "and the room offered is what it takes up");
        Assert.AreEqual(300, round.Tall);
    }

    [TestMethod]
    public void APanelIsNeverDrawnSmallerThanItsLeast()
    {
        var round = DiagramPanel.Round(50, 50, new DiagramEdges(40, 10, 20, 30));

        Assert.AreEqual(DiagramPanel.Smallest, round.Plot.Width);
        Assert.AreEqual(DiagramPanel.Smallest, round.Plot.Height);
    }

    [TestMethod]
    public void APanelGivenAShapeKeepsIt()
    {
        var wide = DiagramPanel.Round(500, 300, new DiagramEdges(0, 0, 0, 0), aspect: 1);
        var tall = DiagramPanel.Round(300, 500, new DiagramEdges(0, 0, 0, 0), aspect: 1);

        Assert.AreEqual(300, wide.Plot.Width, "a square panel in a wide room is as tall as the room allows");
        Assert.AreEqual(300, wide.Plot.Height);
        Assert.AreEqual(300, tall.Plot.Width, "and in a tall room as wide as it allows");
        Assert.AreEqual(300, tall.Plot.Height);
    }

    [TestMethod]
    public void WhatIsGivenBackIsWhatWasDrawnRatherThanTheRoomOffered()
    {
        var edges = new DiagramEdges(40, 10, 20, 30);
        var round = DiagramPanel.Round(500, 300, edges, aspect: 1, shrink: true);

        Assert.AreEqual(260, round.Plot.Height, "the shape holds");
        Assert.AreEqual(260, round.Plot.Width);
        Assert.AreEqual(320, round.Wide, "and the room left over is not counted, so nothing stands away from the panel");
        Assert.AreEqual(300, round.Tall);
    }

    [TestMethod]
    public void TheUprightTitleReadsUpTheAxisAndTheFlatOneSitsUnderItsNumbers() => UiThread.Run(() =>
    {
        var panel = new DiagramPanel(new Rect(40, 10, 400, 260), 500, 300);

        var build = new LayoutBuilder();
        build.Open("page");
        panel.Titles(build, "AxisTitle", DiagramShapesTests.Words("Miles per gallon"),
                     DiagramShapesTests.Words("Weight"), below: 20);
        build.Close();

        var set = build.Seal().Root.SelfAndDescendants().Where(piece => piece.Kind == "AxisTitle").ToList();
        Assert.AreEqual(2, set.Count);

        var upright = set[0];
        var flat = set[1];

        Assert.IsTrue(upright.Bounds.Height > upright.Bounds.Width, "turned a quarter turn, it is taller than it is wide");
        Assert.AreEqual(panel.Plot.Top + (panel.Plot.Height / 2), upright.Bounds.Top + (upright.Bounds.Height / 2), 0.5,
                        "and centred up the axis it names");

        Assert.AreEqual(panel.Plot.Left + (panel.Plot.Width / 2), flat.Bounds.Left + (flat.Bounds.Width / 2), 0.5,
                        "the flat title is centred under the panel");
        Assert.IsTrue(flat.Bounds.Top >= panel.Plot.Bottom + 20, "beyond the room its numbers took");
    });

    [TestMethod]
    public void ATitleWiderThanThePanelStillStartsInsideIt() => UiThread.Run(() =>
    {
        var panel = new DiagramPanel(new Rect(40, 10, 30, 260), 500, 300);

        var build = new LayoutBuilder();
        build.Open("page");
        panel.Titles(build, "AxisTitle", null, DiagramShapesTests.Words("A title far wider than its panel"), below: 20);
        build.Close();

        var flat = build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "AxisTitle");

        Assert.AreEqual(panel.Plot.Left, flat.Bounds.Left, 0.5, "rather than being centred off the left of the page");
    });
}
