using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.IO.Common;

/// <summary>
/// Pure, WPF-free line transforms behind the editor's "Lines" commands. Each operates on the whole
/// document text and re-joins with the input's dominant terminator, so a transform on a CRLF file stays
/// CRLF. Isolated here so they're unit-testable and reusable across viewers.
/// </summary>
public static class TextTransforms
{
    /// <summary>Drops empty lines. With <paramref name="whitespaceIsEmpty"/>, a line of only spaces/tabs also counts as empty.</summary>
    public static string RemoveEmptyLines(string text, bool whitespaceIsEmpty = true)
    {
        var lines = SplitLines(text, out var trailing);
        var kept = lines.Where(l => (whitespaceIsEmpty ? l.AsSpan().Trim().Length : l.Length) > 0).ToList();
        return Join(kept, DominantSeparator(text), trailing && kept.Count > 0);
    }

    /// <summary>Removes duplicate lines, keeping the first occurrence and preserving order. Global by default;
    /// <paramref name="adjacentOnly"/> collapses only consecutive runs (uniq-style).</summary>
    public static string RemoveDuplicateLines(string text, bool adjacentOnly = false, bool ignoreCase = false)
    {
        var lines = SplitLines(text, out var trailing);
        var cmp = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        List<string> kept;
        if (adjacentOnly)
        {
            kept = new List<string>(lines.Count);
            string? prev = null;
            foreach (var l in lines)
            {
                if (prev is null || !cmp.Equals(prev, l)) kept.Add(l);
                prev = l;
            }
        }
        else
        {
            var seen = new HashSet<string>(cmp);
            kept = lines.Where(seen.Add).ToList();
        }

        return Join(kept, DominantSeparator(text), trailing && kept.Count > 0);
    }

    /// <summary>Stable ordinal sort of the lines.</summary>
    public static string SortLines(string text, bool descending = false, bool ignoreCase = false)
    {
        var lines = SplitLines(text, out var trailing);
        var cmp = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var sorted = (descending ? lines.OrderByDescending(l => l, cmp) : lines.OrderBy(l => l, cmp)).ToList();
        return Join(sorted, DominantSeparator(text), trailing);
    }

    /// <summary>Rewrites every terminator to <paramref name="eol"/>. <see cref="LineEnding.Preserve"/> is a no-op.</summary>
    public static string NormalizeLineEndings(string text, LineEnding eol)
    {
        if (eol == LineEnding.Preserve) return text;
        var lines = SplitLines(text, out var trailing);
        return Join(lines, eol.ToSequence(), trailing);
    }

    /// <summary>Splits on \r\n, \r, or \n (terminators stripped). <paramref name="trailingNewline"/> reports whether
    /// the text ended with a terminator, so <see cref="Join"/> can round-trip the final newline.</summary>
    public static List<string> SplitLines(string text, out bool trailingNewline)
    {
        var lines = new List<string>();
        trailingNewline = false;
        if (text.Length == 0) return lines;

        var start = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '\r' or '\n')
            {
                lines.Add(text.Substring(start, i - start));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; // consume the \n of a CRLF pair
                i++;
                start = i;
                if (i == text.Length) trailingNewline = true;
            }
            else i++;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    private static string Join(List<string> lines, string separator, bool trailingNewline)
    {
        if (lines.Count == 0) return string.Empty;
        var joined = string.Join(separator, lines);
        return trailingNewline ? joined + separator : joined;
    }

    /// <summary>Classifies the terminators present in <paramref name="text"/> for a status indicator: a single
    /// kind, <see cref="LineEndingKind.Mixed"/> when more than one appears, or <see cref="LineEndingKind.None"/>
    /// when there are none. Early-exits as soon as a second kind is seen.</summary>
    public static LineEndingKind DetectLineEnding(string text)
    {
        LineEndingKind found = LineEndingKind.None;
        for (var i = 0; i < text.Length; i++)
        {
            LineEndingKind here;
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { here = LineEndingKind.CrLf; i++; }
                else here = LineEndingKind.Cr;
            }
            else if (text[i] == '\n') here = LineEndingKind.Lf;
            else continue;

            if (found == LineEndingKind.None) found = here;
            else if (found != here) return LineEndingKind.Mixed;
        }
        return found;
    }

    /// <summary>The most common terminator in the text; LF when there are none (CRLF wins ties).</summary>
    public static string DominantSeparator(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }

        if (crlf > 0 && crlf >= lf && crlf >= cr) return "\r\n";
        if (cr > lf && cr > 0) return "\r";
        return "\n";
    }
}
