using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A run of text as one piece, with a caret position between any two of its letters.
///
/// <para>
/// <strong>One piece, not one per letter.</strong> A piece per character is what a typeset formula has, because
/// every glyph of one is placed by the typesetter and means something on its own. A label is not that: it is a
/// string the type engine shapes, kerns and breaks in one go, and cutting it into pieces afterwards is a piece,
/// a part and a rectangle for every character of every label on the page — to answer questions the run can
/// answer from the text itself.
/// </para>
/// <para>
/// What it answers is where <em>inside</em> it a position is, in characters. Offsets into the source are the
/// caller's: a run knows it stands for a stretch of text, not where that stretch was written. Rectangles come
/// out of it and never go in — they are how it is drawn, at the width it was laid out for, and they mean
/// nothing to anything above the layout.
/// </para>
/// <para>
/// <see cref="Maps"/> is what makes a run editable. A run drawn as what was written has a character for every
/// character of its source, so a position in it is a position in the source; a run showing something worked out
/// — a percentage, a number set to two decimal places — has no such mapping, and a caret cannot go inside it
/// until the content shows the source instead.
/// </para>
/// </summary>
/// <param name="Glyphs">The text as it was set, which is what it is drawn from and measured by.</param>
/// <param name="At">Where it was drawn, in its piece's own frame.</param>
/// <param name="Maps">Whether what is drawn is what was written, character for character.</param>
/// <param name="Writes">
/// Whether pressing it shows what was written so it can be typed into. True for a run that is a formatted view of its
/// own source — a number set to two decimal places, a title without the quotes it was written in. False for one that
/// says something about a part rather than showing it, like the share of a pie a slice takes: there is nothing in it to
/// write, and a press on it means the slice.
/// </param>
public sealed record LayoutWords(FormattedText Glyphs, Point At, bool Maps, bool Writes = false)
{
    /// <summary>How many characters are drawn.</summary>
    public int Length => Glyphs.Text.Length;

    /// <summary>
    /// Which position <paramref name="x"/> means on the first line of the run, in characters from the start — see
    /// <see cref="IndexAt(Point)"/>.
    /// </summary>
    public int IndexAt(double x) => IndexAt(new Point(x, double.NegativeInfinity));

    /// <summary>
    /// Which position <paramref name="at"/> means, in characters from the start: on the line of letters it is level with —
    /// the first, above them all, and the last, below them all — the nearer side of the letter under it, so pressing the left
    /// half of a letter puts the caret before it and the right half after it. Past the end of a line, the end of that line.
    /// </summary>
    public int IndexAt(Point at)
    {
        var letters = new Rect[Length];
        for (var index = 0; index < Length; index++) letters[index] = Box(index);
        if (letters.Length == 0) return 0;

        // A run broken to fit is set as several lines, and every letter on one stands at the same top.
        var line = letters.Min(letter => letter.Y);
        foreach (var letter in letters)
            if (letter.Y <= at.Y) line = Math.Max(line, letter.Y);

        var last = -1;
        for (var index = 0; index < letters.Length; index++)
        {
            var letter = letters[index];
            if (Math.Abs(letter.Y - line) > Level) continue;

            last = index;
            if (at.X > letter.Right) continue;
            return at.X <= letter.X + (letter.Width / 2) ? index : index + 1;
        }

        return last + 1;
    }

    /// <summary>How far apart two letters' tops can be and still be on one line.</summary>
    private const double Level = 0.5;

    /// <summary>Where the caret stands for a position, in the piece's own frame.</summary>
    public Rect Caret(int index)
    {
        if (Length == 0) return new Rect(At.X, At.Y, 0, Glyphs.Height);

        var at = Math.Clamp(index, 0, Length);
        var letter = Box(at == Length ? Length - 1 : at);
        return new Rect(at == Length ? letter.Right : letter.X, letter.Y, 0, letter.Height);
    }

    /// <summary>
    /// What a stretch of it covers, in the piece's own frame — one rectangle, round every line of it the stretch runs over.
    /// Empty where the stretch is empty or falls outside it.
    /// </summary>
    public Rect Covers(int from, int to)
    {
        var start = Math.Clamp(Math.Min(from, to), 0, Length);
        var end = Math.Clamp(Math.Max(from, to), 0, Length);
        if (end <= start) return Rect.Empty;

        return Glyphs.BuildHighlightGeometry(At, start, end - start) is { } shape ? shape.Bounds : Rect.Empty;
    }

    /// <summary>
    /// The word around a position — what a double press picks out. Letters and digits together are a word, and
    /// anything else is a word of its own, which is what makes a press on a decimal point mean the point.
    /// </summary>
    public (int Start, int End) WordAt(int index)
    {
        var text = Glyphs.Text;
        if (text.Length == 0) return (0, 0);

        var at = Math.Clamp(index, 0, text.Length - 1);
        if (!Wordy(text[at]) && at > 0 && Wordy(text[at - 1])) at--;

        if (!Wordy(text[at])) return (at, at + 1);

        var start = at;
        while (start > 0 && Wordy(text[start - 1])) start--;

        var end = at;
        while (end < text.Length && Wordy(text[end])) end++;

        return (start, end);
    }

    private static bool Wordy(char character) => char.IsLetterOrDigit(character);

    /// <summary>Where one letter was drawn, in the piece's own frame.</summary>
    private Rect Box(int index) =>
        Glyphs.BuildHighlightGeometry(At, index, 1) is { } shape && !shape.Bounds.IsEmpty
            ? shape.Bounds
            : new Rect(At.X, At.Y, 0, Glyphs.Height);
}
