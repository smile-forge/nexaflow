using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Draws an Aztec block. The drawing is <see cref="WpfMatrixRenderer"/>'s; what is left here is what
/// only this symbology has to say about itself — which family and size it settled on, and how much of
/// that size is error correction.
/// </summary>
public static class WpfAztecRenderer
{
    /// <summary>Renders <paramref name="block"/>'s payload, or a message saying why it could not be encoded.</summary>
    public static FrameworkElement Render(AztecBlock block, MarkdownPalette palette)
    {
        if (AztecEncoder.TryEncode(block.Payload, block.Options, out var symbol, out string? error))
            return Render(symbol!, block, palette);

        return DiagramRenderer.ErrorElement(error!, WpfMatrixRenderer.Abridged(block.Payload, 120));
    }

    /// <summary>Renders an already-encoded symbol with the drawing settings from <paramref name="block"/>.</summary>
    public static FrameworkElement Render(AztecSymbol symbol, AztecBlock block, MarkdownPalette palette)
    {
        string flavour = block.Options.Gs1 ? ", GS1"
                       : block.Options.Eci is int eci ? $", ECI {eci}"
                       : string.Empty;

        string tooltip = $"{block.Type} · Aztec {symbol.Designation}, {symbol.Size}×{symbol.Size}{flavour}\n"
                       + $"{symbol.DataCodewords} of {symbol.TotalCodewords} codewords "
                       + $"({symbol.CodewordBits}-bit), {symbol.ErrorCorrectionPercent}% error correction\n\n"
                       + WpfMatrixRenderer.Abridged(Gs1ElementString.Readable(block.Payload));

        return WpfMatrixRenderer.Render(symbol, block.Settings, palette, tooltip);
    }
}
