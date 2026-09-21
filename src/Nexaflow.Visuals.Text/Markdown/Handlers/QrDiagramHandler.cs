using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Qr;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Handlers;

/// <summary>
/// Handles <c>qr</c> fenced code blocks — the QR-code generator described at
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
/// The handler itself is a seam and nothing more: <see cref="MatrixParser"/> reads the block into a
/// tree, and <see cref="QrBuilder"/> lays it out.
/// </para>
/// </summary>
public sealed class QrDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("qr", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, StyleFormat palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) => QrBuilder.Element(source, options);
}
