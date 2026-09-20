using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Handlers;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Nomnoml;

/// <summary>
/// Handles <c>nomnoml</c> fenced code blocks: read by <see cref="Markdown.Nomnoml.NomnomlDiagram"/> and drawn on the
/// shared layout tree by <see cref="NomnomlBuilder"/>, in an element it can be selected and followed in.
/// </summary>
public sealed class NomnomlDiagramHandler : IDiagramHandler
{
    /// <summary>The fence language this draws.</summary>
    public const string Language = "nomnoml";

    public bool CanHandle(string language) =>
        language.Equals(Language, StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) =>
        MermaidBuilder.Host(source, options, NomnomlBuilder.Build, options.ReadOnly);
}
