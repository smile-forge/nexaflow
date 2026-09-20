using System;
using System.Windows;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

namespace Nexaflow.Visuals.Text.Markdown.Handlers;

/// <summary>
/// Handles <c>pdf417</c> fenced code blocks.
///
/// <para>
/// Registered beside the QR and Data Matrix handlers for the same reason: not a diagram, but it arrives
/// as one, and registering it here puts it on both markdown surfaces at once. The handler is a seam and nothing more — <see cref="MatrixParser"/> reads the block into a tree,
/// and <see cref="Pdf417Builder"/> lays it out.
/// </para>
/// </summary>
public sealed class Pdf417DiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("pdf417", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) => Pdf417Builder.Element(source, options);
}
