using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>venn-beta</c> block drawn on the shared layout tree: a circle per set whose area is its size, overlaps where the
/// unions say, and words set where each region has room — every piece pointing at what was written for it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("venn")]
public class VennBuilderTests : MermaidBuilderContract
{
    /// <summary>The first block Mermaid's documentation shows: three sets and every overlap between them.</summary>
    private const string Features =
        "venn-beta\n  title What makes a good feature\n  set Desirable\n  set Feasible\n  set Viable\n  union Desirable,Feasible[\"Buildable\"]\n"
        + "  union Feasible,Viable[\"Sustainable\"]\n  union Desirable,Viable[\"Marketable\"]\n  union Desirable,Feasible,Viable[\"Ship it\"]";

    private const string Sized = "venn-beta\n  title Teams\n  set A[\"Alpha\"]:20\n    text A1[\"React\"]\n  set B[\"Beta\"]:12\n  union A,B[\"AB\"]:3";

    public override MermaidDiagram Diagram => MermaidDiagram.Venn;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("the documented features", Features),
        ("sizes, items and a union", Sized),
        ("styles and the front matter",
            "---\nconfig:\n  venn:\n    width: 500\n    height: 320\n    useDebugLayout: true\n  themeVariables:\n    venn1: \"#ff0000\"\n---\nvenn-beta\n  set A[\"Alpha\"]\n    text A1\n    text A2\n  set B\n  union A,B\n  style A fill:#00ff00, fill-opacity:0.5\n  style A,B stroke:#000, stroke-width:3\n  style A1 color:red"),
        ("names and labels still being written", "venn-beta\n  set \"\"\n  set A[\"\"]\n    text \"\"\n  union A, "),
        ("sets that do not overlap", "venn-beta\n  set A\n  set B\n  set C"),
        ("no sets at all", "venn-beta\n  title Nothing"),
    ];

    private static Laid Build(string source, double room = 700) =>
        VennBuilder.Build(EditState.For(source), MarkdownPalette.Dark, 1.0, room);

    private static IEnumerable<Piece> Pieces(Laid laid, string kind) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind);

    private static string Written(string source, ISourcePart? part) =>
        part is null ? "" : source.Substring(part.Start, part.Length);

    /// <summary>A circle's centre and radius, from the circle it is drawn as.</summary>
    private static (Point Centre, double Radius) Round(Piece circle)
    {
        foreach (var mark in circle.Marks)
            if (mark is GeometryMark { Shape: EllipseGeometry shape })
                return (shape.Center + (Vector)circle.Anchor, shape.RadiusX);

        Assert.Fail("a circle is drawn as a circle");
        return default;
    }

    private static Brush? Fill(Piece piece)
    {
        foreach (var mark in piece.Marks)
            if (mark is GeometryMark { Fill: { } fill }) return fill;

        return null;
    }

    [TestMethod]
    public void EverySetIsACircleStandingForItsRegion() => UiThread.Run(() =>
    {
        var laid = Build(Sized);
        var circles = Pieces(laid, VennPiece.Circle).ToList();

        Assert.AreEqual(2, circles.Count);
        Assert.AreEqual("set A[\"Alpha\"]:20\n    text A1[\"React\"]", Written(Sized, circles[0].Part).Trim(), "the set and the item in it");
        Assert.IsTrue(circles.All(circle => circle.Region is not null), "a circle stands in its own shape, not its box");
        Assert.AreEqual(0, laid.Trouble.Count);
    });

    [TestMethod]
    public void ACirclesAreaIsItsSetsSize() => UiThread.Run(() =>
    {
        var circles = Pieces(Build(Sized), VennPiece.Circle).Select(Round).ToList();

        Assert.AreEqual(20.0 / 12, Math.Pow(circles[0].Radius / circles[1].Radius, 2), 0.01);
    });

    [TestMethod]
    public void TwoSetsOverlapByAsMuchAsTheirUnionSays() => UiThread.Run(() =>
    {
        var circles = Pieces(Build(Sized), VennPiece.Circle).Select(Round).ToList();
        var (a, b) = (circles[0], circles[1]);

        var shared = VennLayout.Lens(a.Radius, b.Radius, (a.Centre - b.Centre).Length);
        Assert.AreEqual(3.0 / 20, shared / (Math.PI * a.Radius * a.Radius), 0.005, "three twentieths of Alpha is shared with Beta");
    });

    [TestMethod]
    public void SetsNothingSaysOverlapStandApart() => UiThread.Run(() =>
    {
        var circles = Pieces(Build("venn-beta\n  set A\n  set B"), VennPiece.Circle).Select(Round).ToList();

        Assert.IsTrue((circles[0].Centre - circles[1].Centre).Length >= circles[0].Radius + circles[1].Radius);
    });

    [TestMethod]
    public void TheFirstSetIsOnTheLeftOfTwo_AndAtTheTopOfThree() => UiThread.Run(() =>
    {
        var two = Pieces(Build(Sized), VennPiece.Circle).Select(Round).ToList();
        Assert.IsTrue(two[0].Centre.X < two[1].Centre.X);
        Assert.AreEqual(two[0].Centre.Y, two[1].Centre.Y, 0.5);

        var three = Pieces(Build(Features), VennPiece.Circle).Select(Round).ToList();
        Assert.IsTrue(three[0].Centre.Y < three[1].Centre.Y && three[0].Centre.Y < three[2].Centre.Y, "the first at the top");
        Assert.IsTrue(three[1].Centre.X < three[2].Centre.X, "and the second left of the third");
    });

    [TestMethod]
    public void APressWhereAUnionsCirclesOverlapMeansTheUnion_AndOneInASetAloneMeansTheSet() => UiThread.Run(() =>
    {
        var laid = Build(Sized);
        var circles = Pieces(laid, VennPiece.Circle).ToList();
        var overlap = Pieces(laid, VennPiece.Overlap).Single();
        var (a, b) = (Round(circles[0]), Round(circles[1]));

        // Towards the top of the lens, and of the part of A nothing else covers — clear of the labels set in the middle of each.
        var apart = b.Centre - a.Centre;
        var along = apart / apart.Length;
        var chord = ((apart.LengthSquared) + (a.Radius * a.Radius) - (b.Radius * b.Radius)) / (2 * apart.Length);
        var lens = a.Centre + (along * chord) + (new Vector(along.Y, -along.X) * Math.Sqrt((a.Radius * a.Radius) - (chord * chord)) * 0.7);
        var alone = a.Centre + new Vector(0, -a.Radius * 0.9);

        Assert.AreEqual(overlap.Sits().Start, laid.PieceAt(lens).Selectable().Sits().Start, "the union");
        Assert.AreEqual("union A,B[\"AB\"]:3", Written(Sized, laid.PieceAt(lens).Selectable().Part).Trim());
        StringAssert.StartsWith(Written(Sized, laid.PieceAt(alone).Selectable().Part).Trim(), "set A", "the set");
    });

    [TestMethod]
    public void ALabelIsTheCharactersItWasWrittenAs() => UiThread.Run(() =>
    {
        var laid = Build(Sized);
        var labels = Pieces(laid, VennPiece.Label).ToList();

        CollectionAssert.AreEqual(new[] { "Alpha", "Beta", "AB" }, labels.Select(label => Written(Sized, label.Part)).ToArray());
        Assert.IsTrue(labels.All(label => label.Words is { Maps: true }), "so the caret can stand between its letters");
    });

    [TestMethod]
    public void ASetWithNoLabelShowsItsName_AndAUnionWithNoneTheNamesOfItsSets() => UiThread.Run(() =>
    {
        const string source = "venn-beta\n  set Frontend\n  set Backend\n  union Frontend,Backend";
        var labels = Pieces(Build(source), VennPiece.Label).ToList();

        CollectionAssert.AreEqual(new[] { "Frontend", "Backend" }, labels.Take(2).Select(label => Written(source, label.Part)).ToArray());
        Assert.AreEqual("Backend ∩ Frontend", labels[2].Words!.Glyphs.Text);
        Assert.IsFalse(labels[2].Words!.Maps, "worked out, so there is nowhere in it to put a caret");
    });

    [TestMethod]
    public void AnItemIsWrittenInsideItsRegion() => UiThread.Run(() =>
    {
        var laid = Build(Sized);
        var item = Pieces(laid, VennPiece.Item).Single();
        var circles = Pieces(laid, VennPiece.Circle).Select(Round).ToList();

        var middle = new Point(item.Bounds.X + (item.Bounds.Width / 2), item.Bounds.Y + (item.Bounds.Height / 2));
        Assert.IsTrue((middle - circles[0].Centre).Length < circles[0].Radius, "inside Alpha");
        Assert.IsTrue((middle - circles[1].Centre).Length > circles[1].Radius, "and clear of Beta");
        Assert.AreEqual("text A1[\"React\"]", Written(Sized, item.Part));
    });

    [TestMethod]
    public void AUnionOfThreeSetsHasARegionOfItsOwn() => UiThread.Run(() =>
    {
        var source = Features;
        var laid = Build(source);
        var overlaps = Pieces(laid, VennPiece.Overlap).ToList();
        var shipIt = Pieces(laid, VennPiece.Label).Single(label => Written(source, label.Part) == "Ship it");

        var middle = new Point(shipIt.Bounds.X + (shipIt.Bounds.Width / 2), shipIt.Bounds.Y + (shipIt.Bounds.Height / 2));
        Assert.AreEqual(4, overlaps.Count, "three pairs and the three together");
        Assert.IsTrue(overlaps[3].Region!.FillContains(middle - overlaps[3].Anchor), "its label sits where all three overlap");
        Assert.AreEqual(0, laid.Trouble.Count);
    });

    [TestMethod]
    public void StylesAndTheFrontMatterAreWhatItIsDrawnIn() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  themeVariables:\n    venn2: \"#00ff00\"\n---\nvenn-beta\n  set A\n  set B\n  style A fill:#ff0000, fill-opacity:1");
        var circles = Pieces(laid, VennPiece.Circle).ToList();

        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), ((SolidColorBrush)Fill(circles[0])!).Color);
        Assert.AreEqual(1, Fill(circles[0])!.Opacity, "as solid as the style asks");
        Assert.AreEqual(Color.FromRgb(0, 0xFF, 0), ((SolidColorBrush)Fill(circles[1])!).Color, "venn2 for the second set");
    });

    [TestMethod]
    public void TheTitleIsSetOverTheDiagram() => UiThread.Run(() =>
    {
        var laid = Build(Sized);
        var title = Pieces(laid, MermaidPiece.Title).Single();

        Assert.AreEqual("Teams", Written(Sized, title.Part));
        Assert.IsTrue(title.Bounds.Bottom <= Pieces(laid, VennPiece.Circle).Min(circle => circle.Bounds.Top) + 0.001);
    });

    [TestMethod]
    public void TheDebugLayoutDrawsItsWorkingsWhereAskedFor() => UiThread.Run(() =>
    {
        Assert.AreEqual(0, Pieces(Build(Sized), VennPiece.Debug).Count());
        Assert.AreEqual(1, Pieces(Build("---\nconfig:\n  venn:\n    useDebugLayout: true\n---\n" + Sized), VennPiece.Debug).Count());
    });

    [TestMethod]
    public void InARoomNarrowerThanTheDrawingItIsFittedToTheRoom() => UiThread.Run(() =>
    {
        var laid = Build(Sized, room: 260);
        Assert.IsTrue(laid.Size.Width <= 260, $"fitted into the room it was given, and took {laid.Size.Width}");

        var wide = Build("---\nconfig:\n  venn:\n    width: 600\n    useMaxWidth: false\n---\n" + Sized, room: 260);
        Assert.IsTrue(wide.Size.Width >= 600, "unless the front matter asks for its own width");
    });

    [TestMethod]
    public void ADiagramOfNoSetsIsShownAsItWasWritten() => UiThread.Run(() =>
    {
        var laid = Build("venn-beta\n  circle A");

        Assert.IsTrue(Pieces(laid, LayoutText.SourceKind).Any(), "the lines are on the page");
        Assert.AreEqual(0, Pieces(laid, VennPiece.Circle).Count());
        StringAssert.Contains(laid.Trouble.Single().Message, "a set, a union");
    });

    [TestMethod]
    public void ItDispatchesThroughTheDiagramRenderer() => UiThread.Run(() =>
    {
        var content = (ContentElement)DiagramRenderer.Render("mermaid", Sized, MarkdownPalette.Dark);

        content.Measure(new Size(700, double.PositiveInfinity));
        Assert.IsTrue(content.DesiredSize.Width > 0 && content.DesiredSize.Height > 0);
        Assert.AreEqual(0, content.Diagnostics.Count);
        Assert.IsFalse(content.IsReadOnly, "its labels are written in");
    });
}
