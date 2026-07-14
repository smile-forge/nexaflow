using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Parsers;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// Turns a <see cref="MusicBlock"/> into a rendered WPF element: picks the parser for the block's
/// <see cref="MusicDialect"/>, parses to the shared <see cref="Score"/> IR, then hands it to the single
/// native engraver (<see cref="WpfScoreRenderer"/>). Parallels <c>DiagramRenderer</c> — never throws;
/// any failure (or an empty/unsupported score) degrades to a themed source-text box so the notation is
/// still visible.
/// </summary>
public static class MusicRenderer
{
    public static FrameworkElement Render(MusicBlock block, MarkdownRenderContext? context = null)
    {
        var ctx = context ?? (MarkdownRenderContext)MarkdownPalette.FromTheme();
        var p   = ctx.Palette;
        try
        {
            var score = Parse(block);
            if (score is null || score.IsEmpty)
                return SourceFallback(block.Dialect, block.Source, p,
                    score?.Warnings.Count > 0 ? string.Join("  ·  ", score.Warnings) : "nothing to render");

            return WpfScoreRenderer.Render(score, p);
        }
        catch (Exception ex)
        {
            return SourceFallback(block.Dialect, block.Source, p, ex.Message);
        }
    }

    /// <summary>The FlowDocument form: the staff as an embedded element, but the score's prose — title,
    /// subtitles, the notes underneath — as real paragraphs, so it drag-selects like any other text.</summary>
    public static IEnumerable<Block> RenderBlocks(MusicBlock block, MarkdownRenderContext ctx)
    {
        var p = ctx.Palette;
        Score? score;
        try
        {
            score = Parse(block);
        }
        catch (Exception ex)
        {
            return [Embed(SourceFallback(block.Dialect, block.Source, p, ex.Message))];
        }

        if (score is null || score.IsEmpty)
            return [Embed(SourceFallback(block.Dialect, block.Source, p,
                score?.Warnings.Count > 0 ? string.Join("  ·  ", score.Warnings) : "nothing to render"))];

        return WpfScoreRenderer.RenderBlocks(score, p);
    }

    private static Block Embed(FrameworkElement fe) => new BlockUIContainer(fe) { Margin = new Thickness(0) };

    private static Score? Parse(MusicBlock block) => block.Dialect switch
    {
        MusicDialect.Abc      => new AbcParser().Parse(block.Source),
        MusicDialect.LilyPond => new LilyPondParser().Parse(block.Source),
        _                     => null,
    };

    // ── Source-text fallback ────────────────────────────────────────────────

    /// <summary>A themed box showing the raw notation source (plus an optional note). Used when the
    /// notation can't be engraved — the reader still sees what they wrote. Colours come from the palette
    /// (no literals), matching the "features never hard-code colours" rule.</summary>
    internal static FrameworkElement SourceFallback(MusicDialect dialect, string source, MarkdownPalette p, string? note)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        string label = dialect == MusicDialect.Abc ? "♪ ABC notation" : "♪ LilyPond";
        stack.Children.Add(new TextBlock
        {
            Text       = note is null ? label : $"{label} — {note}",
            Foreground = p.TextMuted,
            FontFamily = BlockRenderer.BodyFont,
            FontSize   = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin     = new Thickness(0, 0, 0, 4),
        });

        stack.Children.Add(new Border
        {
            Background      = p.CodeBg,
            BorderBrush     = p.CodeBorder,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding         = new Thickness(8, 6, 8, 6),
            Child = new TextBlock
            {
                Text         = source.TrimEnd(),
                FontFamily   = BlockRenderer.MonoFont,
                FontSize     = 11.5,
                Foreground   = p.Text,
                TextWrapping = TextWrapping.NoWrap,
            },
        });

        return new Border
        {
            Background      = p.CodeBg,
            BorderBrush     = p.CodeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10, 8, 10, 8),
            Margin          = new Thickness(0, 4, 0, 10),
            Child           = stack,
        };
    }
}
