namespace Nexaflow.Markdown.Mermaid.Cynefin;

/// <summary>
/// What a <c>cynefin-beta</c> block's front matter asks for: the grid under <c>config: cynefin:</c>, and each domain's
/// background under <c>themeVariables: cynefin:</c> (<c>complexBg</c>, <c>clearBg</c>, <c>boundaryColor</c> and the rest),
/// or under <c>themeVariables:</c> itself where nothing names the diagram.
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

    /// <summary>Whether each domain says how it is worked under its name — <c>probe · sense · respond</c>.</summary>
    public bool ShowDomainDescriptions { get; init; }

    /// <summary>The fill written for each domain, in <see cref="CynefinDomain"/> order — null where none is.</summary>
    public IReadOnlyList<string?> DomainFills { get; init; } = [null, null, null, null, null];

    /// <summary>What the line round the grid and the boundaries inside it are drawn in.</summary>
    public string? BoundaryColour { get; init; }

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
            ShowDomainDescriptions = cynefin.Flag("showDomainDescriptions") ?? false,
            DomainFills = [.. new[] { "clearBg", "complicatedBg", "complexBg", "chaoticBg", "confusionBg" }.Select(Colour)],
            BoundaryColour = Colour("boundaryColor"),
        };

        string? Colour(string key) => theme.Value(key) ?? config.Theme.Value(key);
    }
}
