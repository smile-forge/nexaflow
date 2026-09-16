namespace Nexaflow.Markdown.Mermaid.Quadrant;

/// <summary>
/// What a <c>quadrantChart</c> block's front matter asks for: the chart under <c>config: quadrantChart:</c>, and its colours
/// under <c>themeVariables:</c> (<c>quadrant1Fill</c>, <c>quadrantPointFill</c> and the rest).
///
/// <para>
/// A size or a colour nobody wrote is the theme's, so it is null here. Which side the x-axis's words go is Mermaid's rule
/// where nothing says: over the chart, unless there are points, and then under it. The paddings Mermaid sizes its own layout
/// with are read and kept, not applied.
/// </para>
/// </summary>
public sealed record QuadrantConfig
{
    public static QuadrantConfig Default { get; } = new();

    public double? ChartWidth { get; init; }
    public double? ChartHeight { get; init; }
    public double? TitleFontSize { get; init; }
    public double? QuadrantLabelFontSize { get; init; }
    public double? XAxisLabelFontSize { get; init; }
    public double? YAxisLabelFontSize { get; init; }
    public double? PointLabelFontSize { get; init; }
    public double? PointRadius { get; init; }
    public double? InternalBorderWidth { get; init; }
    public double? ExternalBorderWidth { get; init; }

    /// <summary>Whether the x-axis's words go over the chart — or under it where false — or null for Mermaid's rule.</summary>
    public bool? XAxisOnTop { get; init; }

    /// <summary>Whether the y-axis's words go right of the chart rather than left of it.</summary>
    public bool YAxisOnRight { get; init; }

    /// <summary>The fill written for each quadrant, the first quadrant's first — null where none is.</summary>
    public IReadOnlyList<string?> QuadrantFills { get; init; } = [null, null, null, null];

    /// <summary>The ink written for each quadrant's caption, the first quadrant's first.</summary>
    public IReadOnlyList<string?> QuadrantTextFills { get; init; } = [null, null, null, null];

    public string? PointFill { get; init; }
    public string? PointTextFill { get; init; }
    public string? XAxisTextFill { get; init; }
    public string? YAxisTextFill { get; init; }
    public string? InternalBorderFill { get; init; }
    public string? ExternalBorderFill { get; init; }
    public string? TitleFill { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static QuadrantConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static QuadrantConfig From(MermaidConfig config)
    {
        var chart = config.Diagram("quadrantChart");
        var theme = config.Theme;

        return new QuadrantConfig
        {
            ChartWidth = chart.Size("chartWidth"),
            ChartHeight = chart.Size("chartHeight"),
            TitleFontSize = chart.Size("titleFontSize"),
            QuadrantLabelFontSize = chart.Size("quadrantLabelFontSize"),
            XAxisLabelFontSize = chart.Size("xAxisLabelFontSize"),
            YAxisLabelFontSize = chart.Size("yAxisLabelFontSize"),
            PointLabelFontSize = chart.Size("pointLabelFontSize"),
            PointRadius = chart.Size("pointRadius"),
            InternalBorderWidth = chart.Number("quadrantInternalBorderStrokeWidth") is { } inner ? Math.Max(0, inner) : null,
            ExternalBorderWidth = chart.Number("quadrantExternalBorderStrokeWidth") is { } outer ? Math.Max(0, outer) : null,
            XAxisOnTop = chart.Value("xAxisPosition")?.Trim().ToLowerInvariant() switch { "top" => true, "bottom" => false, _ => null },
            YAxisOnRight = string.Equals(chart.Value("yAxisPosition")?.Trim(), "right", StringComparison.OrdinalIgnoreCase),

            QuadrantFills = [.. Enumerable.Range(1, 4).Select(number => theme.Value($"quadrant{number}Fill"))],
            QuadrantTextFills = [.. Enumerable.Range(1, 4).Select(number => theme.Value($"quadrant{number}TextFill"))],
            PointFill = theme.Value("quadrantPointFill"),
            PointTextFill = theme.Value("quadrantPointTextFill"),
            XAxisTextFill = theme.Value("quadrantXAxisTextFill"),
            YAxisTextFill = theme.Value("quadrantYAxisTextFill"),
            InternalBorderFill = theme.Value("quadrantInternalBorderStrokeFill"),
            ExternalBorderFill = theme.Value("quadrantExternalBorderStrokeFill"),
            TitleFill = theme.Value("quadrantTitleFill"),
        };
    }
}
