using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// A Mermaid block parsed and worked over by its diagram's stages, as the engine works one over — for a test about what the
/// grammar and the stages make of it, with nothing laid out.
/// </summary>
internal static class MermaidStaged
{
    /// <param name="holes">Whether somebody is writing in the block.</param>
    /// <param name="grammar">What reads the block, where its language names its diagram rather than its first line.</param>
    public static ContentNode Read(string? source, bool holes = false, IMermaidGrammar? grammar = null)
    {
        var tree = MermaidParser.Parse(source, grammar);
        return new AstPipeline(MermaidPipeline.Of(tree, holes, grammar)).Run(tree);
    }
}
