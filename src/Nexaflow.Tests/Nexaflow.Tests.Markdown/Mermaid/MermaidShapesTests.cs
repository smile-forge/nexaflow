using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// The brackets Mermaid writes a node's label in, and the shape each pair says — the table every diagram drawing Mermaid's
/// shapes reads its nodes with.
/// </summary>
[TestClass]
[CoversNode("mermaid-diagram-kit")]
public class MermaidShapesTests
{
    /// <summary>
    /// The shapes written in brackets are the bracket table, and only those: the rest are the ones nothing but
    /// <c>@{ shape: … }</c> names, which have no brackets to be written in.
    /// </summary>
    [TestMethod]
    public void EveryShapeWrittenInBracketsHasAPairThatSaysIt_AndNoPairIsWrittenTwice()
    {
        MermaidShape[] named =
            [MermaidShape.None, MermaidShape.Document, MermaidShape.Card, MermaidShape.Cloud, MermaidShape.Bang, MermaidShape.Text];

        CollectionAssert.AreEquivalent(
            Enum.GetValues<MermaidShape>().Except(named).ToArray(),
            MermaidShapes.Nodes.Select(node => node.Shape).Distinct().ToArray());

        Assert.AreEqual(MermaidShapes.Nodes.Count, MermaidShapes.Nodes.Select(node => (node.Open, node.Close)).Distinct().Count());
        Assert.AreEqual(MermaidShapes.Nodes.Count, MermaidShapes.Brackets.Count);
    }

    [TestMethod]
    public void ALongerOpeningIsTriedBeforeTheShorterOneInsideIt()
    {
        foreach (var (open, _, _) in MermaidShapes.Nodes)
            foreach (var (longer, _, _) in MermaidShapes.Nodes.Where(node => node.Open.Length > open.Length && node.Open.StartsWith(open, StringComparison.Ordinal)))
                Assert.IsTrue(Where(longer) < Where(open), $"{longer} has to be tried before {open}, or it is never read");

        static int Where(string open) => MermaidShapes.Nodes.ToList().FindIndex(node => node.Open == open);
    }

    [TestMethod]
    public void TheShapeIsWhatBothBracketsSayTogether()
    {
        Assert.AreEqual(MermaidShape.Circle, MermaidShapes.Of("((", "))"));
        Assert.AreEqual(MermaidShape.Trapezoid, MermaidShapes.Of("[/", "\\]"));
        Assert.AreEqual(MermaidShape.Parallelogram, MermaidShapes.Of("[/", "/]"), "the same opening, ended the other way");
        Assert.AreEqual(MermaidShape.None, MermaidShapes.Of("[", ")"), "brackets that do not go together say no shape");
        Assert.AreEqual(MermaidShape.None, MermaidShapes.Of(null, null), "and neither does a node with no brackets at all");
    }

    [TestMethod]
    public void EveryNameMetadataGivesAShapeComesToOne()
    {
        Assert.AreEqual(MermaidShape.Rectangle, MermaidShapes.Named("rect"));
        Assert.AreEqual(MermaidShape.Cylinder, MermaidShapes.Named("cyl"));
        Assert.AreEqual(MermaidShape.Cylinder, MermaidShapes.Named("database"), "and every other name for it");
        Assert.AreEqual(MermaidShape.Diamond, MermaidShapes.Named("decision"));
        Assert.AreEqual(MermaidShape.Document, MermaidShapes.Named("doc"));
        Assert.AreEqual(MermaidShape.Card, MermaidShapes.Named("notch-rect"));
        Assert.AreEqual(MermaidShape.Cloud, MermaidShapes.Named("cloud"));
        Assert.AreEqual(MermaidShape.Text, MermaidShapes.Named("text"));
        Assert.AreEqual(MermaidShape.Parallelogram, MermaidShapes.Named("LEAN-R"), "however it is capitalised");
        Assert.AreEqual(MermaidShape.Rectangle, MermaidShapes.Named("win-pane"), "a shape with a detail of its own comes to the nearest");
        Assert.AreEqual(MermaidShape.Rectangle, MermaidShapes.Named("wibble"), "and so does a name that says nothing");
        Assert.AreEqual(MermaidShape.Rectangle, MermaidShapes.Named(null));
    }
}
