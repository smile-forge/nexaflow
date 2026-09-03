namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// Mermaid <c>block</c> configuration — the front-matter <c>config.block</c> block.  Mermaid documents
/// <c>padding</c> (inner padding of every block) and <c>useMaxWidth</c>; the renderer applies the
/// first and ignores the second (the canvas is sized to its content and scrolls).
/// </summary>
public sealed class BlockConfig
{
    public double Padding { get; set; } = 8;
    public bool UseMaxWidth { get; set; } = true;
}
