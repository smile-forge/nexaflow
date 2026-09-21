using System;

namespace Nexaflow.Markdown.Ast;

/// <summary>
/// A part of one content that is a whole other content: the language it is written in, that language's own source,
/// and where that source begins in the document holding both.
///
/// <para>
/// One node in the outer tree, never a parse of the inner language into it. The two are different languages with
/// different grammars, and a tree that mixed them would be neither — so the outer tree says only <em>here is a
/// block of that</em>, and the inner content is read by its own parser into a tree of its own, positioned at
/// <see cref="At"/> so every part of it names the characters it was actually written as.
/// </para>
/// </summary>
/// <param name="Part">The node in the outer tree that is the whole of it, fence and all.</param>
/// <param name="Language">What it says it is written in.</param>
/// <param name="Source">That language's own source, the fence taken off.</param>
/// <param name="At">Where <paramref name="Source"/> begins in the document that holds it.</param>
public readonly record struct ContentLink(ContentPart Part, string Language, string Source, int At)
{
    /// <summary>What opens one.</summary>
    public const string Fence = "```";

    /// <summary>
    /// The content a part is a block of, or null where it is not one.
    ///
    /// <para>
    /// Read off the node and never off the characters under it: the grammar settled which language it is and where
    /// that language's source starts while it had the run in front of it, and put both in the tree. What comes back
    /// is the source exactly as written, because what it works out is an offset into the document and an entity code
    /// is not one character there.
    /// </para>
    /// </summary>
    public static ContentLink? Of(ContentPart? part)
    {
        if (part is not { Kind: Kinds.Nested, Length: > 0 } block) return null;
        if (block.Part(Roles.Name) is not { Length: > 0 } language) return null;
        if (block.Part(Roles.Body) is not { Length: > 0 } source) return null;

        return new ContentLink(block, language.Text, source.Text, source.Start);
    }

    /// <summary>Whether a run of characters opens a block of another language — the cheap question, asked while reading.</summary>
    public static bool Opens(string? text) =>
        text is not null && text.Length > Fence.Length && text.StartsWith(Fence, StringComparison.Ordinal);
}
