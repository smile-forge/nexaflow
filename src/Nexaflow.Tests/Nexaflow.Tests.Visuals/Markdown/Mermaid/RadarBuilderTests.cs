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
using Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>radar-beta</c> block drawn on the shared layout tree: a spoke per axis standing for the axis it was written as, the
/// label at its end typed into, a curve standing in its own shape reaching as far as its values say, and a legend saying which
/// curve is which.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("radar")]
public class RadarBuilderTests : MermaidBuilderContract
{
    private const string Restaurants =
        "radar-beta\n  title Restaurant Comparison\n  axis food[\"Food Quality\"], service[\"Service\"], price[\"Price\"]\n  axis ambiance[\"Ambiance\"]\n"
        + "  curve a[\"Restaurant A\"]{4, 3, 2, 4}\n  curve b[\"Restaurant B\"]{3, 4, 3, 3}\n  max 5";

    public override MermaidDiagram Diagram => MermaidDiagram.Radar;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("restaurants", Restaurants),
        ("grades, titled in the front matter, on a polygon",
            "---\ntitle: \"Grades\"\n---\nradar-beta\n  axis m[\"Math\"], s[\"Science\"], e[\"English\"]\n  axis h[\"History\"], g[\"Geography\"], a[\"Art\"]\n"
            + "  curve a[\"Alice\"]{85, 90, 80, 70, 75, 90}\n  curve b[\"Bob\"]{70, 75, 85, 80, 90, 85}\n  graticule polygon\n  max 100\n  min 0"),
        ("the documented config and theme",
            "---\nconfig:\n  radar:\n    axisScaleFactor: 0.25\n    curveTension: 0.1\n  theme: base\n  themeVariables:\n    cScale0: \"#FF0000\"\n    cScale1: \"#00FF00\"\n"
            + "    cScale2: \"#0000FF\"\n    radar:\n      curveOpacity: 0\n---\nradar-beta\n  axis A, B, C, D, E\n  curve c1{1,2,3,4,5}\n  curve c2{5,4,3,2,1}\n  curve c3{3,3,3,3,3}"),
        ("keyed and positional curves, several to a line",
            "radar-beta\n  axis axis1, axis2, axis3\n  curve id1[\"Label1\"]{1, 2, 3}\n  curve id2[\"Label2\"]{4, 5, 6}, id3{7, 8, 9}\n  curve id4{ axis3: 30, axis1: 20, axis2: 10 }"),
        ("still being written", "radar-beta\n  axis a[\"\"], b, \n  curve c{1, }\n  curve "),
        ("values that do not fit", "radar-beta\n  axis a, b, c\n  curve x{1}\n  curve y{ z: 2 }\n  curve w{lots, -1, 2"),
        ("two axes, and no legend", "radar-beta\n  axis a, b\n  curve x{1, 2}\n  graticule polygon\n  showLegend false"),
        ("sizes and margins of its own",
            "---\nconfig:\n  radar:\n    width: 300\n    height: 200\n    marginLeft: 40\n    axisLabelFactor: 1.3\n  themeVariables:\n    fontSize: 22\n"
            + "    radar:\n      axisLabelFontSize: 16px\n      legendBoxSize: 20\n      legendFontSize: 16\n---\nradar-beta\n  title Sized\n  axis a, b, c\n  curve x{1, 2, 3}"),
        ("nothing to draw", "radar-beta\n  title Empty"),
    ];

    private static Laid Build(string source, double room = 700, bool writing = false) =>
        RadarBuilder.Build(MermaidBuilders.Read(source, holes: writing), new DiagramLaying(MarkdownPalette.Dark, 1.0, room, writing));

    [TestMethod]
    public void EveryAxisIsASpokeStandingForTheAxisItWasWrittenAs() => UiThread.Run(() =>
        CollectionAssert.AreEqual(
            new[] { "food[\"Food Quality\"]", "service[\"Service\"]", "price[\"Price\"]", "ambiance[\"Ambiance\"]" },
            Pieces(Build(Restaurants), RadarPiece.Spoke).Select(spoke => Written(Restaurants, spoke.Part)).ToArray()));

    [TestMethod]
    public void TheFirstAxisPointsStraightUp_AndTheRestFollowClockwise() => UiThread.Run(() =>
    {
        var laid = Build(Restaurants);
        var middle = Middle(Pieces(laid, RadarPiece.Ring).Last().Bounds);
        var spokes = Pieces(laid, RadarPiece.Spoke).Select(spoke => Middle(spoke.Bounds)).ToList();

        Assert.IsTrue(spokes[0].Y < middle.Y - 20, "the first up");
        Assert.IsTrue(spokes[1].X > middle.X + 20, "the second right");
        Assert.IsTrue(spokes[2].Y > middle.Y + 20, "the third down");
        Assert.IsTrue(spokes[3].X < middle.X - 20, "the fourth left");
    });

    [TestMethod]
    public void TheLabelAtTheEndOfASpokeIsTheLabelWritten_AndIsTypedInto() => UiThread.Run(() =>
    {
        var labels = Pieces(Build(Restaurants), RadarPiece.Label);

        CollectionAssert.AreEqual(new[] { "Food Quality", "Service", "Price", "Ambiance" }, labels.Select(label => Written(Restaurants, label.Part)).ToArray());
        Assert.IsTrue(labels.All(label => label.Words is { Maps: true }), "so the caret can stand between its letters");
    });

    [TestMethod]
    public void ALabelSitsPastTheEndOfItsSpoke_OutsideTheRim() => UiThread.Run(() =>
    {
        var laid = Build(Restaurants);
        var rim = Pieces(laid, RadarPiece.Ring).Last().Bounds;
        var labels = Pieces(laid, RadarPiece.Label).Select(label => label.Bounds).ToList();

        Assert.IsTrue(labels[0].Bottom <= rim.Top + 1, "over the rim at the top");
        Assert.IsTrue(labels[1].Left >= rim.Right - 1, "right of it on the right");
        Assert.IsTrue(labels[2].Top >= rim.Bottom - 1, "under it at the bottom");
        Assert.IsTrue(labels[3].Right <= rim.Left + 1, "left of it on the left");
    });

    [TestMethod]
    public void ACurveReachesAlongEachAxisAsFarAsItsValueIsFromMinToMax() => UiThread.Run(() =>
    {
        var laid = Build("radar-beta\n  axis a, b, c, d\n  curve full{4, 4, 4, 4}\n  curve half{2, 2, 2, 2}\n  max 4\n  graticule polygon");
        var rim = Pieces(laid, RadarPiece.Ring).Last().Bounds;
        var curves = Pieces(laid, RadarPiece.Curve);

        Assert.AreEqual(rim.Width, curves[0].Bounds.Width, 3, "a value of max reaches the rim");
        Assert.AreEqual(rim.Width / 2, curves[1].Bounds.Width, 3, "and one half way there, half way out");
    });

    [TestMethod]
    public void ACurveStandsInItsOwnShape_AndItsLegendRowForTheSameCurve() => UiThread.Run(() =>
    {
        var laid = Build(Restaurants);
        var curves = Pieces(laid, RadarPiece.Curve);

        Assert.IsTrue(curves.All(curve => curve.Region is not null), "a curve stands in its shape, not its box");
        Assert.AreEqual("a[\"Restaurant A\"]{4, 3, 2, 4}", Written(Restaurants, curves[0].Part));
        CollectionAssert.AreEqual(curves.Select(curve => curve.Part!.Start).ToArray(),
                                  Pieces(laid, MermaidPiece.Key).Select(key => key.Part!.Start).ToArray());
    });

    [TestMethod]
    public void APressInsideACurveMeansThatCurve_TheOneOverTheOtherWhereTheyOverlap() => UiThread.Run(() =>
    {
        const string source = "radar-beta\n  axis a, b, c, d\n  curve big{4, 4, 4, 4}\n  curve small{1, 1, 1, 1}\n  max 4";
        var laid = Build(source);
        var rim = Pieces(laid, RadarPiece.Ring).Last().Bounds;
        var middle = Middle(rim);

        // Along the diagonal between two spokes, so no spoke is pressed.
        Point Along(double share) => middle + new Vector(share * rim.Width / 2 * Math.Sqrt(0.5), -share * rim.Width / 2 * Math.Sqrt(0.5));
        int Pressed(Point point) => laid.PieceAt(point).Selectable().Sits().Start;

        Assert.AreEqual(source.IndexOf("small{", StringComparison.Ordinal), Pressed(Along(0.1)), "inside both, the curve written last, drawn over the other");
        Assert.AreEqual(source.IndexOf("big{", StringComparison.Ordinal), Pressed(Along(0.5)), "inside only the big one, the big one");
    });

    [TestMethod]
    public void APolygonGraticuleHasItsCornersOnTheAxes() => UiThread.Run(() =>
    {
        const string circle = "radar-beta\n  axis a, b, c, d\n  curve x{1, 2, 3, 4}";

        foreach (var (source, round) in new[] { (circle, true), (circle + "\n  graticule polygon", false) })
        {
            var rim = Pieces(Build(source), RadarPiece.Ring).Last();
            var diagonal = Middle(rim.Bounds) + new Vector(rim.Bounds.Width * 0.3, rim.Bounds.Width * 0.3);

            Assert.AreEqual(round, rim.Region!.FillContains(diagonal - rim.Anchor), round ? "a circle reaches out between the axes" : "a polygon does not");
        }
    });

    [TestMethod]
    public void TheFrontMattersColoursAreWhatTheCurvesAreDrawnIn() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  themeVariables:\n    cScale0: \"#ff0000\"\n---\nradar-beta\n  axis a, b, c\n  curve x{1, 2, 3}\n  curve y{3, 2, 1}");
        var strokes = Pieces(laid, RadarPiece.Curve).Select(Stroke).ToList();

        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), strokes[0]);
        Assert.AreNotEqual(Color.FromRgb(0xFF, 0, 0), strokes[1], "the second takes the theme's, since nothing was written for it");
    });

    /// <summary>The colour a piece's outline is drawn in.</summary>
    private static Color Stroke(Piece piece)
    {
        foreach (var mark in piece.Marks)
            if (mark is GeometryMark { Stroke: SolidColorBrush stroke }) return stroke.Color;

        throw new AssertFailedException($"a {piece.Kind} with no outline");
    }

    [TestMethod]
    public void ShowLegendFalseDrawsNoLegend() => UiThread.Run(() =>
    {
        var laid = Build("radar-beta\n  axis a, b, c\n  curve x{1, 2, 3}\n  showLegend false");

        Assert.AreEqual(0, Pieces(laid, MermaidPiece.Legend).Count);
        Assert.AreEqual(1, Pieces(laid, RadarPiece.Curve).Count);
    });

    [TestMethod]
    public void TheLegendSitsRightOfTheChart_AndUnderItWhereTheRoomIsTooNarrowForBoth() => UiThread.Run(() =>
    {
        var wide = Build(Restaurants, room: 900);
        var narrow = Build(Restaurants, room: 280);

        Assert.IsTrue(Pieces(wide, MermaidPiece.Legend).Single().Bounds.Left > Pieces(wide, RadarPiece.Ring).Last().Bounds.Right,
                      "beside the chart, where there is room for it");
        Assert.IsTrue(Pieces(narrow, MermaidPiece.Legend).Single().Bounds.Top > Pieces(narrow, RadarPiece.Ring).Last().Bounds.Bottom,
                      "and under it where there is not");
        Assert.IsTrue(narrow.Size.Width <= 280, $"the chart is fitted into the room it was given, and took {narrow.Size.Width}");
    });

    [TestMethod]
    public void WhileItIsWrittenAnAxisStillToNameHasASpokeAndAHole() => UiThread.Run(() =>
    {
        const string source = "radar-beta\n  axis a, \n  curve x{1}";
        var writing = Build(source, writing: true);

        Assert.AreEqual(2, Pieces(writing, RadarPiece.Spoke).Count, "a spoke for it while the chart is written");
        Assert.AreEqual(1, writing.Holes.Count, "with a hole at its end to name it in");
        Assert.AreEqual(1, Pieces(Build(source), RadarPiece.Spoke).Count, "and none while the chart is only read");
    });

    [TestMethod]
    public void ARadarOfNoAxesIsShownAsItWasWritten() => UiThread.Run(() =>
    {
        var laid = Build("radar-beta\n  title Empty");

        Assert.IsTrue(Pieces(laid, LayoutText.SourceKind).Any(), "the lines are on the page");
        Assert.AreEqual(0, Pieces(laid, RadarPiece.Spoke).Count);
    });

    [TestMethod]
    public void TheTitleIsSetOverTheChart_AtTheSizeTheFrontMatterAsks() => UiThread.Run(() =>
    {
        var plain = Build(Restaurants);
        var title = Pieces(plain, MermaidPiece.Title).Single();

        Assert.AreEqual("Restaurant Comparison", Written(Restaurants, title.Part));
        Assert.IsTrue(title.Bounds.Bottom <= Pieces(plain, RadarPiece.Label).Min(label => label.Bounds.Top) + 0.001);

        var big = Pieces(Build("---\nconfig:\n  themeVariables:\n    fontSize: 30\n---\n" + Restaurants), MermaidPiece.Title).Single();
        Assert.IsTrue(big.Bounds.Height > title.Bounds.Height * 1.5, $"fontSize 30 sets it bigger: {big.Bounds.Height} against {title.Bounds.Height}");
    });
}
