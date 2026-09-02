using Nexaflow.Visuals.Text.Markdown.Barcode;
using System.Windows;

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

    public FrameworkElement Render(string source, DiagramRenderOptions options)
        => BarcodeBlockParser.TryParse(source, out var block, out string? error)
            ? new BarcodeElement(block!, options.Palette)
            : DiagramRenderer.ErrorElement(error!, source);
}
