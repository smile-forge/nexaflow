using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Markdown.TreeView.Stages;

namespace Nexaflow.Markdown.TreeView;

/// <summary>The stages a parsed tree view is worked over by.</summary>
public static class TreeViewPipeline
{
    /// <summary>
    /// What each node holds and is, which icon each icon a node names is, and what the front matter asks for — then a hole wherever
    /// something is still to be written, where somebody is writing.
    /// </summary>
    /// <param name="tree">The block as <see cref="TreeViewParser.Parse"/> read it.</param>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static IReadOnlyList<IAstStage> Of(ContentNode tree, bool holes) =>
    [
        new ResolveNodes(),
        new WithIcons(),
        new WithConfig<TreeViewConfig>(TreeViewConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(TreeViewParser.Holds)] : Array.Empty<IAstStage>(),
    ];
}
