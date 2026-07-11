namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// The Mermaid <c>config.architecture</c> options.  The in-app renderer uses a deterministic grid
/// layout rather than Mermaid's force-directed engine, so most physics keys are parsed-and-tolerated;
/// <see cref="NodeSeparation"/> tunes the grid cell spacing.
/// </summary>
public sealed class ArchitectureConfig
{
    /// <summary>Pixel separation between neighbouring grid cells (Mermaid default 75, tuned for the surface).</summary>
    public double NodeSeparation { get; set; } = 36;

    // Parsed-but-tolerated physics keys (kept so the config round-trips without loss).
    public bool   Randomize { get; set; }
    public double IdealEdgeLengthMultiplier { get; set; } = 1.5;
    public int    Seed { get; set; } = 1;
}
