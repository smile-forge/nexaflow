namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// The Mermaid <c>config.er</c> configuration.  Layout-spacing keys (entity sizes, node/rank spacing) are
/// parsed for completeness but the shared Sugiyama layout uses its own metrics, so only the visually
/// meaningful keys are applied: <see cref="LayoutDirection"/> (when the body has no <c>direction</c>
/// statement) and the explicit <see cref="Fill"/>/<see cref="Stroke"/> colours (null ⇒ keep the theme).
/// </summary>
public sealed class ErConfig
{
    /// <summary>Directional bias for layout; null ⇒ unset (an inline <c>direction</c> or the default wins).</summary>
    public GraphDirection? LayoutDirection { get; set; }

    /// <summary>Explicit entity-box fill / stroke colour; null ⇒ use the theme.</summary>
    public string? Fill   { get; set; }
    public string? Stroke { get; set; }

    public int    FontSize       { get; set; } = 12;
    public int    TitleTopMargin { get; set; } = 25;
    public int    DiagramPadding { get; set; } = 20;
    public int    MinEntityWidth { get; set; } = 100;
    public int    MinEntityHeight { get; set; } = 75;
    public int    EntityPadding  { get; set; } = 15;
    public int    NodeSpacing    { get; set; } = 140;
    public int    RankSpacing    { get; set; } = 80;
    public bool   UseMaxWidth    { get; set; }
}
