namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>How a chart is laid out: bars/lines rise vertically (categories along the bottom)
/// or run horizontally (categories down the left).  Set by the <c>xychart</c> declaration line
/// (<c>xychart horizontal</c>) and overridable by a <c>chartOrientation</c> config value.</summary>
public enum XyOrientation { Vertical, Horizontal }

/// <summary>The two plot kinds a series can be drawn as.</summary>
public enum XySeriesKind { Bar, Line }

/// <summary>One value in a series.  A line point may carry an optional quoted text label drawn
/// beside the marker (Mermaid renders point labels on <c>line</c> plots only).</summary>
public sealed class XyPoint
{
    public required double Value { get; init; }
    public string? Label { get; init; }
}

/// <summary>A single <c>bar</c> or <c>line</c> series.  A <see cref="Name"/> (the quoted token
/// before the value list) opts the series into the legend; unnamed series are omitted from it.</summary>
public sealed class XySeries
{
    public required XySeriesKind Kind { get; init; }
    public string? Name { get; init; }
    public List<XyPoint> Points { get; } = [];
}

/// <summary>
/// One axis of an XY chart.  The x-axis may be <b>categorical</b> (a bracketed list of labels) or
/// a <b>numeric range</b>; the y-axis is numeric only.  A <see cref="Title"/> is optional, and an
/// explicit range (<c>min --&gt; max</c>) is optional — the renderer auto-ranges from the data when
/// <see cref="HasRange"/> is false.
/// </summary>
public sealed class XyAxis
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Categorical labels (x-axis only); empty for a numeric axis.</summary>
    public List<string> Categories { get; } = [];

    public double? Min { get; set; }
    public double? Max { get; set; }

    public bool IsCategorical => Categories.Count > 0;
    public bool HasRange => Min is not null && Max is not null;
}

/// <summary>
/// Data model for a Mermaid <c>xychart</c> / <c>xychart-beta</c> diagram: a title, two axes, and any
/// number of <c>bar</c>/<c>line</c> series sharing those axes.  Independent of the graph/Sugiyama
/// pipeline (an XY chart needs no layout algorithm — the renderer maps values to a fixed plot area).
/// <see cref="Config"/> carries the parsed front-matter <c>xyChart</c> options (the handler injects it);
/// <see cref="Orientation"/> comes from the declaration line and is overridden by a config value.
/// </summary>
public sealed class XyChart
{
    public string Title { get; set; } = string.Empty;
    public XyOrientation Orientation { get; set; } = XyOrientation.Vertical;

    public XyAxis XAxis { get; } = new();
    public XyAxis YAxis { get; } = new();

    public List<XySeries> Series { get; } = [];

    public XyChartConfig Config { get; set; } = new();
}
