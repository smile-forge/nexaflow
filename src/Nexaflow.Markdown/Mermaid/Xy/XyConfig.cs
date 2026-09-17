namespace Nexaflow.Markdown.Mermaid.Xy;

/// <summary>Which way an xychart runs: bars rising with the categories along the foot, or reaching right with them down the side.</summary>
public enum XyOrientation
{
    Vertical,
    Horizontal,
}

/// <summary>
/// What the front matter asks of one axis: <c>config: xyChart: xAxis:</c> or <c>yAxis:</c>, and its colours under
/// <c>themeVariables: xyChart:</c>. A size or a colour nobody wrote is the theme's, so it is null.
/// </summary>
public sealed record XyAxisConfig
{
    public static XyAxisConfig Default { get; } = new();

    public bool ShowLabel { get; init; } = true;
    public double? LabelFontSize { get; init; }
    public double? LabelPadding { get; init; }
    public bool ShowTitle { get; init; } = true;
    public double? TitleFontSize { get; init; }
    public double? TitlePadding { get; init; }
    public bool ShowTick { get; init; } = true;
    public double? TickLength { get; init; }
    public double? TickWidth { get; init; }
    public bool ShowAxisLine { get; init; } = true;
    public double? AxisLineWidth { get; init; }

    /// <summary>How far round the labels are turned, in degrees, as written — see <see cref="XyConfig"/>.</summary>
    public double LabelRotation { get; init; }

    public string? LabelColour { get; init; }
    public string? TitleColour { get; init; }
    public string? TickColour { get; init; }
    public string? LineColour { get; init; }

    /// <summary>Reads an axis's section, and its colours from the theme's keys starting <paramref name="prefix"/> — <c>xAxis</c>, <c>yAxis</c>.</summary>
    internal static XyAxisConfig From(MermaidConfig axis, MermaidConfig theme, string prefix) => new()
    {
        ShowLabel = axis.Flag("showLabel") ?? Default.ShowLabel,
        LabelFontSize = axis.Size("labelFontSize"),
        LabelPadding = Length(axis, "labelPadding"),
        ShowTitle = axis.Flag("showTitle") ?? Default.ShowTitle,
        TitleFontSize = axis.Size("titleFontSize"),
        TitlePadding = Length(axis, "titlePadding"),
        ShowTick = axis.Flag("showTick") ?? Default.ShowTick,
        TickLength = Length(axis, "tickLength"),
        TickWidth = Length(axis, "tickWidth"),
        ShowAxisLine = axis.Flag("showAxisLine") ?? Default.ShowAxisLine,
        AxisLineWidth = Length(axis, "axisLineWidth"),
        LabelRotation = axis.Number("labelRotation") ?? 0,

        LabelColour = theme.Value(prefix + "LabelColor"),
        TitleColour = theme.Value(prefix + "TitleColor"),
        TickColour = theme.Value(prefix + "TickColor"),
        LineColour = theme.Value(prefix + "LineColor"),
    };

    internal static double? Length(MermaidConfig config, string key) => config.Number(key) is { } length ? Math.Max(0, length) : null;
}

/// <summary>
/// What an <c>xychart</c> block's front matter asks for: the chart under <c>config: xyChart:</c>, each axis under its
/// <c>xAxis:</c> and <c>yAxis:</c>, and its colours under <c>themeVariables: xyChart:</c>.
///
/// <para>
/// The switches and the orientation are Mermaid's, and so are their defaults. A size, a padding or a colour nobody wrote is
/// the theme's, so it is null here — what Mermaid's own renderer draws at is not a claim about what this one should.
/// <c>plotReservedSpacePercent</c> and each axis's <c>labelRotation</c> are read and kept, but a chart on the layout tree
/// sizes its plot from what is written round it, and leaves square room for an axis's tick words.
/// </para>
/// </summary>
public sealed record XyConfig
{
    public static XyConfig Default { get; } = new();

