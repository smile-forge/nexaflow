using System;
using System.Collections.Generic;

using System.Linq;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Finding a place in a document — for a search, for a line number, or for a reference somebody saved.
///
/// <para>
/// <strong>Looking is done in the source, never in the drawing.</strong> The parser only ever copies, so
/// every character a reader can see is in the source at the offset the tree puts it at — which makes the
/// source the one place the words are whole. In the drawing they are not: a label is broken across pieces
/// wherever the thing drawing it needed a break, a lyric is split by the notes it is sung on, and a word
/// wrapped over a line is two runs. Searching the drawing would find none of those and there is no reason
/// to: the offsets come back out of the source and the tree turns them into places on the page.
/// </para>
/// <para>
/// So there is nothing here for a language to implement. A diagram's own text is in the fence body, which
/// is in the document, and every piece it was laid out as already carries where in the document it came
/// from — which is what <see cref="Editing.LayoutQuery.RangeRects"/> reads.
/// </para>
/// </summary>
public static class MarkdownFind
{
    /// <summary>
    /// Everywhere <paramref name="term"/> is written in <paramref name="source"/>, in the order a reader
    /// would come to them. Case is not something a reader should have to match.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Every(string? source, string? term) =>
        term is { Length: > 0 } looking ? Every(source, Looking(looking)) : [];

    /// <summary>
    /// The same, for a question more complicated than a word — several terms, a pattern, something a search
    /// box worked out. Whatever it is, it is asked of one string, and that string is the source.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Every(
        string? source, Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences)
    {
        if (source is not { Length: > 0 } text || occurrences is null) return [];

        return [.. occurrences(text).Where(one => one.Length > 0).Select(one => (one.Index, one.Length))];
    }

    /// <summary>A plain word, asked the way a reader means it: anywhere in the text, in any case.</summary>
    private static Func<string, IReadOnlyList<(int Index, int Length)>> Looking(string term) =>
        text =>
        {
            var found = new List<(int, int)>();

            for (var at = 0; at <= text.Length - term.Length;)
            {
                var hit = text.IndexOf(term, at, StringComparison.CurrentCultureIgnoreCase);
                if (hit < 0) break;

                found.Add((hit, term.Length));
                at = hit + 1;
            }

            return found;
        };

    /// <summary>
    /// Everywhere <paramref name="term"/> is on the page although it is nowhere in the source — and where in
    /// the source those places stand.
    ///
    /// <para>
    /// A few things are drawn as something other than what was typed: <c>&amp;amp;</c> is drawn as an
    /// ampersand, <c>\*</c> as an asterisk, <c>[!WARNING]</c> as the word Warning, and an item numbered
    /// <c>1.</c> as the number it actually is. A reader searching for what they can see should find those, and
    /// searching the source never will.
    /// </para>
    /// <para>
    /// <strong>Nothing has to be told which those are.</strong> A run already says whether what is drawn is
    /// what was written, because that is what makes a caret possible inside it — so the ones that say no are
    /// exactly the ones to look at, whatever language drew them and whatever they turn out to be. The place
    /// given back is the source the run stands for, so a hit reads like any other.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Shown(Editing.Laid? laid, string? term) =>
        term is { Length: > 0 } looking ? Shown(laid, Looking(looking)) : [];

    /// <summary>The same, for a question more complicated than a word.</summary>
    public static IReadOnlyList<(int Start, int Length)> Shown(
        Editing.Laid? laid, Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences)
    {
        if (laid is null || occurrences is null) return [];

        var found = new List<(int Start, int Length)>();

        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            if (piece.Words is not { Maps: false } words) continue;
            if (occurrences(words.Glyphs.Text).Count == 0) continue;

            var sits = piece.Sits();
            if (sits.Length > 0) found.Add((sits.Start, sits.Length));
        }

        return found;
    }

    /// <summary>
    /// Everywhere a reader would say <paramref name="term"/> appears: where it is written, and where it is
    /// only drawn. In the order they come to them, each place once.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> In(Editing.Laid? laid, string? source, string? term) =>
        term is { Length: > 0 } looking ? In(laid, source, Looking(looking)) : [];

    /// <summary>The same, for a question more complicated than a word.</summary>
    public static IReadOnlyList<(int Start, int Length)> In(
        Editing.Laid? laid, string? source, Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences) =>
        [.. Every(source, occurrences).Concat(Shown(laid, occurrences))
                                      .GroupBy(place => place.Start)
                                      .Select(same => same.First())
                                      .OrderBy(place => place.Start)];

    /// <summary>
    /// Where a line begins and how long it is, counting from one.
    ///
    /// <para>
    /// A number past the end is the last line there is, rather than an empty place after it: a reference to
    /// line 900 of a file somebody has since cut short should land at the end of what is left, which is what
    /// a reader meant by it and what they can actually be shown.
    /// </para>
    /// </summary>
    public static (int Start, int Length) Line(string? source, int number)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return (0, 0);

        var wanted = Math.Max(number, 1);
        var at = 0;
        var last = 0;

        for (var line = 1; line < wanted; line++)
        {
            var ends = text.IndexOf('\n', at);
            if (ends < 0) break;

            last = at;
            at = ends + 1;
        }

        // Past the end of a file that ends in a line ending, there is a place with nothing in it. The line
        // a reader was asking for is the one before it.
        return Ending(text, at < text.Length ? at : last);
    }

    /// <summary>Which line an offset is on, counting from one — the other way round, for saving a reference.</summary>
    public static int LineAt(string? source, int offset)
    {
        var text = source ?? string.Empty;
        var stop = Math.Clamp(offset, 0, text.Length);
        var line = 1;

        for (var at = 0; at < stop; at++)
            if (text[at] == '\n') line++;

        return line;
    }

    /// <summary>
    /// Where a saved reference leads in this document — or, where it no longer leads all the way, as far as
    /// it still does. A deep link into a section somebody has reorganised should still land in the section.
    /// </summary>
    public static (int Start, int Length)? Followed(ContentPart? root, ContentPath path)
    {
        if (path.In(root) is not { } found) return null;

        var written = found.Written;

        return written.Length > 0 ? written : (found.Start, found.Length);
    }

    private static (int Start, int Length) Ending(string text, int at)
    {
        var from = Math.Clamp(at, 0, text.Length);
        var ends = text.IndexOf('\n', from);

        if (ends < 0) return (from, text.Length - from);

        var stop = ends > from && text[ends - 1] == '\r' ? ends - 1 : ends;

        return (from, stop - from);
    }
}
