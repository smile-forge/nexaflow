namespace Nexaflow.Markdown.Mermaid.Sankey;

/// <summary>What a flow is coloured by.</summary>
public enum SankeyLinkColour
{
    /// <summary>The colour of the node it leaves.</summary>
    Source,

    /// <summary>The colour of the node it reaches.</summary>
    Target,

    /// <summary>From one to the other along its length.</summary>
    Gradient,

    /// <summary>The one colour the front matter writes for every flow.</summary>
    Written,
}

/// <summary>Where a column of nodes sits across the width of the diagram.</summary>
public enum SankeyAlignment
{
    /// <summary>Spread out, with what nothing leaves pushed to the far side.</summary>
    Justify,
    Centre,
    Left,
    Right,
}

/// <summary>How a node's name is written beside it.</summary>
public enum SankeyLabels
{
    Plain,
    Outlined,
}

/// <summary>
/// What a <c>sankey-beta</c> block's front matter asks for, under <c>config: sankey:</c>: how big it is drawn, what its
/// flows are coloured by, where its columns sit, and whether each node says what it is worth.
///
/// <para>
/// Mermaid's are <c>width</c>, <c>height</c>, <c>linkColor</c>, <c>nodeAlignment</c>, <c>showValues</c>, <c>prefix</c>,
/// <c>suffix</c> and <c>useMaxWidth</c>; <c>nodeWidth</c>, <c>nodePadding</c>, <c>labelStyle</c> and <c>nodeColors</c> are
/// this renderer's own.
/// </para>
/// </summary>
public sealed record SankeyConfig
{
    public static SankeyConfig Default { get; } = new();

    /// <summary>The least the diagram is drawn at: it grows past them to hold what is written in it.</summary>
    public double? Width { get; init; }
    public double? Height { get; init; }

    public SankeyLinkColour LinkColour { get; init; } = SankeyLinkColour.Gradient;

    /// <summary>The colour every flow takes, where the front matter writes one instead of naming an end.</summary>
    public string? LinkWritten { get; init; }

    public SankeyAlignment Alignment { get; init; } = SankeyAlignment.Justify;

    /// <summary>Whether each node says what it is worth beside its name.</summary>
    public bool ShowValues { get; init; } = true;

    /// <summary>What goes before and after a value where one is shown.</summary>
    public string Prefix { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;

    /// <summary>How wide a node's bar is, and the least clear air between two of them in a column.</summary>
    public double NodeWidth { get; init; } = 10;
    public double NodePadding { get; init; } = 12;

    public SankeyLabels Labels { get; init; } = SankeyLabels.Plain;

    /// <summary>The colour written for a node by name — the rest take the theme's.</summary>
    public IReadOnlyDictionary<string, string> NodeColours { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static SankeyConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static SankeyConfig From(MermaidConfig config)
    {
        var sankey = config.Diagram("sankey");
        var colour = sankey.Value("linkColor");

        return new SankeyConfig
        {
            Width = sankey.Size("width"),
            Height = sankey.Size("height"),
            LinkColour = Coloured(colour),
            LinkWritten = Coloured(colour) == SankeyLinkColour.Written ? colour : null,
            Alignment = sankey.Value("nodeAlignment")?.ToLowerInvariant() switch
            {
                "center" or "centre" => SankeyAlignment.Centre,
                "left" => SankeyAlignment.Left,
                "right" => SankeyAlignment.Right,
                _ => SankeyAlignment.Justify,
            },
            ShowValues = sankey.Flag("showValues") ?? true,
            Prefix = sankey.Value("prefix") ?? string.Empty,
            Suffix = sankey.Value("suffix") ?? string.Empty,
            NodeWidth = sankey.Size("nodeWidth") is { } wide and > 0 ? wide : 10,
            NodePadding = sankey.Size("nodePadding") is { } apart and >= 0 ? apart : 12,
            Labels = sankey.Value("labelStyle")?.StartsWith('o') == true ? SankeyLabels.Outlined : SankeyLabels.Plain,
            NodeColours = sankey.Section("nodeColors")?.Values ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    private static SankeyLinkColour Coloured(string? said) => said?.ToLowerInvariant() switch
    {
        null or "" or "gradient" => SankeyLinkColour.Gradient,
        "source" => SankeyLinkColour.Source,
        "target" => SankeyLinkColour.Target,
        _ => SankeyLinkColour.Written,
    };
}
