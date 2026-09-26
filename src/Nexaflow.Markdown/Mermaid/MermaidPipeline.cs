using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>The stages a parsed Mermaid block is worked over by, which its diagram decides.</summary>
public static class MermaidPipeline
{
    /// <summary>
    /// The diagram's own stages, then which icon everything naming one is, then a hole wherever something is still to be
    /// written where somebody is writing, then what the block's folds say.
    /// </summary>
    /// <param name="tree">The block as <see cref="MermaidParser.Parse"/> read it, whose header names the diagram.</param>
    /// <param name="holes">Whether somebody is writing in the block.</param>
    /// <param name="grammar">What read the block, where its language names its diagram rather than its first line — see <see cref="MermaidParser.Parse"/>.</param>
    public static IReadOnlyList<IAstStage> Of(ContentNode tree, bool holes, IMermaidGrammar? grammar = null)
    {
        var block = MermaidBlock.Of(tree);
        var stages = new List<IAstStage>();

        if ((grammar ?? MermaidDiagrams.Grammar(block.Diagram)) is { } reading)
        {
            stages.AddRange(reading.Stages(block, holes));
            stages.Add(new WithIcons());
            if (holes) stages.Add(new WithHoles(reading.Holds));
        }

        stages.Add(new WithFolds());
        return stages;
    }
}
