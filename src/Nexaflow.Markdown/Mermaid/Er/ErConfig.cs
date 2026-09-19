namespace Nexaflow.Markdown.Mermaid.Er;

/// <summary>
/// What an ER diagram's front matter asks for, under <c>config: er:</c>: how small an entity may be, the air kept inside one,
/// how big its words are drawn, how far apart they are set, and the colours every entity is drawn in.
///
/// Mermaid's own defaults, every one of them applied — the words are measured rather than guessed, so what an entity takes is
/// what it is at least. <c>useMaxWidth</c>, <c>theme</c>, <c>look</c> and <c>layout</c> name a renderer, a drawing style and a
/// layout engine.
/// </summary>
public sealed record ErConfig
{
    /// <summary>How wide and how deep an entity is at least, whatever little is written in it.</summary>
    public const double Least = 100;
    public const double Shortest = 75;

    /// <summary>The air kept either side of what is written inside one.</summary>
    public const double Inside = 15;

    /// <summary>How big what is written in one is drawn.</summary>
    public const double Size = 12;

    /// <summary>How deep a row of it is, which Mermaid sets from how big the words are.</summary>
    public const double Lines = 1.5;

    /// <summary>How far apart two entities in the same rank are set, and one rank from the next.</summary>
    public const double Beside = 140;
    public const double Along = 80;

    /// <summary>The clear air kept round the drawing.</summary>
    public const double Margin = 20;

    /// <summary>How much room is left above a subgraph's own entities for its name.</summary>
    public const double Heading = 25;

    /// <summary>How wide what is written on a relationship runs before it wraps.</summary>
    public const double Widest = 200;

    public static ErConfig Default { get; } = new();

    /// <summary>How wide an entity is at least — <c>minEntityWidth</c>.</summary>
    public double MinWidth { get; init; } = Least;

    /// <summary>How deep one is at least — <c>minEntityHeight</c>.</summary>
    public double MinHeight { get; init; } = Shortest;

    /// <summary>The air either side of what is written in one — <c>entityPadding</c>.</summary>
    public double Padding { get; init; } = Inside;

    /// <summary>How big what is written in one is drawn — <c>fontSize</c>.</summary>
    public double TextSize { get; init; } = Size;

    /// <summary>How deep a row of attributes is, which follows how big the words are.</summary>
    public double RowHeight => Math.Round(TextSize * Lines);

    /// <summary>How far apart two entities in the same rank are set — <c>nodeSpacing</c>.</summary>
    public double NodeSpacing { get; init; } = Beside;

    /// <summary>How far apart one rank is set from the next — <c>rankSpacing</c>.</summary>
    public double RankSpacing { get; init; } = Along;

    /// <summary>The clear air kept round the drawing — <c>diagramPadding</c>.</summary>
    public double Air { get; init; } = Margin;

    /// <summary>The room left above a subgraph's entities for its name — <c>titleTopMargin</c>.</summary>
    public double TitleMargin { get; init; } = Heading;

    /// <summary>What every entity is filled with, where the front matter says — otherwise the theme's own.</summary>
    public string? Fill { get; init; }

    /// <summary>And what every entity is outlined in.</summary>
    public string? Stroke { get; init; }

    /// <summary>Which way it is laid out where no <c>direction</c> line says — <c>layoutDirection</c>.</summary>
    public string? Way { get; init; }

    /// <summary>How wide what is written on a relationship runs before it wraps.</summary>
    public double Wrapping { get; init; } = Widest;

    /// <summary>Whether a label wraps of its own accord, which <c>markdownAutoWrap: false</c> turns off.</summary>
    public bool Wraps { get; init; } = true;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static ErConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static ErConfig From(MermaidConfig config)
    {
        var entities = config.Diagram("er");

        return new ErConfig
        {
            MinWidth = entities.Number("minEntityWidth") is { } wide and >= 0 ? wide : Least,
            MinHeight = entities.Number("minEntityHeight") is { } deep and >= 0 ? deep : Shortest,
            Padding = entities.Number("entityPadding") is { } inside and >= 0 ? inside : Inside,
            TextSize = entities.Number("fontSize") is { } size and > 0 ? size : Size,
            NodeSpacing = entities.Number("nodeSpacing") is { } beside and >= 0 ? beside : Beside,
            RankSpacing = entities.Number("rankSpacing") is { } along and >= 0 ? along : Along,
            Air = entities.Number("diagramPadding") is { } margin and >= 0 ? margin : Margin,
            TitleMargin = entities.Number("titleTopMargin") is { } heading and >= 0 ? heading : Heading,
            Fill = entities.Value("fill"),
            Stroke = entities.Value("stroke"),
            Way = entities.Value("layoutDirection"),
            Wraps = config.Shared.Flag("markdownAutoWrap") ?? true,
        };
    }
}
