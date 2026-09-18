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
    [TestMethod]
    public void EveryShapeThereIsHasBracketsThatSayIt_AndNoPairIsWrittenTwice()
    {
        CollectionAssert.AreEquivalent(
            Enum.GetValues<MermaidShape>().Where(shape => shape != MermaidShape.None).ToArray(),
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
}
