namespace Nexaflow.Markdown.Mermaid.Journey;

/// <summary>
/// What a <c>journey</c> block's front matter asks for: <c>config: journey:</c> <c>width</c>, <c>height</c>,
/// <c>boxMargin</c> and <c>taskFontSize</c> for how big a task is drawn, and the colour lists <c>actorColours</c> and
/// <c>sectionFills</c> — each written in brackets or as the lines under it — with the <c>themeVariables</c>
/// <c>fillType0</c>…<c>fillType7</c> slots behind the sections' list.
///
/// <para>A size or a colour nobody wrote is the theme's, so it is null here.</para>
/// </summary>
public sealed record JourneyConfig
{
    /// <summary>How many section fills Mermaid's theme has.</summary>
    public const int Fills = 8;

    public static JourneyConfig Default { get; } = new();

    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? BoxMargin { get; init; }
    public double? TaskFontSize { get; init; }

    /// <summary>The fills written for the sections, in the order they are written.</summary>
    public IReadOnlyList<string> SectionFills { get; init; } = [];

    /// <summary>The colours written for the actors, in the order the actors first take part.</summary>
    public IReadOnlyList<string> ActorColours { get; init; } = [];

    /// <summary>The theme's own section fills, by the number each was written for.</summary>
    public IReadOnlyDictionary<int, string> FillTypes { get; init; } = new Dictionary<int, string>();

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static JourneyConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static JourneyConfig From(MermaidConfig config)
    {
        var journey = config.Diagram("journey");

        return new JourneyConfig
        {
            Width = journey.Size("width"),
            Height = journey.Size("height"),
            BoxMargin = journey.Number("boxMargin") is { } margin and >= 0 ? margin : null,
            TaskFontSize = journey.Size("taskFontSize"),
            SectionFills = journey.List("sectionFills"),
            ActorColours = journey.List("actorColours"),
            FillTypes = config.Theme.Swatches("fillType", Fills, first: 0),
        };
    }

    /// <summary>What a section is filled with: the fill written for it, then the theme's slot of that number, else nothing.</summary>
    public string? SectionFill(int at) =>
        at >= 0 && at < SectionFills.Count ? SectionFills[at] : FillTypes.GetValueOrDefault(at);

    /// <summary>What an actor is drawn in, or nothing where none is written for them.</summary>
    public string? ActorColour(int at) => at >= 0 && at < ActorColours.Count ? ActorColours[at] : null;
}
