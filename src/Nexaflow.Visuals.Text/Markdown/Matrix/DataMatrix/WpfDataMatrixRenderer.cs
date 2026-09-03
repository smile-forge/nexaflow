using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

/// <summary>
/// Draws a Data Matrix block. The drawing is <see cref="WpfMatrixRenderer"/>'s; what is left here is
/// what only this symbology has to say about itself — the size and encodation it settled on.
/// </summary>
public static class WpfDataMatrixRenderer
{
    /// <summary>Renders <paramref name="block"/>'s payload, or a message saying why it could not be encoded.</summary>
    public static FrameworkElement Render(DataMatrixBlock block, MarkdownPalette palette)
    {
        if (DataMatrixEncoder.TryEncode(block.Payload, block.Options, out var symbol, out string? error))
            return Render(symbol!, block, palette);

        return DiagramRenderer.ErrorElement(error!, WpfMatrixRenderer.Abridged(block.Payload, 120));
    }

    /// <summary>Renders an already-encoded symbol with the drawing settings from <paramref name="block"/>.</summary>
    public static FrameworkElement Render(DataMatrixSymbol symbol, DataMatrixBlock block, MarkdownPalette palette)
    {
        string flavour = block.Options.Gs1 ? ", GS1"
                       : block.Options.Macro == DataMatrixMacro.Macro06 ? ", Macro 06"
                       : block.Options.Macro == DataMatrixMacro.Macro05 ? ", Macro 05"
                       : string.Empty;

        string tooltip = $"{block.Type} · Data Matrix {symbol.Size}{flavour}, {symbol.Encodation}, "
                       + $"{symbol.DataCodewordsUsed} of {symbol.Size.DataCodewords} codewords\n\n"
                       + WpfMatrixRenderer.Abridged(Gs1ElementString.Readable(block.Payload));

        return WpfMatrixRenderer.Render(symbol, block.Settings, palette, tooltip);
    }

}
