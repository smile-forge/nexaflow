using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// Finding what a search asks for in content laid line by line — code, text — a line at a time, so no document is made one string to be
/// searched; and finding it again after an edit only on the lines the edit touched, every other match carried where it now stands.
///
/// <para>
/// What a match is stays the asker's (<c>occurrences</c>: a word, whole words, wildcards, a pattern), asked of one line at a time, so no
/// match runs across a line break — as a text editor's search does not.
/// </para>
/// </summary>
public static class ContentSearch
{
    /// <summary>
    /// Every match <paramref name="occurrences"/> finds on lines <paramref name="first"/> to <paramref name="last"/> of
    /// <paramref name="document"/>, counting from nought, in order — every line, where no range is said.
    /// </summary>
    public static List<(int Start, int Length)> Lines(ContentDocument document, Func<string, IReadOnlyList<(int Index, int Length)>> occurrences,
                                                      int first = 0, int last = int.MaxValue, CancellationToken cancel = default)
    {
        var found = new List<(int Start, int Length)>();
        var end = Math.Min(last, document.LineCount - 1);

        for (var line = Math.Max(first, 0); line <= end; line++)
        {
            cancel.ThrowIfCancellationRequested();

            var (start, stop) = document.LineAt(document.OffsetOf(line));
            var text = document.Slice(start, stop - start).TrimEnd('\r', '\n');
            if (text.Length == 0) continue;

            foreach (var (index, length) in occurrences(text))
                if (length > 0) found.Add((start + index, length));
        }

        return found;
    }

    /// <summary>
    /// What was <paramref name="found"/> in <paramref name="before"/>, found again in <paramref name="after"/>: the lines the change between
    /// them touched searched again, every match on another line carried to where it now stands — or all of it searched again, where the
    /// two are not one change apart.
    /// </summary>
    public static List<(int Start, int Length)> Again(ContentDocument before, ContentDocument after, IReadOnlyList<(int Start, int Length)> found,
                                                      Func<string, IReadOnlyList<(int Index, int Length)>> occurrences)
    {
        if (ContentDocument.Between(before, after) is not { } change) return Lines(after, occurrences);
        if (change.Writes.Count == 0) return [.. found];

        // The lines the writes touched, whole, as they stood and as they stand.
        var (from, _) = before.LineAt(change.Writes.Min(write => write.Start));
        var (_, to) = before.LineAt(change.Writes.Max(write => write.End));
        var (first, last) = (after.LineOf(change.Carried(from)), after.LineOf(change.Carried(to, leansAfter: true)));
        var grown = after.Length - before.Length;

        var again = new List<(int Start, int Length)>(found.Count + 4);

        foreach (var match in found)
            if (match.Start < from) again.Add(match);

        again.AddRange(Lines(after, occurrences, first, last));

        foreach (var match in found)
            if (match.Start > to) again.Add((match.Start + grown, match.Length));

        return again;
    }
}
