namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>Where the legend goes, beside the chart it explains.</summary>
public enum PieLegend
{
    Right,
    Left,
    Top,
    Bottom,

    /// <summary>Over the chart, in the middle — what a donut's hole is for.</summary>
    Centre,
}

/// <summary>
/// What a <c>pie</c> block's front matter asks for: the shape of the chart under <c>config: pie:</c>, and its colours
/// and sizes under <c>themeVariables:</c>.
///
/// <para>
/// The defaults are Mermaid's, and so are the limits: a label sits three quarters of the way out, a donut has no hole,
/// the legend is on the right. A value outside what the option allows is brought back inside it rather than refused —
/// config is not source, and a chart with a silly number in its front matter should still draw.
/// </para>
/// <para>
/// Colours are the words they were written as. What <c>black</c> or <c>#ff0000</c> comes to on the page is the
/// builder's, and a colour nobody wrote is the theme's.
/// </para>
/// </summary>
public sealed record PieConfig
{
    /// <summary>What a block with no front matter asks for.</summary>
    public static PieConfig Default { get; } = new();

    /// <summary>How far out a slice's label sits: nought in the middle, one at the rim.</summary>
    public double TextPosition { get; init; } = 0.75;

    /// <summary>How much of the middle is cut out, as a share of the radius — nought for a pie, up to nine tenths.</summary>
    public double DonutHole { get; init; }

    /// <summary>Where the legend goes.</summary>
    public PieLegend Legend { get; init; } = PieLegend.Right;

    /// <summary>The label of the slice to pick out, or null. <c>hover</c> means whichever the pointer is over.</summary>
    public string? Highlight { get; init; }

    /// <summary>Whether the slice under the pointer is the one picked out.</summary>
    public bool HighlightsOnHover => string.Equals(Highlight, "hover", StringComparison.OrdinalIgnoreCase);

    /// <summary>The colours written for <c>pie1</c>…<c>pie12</c>, by their number. What is not written is the theme's.</summary>
    public IReadOnlyDictionary<int, string> Swatches { get; init; } = new Dictionary<int, string>();

    /// <summary>The line between two slices.</summary>
    public string? Stroke { get; init; }

    /// <inheritdoc cref="Stroke"/>
    public double StrokeWidth { get; init; } = 2;

    /// <summary>The line round the whole chart.</summary>
    public string? OuterStroke { get; init; }

    /// <summary>How thick that line is, or null where the front matter asks for no line at all.</summary>
    public double? OuterStrokeWidth { get; init; }

    /// <summary>How solid a slice is drawn, or null to draw it as solid as the theme draws anything.</summary>
    public double? Opacity { get; init; }

    /// <summary>
    /// The sizes, or null for the theme's own — which is what a size nobody wrote is, exactly as a colour nobody wrote
    /// is the theme's. Mermaid's own defaults (25px for a title, 17px elsewhere) are what its renderer draws at, not a
    /// claim about what this one should.
    /// </summary>
    public double? TitleTextSize { get; init; }

    public string? TitleTextColour { get; init; }

    /// <inheritdoc cref="TitleTextSize"/>
    public double? SectionTextSize { get; init; }

    /// <summary>The ink of what is written on a slice, or null to let the slice's own colour decide.</summary>
    public string? SectionTextColour { get; init; }

    /// <inheritdoc cref="TitleTextSize"/>
    public double? LegendTextSize { get; init; }

    public string? LegendTextColour { get; init; }

    /// <summary>Whether the chart is drawn to the width it is given, rather than to the size it wants.</summary>
    public bool UseMaxWidth { get; init; } = true;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static PieConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    /// <summary>Reads a block's front matter, already read as config.</summary>
    public static PieConfig From(MermaidConfig config)
    {
        var pie = config.Diagram("pie");
        var theme = config.Theme;

        return new PieConfig
        {
            TextPosition = Math.Clamp(pie.Number("textPosition") ?? Default.TextPosition, 0, 1),
            DonutHole = Math.Clamp(pie.Number("donutHole") ?? Default.DonutHole, 0, 0.9),
            Legend = Placed(pie.Value("legendPosition")),
            Highlight = pie.Value("highlightSlice"),
            UseMaxWidth = pie.Flag("useMaxWidth") ?? Default.UseMaxWidth,

            Swatches = theme.Swatches("pie", 12),
            Stroke = theme.Value("pieStrokeColor"),
            StrokeWidth = Math.Max(0, theme.Number("pieStrokeWidth") ?? Default.StrokeWidth),
            OuterStroke = theme.Value("pieOuterStrokeColor"),
            OuterStrokeWidth = theme.Size("pieOuterStrokeWidth"),
            Opacity = theme.Number("pieOpacity") is { } opacity ? Math.Clamp(opacity, 0, 1) : null,

            TitleTextSize = theme.Size("pieTitleTextSize"),
            TitleTextColour = theme.Value("pieTitleTextColor"),
            SectionTextSize = theme.Size("pieSectionTextSize"),
            SectionTextColour = theme.Value("pieSectionTextColor"),
            LegendTextSize = theme.Size("pieLegendTextSize"),
            LegendTextColour = theme.Value("pieLegendTextColor"),
        };
    }

    private static PieLegend Placed(string? where) => where?.Trim().ToLowerInvariant() switch
    {
        "left" => PieLegend.Left,
        "top" => PieLegend.Top,
        "bottom" => PieLegend.Bottom,
        "center" or "centre" => PieLegend.Centre,
        _ => PieLegend.Right,
    };
}