    /// <summary>How wide and tall the chart is drawn, or null for the builder's own.</summary>
    public double? Width { get; init; }

    /// <inheritdoc cref="Width"/>
    public double? Height { get; init; }

    public bool ShowTitle { get; init; } = true;
    public double? TitleFontSize { get; init; }
    public double? TitlePadding { get; init; }
    public bool ShowLegend { get; init; } = true;
    public double? LegendFontSize { get; init; }
    public double? LegendPadding { get; init; }

    /// <summary>Which way the chart runs, where the front matter says — over what the header says.</summary>
    public XyOrientation? Orientation { get; init; }

    public double PlotReservedSpacePercent { get; init; } = 50;

    /// <summary>Whether each bar has its value written on it.</summary>
    public bool ShowDataLabel { get; init; }

    /// <summary>Whether that value is written past the end of the bar rather than inside it.</summary>
    public bool ShowDataLabelOutsideBar { get; init; }

    /// <summary>Whether the chart is drawn to the width it is given, rather than to the size it wants.</summary>
    public bool UseMaxWidth { get; init; } = true;

    public XyAxisConfig XAxis { get; init; } = XyAxisConfig.Default;
    public XyAxisConfig YAxis { get; init; } = XyAxisConfig.Default;

    public string? BackgroundColour { get; init; }
    public string? TitleColour { get; init; }
    public string? DataLabelColour { get; init; }
    public string? LegendTextColour { get; init; }

    /// <summary>The colours written for the series, in the order they are written: <c>plotColorPalette</c>, a comma between each.</summary>
    public IReadOnlyList<string> Palette { get; init; } = [];

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static XyConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    /// <summary>Reads a block's front matter, already read as config.</summary>
    public static XyConfig From(MermaidConfig config)
    {
        var chart = config.Diagram("xyChart");
        var theme = config.DiagramTheme("xyChart");

        return new XyConfig
        {
            Width = chart.Size("width"),
            Height = chart.Size("height"),
            ShowTitle = chart.Flag("showTitle") ?? Default.ShowTitle,
            TitleFontSize = chart.Size("titleFontSize"),
            TitlePadding = XyAxisConfig.Length(chart, "titlePadding"),
            ShowLegend = chart.Flag("showLegend") ?? Default.ShowLegend,
            LegendFontSize = chart.Size("legendFontSize"),
            LegendPadding = XyAxisConfig.Length(chart, "legendPadding"),
            Orientation = chart.Value("chartOrientation")?.Trim().ToLowerInvariant() switch
            {
                XyGrammar.Horizontal => XyOrientation.Horizontal,
                XyGrammar.Vertical => XyOrientation.Vertical,
                _ => null,
            },
            PlotReservedSpacePercent = Math.Clamp(chart.Number("plotReservedSpacePercent") ?? Default.PlotReservedSpacePercent, 0, 100),
            ShowDataLabel = chart.Flag("showDataLabel") ?? Default.ShowDataLabel,
            ShowDataLabelOutsideBar = chart.Flag("showDataLabelOutsideBar") ?? Default.ShowDataLabelOutsideBar,
            UseMaxWidth = chart.Flag("useMaxWidth") ?? Default.UseMaxWidth,
            XAxis = XyAxisConfig.From(chart.Section("xAxis") ?? MermaidConfig.None, theme, "xAxis"),
            YAxis = XyAxisConfig.From(chart.Section("yAxis") ?? MermaidConfig.None, theme, "yAxis"),

            BackgroundColour = theme.Value("backgroundColor"),
            TitleColour = theme.Value("titleColor"),
            DataLabelColour = theme.Value("dataLabelColor"),
            LegendTextColour = theme.Value("legendTextColor"),
            Palette = theme.Value("plotColorPalette") is { Length: > 0 } palette
                ? [.. palette.Split(',').Select(colour => colour.Trim()).Where(colour => colour.Length > 0)]
                : [],
        };
    }
}
