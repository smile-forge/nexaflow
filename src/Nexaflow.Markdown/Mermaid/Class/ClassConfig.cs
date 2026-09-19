namespace Nexaflow.Markdown.Mermaid.Class;

/// <summary>
/// What a class diagram's front matter asks for, under <c>config: class:</c>: how far apart the classes are set, how much clear
/// air the drawing keeps, how wide a member runs before it wraps, whether a class with no members keeps a box for them, and
/// whether a namespace written with dots in its name nests.
///
/// Mermaid documents <c>textHeight</c>, <c>arrowMarkerAbsolute</c>, <c>htmlLabels</c>, <c>defaultRenderer</c>,
/// <c>useMaxWidth</c>, <c>theme</c>, <c>look</c> and <c>layout</c> beside these. They size a drawing made of SVG text or name a
/// renderer, a style and a layout engine — and here the words are measured rather than guessed, and there is one layout.
/// </summary>
public sealed record ClassConfig
{
    /// <summary>How far apart Mermaid sets two classes side by side when the front matter asks for nothing.</summary>
    public const double Beside = 50;

    /// <summary>How far apart Mermaid sets one rank of classes from the next.</summary>
    public const double Along = 50;

    /// <summary>The clear air Mermaid keeps round the drawing.</summary>
    public const double Air = 8;

    /// <summary>The room Mermaid keeps either side of the line dividing a class's compartments.</summary>
    public const double Divided = 10;

    /// <summary>How wide Mermaid lets a member run before it wraps.</summary>
    public const double Widest = 200;

    public static ClassConfig Default { get; } = new();

    /// <summary>How far apart two classes in the same rank are set.</summary>
    public double NodeSpacing { get; init; } = Beside;

    /// <summary>How far apart one rank is set from the next.</summary>
    public double RankSpacing { get; init; } = Along;

    /// <summary>The clear air kept round the drawing.</summary>
    public double Padding { get; init; } = Air;

    /// <summary>The room kept either side of the line dividing a class's compartments.</summary>
    public double DividerMargin { get; init; } = Divided;

    /// <summary>How much room is left above a namespace's own classes for its name.</summary>
    public double TitleMargin { get; init; }

    /// <summary>How wide a member runs before it wraps.</summary>
    public double Wrapping { get; init; } = Widest;

    /// <summary>Whether a class with no members is drawn without the box they would go in.</summary>
    public bool HideEmptyMembers { get; init; }

    /// <summary>Whether <c>namespace A.B</c> boxes B inside A, rather than drawing one box called <c>A.B</c>.</summary>
    public bool Hierarchical { get; init; } = true;

    /// <summary>Whether a label wraps of its own accord, which <c>markdownAutoWrap: false</c> turns off.</summary>
    public bool Wraps { get; init; } = true;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static ClassConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static ClassConfig From(MermaidConfig config)
    {
        var classes = config.Diagram("class");

        return new ClassConfig
        {
            NodeSpacing = classes.Number("nodeSpacing") is { } beside and >= 0 ? beside : Beside,
            RankSpacing = classes.Number("rankSpacing") is { } along and >= 0 ? along : Along,
            Padding = classes.Number("diagramPadding") is { } air and >= 0 ? air : Air,
            DividerMargin = classes.Number("dividerMargin") is { } divided and >= 0 ? divided : Divided,
            TitleMargin = classes.Number("titleTopMargin") is { } margin and >= 0 ? margin : 0,
            Wrapping = classes.Number("wrappingWidth") is { } widest and > 0 ? widest : Widest,
            HideEmptyMembers = classes.Flag("hideEmptyMembersBox") ?? false,
            Hierarchical = classes.Flag("hierarchicalNamespaces") ?? true,
            Wraps = config.Shared.Flag("markdownAutoWrap") ?? true,
        };
    }
}
