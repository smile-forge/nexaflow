using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using Nexaflow.Visuals.Text.Editing;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// The prose around a tune — its title, who wrote it, and the verses under it — engraved into the same
/// tree as the music.
///
/// <para>
/// It used to be WPF text blocks stacked above and below the drawing, on the reasoning that a title is
/// words a reader wants to select and a title painted into a picture is words nobody can reach. The
/// reasoning was right and the remedy was not: an element embedded in a flow document is selected
/// <em>whole</em> or not at all, so the title was as unreachable as it would have been painted, and a
/// drag from the words into the music was two different ideas of what was selected.
/// </para>
/// <para>
/// The tree is not a picture. Every piece of it names the stretch of source it was drawn from, which is
/// exactly what the words needed — so a title engraved here is more selectable than a title in a
/// TextBlock ever was, and one drag can run from the heading through the music to the last verse.
/// </para>
/// </summary>
internal sealed partial class AbcBuilder
{
    /// <summary>Between two lines of the heading, and between two lines of the verses.</summary>
    private const double ProseLine = 0.2 * S;

    /// <summary>Between the prose and the music, either side.</summary>
    private const double ProseGap = 1.1 * S;

    /// <summary>Verses are set in from the staff, because prose flush with it reads as another system.</summary>
    private const double VerseIndent = 2 * S;

    /// <summary>
    /// The heading over the first system: the title, any further titles under it, and the row carrying
    /// the rhythm at the left and the composer at the right. Gives back the room it took, which is where
    /// the music starts.
    /// </summary>
    private double Heading(AbcHeader header, double width)
    {
        var y = 0.0;

        // Inside the margins, not across the whole page: text aligned to a column the width of the paper
        // hangs a right-aligned credit off the edge of it.
        var room = Inside(width);

        if (header.Title is { } title)
            y = Written(title, "title", y, room, TitleSize, TextAlignment.Center,
                        weight: FontWeights.SemiBold);

        foreach (var subtitle in header.Subtitles)
            y = Written(subtitle, "subtitle", y, room, SubtitleSize, TextAlignment.Center);

        // The rhythm at the left and the composer at the right, on one line — which is where an engraver
        // puts them, and why they share a y rather than being stacked.
        if (header.Rhythm is { } rhythm || header.Credit is not null)
        {
            var row = y;

            if (header.Rhythm is { } left)
                y = Math.Max(y, Written(left, "rhythm", row, room, CreditSize, TextAlignment.Left,
                                        style: FontStyles.Italic));

            if (header.Credit is { } right)
                y = Math.Max(y, Written(right, "credit", row, room, CreditSize, TextAlignment.Right));
        }

        return y > 0 ? y + ProseGap : 0;
    }

    /// <summary>
    /// The verses under the last system, in one column or two.
    /// <para>
    /// A blank line among them is the gap between two stanzas, so it takes room and draws nothing.
    /// </para>
    /// </summary>
    private void Verses(AbcHeader header, double width, double top)
    {
        if (header.Footer.Count == 0) return;

        var y = top + ProseGap;
        var room = Inside(width);

        if (ColumnBreak(header.Footer) is not { } split)
        {
            foreach (var line in header.Footer)
                y = Verse(line, y, LeftMargin + VerseIndent, room - VerseIndent);
            return;
        }

        var column = room / 2;
        double left = y, right = y;

        for (var at = 0; at < header.Footer.Count; at++)
            if (at < split)
                left = Verse(header.Footer[at], left, LeftMargin + VerseIndent, column - VerseIndent);
            else
                right = Verse(header.Footer[at], right, LeftMargin + column + VerseIndent,
                                              column - VerseIndent);
    }

    /// <summary>One line of the verses — or the gap between two stanzas, which is a line that is empty.</summary>
    private double Verse(AbcHeader.Prose line, double y, double x, double room) =>
        line.Text.Trim().Length == 0
            ? y + (FooterSize * 0.7)
            : Written(line, "verse", y, room, FooterSize, TextAlignment.Left, at: x);

    /// <summary>The width prose is set in: the page, less the margins the music keeps either side.</summary>
    private static double Inside(double width) => Math.Max(4 * S, width - LeftMargin - RightMargin);

    /// <summary>
    /// Where to break the verses into two columns, or null to leave them in one.
    ///
    /// <para>
    /// Four stanzas of a hymn set one under another is a column of text as tall as the music it belongs
    /// to, and a reader has to scroll away from the tune to read the words to it. Engravers set them in
    /// two, and abcm2ps does it by default — which is why the corpus's own pages are so much shorter than
    /// ours were. The test is the same one it uses: the lines have to be short enough that two columns of
    /// them are still comfortable, and there have to be enough of them to be worth splitting.
    /// </para>
    /// <para>
    /// The break goes at a blank line where there is one near the middle, so a stanza is never cut in
    /// half.
    /// </para>
    /// </summary>
    private static int? ColumnBreak(IReadOnlyList<AbcHeader.Prose> footer)
    {
        const int Enough = 6;
        const int Short = 44;

        if (footer.Count < Enough) return null;
        if (footer.Any(line => line.Text.Length > Short)) return null;

        var middle = (footer.Count + 1) / 2;

        for (var away = 0; away <= footer.Count / 4; away++)
            foreach (var at in new[] { middle - away, middle + away })
                if (at > 0 && at < footer.Count && footer[at - 1].Text.Trim().Length == 0)
                    return at;

        return middle;
    }

    /// <summary>
    /// One line of prose as a piece of the tune, in the face a score sets it in.
    ///
    /// <para>
    /// Where it goes is the only part of this that is about music — a title is centred over the first
    /// system, a composer is right against the margin, a verse is set in from the staff. That the words
    /// become pieces of the tree naming what they were written as is not, and is done by
    /// <see cref="LayoutText.Place"/> for anything that draws words at all.
    /// </para>
    /// </summary>
    /// <returns>The y the next line starts at.</returns>
    private double Written(AbcHeader.Prose prose, string kind, double y, double room,
                           double size, TextAlignment align, double? at = null,
                           FontWeight? weight = null, FontStyle? style = null)
    {
        var glyphs = ScoreText.Build(prose.Text, size, _ppd, weight, style);

        LayoutText.Place(_build, glyphs, new Point(at ?? LeftMargin, y), room, align, prose.Part, kind,
                         Letters(prose));

        // Asked of the type engine after it has been given its room, because that is when it knows: a title
        // too wide for its page is a paragraph, and how tall it is depends on where it broke.
        return y + glyphs.Height + ProseLine;
    }

    /// <summary>
    /// Where each character of a line of prose was written, so it can be selected a letter at a time — or
    /// null where the text is not the source, and no character of one is a character of the other.
    ///
    /// <para>
    /// Worked out here because only the reading knows it. A title is the characters between the colon and
    /// the end of its line; a note field is printed as "Notes: …" and a composer and an origin are set as
    /// one line, and for those two there is no character-by-character answer to give.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ISourcePart>? Letters(AbcHeader.Prose prose) =>
        prose is { IsWritten: true, Part: { } part }
            ? [.. Enumerable.Range(0, prose.Text.Length)
                  .Select(at => (ISourcePart)new SourceSpan(part.Start + at, 1))]
            : null;
}
