namespace Nexaflow.Markdown.Mermaid.Architecture;

/// <summary>
/// What an <c>architecture-beta</c> block's front matter asks for, under <c>config: architecture:</c>: how big an icon is
/// drawn, how big what is written under it is, the clear air round a group's edge, and how far apart two services sit.
///
/// <para>
/// Mermaid lays an architecture diagram out by running a force-directed solver over the sides its edges name, so it also
/// takes <c>randomize</c>, <c>seed</c>, <c>numIter</c> and <c>edgeElasticity</c> to steer and to settle that solver. Here
/// the sides are followed exactly as they are written (<c>ArchitectureBuilder</c>), so the drawing is the
/// same every time and there is nothing for those to ask.
/// </para>
/// </summary>
public sealed record ArchitectureConfig
{
    /// <summary>How big Mermaid draws an icon when the front matter asks for no size.</summary>
    public const double Icon = 40;

    /// <summary>How far apart Mermaid leaves two services in the same group.</summary>
    public const double Apart = 75;

    public static ArchitectureConfig Default { get; } = new();

    /// <summary>How big the icon over a service is drawn.</summary>
    public double IconSize { get; init; } = Icon;

    /// <summary>How big what is written under a service is.</summary>
    public double? FontSize { get; init; }

    /// <summary>The clear air between a group's edge and what is in it.</summary>
    public double? Padding { get; init; }

    /// <summary>How far apart two services beside one another sit.</summary>
    public double NodeSeparation { get; init; } = Apart;

    /// <summary>How far apart two services sharing a cell are spread, as a multiple of <see cref="IconSize"/>.</summary>
    public double EdgeLength { get; init; } = 1.5;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static ArchitectureConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static ArchitectureConfig From(MermaidConfig config)
    {
        var architecture = config.Diagram("architecture");

        return new ArchitectureConfig
        {
            IconSize = architecture.Size("iconSize") is { } icon and > 0 ? icon : Icon,
            FontSize = architecture.Size("fontSize") is { } font and > 0 ? font : null,
            Padding = architecture.Number("padding") is { } padding and >= 0 ? padding : null,
            NodeSeparation = architecture.Number("nodeSeparation") is { } apart and >= 0 ? apart : Apart,
            EdgeLength = architecture.Number("idealEdgeLengthMultiplier") is { } length and > 0 ? length : 1.5,
        };
    }
}
