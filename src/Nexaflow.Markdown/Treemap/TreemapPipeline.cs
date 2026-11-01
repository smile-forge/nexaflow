using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Markdown.Treemap.Stages;

namespace Nexaflow.Markdown.Treemap;

/// <summary>The stages a parsed treemap is worked over by.</summary>
public static class TreemapPipeline
{
    /// <summary>
    /// What each item holds and comes to, and what the front matter asks for, then a hole wherever something is still to be written
    /// where somebody is writing.
    /// </summary>
    /// <param name="tree">The block as <see cref="TreemapParser.Parse"/> read it.</param>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static IReadOnlyList<IAstStage> Of(ContentNode tree, bool holes) =>
    [
        new ResolveItems(),
        new WithConfig<TreemapConfig>(TreemapConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(TreemapParser.Holds)] : Array.Empty<IAstStage>(),
    ];
}
