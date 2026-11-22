using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What a <c>mermaid</c> fence holds: a diagram, written in the language its header names. Every diagram is a language of its
/// own, and a <c>mermaid</c> fence only says the block's header names which — so this reads no further than the header, and
/// holds the whole block as written in that language (<see cref="ContentNested"/>), for the engine to have it read there.
///
/// <para>
/// Which words name a language a <c>mermaid</c> block may be written in is the languages' to say, not this: the parser is
/// handed what holds a word, and asks it. Nothing of this reading is drawn: the diagram is. A header naming no such language is
/// a parse error, and the one thing this reads for itself — the header, its keyword marked as naming nothing, amid the block
/// as written — so the block is shown as written with the wave under the keyword and why beneath.
/// </para>
/// </summary>
public static class MermaidFenceParser
{
    /// <summary>The word naming what a <c>mermaid</c> block is written in, where its header names no diagram.</summary>
    public const string Language = "mermaid";

    /// <summary>What the diagram a block with no header yet is written in.</summary>
    private const string Unheaded = "flowchart";

    /// <param name="holds">Whether a language a <c>mermaid</c> block may be written in reads the word its header starts with.</param>
    public static ContentNode Parse(string? source, Func<string, bool> holds)
    {
        source ??= string.Empty;

        if (MermaidParser.Heading(source) is not var (at, header)) return Holding(source, Unheaded);

        var keyword = header.Part(Roles.Name);
        if (keyword is not null && holds(keyword.Text)) return Holding(source, keyword.Text);

        // What the header says is what went wrong, and it says so on the word naming nothing; the rest is only the block as written.
        var named = keyword is null
            ? header
            : header.With([.. header.Children.Select(child => ReferenceEquals(child, keyword)
                ? ContentNode.Leaf(MermaidKinds.Keyword, keyword.Text, Roles.Name, $"'{keyword.Text}' is not a diagram Nexaflow draws.", keyword.Offset)
                : child)]);

        var end = at + header.Width;
        return new BlockNode(Language,
        [
            .. at > 0 ? [ContentNode.Leaf(Kinds.Verbatim, source[..at])] : Array.Empty<ContentNode>(),
            named,
            .. end < source.Length ? [ContentNode.Leaf(Kinds.Verbatim, source[end..])] : Array.Empty<ContentNode>(),
        ], MermaidKinds.Block);
    }

    /// <summary>The whole of <paramref name="source"/>, held as written in the diagram <paramref name="language"/> names.</summary>
    private static ContentNode Holding(string source, string language) =>
        new BlockNode(language, [ContentNode.Leaf(Kinds.Nested, source, Roles.Body)]);
}
