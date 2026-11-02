using System;
using System.Collections.Generic;

using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;

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
/// from — which is what <see cref="LayoutQuery.RangeRects"/> reads.
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
    public static IReadOnlyList<(int Start, int Length)> Shown(Laid? laid, string? term) =>
        term is { Length: > 0 } looking ? Shown(laid, Looking(looking)) : [];

    /// <summary>The same, for a question more complicated than a word.</summary>
    public static IReadOnlyList<(int Start, int Length)> Shown(
        Laid? laid, Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences)
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
    public static IReadOnlyList<(int Start, int Length)> In(Laid? laid, string? source, string? term) =>
        term is { Length: > 0 } looking ? In(laid, source, Looking(looking)) : [];

    /// <summary>The same, for a question more complicated than a word.</summary>
    public static IReadOnlyList<(int Start, int Length)> In(
        Laid? laid, string? source, Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences) =>
        [.. Every(source, occurrences).Concat(Shown(laid, occurrences))
                                      .GroupBy(place => place.Start)
                                      .Select(same => same.First())
                                      .OrderBy(place => place.Start)];

    /// <summary>
    /// Everywhere a reader would say <paramref name="term"/> appears in content whose source is formatting nobody reads
    /// (<see cref="IContentLanguage.ReadsAsDrawn"/>): looked for in what <paramref name="drawn"/> reads as, and given back as the
    /// source it stands for.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Drawn(DrawnText drawn, string? term) =>
        term is { Length: > 0 } looking ? drawn.Every(Looking(looking)) : [];

    /// <summary>
    /// Where a line of <paramref name="document"/> begins and how long it is without its line ending, counting from one — asked of the
    /// document's line index (<see cref="ContentDocument.OffsetOf"/>), never by reading what comes before it, so going to a line of a
    /// document megabytes long costs what going to its first does.
    ///
    /// <para>
    /// A number past the end is the last line there is, rather than an empty place after it: a reference to
    /// line 900 of a file somebody has since cut short should land at the end of what is left, which is what
    /// a reader meant by it and what they can actually be shown.
    /// </para>
    /// </summary>
    public static (int Start, int Length) Line(ContentDocument document, int number)
    {
        var line = Math.Clamp(number, 1, document.LineCount) - 1;

        // Past the end of a file that ends in a line ending, there is a place with nothing in it. The line
        // a reader was asking for is the one before it.
        if (line > 0 && document.OffsetOf(line) == document.Length) line--;

        var (start, end) = document.LineAt(document.OffsetOf(line));

        return (start, end - start);
    }

    /// <summary>Which line of <paramref name="document"/> an offset is on, counting from one — the other way round, for saving a reference.</summary>
    public static int LineAt(ContentDocument document, int offset) => document.LineOf(offset) + 1;
}
