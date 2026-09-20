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
    /// Read off the characters as they were written, never off what they decode to, because what it works out is an
    /// offset into the source and an entity code is not one character there.
    /// </para>
    /// </summary>
    public static ContentLink? Of(ContentPart? part)
    {
        if (part is not { Length: > 0 } written) return null;

        // What it prints as, not what its own node says: the node holding a block is a branch, and the characters
        // are in the leaf under it.
        var text = written.Print();
        if (!text.StartsWith(Fence, StringComparison.Ordinal)) return null;

        var named = Fence.Length;
        while (named < text.Length && !char.IsWhiteSpace(text[named])) named++;

        var language = text[Fence.Length..named];
        if (language.Length == 0) return null;

        var at = named;
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        if (at >= text.Length) return null;

        return new ContentLink(written, language, text[at..], written.Start + at);
    }

    /// <summary>Whether a run of characters opens a block of another language — the cheap question, asked while reading.</summary>
    public static bool Opens(string? text) =>
        text is not null && text.Length > Fence.Length && text.StartsWith(Fence, StringComparison.Ordinal);
}
