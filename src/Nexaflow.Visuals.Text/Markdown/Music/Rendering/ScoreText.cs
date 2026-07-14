using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>Prose inside a score — titles, chord symbols, lyrics, tuplet numbers, footer credits. Uses the
/// markdown body face so a score reads as part of the document rather than as a foreign object.</summary>
internal static class ScoreText
{
    public static FormattedText Build(string text, double size, double ppd,
        FontWeight? weight = null, FontStyle? style = null, Brush? brush = null) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BlockRenderer.BodyFont, style ?? FontStyles.Normal, weight ?? FontWeights.Normal,
                FontStretches.Normal),
            size, brush ?? Brushes.Black, ppd);

    public static double Width(string text, double size, double ppd) => Build(text, size, ppd).Width;

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
