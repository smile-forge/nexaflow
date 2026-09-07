using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>Prose inside a score — titles, chord symbols, lyrics, tuplet numbers, footer credits. Uses the
/// markdown body face so a score reads as part of the document rather than as a foreign object.</summary>
internal static class ScoreText
{
    /// <summary>
    /// The face chord symbols are set in — a serif, where everything else on the page is the document's
    /// sans.
    ///
    /// <para>
    /// They need to be told apart from the lyrics at a glance, and on a hymn they are two rows apart in the
    /// same size and the same face: a reader scanning for the next chord finds a syllable. Engravers have
    /// always solved this by setting chords in a different face, and it costs nothing here.
    /// </para>
    /// </summary>
    public static readonly FontFamily ChordFont = new("Times New Roman, Georgia, serif");

    public static FormattedText Build(string text, double size, double ppd,
        FontWeight? weight = null, FontStyle? style = null, Brush? brush = null,
        FontFamily? family = null) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(family ?? BlockRenderer.BodyFont, style ?? FontStyles.Normal,
                weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush ?? Brushes.Black, ppd);

    public static double Width(string text, double size, double ppd) => Build(text, size, ppd).Width;

    /// <summary>
    /// A chord symbol: its letter set in the chord face, its accidental set as the symbol it means and
    /// left at a normal weight.
    ///
    /// <para>
    /// <c>Bb</c> is not a B followed by the letter b — it is B flat, and an engraver draws the flat. Left
    /// as a letter it reads as part of the name, and set bold along with the letter it reads as a blob:
    /// ♭ and ♯ are drawn from thin strokes and a semi-bold face fills them in. So the substitution and the
    /// weight are one decision, made here, where the chord's face is already decided.
    /// </para>
    /// </summary>
    public static FormattedText Chord(string text, double size, double ppd, Brush? brush = null)
    {
        var written = new System.Text.StringBuilder(text.Length);
        var signs = new List<int>();

        for (var at = 0; at < text.Length; at++)
        {
            // An accidental is one that follows a note letter, or another accidental — so `Bb` is B flat
            // and `Bbm` is B flat minor, while the b of a word is left alone.
            var after = written.Length > 0 ? written[^1] : '\0';
            var isNote = after is >= 'A' and <= 'G' or '♭' or '♯';

            if (isNote && text[at] is 'b' or '#')
            {
                signs.Add(written.Length);
                written.Append(text[at] == 'b' ? '♭' : '♯');
                continue;
            }

            written.Append(text[at]);
        }

        var glyphs = Build(written.ToString(), size, ppd, FontWeights.SemiBold, brush: brush,
                           family: ChordFont);

        foreach (var at in signs) glyphs.SetFontWeight(FontWeights.Normal, at, 1);

        return glyphs;
    }

    /// <summary>Draws text anchored horizontally by <paramref name="align"/>, with <paramref name="y"/> the top.</summary>
    public static void Draw(DrawingContext dc, string text, Point anchor, double size, Brush brush, double ppd,
        TextAlignment align = TextAlignment.Left, FontWeight? weight = null, FontStyle? style = null)
    {
        var ft = Build(text, size, ppd, weight, style, brush);
        double x = align switch
        {
            TextAlignment.Center => anchor.X - ft.Width / 2,
            TextAlignment.Right  => anchor.X - ft.Width,
            _                    => anchor.X,
        };
        dc.DrawText(ft, new Point(x, anchor.Y));
    }
}
