using System.Windows;
using System.Windows.Controls;
using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// The single native-WPF sheet-music engraver: turns a <see cref="Score"/> into a themed
/// <see cref="FrameworkElement"/> drawn with the Bravura SMuFL font (glyphs) plus WPF geometry (staff lines,
/// stems, beams, bar lines, slurs). Both notation parsers target the same <see cref="Score"/> IR, so this is the
/// one place engraving lives. All ink is the palette's text brush — no hard-coded colours.
///
/// The work is split three ways: <see cref="ScoreLayoutEngine"/> decides where everything goes,
/// <see cref="ScorePainter"/> draws it, and <see cref="ScoreElement"/> hosts the result and owns selection.
/// </summary>
public static class WpfScoreRenderer
{
    /// <summary>True when the Bravura SMuFL font loaded; false means the engraver is drawing its geometric
    /// fallback (still functional, but plain note heads and no glyph clefs).</summary>
    public static bool FontAvailable => Smufl.Available;

    public static FrameworkElement Render(Score score, MarkdownPalette palette)
    {
        var element = new ScoreElement(score, palette);
        if (score.Warnings.Count == 0) return element;

        var stack = new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(element);
        stack.Children.Add(new TextBlock
        {
            Text         = "⚠ " + string.Join("  ·  ", score.Warnings),
            Foreground   = palette.TextMuted,
            FontFamily   = BlockRenderer.BodyFont,
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(2, 4, 0, 0),
        });
        return stack;
    }
}
