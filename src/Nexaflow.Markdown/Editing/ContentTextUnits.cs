using System;

namespace Nexaflow.Markdown.Editing;

/// <summary>The stretches a document is read and moved through a unit at a time — by a screen reader, or anything reading it as one does.</summary>
public enum ContentTextUnit
{
    /// <summary>One character.</summary>
    Character,

    /// <summary>A word, or a run of punctuation, with the space after it (<see cref="ContentWordEdges"/>).</summary>
    Word,

    /// <summary>A line, with its line break.</summary>
    Line,

    /// <summary>The whole document.</summary>
    Document,
}

/// <summary>
/// A document read a unit at a time: the unit holding a place, the start of the next one, and words looked for within a stretch.
///
/// <para>
/// <strong>Never more than the unit is read.</strong> A word is found from the caret's line outward, a line by where it starts and where the
/// next does, and a search a line at a time — so moving through a long document by any of them costs what the unit holds, not the document.
/// </para>
/// </summary>
public static class ContentTextUnits
{
    /// <summary>The <paramref name="unit"/> holding <paramref name="at"/>: a character, a word, a line with its line break, or the whole.</summary>
    public static (int Start, int End) Enclosing(ContentDocument document, int at, ContentTextUnit unit)
    {
        var length = document.Length;

        switch (unit)
        {
            case ContentTextUnit.Character:
                return (at, Math.Min(at + 1, length));

            case ContentTextUnit.Word:
                var after = ContentWordEdges.Next(document, at, forward: true);
                var begins = ContentWordEdges.Next(document, Math.Min(at + 1, length), forward: false);
                var start = begins <= at ? begins : at;
                return (start, Math.Max(after, ContentWordEdges.Next(document, start, forward: true)));

            case ContentTextUnit.Line:
                var line = document.LineOf(at);
                return (document.OffsetOf(line), line + 1 < document.LineCount ? document.OffsetOf(line + 1) : length);

            default:
                return (0, length);
        }
    }

    /// <summary>Where the start of the next <paramref name="unit"/> after <paramref name="at"/> is — or the one before, back — or <paramref name="at"/> at an end.</summary>
    public static int Step(ContentDocument document, int at, ContentTextUnit unit, bool forward)
    {
        switch (unit)
        {
            case ContentTextUnit.Character:
                return Math.Clamp(at + (forward ? 1 : -1), 0, document.Length);

            case ContentTextUnit.Word:
                return ContentWordEdges.Next(document, at, forward);

            case ContentTextUnit.Line:
                var line = document.LineOf(at) + (forward ? 1 : -1);
                return line < 0 ? 0 : line >= document.LineCount ? document.Length : document.OffsetOf(line);

            default:
                return forward ? document.Length : 0;
        }
    }

    /// <summary>
    /// Moves <paramref name="at"/> by up to <paramref name="count"/> units — forward where it is more than nought, back where it is less — and
    /// says how many it went before it reached an end.
    /// </summary>
    public static (int At, int Moved) Stepped(ContentDocument document, int at, ContentTextUnit unit, int count)
    {
        var moved = 0;

        while (moved != count)
        {
            var next = Step(document, at, unit, forward: count > 0);
            if (next == at) break;

            at = next;
            moved += Math.Sign(count);
        }

        return (at, moved);
    }

    /// <summary>
    /// Where <paramref name="text"/> is first written between <paramref name="from"/> and <paramref name="to"/> — the last, looking
    /// <paramref name="backward"/> — or null where it is not. A line at a time: no match runs across a line break, and nothing longer than a
    /// line is read to find one.
    /// </summary>
    public static (int Start, int End)? Found(ContentDocument document, int from, int to, string text, bool backward, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var (first, last) = (document.LineOf(from), document.LineOf(to));

        for (var step = 0; step <= last - first; step++)
        {
            var line = backward ? last - step : first + step;
            var (lineStart, lineEnd) = document.LineAt(document.OffsetOf(line));
            var (low, high) = (Math.Max(lineStart, from), Math.Min(lineEnd, to));
            if (high - low < text.Length) continue;

            var read = document.Slice(low, high - low);
            var at = backward ? read.LastIndexOf(text, comparison) : read.IndexOf(text, comparison);
            if (at >= 0) return (low + at, low + at + text.Length);
        }

        return null;
    }
}
