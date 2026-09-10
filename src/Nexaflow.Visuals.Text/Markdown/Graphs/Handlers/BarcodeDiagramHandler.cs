using Nexaflow.Visuals.Text.Markdown.Barcode;
using System.Windows;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles <c>barcode</c> fenced code blocks — the linear-barcode generator described at
/// <see href="https://markdown.org/tools/diagrams/barcode/"/>.
///
/// <para>
/// Registered here beside the QR handler for the same reason: it is not a diagram, but it arrives as
/// one — a fenced block whose info string names a language, rendered to an element in place of its
/// source — and registering it here is what puts it on both markdown surfaces at once.
/// </para>
///
/// <para>
/// The split between the two kinds of failure is the whole of the logic. A block that cannot be
/// understood at all — an unknown setting, a format that does not exist — falls back to its source with
/// the reason above it, which is all anyone can do with it. A block that is well formed but whose value
/// this format cannot carry still renders: <see cref="BarcodeElement"/> draws it struck through and
/// waved under, because that value is the part the reader edits, and it is invalid every time they are
/// halfway through changing it.
/// </para>
/// </summary>
public sealed class BarcodeDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("barcode", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    /// <summary>
    /// Renders the block, rebasing the value's offset onto the markdown block the fence came from —
    /// <see cref="DiagramRenderOptions.SourceOffset"/>. The parser is handed the fence's content and
    /// reports offsets into that; an editing host splices into the whole block, fence lines and all, and
    /// without the bias every edit would land a couple of lines early.
    ///
    /// <para>
    /// A plain <see cref="ContentElement"/> and a builder, with no element of its own in between. There used
    /// to be a <c>BarcodeElement</c>: four hundred lines that hosted a layout, measured it, painted it,
    /// hit-tested it, held a selection and a caret and drew it blinking — every one of them a second copy of
    /// what the shared element already did, and each drifting from it as the other moved. What was genuinely
    /// the barcode's — reading the value, saying why it would not encode, and striking the bars through when
    /// it did not — belongs to the builder and is there now.
    /// </para>
    /// </summary>
    public FrameworkElement Render(string source, DiagramRenderOptions options)
    {
        if (!BarcodeBlockParser.TryParse(source, out var block, out string? error))
            return DiagramRenderer.ErrorElement(error!, source);

        var placed = block!.At(block.ValueStart + options.SourceOffset);

        return new Editing.ContentElement(placed.Value, options.Palette,
            (state, _, pixelsPerDip) => BarcodeBuilder.Build(placed.With(state.Source), options.Palette, pixelsPerDip))
        {
            // Where the value sits inside the fence that produced it. Without it the host reads this as a
            // block that IS its content — which only a $$…$$ formula is — and puts the delimiters back on
            // every edit, so typing a digit into a barcode turned it into a formula.
            SourceStart = placed.ValueStart,
            SourceLength = placed.Value.Length,
        };
    }
}
