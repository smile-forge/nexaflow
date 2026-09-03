using System;
using System.Windows;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles <c>aztec</c> fenced code blocks.
///
/// <para>
/// Registered beside the QR and Data Matrix handlers for the same reason they are: not a diagram, but
/// it arrives as one, and registering it here is what puts it on both markdown surfaces at once. The
/// handler is a seam and nothing more — <see cref="AztecBlockParser"/> reads the block,
/// <see cref="AztecEncoder"/> encodes the payload and <see cref="WpfAztecRenderer"/> draws it.
/// </para>
/// </summary>
public sealed class AztecDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("aztec", StringComparison.OrdinalIgnoreCase)
        || language.Equals("aztec-code", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options)
        => AztecBlockParser.TryParse(source, out var block, out string? error)
            ? WpfAztecRenderer.Render(block!, options.Palette)
            : DiagramRenderer.ErrorElement(error!, source);
}
