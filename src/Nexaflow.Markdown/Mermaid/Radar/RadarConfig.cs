namespace Nexaflow.Markdown.Mermaid.Radar;

/// <summary>
/// What a <c>radar-beta</c> block's front matter asks for: the chart's shape under <c>config: radar:</c>, its lines and words
/// under <c>themeVariables: radar:</c>, and its title and curve colours under <c>themeVariables:</c>.
///
/// <para>
/// The shape is Mermaid's, and so are its defaults: how far out the axes reach and their labels sit, and how round the curves
/// are. A size, a colour, a line's width or how solid a fill is that nobody wrote is the theme's, so it is null here — what
/// Mermaid's own renderer draws at is not a claim about what this one should. A value outside what an option allows is brought
/// back inside it rather than refused: config is not source, and a chart with a silly number in its front matter should still
/// draw.
/// </para>
/// </summary>
public sealed record RadarConfig
{
    /// <summary>How many of <c>cScale0</c>… a theme has: a curve past the last takes the first again.</summary>
    public const int PaletteSize = 12;

    /// <summary>What a block with no front matter asks for.</summary>
    public static RadarConfig Default { get; } = new();

    /// <summary>How wide the chart is — its radius is half the smaller of this and <see cref="Height"/> — or null for the builder's own.</summary>
    public double? Width { get; init; }

    /// <inheritdoc cref="Width"/>
    public double? Height { get; init; }

    /// <summary>The clear air round the chart on each side, or null for the builder's own.</summary>
    public double? MarginTop { get; init; }

    /// <inheritdoc cref="MarginTop"/>
    public double? MarginBottom { get; init; }

    /// <inheritdoc cref="MarginTop"/>
    public double? MarginLeft { get; init; }

    /// <inheritdoc cref="MarginTop"/>
    public double? MarginRight { get; init; }

    /// <summary>How far out the spokes reach, as a share of the radius.</summary>
    public double AxisScaleFactor { get; init; } = 1;

    /// <summary>How far out an axis's label sits, as a share of the radius.</summary>
    public double AxisLabelFactor { get; init; } = 1.05;

    /// <summary>How round a curve is over a circular graticule: nought draws it straight from axis to axis.</summary>
    public double CurveTension { get; init; } = 0.17;

    /// <summary>Whether the chart is drawn to the width it is given, rather than to the size it wants.</summary>
    public bool UseMaxWidth { get; init; } = true;

    public string? AxisColour { get; init; }

    public double? AxisStrokeWidth { get; init; }

    public double? AxisLabelTextSize { get; init; }

    /// <summary>How solid a curve is filled, from nought to one.</summary>
    public double? CurveOpacity { get; init; }

    public double? CurveStrokeWidth { get; init; }

    public string? GraticuleColour { get; init; }

    /// <summary>How solid each of the graticule's rings is filled, from nought to one.</summary>
    public double? GraticuleOpacity { get; init; }

    public double? GraticuleStrokeWidth { get; init; }

    /// <summary>How big a legend row's square of colour is.</summary>
    public double? LegendBoxSize { get; init; }

    public double? LegendTextSize { get; init; }

    public string? TitleTextColour { get; init; }

    public double? TitleTextSize { get; init; }

    /// <summary>The colours written for <c>cScale0</c>…<c>cScale11</c>, by their number: the curves' colours, in the order they are written.</summary>
    public IReadOnlyDictionary<int, string> Swatches { get; init; } = new Dictionary<int, string>();

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static RadarConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    /// <summary>Reads a block's front matter, already read as config.</summary>
    public static RadarConfig From(MermaidConfig config)
    {
        var radar = config.Diagram("radar");
        var theme = config.Theme;
        var style = config.DiagramTheme("radar");

        return new RadarConfig
        {
            Width = radar.Size("width"),
            Height = radar.Size("height"),
            MarginTop = Margin(radar, "marginTop"),
            MarginBottom = Margin(radar, "marginBottom"),
            MarginLeft = Margin(radar, "marginLeft"),
            MarginRight = Margin(radar, "marginRight"),
            AxisScaleFactor = Math.Clamp(radar.Number("axisScaleFactor") ?? Default.AxisScaleFactor, 0, 2),
            AxisLabelFactor = Math.Clamp(radar.Number("axisLabelFactor") ?? Default.AxisLabelFactor, 0, 2),
            CurveTension = Math.Clamp(radar.Number("curveTension") ?? Default.CurveTension, 0, 1),
            UseMaxWidth = radar.Flag("useMaxWidth") ?? Default.UseMaxWidth,

            AxisColour = style.Value("axisColor"),
            AxisStrokeWidth = Stroke(style, "axisStrokeWidth"),
            AxisLabelTextSize = style.Size("axisLabelFontSize"),
            CurveOpacity = Opacity(style, "curveOpacity"),
            CurveStrokeWidth = Stroke(style, "curveStrokeWidth"),
            GraticuleColour = style.Value("graticuleColor"),
            GraticuleOpacity = Opacity(style, "graticuleOpacity"),
            GraticuleStrokeWidth = Stroke(style, "graticuleStrokeWidth"),
            LegendBoxSize = style.Size("legendBoxSize"),
            LegendTextSize = style.Size("legendFontSize"),

            TitleTextColour = theme.Value("titleColor"),
            TitleTextSize = theme.Size("fontSize"),
            Swatches = theme.Swatches("cScale", PaletteSize, first: 0),
        };
    }

    private static double? Margin(MermaidConfig config, string key) => config.Number(key) is { } margin ? Math.Max(0, margin) : null;

    private static double? Stroke(MermaidConfig config, string key) => config.Number(key) is { } width ? Math.Max(0, width) : null;

    private static double? Opacity(MermaidConfig config, string key) => config.Number(key) is { } opacity ? Math.Clamp(opacity, 0, 1) : null;
}
