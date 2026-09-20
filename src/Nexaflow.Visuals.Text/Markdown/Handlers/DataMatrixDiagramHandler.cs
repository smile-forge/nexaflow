using System;
using System.Windows;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

namespace Nexaflow.Visuals.Text.Markdown.Handlers;

/// <summary>
/// Handles <c>datamatrix</c> fenced code blocks.
///
/// <para>
/// Registered beside the QR handler for the same reason it is: not a diagram, but it arrives as one,
/// and registering it here is what puts it on both markdown surfaces at once. The handler is a seam and nothing more — <see cref="MatrixParser"/> reads the block into a tree,
/// and <see cref="DataMatrixBuilder"/> lays it out.
/// </para>
/// </summary>
public sealed class DataMatrixDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("datamatrix", StringComparison.OrdinalIgnoreCase)
        || language.Equals("data-matrix", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) => DataMatrixBuilder.Element(source, options);
}
