using Nexaflow.Markdown.Mermaid.Mindmap;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Mindmap;

/// <summary>A <c>mindmap</c> block read back into its root and everything hanging off it, with each node's shape and branch.</summary>
[TestClass]
[CoversNode("mindmap-ast")]
public class MindmapTreeTests
{
    [TestMethod]
    public void TheFirstNodeIsTheRoot_AndEveryOtherHangsOffTheNearestNodeIndentedLess()
    {
        var map = MindmapTree.Of(MermaidStaged.Read(MindmapGrammarTests.Example));
        var root = map.Root!;

        Assert.AreEqual("mindmap", root.Title!.Text);
        Assert.AreEqual(MindmapShape.Circle, root.Shape);
        CollectionAssert.AreEqual(new[] { "Origins", "Research", "Tools" }, root.Children.Select(child => child.Title!.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "Long history", "Popularisation" }, root.Children[0].Children.Select(child => child.Title!.Text).ToArray());
        Assert.AreEqual("British popular psychology author Tony Buzan", root.Children[0].Children[1].Children.Single().Title!.Text);
        Assert.AreEqual(3, root.Children[1].Children[1].Children.Single().Children.Count, "the three uses");
        Assert.AreEqual(15, map.Nodes.Count());
    }

    [TestMethod]
    public void UnclearIndentationHangsANodeOffTheNearestNodeIndentedLess()
    {
        var root = MindmapTree.Of(MermaidStaged.Read("mindmap\n    Root\n        A\n            B\n          C")).Root!;

        CollectionAssert.AreEqual(new[] { "B", "C" }, root.Children.Single().Children.Select(child => child.Title!.Text).ToArray(),
                                  "C is neither B's child nor its sibling by indentation, so it is A's child, as Mermaid reads it");
    }

    [TestMethod]
    public void EachBranchOffTheRootIsItsOwn_AndEveryNodeUnderItTakesIt()
    {
        var root = MindmapTree.Of(MermaidStaged.Read(MindmapGrammarTests.Example)).Root!;

        Assert.AreEqual(-1, root.Branch);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, root.Children.Select(child => child.Branch).ToArray());
        Assert.IsTrue(root.Children[1].Children.All(node => node.Branch == 1));
        CollectionAssert.AreEqual(new[] { 0, 1, 1, 2, 2, 2, 3 }, MindmapTree.Of(MermaidStaged.Read("mindmap\nr\n a\n b\n  c\n d\n  e\n   f\n g")).Root!.Children
            .SelectMany(child => new[] { child }.Concat(Under(child))).Select(node => node.Branch).ToArray());
    }

    [TestMethod]
    public void EveryBracketIsItsShape_AndABareIdHasNoBorder()
    {
        var map = MindmapTree.Of(MermaidStaged.Read("mindmap\n  r((root))\n    a[Square]\n    b(Rounded)\n    c((Circle))\n    d)Cloud(\n    e))Bang((\n    f{{Hexagon}}\n    g Plain"));

        CollectionAssert.AreEqual(
            new[] { MindmapShape.Square, MindmapShape.Rounded, MindmapShape.Circle, MindmapShape.Cloud, MindmapShape.Bang, MindmapShape.Hexagon, MindmapShape.Plain },
            map.Root!.Children.Select(child => child.Shape).ToArray());
    }

    [TestMethod]
    public void AnIconOrAClassIsTheNodeAboveIts_AndANodeHangingOffNothingIsLeftOut()
    {
        var decorated = MindmapTree.Of(MermaidStaged.Read("mindmap\n  Root\n    A\n    ::icon(fa fa-book)\n    :::urgent large")).Root!.Children.Single();

        Assert.AreEqual(("fa fa-book", "urgent large"), (decorated.Icon, decorated.Class));
        Assert.AreEqual(2, MindmapTree.Of(MermaidStaged.Read("mindmap\n  root((r))\n    A\n  another")).Nodes.Count(), "the root and A, the second root not drawn");
    }

    [TestMethod]
    public void TheFrontMatterIsRead()
    {
        var config = MindmapTree.Of(MermaidStaged.Read("---\nconfig:\n  layout: tidy-tree\n  mindmap:\n    padding: 14\n    maxNodeWidth: 150\n  themeVariables:\n    cScale1: \"#ff0000\"\n    git0: \"#00ff00\"\n---\nmindmap\n  r((root))")).Config;

        Assert.AreEqual(("tidy-tree", 14d, 150d, "#ff0000", "#00ff00"), (config.Layout, config.Padding!.Value, config.MaxNodeWidth!.Value, config.Scale[1], config.RootFill));
    }

    private static IEnumerable<MindmapNode> Under(MindmapNode node) => node.Children.SelectMany(child => new[] { child }.Concat(Under(child)));
}
