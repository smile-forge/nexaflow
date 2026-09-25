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
        MermaidShape[] bracketed =
        [
            MermaidShape.Rectangle, MermaidShape.Rounded, MermaidShape.Stadium, MermaidShape.Subroutine, MermaidShape.Cylinder,
            MermaidShape.Circle, MermaidShape.DoubleCircle, MermaidShape.Asymmetric, MermaidShape.Diamond, MermaidShape.Hexagon,
            MermaidShape.Parallelogram, MermaidShape.ParallelogramAlt, MermaidShape.Trapezoid, MermaidShape.TrapezoidAlt,
        ];

        CollectionAssert.AreEquivalent(bracketed, MermaidShapes.Nodes.Select(node => node.Shape).Distinct().ToArray());

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
        Assert.AreEqual(MermaidShape.WindowPane, MermaidShapes.Named("internal-storage"));
        Assert.AreEqual(MermaidShape.StackedRectangle, MermaidShapes.Named("procs"));
        Assert.AreEqual(MermaidShape.Flag, MermaidShapes.Named("paper-tape"), "paper tape is Mermaid's flag, not the notched flag odd is");
        Assert.AreEqual(MermaidShape.Parallelogram, MermaidShapes.Named("LEAN-R"), "however it is capitalised");
        Assert.IsNull(MermaidShapes.Named("wibble"), "a name Mermaid has no shape by is none");
        Assert.IsNull(MermaidShapes.Named(null));
    }

    /// <summary>Every short name in Mermaid's table of shapes is a shape of its own, and together they are every shape there is.</summary>
    [TestMethod]
    public void EveryShortNameMermaidGivesIsAShapeOfItsOwn()
    {
        string[] names =
        [
            "rect", "rounded", "stadium", "fr-rect", "cyl", "circle", "dbl-circ", "odd", "diam", "hex", "lean-r", "lean-l", "trap-b",
            "trap-t", "doc", "lin-doc", "docs", "tag-doc", "notch-rect", "notch-pent", "lin-rect", "div-rect", "win-pane", "tag-rect",
            "st-rect", "sl-rect", "delay", "curv-trap", "bow-rect", "flag", "tri", "flip-tri", "hourglass", "bolt", "fork", "sm-circ",
            "fr-circ", "f-circ", "cross-circ", "h-cyl", "lin-cyl", "datastore", "bucket", "brace", "brace-r", "braces", "browser",
            "console", "folder", "person", "cloud", "bang", "text",
        ];

        var shapes = names.Select(name => MermaidShapes.Named(name) ?? throw new AssertFailedException($"{name} names no shape")).ToList();

        Assert.AreEqual(names.Length, shapes.Distinct().Count(), "no two names are drawn the same");
        CollectionAssert.AreEquivalent(Enum.GetValues<MermaidShape>().Except([MermaidShape.None]).ToArray(), shapes.ToArray());
    }
}
