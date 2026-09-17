namespace Nexaflow.Markdown.Mermaid.Timeline;

/// <summary>
/// What a <c>timeline</c> block's front matter asks for: <c>config: timeline:</c> <c>disableMulticolor</c> and
/// <c>padding</c>, and the <c>themeVariables:</c> colour slots <c>cScale0</c>…<c>cScale11</c> with the ink to write on
/// them, <c>cScaleLabel0</c>…<c>cScaleLabel11</c>.
///
/// <para>
/// The slots are kept by the number they were written for, so <c>cScale2</c> alone leaves nought and one to the theme,
/// and a label's ink always pairs with the fill it was written beside.
/// </para>
/// </summary>
public sealed record TimelineConfig
{
    /// <summary>How many colour slots Mermaid's theme has.</summary>
    public const int Slots = 12;

    public static TimelineConfig Default { get; } = new();

    /// <summary>Whether every period takes the first slot rather than one of its own.</summary>
    public bool DisableMulticolor { get; init; }

    /// <summary>The clear air inside a period's or an event's box, or null for the theme's.</summary>
    public double? Padding { get; init; }

    /// <summary>The fill written for a slot, by its number — what is not written is the theme's.</summary>
    public IReadOnlyDictionary<int, string> Scale { get; init; } = new Dictionary<int, string>();

    /// <summary>The ink written for a slot's words, by its number.</summary>
    public IReadOnlyDictionary<int, string> ScaleLabel { get; init; } = new Dictionary<int, string>();

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static TimelineConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static TimelineConfig From(MermaidConfig config)
    {
        var timeline = config.Diagram("timeline");
        var theme = config.Theme;

        return new TimelineConfig
        {
            DisableMulticolor = timeline.Flag("disableMulticolor") ?? false,
            Padding = timeline.Number("padding") is { } padding and >= 0 ? padding : null,
            Scale = theme.Swatches("cScale", Slots, first: 0),
            ScaleLabel = theme.Swatches("cScaleLabel", Slots, first: 0),
        };
    }

    /// <summary>The fill written for a slot, or null for the theme's.</summary>
    public string? ScaleAt(int slot) => Scale.GetValueOrDefault(slot);

    /// <summary>The ink written for a slot's words, or null for the theme's.</summary>
    public string? ScaleLabelAt(int slot) => ScaleLabel.GetValueOrDefault(slot);
}
