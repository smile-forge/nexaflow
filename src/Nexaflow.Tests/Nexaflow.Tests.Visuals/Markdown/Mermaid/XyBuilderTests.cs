using System;
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
using Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// An <c>xychart</c> block drawn on the shared layout tree: a bar standing for each value, a line for each line series, the
/// categories under their ticks typed into, the numbers up the side worked out, and a legend for the series with names.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("xy-chart")]
public class XyBuilderTests : MermaidBuilderContract
{
    private const string Revenue =
        "xychart\n  title \"Sales Revenue\"\n  x-axis \"Month\" [jan, feb, mar]\n  y-axis \"Revenue\" 0 --> 100\n  bar \"Sold\" [20, 50, 80]\n  line \"Target\" [40, 60, 70]";

    public override MermaidDiagram Diagram => MermaidDiagram.XyChart;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("revenue", Revenue),
        ("the documented chart",
            "xychart\n    title \"Sales Revenue\"\n    x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]\n    y-axis \"Revenue (in $)\" 4000 --> 11000\n"
            + "    bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]\n    line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]"),
        ("horizontal, two bar series, data labels outside",
            "---\nconfig:\n  xyChart:\n    showDataLabel: true\n    showDataLabelOutsideBar: true\n---\nxychart horizontal\n  x-axis [a, b, c]\n  bar [1, -2, 3]\n  bar [2, 2, 2]"),
        ("a numbered x-axis and labelled points", "xychart\n  x-axis 0 --> 10\n  line [540 \"PaLM\", 65 \"LLaMA\", 7 \"Mistral\"]"),
        ("the minimal chart", "xychart\n    line [+1.3, .6, 2.4, -.34]"),
        ("config and theme of its own",
            "---\nconfig:\n  xyChart:\n    width: 300\n    height: 200\n    showTitle: false\n    xAxis:\n      showTick: false\n      labelFontSize: 16\n    yAxis:\n      showAxisLine: false\n"
            + "  themeVariables:\n    xyChart:\n      backgroundColor: \"#202020\"\n      plotColorPalette: \"#ff0000, #00ff00\"\n---\nxychart\n  title Hidden\n  x-axis [a, b]\n  bar [1, 2]\n  line [2, 1]"),
        ("still being written", "xychart\n  x-axis \"\" [a, \n  bar \"\" [1, ]\n  line [1 \"\", 2"),
        ("axes and nothing to plot", "xychart\n  x-axis [a, b]\n  y-axis 0 --> 5"),
        ("nothing to draw", "xychart"),
    ];

    private static Laid Build(string source, double room = 700, bool writing = false) =>
        XyBuilder.Build(EditState.For(source), MarkdownPalette.Dark, 1.0, room, writing);

    private static List<Piece> Pieces(Laid laid, string kind) => [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    private static string Written(string source, ISourcePart? part) => part is null ? "" : source.Substring(part.Start, part.Length);

    [TestMethod]
    public void EveryValueOfABarSeriesIsABarStandingForTheValueWritten() => UiThread.Run(() =>
        CollectionAssert.AreEqual(new[] { "20", "50", "80" }, Pieces(Build(Revenue), XyPiece.Bar).Select(bar => Written(Revenue, bar.Part)).ToArray()));

    [TestMethod]
    public void ABarRisesAsFarAsItsValueIsUpTheRange() => UiThread.Run(() =>
    {
        var bars = Pieces(Build(Revenue), XyPiece.Bar).Select(bar => bar.Bounds).ToList();

        Assert.AreEqual(bars[0].Bottom, bars[2].Bottom, 0.5, "every bar stands on the same foot");
        Assert.AreEqual(bars[2].Height / 4, bars[0].Height, 1, "and 20 is a quarter as tall as 80");
        Assert.IsTrue(bars[0].Left < bars[1].Left && bars[1].Left < bars[2].Left, "in the order of the categories");
    });

    [TestMethod]
    public void AHorizontalChartsBarsReachRight_DownTheCategoriesInOrder() => UiThread.Run(() =>
    {
        var bars = Pieces(Build("xychart horizontal\n  x-axis [a, b]\n  y-axis 0 --> 10\n  bar [2, 8]"), XyPiece.Bar).Select(bar => bar.Bounds).ToList();

        Assert.AreEqual(bars[0].Left, bars[1].Left, 0.5, "from the same foot");
        Assert.IsTrue(bars[1].Width > bars[0].Width * 3, "as far right as their values");
        Assert.IsTrue(bars[0].Top < bars[1].Top, "the first category at the top");
    });

    [TestMethod]
    public void SeveralBarSeriesStandSideBySideInEachSlot() => UiThread.Run(() =>
    {
        var bars = Pieces(Build("xychart\n  x-axis [a]\n  bar [1]\n  bar [1]"), XyPiece.Bar).Select(bar => bar.Bounds).ToList();
        Assert.IsTrue(bars[0].Right <= bars[1].Left + 0.5, "the second beside the first, not over it");
    });

    [TestMethod]
    public void ALineStandsForItsSeries_ThroughItsValues() => UiThread.Run(() =>
    {
        var trace = Pieces(Build(Revenue), XyPiece.Trace).Single();

        Assert.AreEqual("line \"Target\" [40, 60, 70]", Written(Revenue, trace.Part));
        Assert.IsNotNull(trace.Region, "pressed near its line, not anywhere in its box");
    });

    [TestMethod]
    public void TheCategoriesAreTheWordsWritten_TypedInto_AndTheNumbersAreWorkedOut() => UiThread.Run(() =>
    {
        var ticks = Pieces(Build(Revenue), XyPiece.Tick);
        var categories = ticks.Where(tick => tick.Words is { Maps: true }).Select(tick => Written(Revenue, tick.Part)).ToArray();

        CollectionAssert.AreEqual(new[] { "jan", "feb", "mar" }, categories);
        Assert.IsTrue(ticks.Any(tick => tick.Words is { Maps: false } && tick.Words.Glyphs.Text == "100"), "and the range's end is a number on the axis");
    });

    [TestMethod]
    public void EachAxisTitleIsTheTitleWritten() => UiThread.Run(() =>
        CollectionAssert.AreEquivalent(new[] { "Month", "Revenue" }, Pieces(Build(Revenue), XyPiece.AxisTitle).Select(title => Written(Revenue, title.Part)).ToArray()));

    [TestMethod]
    public void OnlySeriesWithNamesHaveLegendRows() => UiThread.Run(() =>
    {
        var laid = Build("xychart\n  x-axis [a, b]\n  bar \"Sold\" [1, 2]\n  line [2, 1]");
        Assert.AreEqual(1, Pieces(laid, MermaidPiece.Key).Count);
        Assert.AreEqual("Sold", Pieces(laid, XyPiece.Name).Single().Words!.Glyphs.Text);
    });

    [TestMethod]
    public void APointsLabelSitsOverIt() => UiThread.Run(() =>
    {
        const string source = "xychart\n  x-axis [a, b]\n  line [1 \"low\", 9 \"high\"]";
        var labels = Pieces(Build(source), XyPiece.Label);

        CollectionAssert.AreEqual(new[] { "low", "high" }, labels.Select(label => Written(source, label.Part)).ToArray());
        Assert.IsTrue(labels[1].Bounds.Bottom < labels[0].Bounds.Bottom, "the higher value's label the higher");
    });

    [TestMethod]
    public void DataLabelsAreWrittenOnlyWhereTheFrontMatterAsks() => UiThread.Run(() =>
    {
        const string body = "xychart\n  x-axis [a, b]\n  bar [1, 2]";

        Assert.AreEqual(0, Pieces(Build(body), XyPiece.Value).Count);
        Assert.AreEqual(2, Pieces(Build("---\nconfig:\n  xyChart:\n    showDataLabel: true\n---\n" + body), XyPiece.Value).Count);
    });

    [TestMethod]
    public void ThePalettesColoursAreWhatTheSeriesAreDrawnIn() => UiThread.Run(() =>
    {
        var bar = Pieces(Build("---\nconfig:\n  themeVariables:\n    xyChart:\n      plotColorPalette: \"#ff0000\"\n---\nxychart\n  bar [1]"), XyPiece.Bar).Single();
        Assert.IsTrue(bar.Marks.ToArray().OfType<GeometryMark>().Any(mark => mark.Fill is SolidColorBrush { Color: var colour } && colour == Color.FromRgb(0xFF, 0, 0)));
    });

    [TestMethod]
    public void ShowTitleFalseSetsNoTitle() => UiThread.Run(() =>
        Assert.AreEqual(0, Pieces(Build("---\nconfig:\n  xyChart:\n    showTitle: false\n---\n" + Revenue), MermaidPiece.Title).Count));

    [TestMethod]
    public void TheChartIsFittedIntoTheRoomItIsGiven() => UiThread.Run(() =>
    {
        var laid = Build(Revenue, room: 320);
        Assert.IsTrue(laid.Size.Width <= 320, $"took {laid.Size.Width}");
    });

    [TestMethod]
    public void AChartOfNothingIsShownAsItWasWritten() => UiThread.Run(() =>
        Assert.IsTrue(Pieces(Build("xychart"), LayoutText.SourceKind).Any()));
}
