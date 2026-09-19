namespace Nexaflow.Markdown.Mermaid.Requirement;

/// <summary>
/// What a requirement diagram's front matter asks for, under <c>config: requirement:</c>: how small a box may be, the air kept
/// inside one, how big its words are drawn and how deep a row of them is.
///
/// Mermaid's own smallest box is 200 by 200, which leaves one mostly empty where the words in it are measured rather than
/// guessed, so what a box takes is what it is at least. <c>rect_fill</c>, <c>text_color</c>, <c>rect_border_size</c> and
/// <c>rect_border_color</c> name colours the theme decides here, and <c>useMaxWidth</c>, <c>theme</c>, <c>look</c> and
/// <c>layout</c> name a renderer, a drawing style and a layout engine.
/// </summary>
public sealed record RequirementConfig
{
    /// <summary>How wide and how deep a box is at least, whatever little is written in it.</summary>
    public const double Least = 96;
    public const double Shortest = 34;

    /// <summary>The air kept either side of what is written inside a box.</summary>
    public const double Inside = 12;

    /// <summary>How big what is written in a box is drawn, and how deep a row of it is.</summary>
    public const double Size = 12;
    public const double Row = 18;

    /// <summary>How far apart two boxes in the same rank are set, and one rank from the next.</summary>
    public const double Beside = 50;
    public const double Along = 50;

    /// <summary>The clear air kept round the drawing.</summary>
    public const double Margin = 8;

    /// <summary>How wide what a field says runs before it wraps.</summary>
    public const double Widest = 200;

    public static RequirementConfig Default { get; } = new();

    /// <summary>How wide a box is at least — <c>rect_min_width</c>.</summary>
    public double MinWidth { get; init; } = Least;

    /// <summary>How deep one is at least — <c>rect_min_height</c>.</summary>
    public double MinHeight { get; init; } = Shortest;

    /// <summary>The air either side of what is written in one — <c>rect_padding</c>.</summary>
    public double Padding { get; init; } = Inside;

    /// <summary>How big what is written in one is drawn — <c>fontSize</c>.</summary>
    public double TextSize { get; init; } = Size;

    /// <summary>How deep a row of it is — <c>line_height</c>.</summary>
    public double RowHeight { get; init; } = Row;

    /// <summary>How far apart two boxes in the same rank are set.</summary>
    public double NodeSpacing { get; init; } = Beside;

    /// <summary>How far apart one rank is set from the next.</summary>
    public double RankSpacing { get; init; } = Along;

    /// <summary>The clear air kept round the drawing.</summary>
    public double Air { get; init; } = Margin;

    /// <summary>How wide what a field says runs before it wraps.</summary>
    public double Wrapping { get; init; } = Widest;

    /// <summary>Whether a value wraps of its own accord, which <c>markdownAutoWrap: false</c> turns off.</summary>
    public bool Wraps { get; init; } = true;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static RequirementConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static RequirementConfig From(MermaidConfig config)
    {
        var requirements = config.Diagram("requirement");

        return new RequirementConfig
        {
            MinWidth = requirements.Number("rect_min_width") is { } wide and >= 0 ? wide : Least,
            MinHeight = requirements.Number("rect_min_height") is { } deep and >= 0 ? deep : Shortest,
            Padding = requirements.Number("rect_padding") is { } inside and >= 0 ? inside : Inside,
            TextSize = requirements.Number("fontSize") is { } size and > 0 ? size : Size,
            RowHeight = requirements.Number("line_height") is { } row and > 0 ? row : Row,
            Wraps = config.Shared.Flag("markdownAutoWrap") ?? true,
        };
    }
}
