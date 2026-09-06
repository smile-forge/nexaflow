using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// A whole tune on the page: its title and credits, the engraved music, and whatever is printed under it.
///
/// <para>
/// <strong>The prose is text and stays text.</strong> A title painted into the drawing is words nobody can
/// select, and the words around a tune — its name, who wrote it, where it was collected, the verses that
/// do not fit under the notes — are exactly the ones a reader wants to copy. So they are real
/// <see cref="TextBlock"/>s stacked around the score rather than marks inside it.
/// </para>
/// <para>
/// The syllables under the notes are the exception, and deliberately: they are glued to head positions, so
/// they are part of the drawing.
/// </para>
/// </summary>
public sealed class AbcScore : StackPanel
{
    public AbcScore(string abc, MarkdownPalette palette, int sourceStart)
    {
        Score = new AbcElement(abc, palette) { SourceStart = sourceStart };

        var header = AbcHeader.Of(ContentReading.Of(AbcParser.Parse(abc)));
        HorizontalAlignment = HorizontalAlignment.Stretch;

        foreach (var line in Above(header, palette)) Children.Add(line);
        Children.Add(Score);
        foreach (var line in Below(header, palette)) Children.Add(line);
    }

    /// <summary>The engraved music — what the caret enters and what an edit changes.</summary>
    public AbcElement Score { get; }

    /// <summary>Title, subtitles, and the credit row that sits over the first system.</summary>
    private static IEnumerable<FrameworkElement> Above(AbcHeader header, MarkdownPalette palette)
    {
        if (header.Title is { Length: > 0 } title)
            yield return Line(title, ScoreMetrics.TitleSize, palette, TextAlignment.Center, FontWeights.SemiBold);

        foreach (var subtitle in header.Subtitles)
            yield return Line(subtitle, ScoreMetrics.SubtitleSize, palette, TextAlignment.Center);

        if (header.Rhythm is null && header.Credit is null) yield break;

        // The rhythm at the left and the composer at the right, on one line — which is where an engraver
        // puts them, and why they are a grid rather than two stacked blocks.
        var credits = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        credits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        credits.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (header.Rhythm is { Length: > 0 } rhythm)
        {
            var left = Line(rhythm, ScoreMetrics.CreditSize, palette, TextAlignment.Left, style: FontStyles.Italic);
            Grid.SetColumn(left, 0);
            credits.Children.Add(left);
        }

        if (header.Credit is { Length: > 0 } credit)
        {
            var right = Line(credit, ScoreMetrics.CreditSize, palette, TextAlignment.Right);
            Grid.SetColumn(right, 1);
            credits.Children.Add(right);
        }

        yield return credits;
    }

    /// <summary>Whatever is printed under the last system: the source, the transcription, the verses.</summary>
    private static IEnumerable<FrameworkElement> Below(AbcHeader header, MarkdownPalette palette)
    {
        foreach (var line in header.Footer)
            yield return Line(line, ScoreMetrics.FooterSize, palette, TextAlignment.Left);
    }

    private static TextBlock Line(string text, double size, MarkdownPalette palette, TextAlignment align,
                                  FontWeight? weight = null, FontStyle? style = null) =>
        new()
        {
            Text = text,
            FontFamily = BlockRenderer.BodyFont,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            FontStyle = style ?? FontStyles.Normal,
            Foreground = palette.Text,
            TextAlignment = align,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1),
        };
}
