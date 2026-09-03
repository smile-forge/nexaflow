using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

/// <summary>
/// Draws a PDF417 block. The drawing is <see cref="WpfMatrixRenderer"/>'s — this is the first caller to
/// use its row-height multiplier, which exists because a stacked symbology's rows are drawn taller than
/// they are wide.
/// </summary>
public static class WpfPdf417Renderer
{
    /// <summary>Renders <paramref name="block"/>'s payload, or a message saying why it could not be encoded.</summary>
    public static FrameworkElement Render(Pdf417Block block, MarkdownPalette palette)
    {
        if (Pdf417Encoder.TryEncode(block.Payload, block.Options, out var symbol, out string? error))
            return Render(symbol!, block, palette);

        return DiagramRenderer.ErrorElement(error!, WpfMatrixRenderer.Abridged(block.Payload, 120));
    }

    /// <summary>Renders an already-encoded symbol with the drawing settings from <paramref name="block"/>.</summary>
    public static FrameworkElement Render(Pdf417Symbol symbol, Pdf417Block block, MarkdownPalette palette)
    {
        string tooltip = $"{block.Type} · PDF417 {symbol.Rows}×{symbol.Columns}"
                       + $"{(symbol.Truncated ? " truncated" : string.Empty)}, {symbol.Compaction} compaction, "
                       + $"error correction level {symbol.ErrorCorrectionLevel}\n\n"
                       + WpfMatrixRenderer.Abridged(block.Payload);

        return WpfMatrixRenderer.Render(symbol, block.Settings, palette, tooltip, block.RowHeight);
    }
}
