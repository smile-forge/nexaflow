using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
///
/// The <em>prose</em> around a score — its title, its subtitles, the notes and verses underneath — is not drawn
/// into the element at all. Anything painted onto a <see cref="ScoreElement"/> is pixels, and a reader cannot
/// select or copy pixels. So the words stay words: <see cref="Render"/> stacks them as text blocks around the
/// staff, and <see cref="RenderBlocks"/> emits them as real FlowDocument paragraphs, which is what makes them
/// selectable in the markdown editor. Only the notation itself — staff, notes, and the syllables glued under
/// them — is engraved.
/// </summary>
public static class WpfScoreRenderer
{
    /// <summary>True when the Bravura SMuFL font loaded; false means the engraver is drawing its geometric
    /// fallback (still functional, but plain note heads and no glyph clefs).</summary>
    public static bool FontAvailable => Smufl.Available;

    /// <summary>The score as one element: the staff, wrapped in whatever text belongs above and below it.</summary>
    public static FrameworkElement Render(Score score, MarkdownPalette palette)
    {
        var element = new ScoreElement(score, palette);
        var above = new List<TextBlock>();
        var below = new List<TextBlock>();
        Collect(score, palette, above, below);

        if (above.Count == 0 && below.Count == 0) return element;

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var t in above) stack.Children.Add(t);
        stack.Children.Add(element);
        foreach (var t in below) stack.Children.Add(t);
        return stack;
    }

    /// <summary>The score as FlowDocument blocks — the staff in a <see cref="BlockUIContainer"/>, everything
    /// else as ordinary paragraphs the reader can drag-select.</summary>
    public static IReadOnlyList<Block> RenderBlocks(Score score, MarkdownPalette palette)
    {
        var above = new List<TextBlock>();
        var below = new List<TextBlock>();
        Collect(score, palette, above, below);

        var blocks = new List<Block>(above.Count + below.Count + 1);
        foreach (var t in above) blocks.Add(Para(t));
        blocks.Add(new BlockUIContainer(new ScoreElement(score, palette)) { Margin = new Thickness(0) });
        foreach (var t in below) blocks.Add(Para(t));
        return blocks;
    }

    private static Paragraph Para(TextBlock t) => new(new Run(t.Text))
    {
        FontFamily = t.FontFamily,
        FontSize = t.FontSize,
        FontWeight = t.FontWeight,
        FontStyle = t.FontStyle,
        Foreground = t.Foreground,
        TextAlignment = TextAlignment.Center,
        Margin = t.Margin,
    };

    /// <summary>The text that belongs above the staff (title, subtitles) and below it (notes, source,
    /// transcription, verses, and any parser warnings). Centred on the score, which is itself centred in the
    /// column.</summary>
    private static void Collect(Score score, MarkdownPalette palette, List<TextBlock> above, List<TextBlock> below)
    {
        if (!string.IsNullOrWhiteSpace(score.Title))
            above.Add(Line(score.Title!, palette.Text, ScoreMetrics.TitleSize, FontWeights.SemiBold));

        foreach (var sub in score.Subtitles)
            above.Add(Line(sub, palette.Text, ScoreMetrics.SubtitleSize, FontWeights.Normal));

        foreach (var n in score.Notes)
            below.Add(Line($"Notes: {n}", palette.Text, ScoreMetrics.FooterSize, FontWeights.Normal));
        if (!string.IsNullOrWhiteSpace(score.Source))
            below.Add(Line($"Source: {score.Source}", palette.Text, ScoreMetrics.FooterSize, FontWeights.Normal));
        if (!string.IsNullOrWhiteSpace(score.Transcription))
            below.Add(Line($"Transcription: {score.Transcription}", palette.Text, ScoreMetrics.FooterSize, FontWeights.Normal));

        // A blank W: line is a verse break. It stays a break — but as a space, never as an empty string: an
        // empty Run is a paragraph with no text symbols, which is a node the FlowDocument text tree stumbles on.
        foreach (var v in score.Verses)
            below.Add(Line(v.Length == 0 ? " " : v, palette.Text, ScoreMetrics.FooterSize, FontWeights.Normal));

        if (score.Warnings.Count > 0)
            below.Add(Line("⚠ " + string.Join("  ·  ", score.Warnings), palette.TextMuted,
                11, FontWeights.Normal));
    }

    private static TextBlock Line(string text, System.Windows.Media.Brush brush, double size, FontWeight weight) =>
        new()
        {
            Text = text,
            Foreground = brush,
            FontFamily = BlockRenderer.BodyFont,
            FontSize = size,
            FontWeight = weight,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
        };
}
