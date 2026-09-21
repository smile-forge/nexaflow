using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Quadrant;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>quadrantChart</c> block drawn on the shared layout tree: four quadrants standing for their captions' lines, a dot
/// standing for each point where it stands, and the captions, the axes' ends and the points' names typed into where drawn.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("quadrant-graph")]
public class QuadrantBuilderTests : MermaidBuilderContract
{
    private const string Campaigns =
        "quadrantChart\n  title Reach\n  x-axis Low Reach --> High Reach\n  y-axis Low Engagement --> High Engagement\n"
        + "  quadrant-1 Expand\n  quadrant-2 Promote\n  quadrant-3 Re-evaluate\n  quadrant-4 Improve\n  Campaign A: [0.3, 0.6]\n  Campaign B: [0.8, 0.2]";

    public override MermaidDiagram Diagram => MermaidDiagram.Quadrant;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("campaigns", Campaigns),
        ("styled points and classes",
            "quadrantChart\n  A: [0.9, 0.0] radius: 12\n  B:::hot: [0.8, 0.1] color: #ff3300\n  C: [0.7, 0.2] stroke-color: #10f0f0, stroke-width: 5px\n  classDef hot color: #109060, radius : 10"),
        ("captions and axes with no points", "quadrantChart\n  x-axis Urgent --> Not Urgent\n  y-axis Not Important --> \"Important\"\n  quadrant-1 Plan\n  quadrant-2 Do"),
        ("config and theme of its own",
            "---\nconfig:\n  quadrantChart:\n    chartWidth: 300\n    chartHeight: 200\n    pointRadius: 8\n    xAxisPosition: top\n    yAxisPosition: right\n"
            + "  themeVariables:\n    quadrant1Fill: \"#203040\"\n    quadrantPointFill: \"#ff0000\"\n---\nquadrantChart\n  x-axis Low --> High\n  y-axis Low --> High\n  A: [0.5, 0.5]"),
        ("still being written", "quadrantChart\n  x-axis \"\" --> \n  quadrant-2 \"\"\n  : [0.5, 0.5]\n  A: [0.1, \n  B: "),
        ("what nobody means to write", "quadrantChart\n  A: [1.5, -1]\n  B:::cold: [0.2, 0.2]"),
        ("nothing to draw", "quadrantChart"),
    ];

    private static Laid Build(string source, double room = 700) =>
        QuadrantBuilder.Build(MermaidBuilders.Read(source), new DiagramLaying(MarkdownPalette.Dark, room));

    [TestMethod]
    public void TheFirstQuadrantIsTopRight_AndTheRestGoAnticlockwise() => UiThread.Run(() =>
    {
        var cells = Pieces(Build(Campaigns), QuadrantPiece.Quadrant).Select(piece => Middle(piece.Bounds)).ToList();

        Assert.IsTrue(cells[0].X > cells[1].X && cells[0].Y < cells[3].Y, "first top right");
        Assert.IsTrue(cells[1].X < cells[0].X && cells[1].Y < cells[2].Y, "second top left");
        Assert.IsTrue(cells[2].X < cells[3].X && cells[2].Y > cells[1].Y, "third bottom left");
    });

    [TestMethod]
    public void AQuadrantStandsForItsCaptionsLine_AndItsCaptionIsTheWordsWritten() => UiThread.Run(() =>
    {
        var laid = Build(Campaigns);

        Assert.AreEqual("quadrant-1 Expand", Written(Campaigns, Pieces(laid, QuadrantPiece.Quadrant)[0].Part));
        Assert.IsTrue(Pieces(laid, QuadrantPiece.Caption).All(caption => caption.Words is { Maps: true }));
    });

    [TestMethod]
    public void APointStandsWhereItIsWritten_ItsNameUnderIt() => UiThread.Run(() =>
    {
        var laid = Build(Campaigns);
        var plot = Pieces(laid, QuadrantPiece.Quadrant).Select(piece => piece.Bounds).Aggregate(Rect.Union);
        var point = Pieces(laid, QuadrantPiece.Point)[1];
        var name = Pieces(laid, QuadrantPiece.Name)[1];

        Assert.AreEqual("Campaign B: [0.8, 0.2]", Written(Campaigns, point.Part));
        Assert.AreEqual(plot.Left + (0.8 * plot.Width), Middle(point.Bounds).X, 2);
        Assert.AreEqual(plot.Bottom - (0.2 * plot.Height), Middle(point.Bounds).Y, 2);
        Assert.IsTrue(name.Bounds.Top > point.Bounds.Bottom - 0.5, "the name under its dot");
    });

    [TestMethod]
    public void APointsStyleAndClassAreWhatItIsDrawnIn() => UiThread.Run(() =>
    {
        var laid = Build("quadrantChart\n  A:::hot: [0.5, 0.5] radius: 12\n  classDef hot color: #ff0000, radius: 4");
        var dot = Pieces(laid, QuadrantPiece.Point).Single();

        Assert.AreEqual(24, dot.Bounds.Width, 1, "its own radius over its class's");
        var mark = dot.Marks.ToArray().OfType<GeometryMark>().Single();
        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), ((SolidColorBrush)mark.Fill!).Color, "its class's colour");
    });

    [TestMethod]
    public void TheXAxissEndsGoUnderTheChartWhereThereArePoints_AndOverItWhereThereAreNone() => UiThread.Run(() =>
    {
        foreach (var (source, under) in new[] { (Campaigns, true), ("quadrantChart\n  x-axis Low --> High\n  quadrant-1 Plan", false) })
        {
            var laid = Build(source);
            var plot = Pieces(laid, QuadrantPiece.Quadrant).Select(piece => piece.Bounds).Aggregate(Rect.Union);
            var low = Pieces(laid, QuadrantPiece.AxisLabel).First(label => label.Words!.Glyphs.Text == "Low" || label.Words.Glyphs.Text == "Low Reach");

            Assert.AreEqual(under, low.Bounds.Top >= plot.Bottom - 0.5, source);
        }
    });

    [TestMethod]
    public void TheChartIsFittedIntoTheRoomItIsGiven() => UiThread.Run(() =>
        Assert.IsTrue(Build(Campaigns, room: 300).Size.Width <= 300));
}
