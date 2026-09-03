using System.Windows;
using Nexaflow.Visuals.Text.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// Draws a QR block. The drawing itself is <see cref="WpfMatrixRenderer"/>'s — the same for every
/// matrix symbology — and what is left here is what only a QR code has to say about itself: the
/// version and level it settled on, for the tooltip, and the one encoding failure it can suffer.
/// </summary>
public static class WpfQrRenderer
{
    /// <summary>Renders <paramref name="block"/>'s payload, or a message saying why it could not be encoded.</summary>
    public static FrameworkElement Render(QrBlock block, MarkdownPalette palette)
    {
        if (QrEncoder.TryEncode(block.Payload, block.ErrorCorrection, out var matrix, out string? error))
            return Render(matrix!, block, palette);

        // The error box prints its source on one unwrapped line, so the payload is trimmed first: the one
        // failure that reaches here is a payload too long to encode, which would stretch across the page.
        return DiagramRenderer.ErrorElement(error!, WpfMatrixRenderer.Abridged(block.Payload, 120));
    }

    /// <summary>Renders an already-encoded symbol with the drawing settings from <paramref name="block"/>.</summary>
    public static FrameworkElement Render(QrMatrix matrix, QrBlock block, MarkdownPalette palette)
    {
        string tooltip = $"{block.Type} · version {matrix.Version} ({matrix.Size}×{matrix.Size}), "
                       + $"error correction {block.ErrorCorrection}\n\n{WpfMatrixRenderer.Abridged(block.Payload)}";

        return WpfMatrixRenderer.Render(matrix, block.Settings, palette, tooltip);
    }
}
