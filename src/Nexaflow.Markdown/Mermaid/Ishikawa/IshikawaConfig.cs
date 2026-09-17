namespace Nexaflow.Markdown.Mermaid.Ishikawa;

/// <summary>
/// What an <c>ishikawa</c> block's front matter asks for: the room round the diagram under <c>config: ishikawa:</c>, the size
/// its words are written at (<c>config: fontSize</c>), and its colours under <c>themeVariables:</c> — <c>lineColor</c> for the
/// bones and the outlines, <c>mainBkg</c> for the head and the causes' boxes, <c>textColor</c> for the words.
///
/// <para>A size or a colour nobody wrote is the theme's, so it is null here.</para>
/// </summary>
public sealed record IshikawaConfig
{
    public static IshikawaConfig Default { get; } = new();

    /// <summary>The room round the diagram, beyond the room every diagram has.</summary>
    public double? DiagramPadding { get; init; }

    /// <summary>Mermaid's word for letting the diagram shrink to its room — read and kept; a diagram here is drawn to its content.</summary>
    public bool UseMaxWidth { get; init; }

    public double? FontSize { get; init; }

    public string? LineColour { get; init; }
    public string? Background { get; init; }
    public string? TextColour { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static IshikawaConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static IshikawaConfig From(MermaidConfig config)
    {
        var diagram = config.Diagram("ishikawa");
        var theme = config.Theme;

        return new IshikawaConfig
        {
            DiagramPadding = diagram.Number("diagramPadding") is { } padding ? Math.Max(0, padding) : null,
            UseMaxWidth = diagram.Flag("useMaxWidth") ?? false,
            FontSize = config.Shared.Size("fontSize"),
            LineColour = theme.Value("lineColor"),
            Background = theme.Value("mainBkg"),
            TextColour = theme.Value("textColor"),
        };
    }
}
