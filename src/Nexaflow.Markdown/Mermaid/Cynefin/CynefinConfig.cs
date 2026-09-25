namespace Nexaflow.Markdown.Mermaid.Cynefin;

/// <summary>
/// What a <c>cynefin-beta</c> block's front matter asks for: the grid under <c>config: cynefin:</c>, and how it is drawn
/// under <c>themeVariables: cynefin:</c> — each domain's background, the boundaries between them, the cliff between clear
/// and chaotic, the transition arrows, and the ink and size of what is written — or under <c>themeVariables:</c> itself
/// where nothing names the diagram.
///
/// <para>
/// A size or a colour nobody wrote is the theme's, so it is null here. <c>width</c> and <c>height</c> are the least the
/// grid is drawn at: it grows past them to hold what is written in it, which is what keeps every item drawn.
/// </para>
/// </summary>
public sealed record CynefinConfig
{
    public static CynefinConfig Default { get; } = new();

    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? Padding { get; init; }

    /// <summary>Whether each domain says how it is worked under its name — which Mermaid shows unless the front matter says not to.</summary>
    public bool ShowDomainDescriptions { get; init; } = true;

    /// <summary>The fill written for each domain, in the order <see cref="CynefinGrammar.Domains"/> lists them — null where none is.</summary>
    public IReadOnlyList<string?> DomainFills { get; init; } = [null, null, null, null, null];

    /// <summary>What the boundaries between the domains are drawn in.</summary>
    public string? BoundaryColour { get; init; }
    public double? BoundaryWidth { get; init; }

    /// <summary>What the cliff between clear and chaotic is drawn in — the fall Cynefin draws heavier than the rest.</summary>
    public string? CliffColour { get; init; }
    public double? CliffWidth { get; init; }

    /// <summary>What a movement from one domain to another is drawn in.</summary>
    public string? ArrowColour { get; init; }
    public double? ArrowWidth { get; init; }

    /// <summary>What a domain's name is written in, and how big.</summary>
    public string? LabelColour { get; init; }
    public double? DomainFontSize { get; init; }

    /// <summary>What an item and a domain's description are written in, and how big.</summary>
    public string? TextColour { get; init; }
    public double? ItemFontSize { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static CynefinConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static CynefinConfig From(MermaidConfig config)
    {
        var cynefin = config.Diagram("cynefin");
        var theme = config.DiagramTheme("cynefin");

        return new CynefinConfig
        {
            Width = cynefin.Size("width"),
            Height = cynefin.Size("height"),
            Padding = cynefin.Number("padding") is { } padding and >= 0 ? padding : null,
            ShowDomainDescriptions = cynefin.Flag("showDomainDescriptions") ?? true,

            DomainFills = [.. new[] { "clearBg", "complicatedBg", "complexBg", "chaoticBg", "confusionBg" }.Select(Colour)],
            BoundaryColour = Colour("boundaryColor"),
            BoundaryWidth = Size("boundaryWidth"),
            CliffColour = Colour("cliffColor"),
            CliffWidth = Size("cliffWidth"),
            ArrowColour = Colour("arrowColor"),
            ArrowWidth = Size("arrowWidth"),
            LabelColour = Colour("labelColor"),
            DomainFontSize = Size("domainFontSize"),
            TextColour = Colour("textColor"),
            ItemFontSize = Size("itemFontSize"),
        };

        string? Colour(string key) => theme.Value(key) ?? config.Theme.Value(key);

        double? Size(string key) => theme.Size(key) ?? config.Theme.Size(key);
    }
}
