using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>
/// Mermaid <c>radar</c> configuration — the front-matter <c>config.radar</c> geometry block, the
/// <c>config.themeVariables.radar</c> styling block, and the radar-relevant global <c>themeVariables</c>
/// (<c>titleColor</c>, <c>fontSize</c>, the <c>cScale0…N</c> curve palette).  Values carry Mermaid's
/// documented defaults, except <see cref="Width"/>/<see cref="Height"/>, which are tuned smaller for the
/// in-app surface.  Colours are <see cref="Brush"/>? (null ⇒ fall back to the active
/// <see cref="MarkdownPalette"/>); <see cref="CurvePalette"/> is the parsed <c>cScale</c> bank.
/// </summary>
public sealed class RadarConfig
{
    // ── config.radar geometry ───────────────────────────────────────────────
    public double Width  { get; set; } = 440;   // Mermaid default 600; tuned for the markdown surface
    public double Height { get; set; } = 440;
    public double MarginTop    { get; set; } = 50;
    public double MarginBottom { get; set; } = 50;
    public double MarginLeft   { get; set; } = 50;
    public double MarginRight  { get; set; } = 50;
    /// <summary>Scales the plotted radius (1 = fill the available radius).</summary>
    public double AxisScaleFactor { get; set; } = 1;
    /// <summary>Pushes axis labels out past the outer ring (1.05 = 5% beyond).</summary>
    public double AxisLabelFactor { get; set; } = 1.05;
    /// <summary>Cardinal-spline tension for the curves (0 = round, 1 = straight polygon).</summary>
    public double CurveTension { get; set; } = 0.17;

    // ── config.themeVariables.radar styling ─────────────────────────────────
    public Brush?  AxisColor       { get; set; }
    public double  AxisStrokeWidth { get; set; } = 1;
    public double  AxisLabelFontSize { get; set; } = 12;
    public double  CurveOpacity    { get; set; } = 0.7;
    public double  CurveStrokeWidth { get; set; } = 2;
    public Brush?  GraticuleColor  { get; set; }
    public double  GraticuleOpacity { get; set; } = 0.5;
    public double  GraticuleStrokeWidth { get; set; } = 1;
    public double  LegendBoxSize   { get; set; } = 10;
    public double  LegendFontSize  { get; set; } = 14;

    // ── global themeVariables ───────────────────────────────────────────────
    public Brush?  TitleColor    { get; set; }
    public double  TitleFontSize { get; set; } = 20;

    /// <summary>Curve colours parsed from <c>cScale0…N</c> (index-ordered). Empty ⇒ use the palette's
    /// <see cref="MarkdownPalette.Series"/> bank.</summary>
    public List<Brush> CurvePalette { get; } = [];
}
