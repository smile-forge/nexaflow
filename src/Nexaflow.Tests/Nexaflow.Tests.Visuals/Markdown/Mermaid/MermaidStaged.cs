using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A Mermaid block parsed and worked over by its diagram's stages, as the engine works one over — for a test about what a diagram's
/// edit handler makes of a place in it, with nothing laid out.
/// </summary>
internal static class MermaidStaged
{
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static ContentPart Read(string source, bool holes = false)
    {
        // Read by whatever parser the diagram's header names, so a test holds the parser the app actually uses for it.
        var named = MermaidParser.Heading(source)?.Header.Part(Roles.Name)?.Text;
        var tree = MermaidDiagrams.ParserFor(MermaidDiagrams.Named(named))(source);

        return ContentReading.Of(new AstPipeline(MermaidPipeline.Of(tree, holes)).Run(tree)).Root;
    }

    /// <summary><paramref name="source"/> with <paramref name="writing"/> written into it.</summary>
    public static string Written(string source, MermaidWriting writing) =>
        source[..writing.Start] + writing.Text + source[writing.End..];
}
