using Nexaflow.Markdown.Mermaid;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Languages;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Class;
using Nexaflow.Visuals.Text.Markdown.Nomnoml;
using System;
using System.Linq;
using System.Windows;

namespace Nexaflow.Tests.Visuals.Markdown.Nomnoml;

/// <summary>
/// What a nomnoml block draws. What nomnoml says <em>is</em> a class diagram, so it is drawn by the class diagram's own
/// drawing from a walk of its own tree (<see cref="NomnomlBuilder"/>) — and what is asked here is that the two agree, and
/// that everything the class drawing draws is reached by writing it nomnoml's way.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("class-diagram")]
public class NomnomlBuilderTests : MermaidBuilderContract
{
    /// <summary>A nomnoml block is not a Mermaid diagram: its fence's language names it, so no keyword does.</summary>
    public override MermaidDiagram Diagram => MermaidDiagram.Unknown;

    /// <inheritdoc/>
    public override string Language => "nomnoml";

    /// <inheritdoc/>
    protected override IEnumerable<(string What, string Source)> Drawn { get; } =
    [
        ("a node on its own", "[Pirate]"),
        ("a node with compartments", "[Pirate|eyeCount: Int|raid();pillage()]"),
        ("a node saying what it is", "[<abstract>Shape]"),
        ("an association", "[Shape] <:- [Circle]"),
        ("counts either side", "[Order] 1 -> 0..n [Line item]"),
        ("what an association says", "[Order] -> [Customer] : billed to"),
        ("a group", "[Outer|\n  [A] -> [B]\n]"),
        ("a note", "[<note>Read me]\n[A]"),
        ("the way it is laid out", "#direction: right\n[A] -> [B]"),
        ("a comment and a directive", "// why\n#arrowSize: 1.5\n[A]"),
        ("nothing written yet", string.Empty),
        ("a node half written", "[Pira"),
    ];

    [TestMethod]
    public void ANodeIsDrawnAsAClassWithItsCompartments() => UiThread.Run(() =>
    {
        var laid = Lay("[Pirate|eyeCount: Int|raid()]");

        Assert.AreEqual(1, Pieces(laid, ClassPiece.Class).Count);
        Assert.IsTrue(Saying(Pieces(laid, ClassPiece.Class)[0]).Contains("Pirate"), "its name");
        Assert.IsTrue(Saying(Pieces(laid, ClassPiece.Class)[0]).Contains("eyeCount: Int"), "its field");
        Assert.IsTrue(Saying(Pieces(laid, ClassPiece.Class)[0]).Contains("raid()"), "its method");
    });

    [TestMethod]
    public void AGroupBoxesWhatIsWrittenInsideIt() => UiThread.Run(() =>
    {
        var laid = Lay("[Outer|\n  [A] -> [B]\n]");

        Assert.AreEqual(1, Pieces(laid, ClassPiece.Space).Count, "the group is a space of its own");
        Assert.AreEqual(2, Pieces(laid, ClassPiece.Class).Count, "with both nodes inside it");
    });

    [TestMethod]
    public void ItSaysTheSameAsTheClassDiagramItStandsFor() => UiThread.Run(() =>
    {
        var nomnoml = Lay("[<abstract>Shape|area: double|+draw()]\n[Shape] <:- [Circle]");
        var mermaid = Laying.Lay("mermaid", "classDiagram\n  class Shape {\n    <<abstract>>\n    double area\n    +draw()\n  }\n  Shape <|-- Circle", 900);

        Assert.AreEqual(Pieces(mermaid, ClassPiece.Class).Count, Pieces(nomnoml, ClassPiece.Class).Count);
        CollectionAssert.AreEqual(Heads(mermaid), Heads(nomnoml), "an inheritance points the same way, with the same head");
    });

    /// <summary>What every word drawn inside a piece says, run together.</summary>
    private static string Saying(Piece piece) =>
        string.Concat(piece.SelfAndDescendants().Select(part => part.Words?.Glyphs.Text ?? string.Empty));

    [TestMethod]
    public void ANodesCompartmentsAreItsBandsAsWritten() => UiThread.Run(() =>
    {
        var box = Pieces(Lay("[Pirate|draw();eyeCount: Int|raid()]"), ClassPiece.Class).Single();
        var said = box.SelfAndDescendants().Where(part => part.Words is not null).ToDictionary(part => part.Words!.Glyphs.Text, part => part.Bounds);

        Assert.IsTrue(said["draw()"].Top < said["eyeCount: Int"].Top, "the first compartment in the order it is written");
        Assert.IsTrue(said["raid()"].Top > said["eyeCount: Int"].Bottom, "and the second under it");
    });

    [TestMethod]
    public void AGroupAcrossLinesInsideOneIsABoxInsideItsBox() => UiThread.Run(() =>
    {
        var spaces = Pieces(Lay("[Outer|\n  [Inner|\n    [A]\n  ]\n  [B]\n]"), ClassPiece.Space).Select(piece => piece.Bounds).ToList();

        Assert.AreEqual(2, spaces.Count);
        Assert.IsTrue(spaces[0].Contains(spaces[1]) || spaces[1].Contains(spaces[0]), $"one inside the other: {spaces[0]} and {spaces[1]}");
    });

    /// <summary>What each relation draws at its ends, as the widths and heights of its marks.</summary>
    private static string[] Heads(Laid laid) =>
        [.. Pieces(laid, ClassPiece.Relation).SelectMany(piece => piece.SelfAndDescendants())
              .SelectMany(piece => piece.Marks.ToArray()).OfType<GeometryMark>().Skip(1)
              .Select(mark => $"{Math.Round(mark.Shape.Bounds.Width, 1)}x{Math.Round(mark.Shape.Bounds.Height, 1)}")];
}
