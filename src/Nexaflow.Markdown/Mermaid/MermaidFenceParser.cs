using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What a <c>mermaid</c> fence holds: a diagram, written in the language its header names. Every diagram is a language of its
/// own, and a <c>mermaid</c> fence only says the block's header names which — so this reads no further than the header, and
/// holds the whole block as written in that language (<see cref="ContentNested"/>), for the engine to have it read there.
///
/// <para>
/// Nothing of this reading is drawn: the diagram is. A header naming no diagram is a parse error, and the one thing this reads
/// for itself — the header, its keyword marked as naming nothing, amid the block as written — so the block is shown as written
/// with the wave under the keyword and why beneath.
/// </para>
/// </summary>
public static class MermaidFenceParser
{
    /// <summary>The word naming what a <c>mermaid</c> block is written in, where its header names no diagram.</summary>
    public const string Language = "mermaid";

    /// <summary>What the diagram a block with no header yet is written in.</summary>
    private const string Unheaded = "flowchart";

    public static ContentNode Parse(string? source)
    {
        source ??= string.Empty;

        if (MermaidParser.Heading(source) is not var (at, header)) return Holding(source, Unheaded);

        if (header.Part(Roles.Name) is { } keyword && MermaidDiagrams.Named(keyword.Text) != MermaidDiagram.Unknown)
            return Holding(source, keyword.Text);

        // What the header says is what went wrong, and it says so where it is written; the rest is only the block as written.
        var end = at + header.Width;
        return new BlockNode(Language,
        [
            .. at > 0 ? [ContentNode.Leaf(Kinds.Verbatim, source[..at])] : Array.Empty<ContentNode>(),
            header,
            .. end < source.Length ? [ContentNode.Leaf(Kinds.Verbatim, source[end..])] : Array.Empty<ContentNode>(),
        ], MermaidKinds.Block);
    }

    /// <summary>The whole of <paramref name="source"/>, held as written in the diagram <paramref name="language"/> names.</summary>
    private static ContentNode Holding(string source, string language) =>
        new BlockNode(language, [ContentNode.Leaf(Kinds.Nested, source, Roles.Body)]);
}
