using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A block written in Mermaid's syntax, read and worked over by the engine as the language its header names — for a test about
/// what a diagram's edit handler makes of a place in it, with nothing laid out.
/// </summary>
internal static class MermaidStaged
{
    /// <param name="holes">Whether somebody is writing in the block.</param>
    public static ContentPart Read(string source, bool holes = false) =>
        Laying.Read(Named(source) ?? "flowchart", source, holes).Root;

    /// <summary>
    /// A block read by the language its header names, as a <c>mermaid</c> fence hands it to one — whichever language that is, so
    /// a test holds the parser the app actually reads it with.
    /// </summary>
    public static ContentNode Parsed(string source) =>
        (ContentLanguages.For(Named(source) ?? "flowchart") ?? ContentLanguages.For(MermaidFenceParser.Language))!.Parser()(source).Tree;

    /// <summary><paramref name="source"/> with <paramref name="writing"/> written into it.</summary>
    public static string Written(string source, MermaidWriting writing) =>
        source[..writing.Start] + writing.Text + source[writing.End..];

    /// <summary>The word a block's header starts with.</summary>
    private static string? Named(string source) => MermaidParser.Heading(source)?.Header.Part(Roles.Name)?.Text;
}
