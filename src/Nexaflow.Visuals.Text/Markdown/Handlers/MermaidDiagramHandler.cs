using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Handlers;

/// <summary>
/// Handles all <c>mermaid</c> fenced code blocks.
///
/// <para>
/// Mermaid is a family of diagram types sharing one language tag. The block is read once, by
/// <see cref="MermaidParser"/>, and the diagram its header names chooses the builder: one named in
/// <see cref="MermaidBuilders"/> is drawn by it on the shared layout tree, in an element it can be selected and
/// written in (docs/mermaid-diagrams.md); a header naming no type falls to
/// <see cref="UnknownDiagramBuilder"/>, which shows the block as written with the reason.
/// </para>
/// <para>
/// Which keyword names which diagram is <see cref="MermaidDiagrams"/>; adding a type means naming it there and in
/// <see cref="MermaidBuilders.For"/>, and nowhere else.
/// </para>
/// </summary>
public sealed class MermaidDiagramHandler : IDiagramHandler
{
    public bool CanHandle(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options) =>
        MermaidBuilders.Element(source, MermaidBlock.Read(source).Diagram, options)
        ?? UnknownDiagramBuilder.Element(source, options);
}
