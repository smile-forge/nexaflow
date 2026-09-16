using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// An axis and the numbers along it: round numbers covering a range, each written with the decimals its step needs, and
/// ticks with their words beside them along the line.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramAxisTests
{
    [TestMethod]
    public void ARangeIsMarkedWithRoundNumbers()
    {
        CollectionAssert.AreEqual(new[] { 0.0, 20, 40, 60, 80, 100 }, DiagramScale.Ticks(0, 100).ToArray());
        CollectionAssert.AreEqual(new[] { 0.0, 0.5, 1, 1.5, 2, 2.5 }, DiagramScale.Ticks(0, 2.5).ToArray());
        Assert.AreEqual((0.0, 100.0), DiagramScale.Nice(3, 97));
        Assert.AreEqual(0.25, DiagramScale.At(25, 0, 100));
    }

    [TestMethod]
    public void ATickIsWrittenWithTheDecimalsItsStepNeeds()
    {
        Assert.AreEqual(20.ToString(CultureInfo.CurrentCulture), DiagramScale.Label(20, 20));
        Assert.AreEqual(0.5.ToString("F1", CultureInfo.CurrentCulture), DiagramScale.Label(0.5, 0.5));
        Assert.AreEqual(0.25.ToString("F2", CultureInfo.CurrentCulture), DiagramScale.Label(0.25, 0.05));
    }

    [TestMethod]
    public void EachTicksWordsStandBesideItsTick() => UiThread.Run(() =>
    {
        var ticks = new[] { 0.0, 0.5, 1 }.Select(at => new DiagramTick(at, DiagramShapesTests.Words(at.ToString(CultureInfo.InvariantCulture)))).ToList();

        var build = new LayoutBuilder();
        build.Open("page");
        DiagramAxis.Draw(build, "Axis", null, new Point(20, 100), new Point(220, 100), ticks, new DiagramStroke(Brushes.Black), "Tick");
        DiagramAxis.Draw(build, "Side", null, new Point(20, 100), new Point(20, 0), ticks, new DiagramStroke(Brushes.Black), "Value", after: false);
        build.Close();

        var root = build.Seal().Root;
        var under = root.SelfAndDescendants().Where(piece => piece.Kind == "Tick").ToList();
        var beside = root.SelfAndDescendants().Where(piece => piece.Kind == "Value").ToList();

        CollectionAssert.AreEqual(new[] { 20.0, 120, 220 }, under.Select(piece => System.Math.Round(piece.Bounds.X + (piece.Bounds.Width / 2))).ToArray(),
                                  "centred under their ticks");
        Assert.IsTrue(under.All(piece => piece.Bounds.Top >= 100 + DiagramAxis.TickLength), "and under the line");
        Assert.IsTrue(beside.All(piece => piece.Bounds.Right <= 20 - DiagramAxis.TickLength), "an upright axis's words are to its left, where asked");
        Assert.IsTrue(DiagramAxis.Room(ticks, upright: true) >= beside.Max(piece => piece.Bounds.Width) + DiagramAxis.TickLength,
                      "and the room it says they take is at least what they take");
    });
}
