using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Block;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Mermaid;

namespace Nexaflow.Tests.Markdown.Mermaid.Block;

/// <summary>
/// What a <c>block-beta</c> block's stages write into its tree: each composite gathered with what is written in it, what styles
/// each block, said on the name first writing it, and what its front matter asks for.
/// </summary>
[TestClass]
[CoversNode("block-ast")]
public class BlockStagesTests
{
    /// <summary>What styles each block, by its name.</summary>
    private static Dictionary<string, MermaidStyle> Styles(string source) =>
        MermaidStaged.Read(source).SelfAndDescendants().OfType<StyledNode>().ToDictionary(named => named.Words()!.Text, named => named.Style);

    [TestMethod, TestCategory("Unit")]
    public void CompositesAreGatheredAsDeepAsTheyAreWritten()
    {
        const string source = "block-beta\n  block:one\n    block:two\n      a\n    end\n  end\n  block:three\n    b\n  end";
        var tree = MermaidStaged.Read(source);
        var outer = tree.Children.Where(child => child.Kind == MermaidKinds.Group).ToList();

        Assert.AreEqual(2, outer.Count, "one and three");
        Assert.AreEqual(1, outer[0].Children.Count(child => child.Kind == MermaidKinds.Group), "with two inside one");
        Assert.AreEqual(source, tree.Print());
    }

    [TestMethod, TestCategory("Unit")]
    public void AStyleIsWhatTheClassesAndTheStyleLinesAddUpTo_TheNearestWinning()
    {
        var styles = Styles("block-beta\n  a b c\n  classDef default stroke:#111\n  classDef blue fill:#6e6ce6,stroke:#333\n"
                            + "  class a,b blue\n  style b fill:#bbf,stroke-dasharray: 5 5");

        Assert.AreEqual("#111", styles["c"].Stroke, "every block starts from the default class");
        Assert.AreEqual("#6e6ce6", styles["a"].Fill);
        Assert.AreEqual("#333", styles["a"].Stroke, "which is laid over the default");
        Assert.AreEqual("#bbf", styles["b"].Fill, "a style of its own wins over its class");
        Assert.AreEqual("5 5", styles["b"].Dashes);
        Assert.AreEqual("#333", styles["b"].Stroke, "and what it says nothing about stays what the class asked");
    }

    [TestMethod, TestCategory("Unit")]
    public void TheStyleIsSaidOnTheNameFirstWritingIt()
    {
        var tree = MermaidStaged.Read("block-beta\n  A space B\n  A --> B\n  style A fill:#f00");

        var styled = tree.SelfAndDescendants().OfType<StyledNode>().Where(named => named.Words()!.Text == "A").ToList();
        Assert.AreEqual(1, styled.Count, "once, where it is first laid out");
        Assert.AreEqual("#f00", styled[0].Style.Fill);
    }

    [TestMethod, TestCategory("Unit")]
    public void TheFrontMattersPaddingIsRead()
    {
        static BlockConfig Config(string source) => ((ConfiguredNode<BlockConfig>)MermaidStaged.Read(source)).Config;

        Assert.AreEqual(BlockConfig.Air, Config("block-beta\n  a").Padding);
        Assert.AreEqual(12, Config("---\nconfig:\n  block:\n    padding: 12\n---\nblock-beta\n  a").Padding);
    }
}
