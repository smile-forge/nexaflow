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
    public static ContentNode Read(string? source, bool holes = false)
    {
        var tree = MermaidParser.Parse(source);
        return new AstPipeline(MermaidPipeline.Of(tree, holes)).Run(tree);
    }
}
