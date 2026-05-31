using System.Windows;
using System.Windows.Controls;
using MdMarkdown = Markdig.Markdown;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Read-only markdown display control.  Parses <see cref="Markdown"/> with the
/// shared <see cref="MarkdownPipelineFactory.Default"/> pipeline and renders
/// each top-level block via <see cref="BlockRenderer"/>.
///
/// Use this control for display surfaces (chat messages, log details, scratch
/// notes).  The block-based editor in <c>Nexaflow.Features.Markdown</c> does
/// not use this control — it composes <see cref="BlockRenderer"/> into its own
/// per-block edit/render container.
/// </summary>
public partial class MarkdownView : UserControl
{
    public MarkdownView()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownView),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Colour scheme for rendering. Defaults to <see cref="MarkdownPalette.Dark"/>;
    /// set <see cref="MarkdownPalette.Light"/> for light surfaces (e.g. scratchpad post-its).</summary>
    public static readonly DependencyProperty PaletteProperty =
        DependencyProperty.Register(
            nameof(Palette), typeof(MarkdownPalette), typeof(MarkdownView),
            new PropertyMetadata(MarkdownPalette.Dark, OnMarkdownChanged));

    public MarkdownPalette Palette
    {
        get => (MarkdownPalette)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MarkdownView)d).Rebuild();

    private void Rebuild()
    {
        BlocksPanel.Children.Clear();

        var raw = Markdown ?? string.Empty;
        if (raw.Length == 0) return;

        var doc      = MdMarkdown.Parse(raw, MarkdownPipelineFactory.Default);
        var rawLines = raw.Split('\n');

        foreach (var block in doc)
        {
            int start = Math.Clamp(block.Line, 0, Math.Max(rawLines.Length - 1, 0));
            int end   = Math.Clamp(BlockEndLine(block), start, Math.Max(rawLines.Length - 1, start));
            string blockRaw = rawLines.Length > 0
                ? string.Join("\n", rawLines[start..(end + 1)])
                : string.Empty;

            BlocksPanel.Children.Add(BlockRenderer.Render(block, blockRaw, Palette));
        }
    }

    private static int BlockEndLine(Markdig.Syntax.Block block)
    {
        // FencedCodeBlock stores only content lines; total span = opening fence + content + closing fence
        if (block is Markdig.Syntax.FencedCodeBlock fcb)
            return block.Line + fcb.Lines.Count + 1;
        if (block is Markdig.Syntax.LeafBlock lb && lb.Lines.Count > 0)
            return block.Line + lb.Lines.Count - 1;
        if (block is Markdig.Syntax.ContainerBlock cb && cb.Count > 0)
            return BlockEndLine(cb[cb.Count - 1]);
        return block.Line;
    }
}
