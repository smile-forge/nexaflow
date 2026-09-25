using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// What a block's front matter asks for, hung on the block by <see cref="WithConfig{TConfig}"/>: read once, kept through every
/// stage that works the tree over after it, and printing as nothing but the block that was written.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class WithConfigTests
{
    private const string Source = "---\nconfig:\n  sankey:\n    width: 800\n---\nsankey-beta\na,b,";

    [TestMethod]
    public void TheBlockCarriesWhatItsFrontMatterAsks() =>
        Assert.AreEqual(800, ((ConfiguredNode<SankeyConfig>)MermaidStaged.Read(Source)).Config.Width);

    [TestMethod]
    public void ItIsKeptThroughTheStagesAfterIt_AndPrintsAsTheBlockWritten()
    {
        // Being written, the value still to write is a hole, so the tree is worked over again after the config is hung.
        var tree = MermaidStaged.Read(Source, holes: true);

        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == Kinds.Hole), "the tree was worked over after it");
        Assert.AreEqual(800, ((ConfiguredNode<SankeyConfig>)tree).Config.Width);
        Assert.AreEqual(Source, tree.Print());
    }
}
