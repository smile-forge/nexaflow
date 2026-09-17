namespace Nexaflow.Markdown.Mermaid.Kanban;

/// <summary>
/// What a <c>kanban</c> block's front matter asks for: <c>config: kanban:</c> <c>ticketBaseUrl</c>, <c>sectionWidth</c>,
/// <c>padding</c> and <c>useMaxWidth</c>, and its colours under <c>themeVariables:</c> — each column's <c>cScale</c> and
/// <c>cScaleLabel</c>, and a card's <c>background</c>, <c>nodeBorder</c> and <c>textColor</c>.
///
/// <para>A size nobody wrote is Mermaid's own, and a colour nobody wrote the theme's, so it is null here.</para>
/// </summary>
public sealed record KanbanConfig
{
    public static KanbanConfig Default { get; } = new();

    /// <summary>Where a card's ticket links to, <c>#TICKET#</c> standing for the ticket.</summary>
    public string? TicketBaseUrl { get; init; }

    public double? SectionWidth { get; init; }
    public double? Padding { get; init; }
    public bool UseMaxWidth { get; init; }

    /// <summary>The <c>cScale</c> and <c>cScaleLabel</c> colours written, by their number.</summary>
    public IReadOnlyDictionary<int, string> Scale { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> ScaleLabel { get; init; } = new Dictionary<int, string>();

    public string? Background { get; init; }
    public string? NodeBorder { get; init; }
    public string? TextColour { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static KanbanConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static KanbanConfig From(MermaidConfig config)
    {
        var kanban = config.Diagram("kanban");
        var theme = config.Theme;

        return new KanbanConfig
        {
            TicketBaseUrl = kanban.Value("ticketBaseUrl") is { Length: > 0 } url ? url : null,
            SectionWidth = kanban.Size("sectionWidth"),
            Padding = kanban.Number("padding") is { } padding ? Math.Max(0, padding) : null,
            UseMaxWidth = kanban.Flag("useMaxWidth") ?? false,
            Scale = theme.Swatches("cScale", 12, first: 0),
            ScaleLabel = theme.Swatches("cScaleLabel", 12, first: 0),
            Background = theme.Value("background"),
            NodeBorder = theme.Value("nodeBorder"),
            TextColour = theme.Value("textColor"),
        };
    }
}
