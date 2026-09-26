using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Er;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// Each group of a block gathered with what is written in it (<see cref="MermaidNesting.Nest"/>), read here through an ER
/// diagram's subgraphs: the tree says what is inside what, and still prints as it was written.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class MermaidNestingTests
{
    private static ContentNode Nested(string source) =>
        MermaidNesting.Nest(MermaidParser.Parse(source), [ErKinds.Subgraph], [ErKinds.Ends],
                            stray: "stray", unclosed: "unclosed");

    private static List<ContentNode> Groups(ContentNode holder) => [.. holder.Children.Where(child => child.Kind == MermaidKinds.Group)];

    /// <summary>What each line a node holds says, trimmed — its groups' lines among them, as the group they are in.</summary>
    private static string[] Said(ContentNode holder) =>
        [.. holder.Children.Where(child => child.Kind is MermaidKinds.Line or MermaidKinds.Group).Select(child => child.Kind == MermaidKinds.Group ? "[group]" : child.Print().Trim())];

    [TestMethod, TestCategory("Unit")]
    public void AGroupHoldsTheLineOpeningItWhatIsWrittenInItAndItsEnd()
    {
        const string source = "erDiagram\n  subgraph Sales\n    A\n    B\n  end\n  C";
        var tree = Nested(source);
        var group = Groups(tree).Single();

        CollectionAssert.AreEqual(new[] { "subgraph Sales", "A", "B", "end" }, Said(group));
        CollectionAssert.AreEqual(new[] { "erDiagram", "[group]", "C" }, Said(tree), "what follows its end is outside it");
        Assert.AreEqual(source, tree.Print(), "and gathering them changes nothing written");
    }

    [TestMethod, TestCategory("Unit")]
    public void AGroupInsideOneIsGatheredInsideIt()
    {
        var outer = Groups(Nested("erDiagram\n  subgraph Outer\n    subgraph Inner\n      A\n    end\n    B\n  end")).Single();

        CollectionAssert.AreEqual(new[] { "subgraph Outer", "[group]", "B", "end" }, Said(outer));
        CollectionAssert.AreEqual(new[] { "subgraph Inner", "A", "end" }, Said(Groups(outer).Single()));
    }

    [TestMethod, TestCategory("Unit")]
    public void AGroupNothingEndsHoldsTheRestOfTheBlock_AndSaysSo()
    {
        const string source = "erDiagram\n  subgraph Sales\n    A\n    B";
        var tree = Nested(source);
        var group = Groups(tree).Single();

        CollectionAssert.AreEqual(new[] { "subgraph Sales", "A", "B" }, Said(group));
        Assert.AreEqual("unclosed", group.Children[0].Stated()!.Trouble);
        Assert.AreEqual(source, tree.Print());
    }

    [TestMethod, TestCategory("Unit")]
    public void AnEndClosingNothingStaysWhereItIsWritten_AndSaysSo()
    {
        const string source = "erDiagram\n  A\n  end";
        var tree = Nested(source);

        Assert.AreEqual(0, Groups(tree).Count);
        Assert.AreEqual("stray", tree.Children.Last(child => child.Kind == MermaidKinds.Line).Stated()!.Trouble);
        Assert.AreEqual(source, tree.Print());
    }

    [TestMethod, TestCategory("Unit")]
    public void ABlockWithNoGroupsIsLeftAsItIs()
    {
        var tree = MermaidParser.Parse("erDiagram\n  A ||--|| B : x");

        Assert.AreSame(tree, MermaidNesting.Nest(tree, [ErKinds.Subgraph], [ErKinds.Ends], "stray", "unclosed"));
    }
}
