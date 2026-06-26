namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>The graticule (background grid) shape of a radar chart — concentric rings or polygons.</summary>
public enum RadarGraticule { Circle, Polygon }

/// <summary>One spoke of a radar chart: a stable <see cref="Id"/> (referenced by keyed curve values)
/// and a display <see cref="Label"/> (defaults to the id when no <c>["label"]</c> is given).</summary>
public sealed class RadarAxis
{
    public required string Id { get; init; }
    public string Label { get; set; } = string.Empty;
    public string Display => string.IsNullOrEmpty(Label) ? Id : Label;
}

/// <summary>One dataset drawn as a closed polygon/area over the axes.  <see cref="Values"/> is aligned
/// to the chart's <see cref="RadarChart.Axes"/> order; a <c>null</c> entry means the curve gave no value
/// for that axis (the renderer plots it at <see cref="RadarChart.Min"/>).</summary>
public sealed class RadarCurve
{
    public required string Id { get; init; }
    public string Label { get; set; } = string.Empty;
    public List<double?> Values { get; } = [];
    public string Display => string.IsNullOrEmpty(Label) ? Id : Label;
}

/// <summary>
/// Data model for a Mermaid <c>radar-beta</c> diagram (a radar / spider / Kiviat chart): a set of axes
/// (spokes) and any number of curves overlaid on them.  Body-line options (<c>min</c>/<c>max</c>/
/// <c>ticks</c>/<c>graticule</c>/<c>showLegend</c>) live here; the front-matter <c>config.radar</c> /
/// <c>themeVariables</c> options live on <see cref="Config"/> (injected by the handler).  Independent of
/// the graph/Sugiyama pipeline — the renderer maps values to a polar plot.
/// </summary>
public sealed class RadarChart
{
    public string Title { get; set; } = string.Empty;

    public List<RadarAxis> Axes { get; } = [];
    public List<RadarCurve> Curves { get; } = [];

    /// <summary>Scale floor (default 0).</summary>
    public double Min { get; set; }
    /// <summary>Scale ceiling; null ⇒ auto from the data.</summary>
    public double? Max { get; set; }
    /// <summary>Number of concentric graticule rings (default 5).</summary>
    public int Ticks { get; set; } = 5;
    public bool ShowLegend { get; set; } = true;
    public RadarGraticule Graticule { get; set; } = RadarGraticule.Circle;

    public RadarConfig Config { get; set; } = new();
}
