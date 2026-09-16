// LEGACY — frozen. Diagrams move off this code onto the shared layout tree one at a time, built from the Mermaid kit
// (docs/mermaid-diagrams.md); this file goes when the last diagram using it has moved. Read it for what a diagram draws —
// never copy from it, add to it, or use it from code on the shared tree (MermaidDiagramRulesTests).
namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// The Mermaid <c>ishikawa</c> configuration — the complete documented surface is two keys under
/// <c>config.ishikawa</c>.  (Ishikawa exposes no colour/size theme options yet; bones take their
/// colours from the active <see cref="MarkdownPalette"/>.)
/// </summary>
public sealed class IshikawaConfig
{
    /// <summary>Padding around the whole diagram, in pixels.</summary>
    public double DiagramPadding { get; set; } = 20;

    /// <summary>Mermaid's responsive-width flag — parsed for completeness; the diagram already sizes
    /// to its content on the markdown surface.</summary>
    public bool UseMaxWidth { get; set; }
}
