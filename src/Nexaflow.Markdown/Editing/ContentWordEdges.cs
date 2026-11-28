using System;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// Where Ctrl+Left and Ctrl+Right take a caret, and how much Ctrl+Backspace and Ctrl+Delete take out: to the next start of a word, a run
/// of punctuation, or a line — as a text editor moves by words.
///
/// <para>
/// <strong>Only the caret's line is read</strong>, a character at a time from the caret outward, so a step costs what the word it
/// crosses holds however long the document or the line is. A line's end is a stop of its own: from it the next step goes to the start
/// of the next line, and back from a line's start to the end of the one before.
/// </para>
/// </summary>
public static class ContentWordEdges
{
    /// <summary>What a character is to a step by words: part of a word, space, or punctuation.</summary>
    private enum Sort
    {
        Word,
        Space,
        Mark,
    }

    /// <summary>Where a step by words from <paramref name="offset"/> lands, forward or back.</summary>
    public static int Next(ContentDocument document, int offset, bool forward)
    {
        offset = Math.Clamp(offset, 0, document.Length);
        var (start, end) = document.LineAt(offset);

        return forward ? Forward(document, offset, end) : Back(document, offset, start);
    }

    private static int Forward(ContentDocument document, int offset, int end)
    {
        // At the end of a line, the step is over its line break.
        if (offset >= end) return offset >= document.Length ? offset : AfterBreak(document, offset);

        var at = offset;
        var sort = Of(document[at]);

        if (sort != Sort.Space)
            while (at < end && Of(document[at]) == sort) at++;

        while (at < end && Of(document[at]) == Sort.Space) at++;

        return at;
    }

    private static int Back(ContentDocument document, int offset, int start)
    {
        // At the start of a line, the step is back over the line break in front of it.
        if (offset <= start) return offset <= 0 ? 0 : BeforeBreak(document, offset);

        var at = offset;
        while (at > start && Of(document[at - 1]) == Sort.Space) at--;
        if (at == start) return at;

        var sort = Of(document[at - 1]);
        while (at > start && Of(document[at - 1]) == sort) at--;

        return at;
    }

    private static int AfterBreak(ContentDocument document, int offset) =>
        document[offset] == '\r' && offset + 1 < document.Length && document[offset + 1] == '\n' ? offset + 2 : offset + 1;

    private static int BeforeBreak(ContentDocument document, int offset) =>
        document[offset - 1] == '\n' && offset >= 2 && document[offset - 2] == '\r' ? offset - 2 : offset - 1;

    private static Sort Of(char character) =>
        char.IsLetterOrDigit(character) || character == '_' ? Sort.Word
        : char.IsWhiteSpace(character) ? Sort.Space
        : Sort.Mark;
}
