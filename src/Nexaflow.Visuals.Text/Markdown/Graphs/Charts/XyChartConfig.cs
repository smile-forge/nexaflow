using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// Per-axis layout options (Mermaid's <c>AxisConfig</c>), applied to both the x- and y-axis.
/// Every field carries Mermaid's documented default; <see cref="Parsers.XyChartConfigParser"/>
/// overwrites only the keys a chart's front-matter actually sets.
/// </summary>
public sealed class XyAxisConfig
{
    public bool   ShowLabel     { get; set; } = true;
    public double LabelFontSize { get; set; } = 14;
    public double LabelPadding  { get; set; } = 5;
    public bool   ShowTitle     { get; set; } = true;
    public double TitleFontSize { get; set; } = 16;
    public double TitlePadding  { get; set; } = 5;
    public bool   ShowTick      { get; set; } = true;
    public double TickLength    { get; set; } = 5;
    public double TickWidth     { get; set; } = 2;
    public bool   ShowAxisLine  { get; set; } = true;
    public double AxisLineWidth { get; set; } = 2;
    /// <summary>Label rotation in degrees (Mermaid applies it to the bottom x-axis only).</summary>
    public double LabelRotation { get; set; }
}

/// <summary>
/// The full set of Mermaid <c>xyChart</c> configuration — the <c>config: xyChart:</c> layout block and
/// the <c>config: themeVariables: xyChart:</c> colour block, both delivered in a diagram's front-matter.
/// Layout/flag values carry Mermaid's documented defaults; colours are <see cref="Brush"/>? (null ⇒ the
/// renderer falls back to the active <see cref="MarkdownPalette"/>), and <see cref="PlotPalette"/> is the
/// parsed <c>plotColorPalette</c> (empty ⇒ the palette's series bank).  Defaults match the Mermaid docs,
/// except <see cref="Width"/>/<see cref="Height"/>, which are tuned smaller for the in-app surface.
/// </summary>
public sealed class XyChartConfig
{
    // ── Chart layout / flags ────────────────────────────────────────────────
    public double Width  { get; set; } = 600;   // Mermaid default 700; tuned for the markdown surface
    public double Height { get; set; } = 400;   // Mermaid default 500
    public double TitlePadding  { get; set; } = 10;
    public double TitleFontSize { get; set; } = 20;
    public bool   ShowTitle     { get; set; } = true;
    public bool   ShowLegend    { get; set; } = true;
    public double LegendFontSize { get; set; } = 14;
    public double LegendPadding  { get; set; } = 10;
    /// <summary>Minimum percentage of the chart the plot area must occupy (data headroom is the rest).</summary>
    public double PlotReservedSpacePercent { get; set; } = 50;
    public bool   ShowDataLabel           { get; set; }
    public bool   ShowDataLabelOutsideBar { get; set; }

    /// <summary><c>chartOrientation</c> if set in config (overrides the declaration-line keyword); else null.</summary>
    public XyOrientation? Orientation { get; set; }

    public XyAxisConfig XAxis { get; } = new();
    public XyAxisConfig YAxis { get; } = new();

    // ── Theme variables (colours) ───────────────────────────────────────────
    public Brush? BackgroundColor { get; set; }
    public Brush? TitleColor      { get; set; }
    public Brush? DataLabelColor  { get; set; }
    public Brush? LegendTextColor { get; set; }

    public Brush? XAxisLabelColor { get; set; }
    public Brush? XAxisTitleColor { get; set; }
    public Brush? XAxisTickColor  { get; set; }
    public Brush? XAxisLineColor  { get; set; }

    public Brush? YAxisLabelColor { get; set; }
    public Brush? YAxisTitleColor { get; set; }
    public Brush? YAxisTickColor  { get; set; }
    public Brush? YAxisLineColor  { get; set; }

    /// <summary>Parsed <c>plotColorPalette</c> — one brush per chart element, in order. Empty ⇒ use the
    /// palette's <see cref="MarkdownPalette.Series"/> bank.</summary>
    public List<Brush> PlotPalette { get; } = [];
}
