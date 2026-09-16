using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Editing;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>A legend: down a column its rows are a table whose columns line up, and along a line they follow one another.</summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramLegendTests
{
    private static readonly string[] Columns = ["Label", "Value"];

    private static Piece Drawn(bool across, out DiagramLegend legend)
    {
        legend = new DiagramLegend(
        [
            new DiagramKey(new TestPart(0, 5), Brushes.Red, [DiagramShapesTests.Words("Dogs"), DiagramShapesTests.Words("386")]),
            new DiagramKey(new TestPart(6, 5), null, [DiagramShapesTests.Words("Hamsters and gerbils"), DiagramShapesTests.Words("15")]),
        ], Columns, across, Brushes.Gray);

        var build = new LayoutBuilder();
        build.Open("page");
        legend.Draw(build, new Point(10, 10));
        build.Close();

        return build.Seal().Root;
    }

    [TestMethod]
    public void DownAColumnTheRowsAreATable() => UiThread.Run(() =>
    {
        var root = Drawn(across: false, out var legend);
        var values = root.SelfAndDescendants().Where(piece => piece.Kind == "Value").ToList();
        var keys = root.SelfAndDescendants().Where(piece => piece.Kind == MermaidPiece.Key).ToList();

        Assert.AreEqual(values[0].Bounds.X, values[1].Bounds.X, 0.5, "each value starts where the other does, past the longest label");
        Assert.IsTrue(keys[1].Bounds.Top >= keys[0].Bounds.Bottom, "and the second row is under the first");
        Assert.IsTrue(legend.Size.Width >= values.Max(piece => piece.Bounds.Right) - 10 - 0.5, "as wide as it says it is");
    });

    [TestMethod]
    public void AlongALineTheRowsFollowOneAnother() => UiThread.Run(() =>
    {
        var root = Drawn(across: true, out _);
        var keys = root.SelfAndDescendants().Where(piece => piece.Kind == MermaidPiece.Key).ToList();

        Assert.AreEqual(keys[0].Bounds.Top, keys[1].Bounds.Top, 0.5, "on one line");
        Assert.IsTrue(keys[1].Bounds.Left >= keys[0].Bounds.Right + DiagramLegend.Apart - 0.5, "the second after the first");
    });

    [TestMethod]
    public void ARowStandsForWhatItExplains_AndOneWithNoColourYetHasTheSquareOneGoesIn() => UiThread.Run(() =>
    {
        var root = Drawn(across: false, out _);
        var keys = root.SelfAndDescendants().Where(piece => piece.Kind == MermaidPiece.Key).ToList();
        var swatches = root.SelfAndDescendants().Where(piece => piece.Kind == MermaidPiece.Swatch).ToList();

        Assert.AreEqual(0, keys[0].Part!.Start);
        Assert.IsInstanceOfType<RuleMark>(swatches[0].Marks[0], "a colour is a square of it");
        Assert.IsInstanceOfType<GeometryMark>(swatches[1].Marks[0], "and no colour yet is the square's outline");
    });
}
