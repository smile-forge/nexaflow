using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Syntax;

/// <summary>
/// A piece of source with its CRLF line breaks read as LF, and the way back to where each character really is.
/// <para>
/// A search written on a command line — an escaped <c>\n</c>, a heredoc, a file saved on another machine —
/// arrives with LF breaks, and a checkout with <c>core.autocrlf</c> set keeps CRLF. Matched on the raw text,
/// nothing spanning two lines could ever be found, and the refusal read as though the fragment were wrong.
/// Matching here and splicing through <see cref="Splice"/> keeps the edit to exactly the characters matched:
/// a line the edit does not reach keeps the ending it had, even in a file that mixes them.
/// </para>
/// </summary>
internal sealed class LineBreakView
{
    private readonly string _original;
    private readonly int[] _toOriginal;   // one entry per character of Text, plus one for its end

    private LineBreakView(string original, string text, int[] toOriginal)
    {
        _original   = original;
        _toOriginal = toOriginal;
        Text        = text;
    }

    /// <summary>The text with every CRLF read as a single LF. A lone CR is left as it is.</summary>
    public string Text { get; }

    public static LineBreakView Of(string original)
    {
        var text = new StringBuilder(original.Length);
        var map  = new List<int>(original.Length + 1);

        for (var i = 0; i < original.Length; i++)
        {
            // The LF stands for both characters, and maps to the CR: a match that begins at the break takes
            // the whole break with it, and one that ends just before it leaves the whole break behind.
            if (original[i] == '\r' && i + 1 < original.Length && original[i + 1] == '\n')
            {
                map.Add(i);
                text.Append('\n');
                i++;
                continue;
            }
            map.Add(i);
            text.Append(original[i]);
        }
        map.Add(original.Length);

        return new LineBreakView(original, text.ToString(), [.. map]);
    }

    /// <summary>Where the character at <paramref name="offset"/> in <see cref="Text"/> sits in the original.</summary>
    public int ToOriginal(int offset) => _toOriginal[Math.Clamp(offset, 0, _toOriginal.Length - 1)];

    /// <summary>
    /// The original with each range of <see cref="Text"/> replaced. Replacements are written with LF breaks and
    /// leave with <paramref name="newline"/>; ranges must not overlap.
    /// </summary>
    public string Splice(IEnumerable<(int Start, int End, string Text)> edits, string newline)
    {
        var sorted = new List<(int Start, int End, string Text)>(edits);
        sorted.Sort((x, y) => x.Start.CompareTo(y.Start));

        var sb   = new StringBuilder(_original.Length);
        var from = 0;
        foreach (var (start, end, replacement) in sorted)
        {
            var at = ToOriginal(start);
            sb.Append(_original, from, at - from);
            sb.Append(newline == "\n" ? replacement : replacement.Replace("\n", newline));
            from = ToOriginal(end);
        }
        sb.Append(_original, from, _original.Length - from);
        return sb.ToString();
    }
}
