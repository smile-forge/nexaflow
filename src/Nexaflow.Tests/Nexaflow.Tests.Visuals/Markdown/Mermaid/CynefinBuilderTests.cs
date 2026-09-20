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
using Nexaflow.Visuals.Text.Markdown.Mermaid.Cynefin;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>cynefin-beta</c> block drawn on the shared layout tree: the four practised domains in the corners standing for the
/// lines opening them, disorder as a cloud in the middle, every item carded in its domain, and each word typed into where drawn.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("cynefin")]
public class CynefinBuilderTests : MermaidBuilderContract
{
    private const string Sense =
        "cynefin-beta\n  title Making sense\n  complex\n    \"Investigate root cause\"\n    \"Run an experiment\"\n"
        + "  complicated\n    \"Consult an expert\"\n  clear\n    \"Apply the runbook\"\n  chaotic\n    \"Stop the bleeding\"\n"
        + "  confusion\n    \"Unclassified A\"\n    \"Unclassified B\"\n  chaotic --> complex : \"Stabilised\"\n  complex --> clear : \"Learned\"";

    public override MermaidDiagram Diagram => MermaidDiagram.Cynefin;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("making sense of the work", Sense),
        ("descriptions and colours of its own",
            "---\nconfig:\n  cynefin:\n    showDomainDescriptions: true\n    width: 600\n  themeVariables:\n    cynefin:\n      complexBg: \"#4e79a7\"\n"
            + "      boundaryColor: \"#888888\"\n---\ncynefin-beta\n  complex\n    \"Emergent practice\"\n  clear\n    \"Best practice\""),
        ("domains with no items", "cynefin-beta\n  complex\n  complicated\n  clear\n  chaotic"),
        ("a movement with no disorder in the way", "cynefin-beta\n  complex\n  complicated\n  complex --> complicated : \"Understood\""),
        ("items bare and long enough to wrap",
            "cynefin-beta\n  clear\n    Apply the standard runbook, then write down what it did not cover this time\n    Short"),
        ("still being written", "cynefin-beta\n  complex\n    \"\"\n  complex --> \n  clear"),
        ("what nobody means to write", "cynefin-beta\n  \"Stranded\"\n  complex --> nowhere"),
        ("nothing to draw", "cynefin-beta"),
    ];

    private static Laid Build(string source, double room = 900) =>
        CynefinBuilder.Build(EditState.For(source), new DiagramLaying(MarkdownPalette.Dark, 1.0, room));

    [TestMethod]
    public void TheDomainsGoInTheCorners_AndDisorderInTheMiddle() => UiThread.Run(() =>
    {
        var laid = Build(Sense);
        var cells = Pieces(laid, CynefinPiece.Domain).Select(piece => Middle(piece.Bounds)).ToList();
        var cloud = Middle(Pieces(laid, CynefinPiece.Centre).Single().Bounds);

        Assert.IsTrue(cells[0].X < cells[1].X && cells[0].Y < cells[2].Y, "complex top left");
        Assert.IsTrue(cells[1].X > cells[0].X && cells[1].Y < cells[3].Y, "complicated top right");
        Assert.IsTrue(cells[2].X < cells[3].X && cells[2].Y > cells[0].Y, "chaotic bottom left");
        Assert.AreEqual((cells[0].X + cells[1].X) / 2, cloud.X, 1, "disorder across the middle");
        Assert.AreEqual((cells[0].Y + cells[2].Y) / 2, cloud.Y, 1, "disorder up the middle");
    });

    [TestMethod]
    public void ADomainStandsForTheLineOpeningIt_AndDisorderForItsOwn() => UiThread.Run(() =>
    {
        var laid = Build(Sense);

        Assert.AreEqual("complex", Written(Sense, Pieces(laid, CynefinPiece.Domain)[0].Part));
        Assert.AreEqual("confusion", Written(Sense, Pieces(laid, CynefinPiece.Centre).Single().Part));
    });

    [TestMethod]
    public void EveryItemIsCardedInItsDomain_StandingForTheLineItIsWrittenOn() => UiThread.Run(() =>
    {
        var laid = Build(Sense);
        var cards = Pieces(laid, CynefinPiece.Item);
        var complex = Pieces(laid, CynefinPiece.Domain)[0].Bounds;

        // Five carded in the corners; the two in disorder are words inside the cloud.
        Assert.AreEqual(5, cards.Count);
        Assert.AreEqual("\"Investigate root cause\"", Written(Sense, cards[0].Part));
        Assert.IsTrue(complex.Contains(cards[0].Bounds), "carded inside its domain");
        Assert.IsTrue(cards[0].Bounds.Top < cards[1].Bounds.Top, "in the order written");
    });

    [TestMethod]
    public void WhatIsWrittenOnTheGridIsTheCharactersWritten() => UiThread.Run(() =>
    {
        var laid = Build(Sense);

        foreach (var kind in new[] { CynefinPiece.Name, CynefinPiece.Says, CynefinPiece.Label })
            Assert.IsTrue(Pieces(laid, kind).All(piece => piece.Words is { Maps: true }), kind);

        Assert.AreEqual("Stabilised", Pieces(laid, CynefinPiece.Label)[0].Words!.Glyphs.Text);
    });

    [TestMethod]
    public void AMovementStandsForItsTransition_AndKeepsClearOfTheDisorder() => UiThread.Run(() =>
    {
        var laid = Build(Sense);
        var moves = Pieces(laid, CynefinPiece.Move);
        var cloud = Middle(Pieces(laid, CynefinPiece.Centre).Single().Bounds);

        Assert.AreEqual(2, moves.Count);
        Assert.AreEqual("complex --> clear : \"Learned\"", Written(Sense, moves[1].Part));

        // complex to clear runs corner to corner, which would take it under the cloud: it bends round instead.
        var line = moves[1].Marks.ToArray().OfType<GeometryMark>().First().Shape;

        Assert.IsFalse(line.StrokeContains(new Pen(Brushes.Black, 20), cloud - (Vector)moves[1].Anchor),
            "the movement keeps clear of the disorder in the middle");
    });

    [TestMethod]
    public void ADomainTheFrontMatterColoursIsDrawnInThatColour() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  themeVariables:\n    cynefin:\n      complexBg: \"#4e79a7\"\n---\ncynefin-beta\n  complex\n    \"One\"");
        var mark = Pieces(laid, CynefinPiece.Domain)[0].Marks.ToArray().OfType<GeometryMark>().Single();

        Assert.AreEqual(Color.FromRgb(0x4E, 0x79, 0xA7), ((SolidColorBrush)mark.Fill!).Color);
    });

    [TestMethod]
    public void TheGridGrowsToHoldWhatIsWrittenInIt() => UiThread.Run(() =>
    {
        var few = Build("cynefin-beta\n  complex\n    \"One\"").Size;
        var many = Build("cynefin-beta\n  complex\n" + string.Join("\n", Enumerable.Range(0, 12).Select(at => $"    \"Item {at}\""))).Size;

        Assert.IsTrue(many.Height > few.Height, $"{many.Height} over {few.Height}");
    });

    [TestMethod]
    public void EachDomainSaysHowItIsWorked_UnlessTheFrontMatterSaysNot() => UiThread.Run(() =>
    {
        var said = Pieces(Build("cynefin-beta\n  complex"), CynefinPiece.About).Select(piece => piece.Words!.Glyphs.Text).ToArray();

        CollectionAssert.AreEqual(new[] { "Probe → Sense → Respond", "Emergent Practices" }, said);
        Assert.AreEqual(0, Pieces(Build("---\nconfig:\n  cynefin:\n    showDomainDescriptions: false\n---\ncynefin-beta\n  complex"), CynefinPiece.About).Count);
    });

    [TestMethod]
    public void TheCliffFromClearIntoChaoticIsDrawnHeavierThanTheOtherBoundaries() => UiThread.Run(() =>
    {
        var marks = Pieces(Build(Sense), CynefinPiece.Boundaries).Single().Marks.ToArray().OfType<GeometryMark>().ToList();

        Assert.AreEqual(2, marks.Count, "the boundaries, and the cliff");
        Assert.IsTrue(marks[1].Thickness > marks[0].Thickness, $"the cliff is heavier: {marks[1].Thickness} over {marks[0].Thickness}");
        Assert.IsTrue(marks[1].Shape.Bounds.Top > marks[0].Shape.Bounds.Top, "and it is the boundary at the foot of the grid");
    });

    [TestMethod]
    public void TheColoursAndSizesTheFrontMatterWritesAreWhatIsDrawn() => UiThread.Run(() =>
    {
        var laid = Build(
            "---\nconfig:\n  themeVariables:\n    cynefin:\n      cliffColor: \"#ff0000\"\n      cliffWidth: 5\n      arrowColor: \"#00ff00\"\n"
            + "      labelColor: \"#0000ff\"\n---\ncynefin-beta\n  complex\n  clear\n  complex --> clear : \"Learned\"");

        var cliff = Pieces(laid, CynefinPiece.Boundaries).Single().Marks.ToArray().OfType<GeometryMark>().Last();
        var move = Pieces(laid, CynefinPiece.Move).Single().Marks.ToArray().OfType<GeometryMark>().First();

        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), ((SolidColorBrush)cliff.Stroke!).Color);
        Assert.AreEqual(5, cliff.Thickness);
        Assert.AreEqual(Color.FromRgb(0, 0xFF, 0), ((SolidColorBrush)move.Stroke!).Color);

    });
}
