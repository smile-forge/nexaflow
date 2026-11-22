using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// What a block's front matter asks for, hung on the block by <see cref="WithConfig{TConfig}"/>: read once, kept through every
/// stage that works the tree over after it, and printing as nothing but the block that was written. A sankey is the block it
/// is hung on here, with the stages a sankey is worked over by.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class WithConfigTests
{
    private const string Source = "---\nconfig:\n  sankey:\n    width: 800\n---\nsankey-beta\na,b,";

    [TestMethod]
    public void TheBlockCarriesWhatItsFrontMatterAsks() =>
        Assert.AreEqual(800, ((ConfiguredNode<SankeyConfig>)Worked(holes: false)).Config.Width);

    [TestMethod]
    public void ItIsKeptThroughTheStagesAfterIt_AndPrintsAsTheBlockWritten()
    {
        // Being written, the value still to write is a hole, so the tree is worked over again after the config is hung.
        var tree = Worked(holes: true);

        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == Kinds.Hole), "the tree was worked over after it");
        Assert.AreEqual(800, ((ConfiguredNode<SankeyConfig>)tree).Config.Width);
        Assert.AreEqual(Source, tree.Print());
    }

    /// <summary>The block read, its config hung on it, and — where somebody is writing — its holes put in after.</summary>
    private static ContentNode Worked(bool holes)
    {
        var tree = SankeyParser.Parse(Source);
        IAstStage[] stages =
        [
            new WithConfig<SankeyConfig>(SankeyConfig.Read(MermaidBlock.Of(tree).Config)),
            .. holes ? [new WithHoles(SankeyParser.Holds)] : Array.Empty<IAstStage>(),
        ];

        return new AstPipeline(stages).Run(tree);
    }
}
