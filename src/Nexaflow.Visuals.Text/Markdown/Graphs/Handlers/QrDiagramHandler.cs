using Nexaflow.Visuals.Text.Markdown.Qr;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles <c>qr</c> fenced code blocks ΓÇö the QR-code generator described at
/// <see href="https://markdown.org/tools/diagrams/qr/"/>.
///
/// <para>
/// It sits beside the diagram handlers because it arrives the same way (a fenced block whose info
/// string names a language, rendered to an element in place of its source) even though a QR code is
/// not a diagram. Registering it here is what gets it onto both markdown surfaces at once, since both
/// route fenced blocks through <see cref="DiagramRenderer"/>.
/// </para>
///
/// <para>
/// The handler itself is a seam and nothing more: <see cref="QrBlockParser"/> reads the block,
/// <see cref="QrEncoder"/> encodes the payload and <see cref="WpfQrRenderer"/> draws it.
/// </para>
/// </summary>
public sealed class QrDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("qr", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options)
        => QrBlockParser.TryParse(source, out var block, out string? error)
            ? WpfQrRenderer.Render(block!, options.Palette)
            : DiagramRenderer.ErrorElement(error!, source);
}
