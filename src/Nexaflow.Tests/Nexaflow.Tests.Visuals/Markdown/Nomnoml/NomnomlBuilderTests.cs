using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Markdown.Nomnoml;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Class;
using Nexaflow.Visuals.Text.Markdown.Nomnoml;
using System.Windows;

namespace Nexaflow.Tests.Visuals.Markdown.Nomnoml;

/// <summary>
/// What a nomnoml block draws. It is drawn by the class diagram's own builder — what nomnoml says <em>is</em> a class
/// diagram (<see cref="NomnomlDiagram"/>) — so what is asked here is that the two agree, and that everything the
/// class builder draws is reached by writing it nomnoml's way.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("class-diagram")]
public class NomnomlBuilderTests : MermaidBuilderContract
{
    /// <summary>A nomnoml block is not a Mermaid diagram: its fence's language names it, so no keyword does.</summary>
    public override MermaidDiagram Diagram => MermaidDiagram.Unknown;

    /// <inheritdoc/>
    public override string Language => NomnomlDiagramHandler.Language;

    /// <inheritdoc/>
    internal override MermaidBuilders.Build Builder => NomnomlBuilder.Build;

    /// <inheritdoc/>
    /// <remarks>Nothing inside a nomnoml block names its type — the fence's language does — so its grammar is named here.</remarks>
    internal override Nexaflow.Markdown.Mermaid.IMermaidGrammar? Grammar => Nexaflow.Markdown.Nomnoml.NomnomlDiagram.Grammar;

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
        var nomnoml = NomnomlDiagram.Read("[<abstract>Shape|area: double|+draw()]\n[Shape] <:- [Circle]");
        var mermaid = ClassDiagram.Read("classDiagram\n  class Shape {\n    <<abstract>>\n    double area\n    +draw()\n  }\n  Shape <|-- Circle");

        Assert.AreEqual(mermaid.Nodes.Count, nomnoml.Nodes.Count);
        Assert.AreEqual(mermaid.Relations.Count, nomnoml.Relations.Count);
        Assert.AreEqual(mermaid.Relations[0].Head, nomnoml.Relations[0].Head, "an inheritance points the same way");
        Assert.AreEqual(mermaid.Relations[0].Tail, nomnoml.Relations[0].Tail);
    });

    /// <summary>What every word drawn inside a piece says, run together.</summary>
    private static string Saying(Piece piece) =>
        string.Concat(piece.SelfAndDescendants().Select(part => part.Words?.Glyphs.Text ?? string.Empty));
}
