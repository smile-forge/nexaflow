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
