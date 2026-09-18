namespace Nexaflow.Markdown.Mermaid.State;

/// <summary>
/// What a state diagram's front matter asks for, under <c>config: state:</c>: how far apart the states are set, how much clear air
/// the drawing keeps, how wide what is written on a state runs before it wraps, and how big the pieces Mermaid draws in one size
/// are — the bar a fork is drawn as, the room round a note and round the line dividing two regions.
///
/// <para>
/// Mermaid documents <c>sizeUnit</c>, <c>textHeight</c>, <c>titleShift</c>, <c>miniPadding</c>, <c>fontSizeFactor</c>,
/// <c>fontSize</c>, <c>labelHeight</c>, <c>edgeLengthFactor</c>, <c>compositTitleSize</c>, <c>radius</c>,
/// <c>arrowMarkerAbsolute</c>, <c>defaultRenderer</c>, <c>useMaxWidth</c>, <c>theme</c>, <c>look</c> and <c>layout</c> beside these.
/// They size a drawing made of SVG text, guess how wide a label will come out before laying it out, or name a renderer, a style and
/// a layout engine — and here the words are measured rather than guessed, the drawing is the layout tree, and there is one layout.
/// </para>
/// </summary>
public sealed record StateConfig
{
    /// <summary>How far apart Mermaid sets two states side by side when the front matter asks for nothing.</summary>
    public const double Beside = 50;

    /// <summary>How far apart Mermaid sets one rank of states from the next.</summary>
    public const double Along = 50;

    /// <summary>The clear air Mermaid keeps inside a state.</summary>
    public const double Air = 8;

    /// <summary>How wide Mermaid lets what is written on a state run before it wraps.</summary>
    public const double Widest = 120;

    /// <summary>The least room Mermaid gives what is written on a state, so states with little to say come out alike.</summary>
    public const double Narrowest = 120;

    /// <summary>The room Mermaid keeps round a note, and round the line dividing two regions.</summary>
    public const double Noted = 10;
    public const double Divided = 10;

    /// <summary>How wide and how deep Mermaid draws the bar a fork or a join is.</summary>
    public const double Forked = 70;
    public const double Thick = 7;

    public static StateConfig Default { get; } = new();

    /// <summary>How far apart two states in the same rank are set.</summary>
    public double NodeSpacing { get; init; } = Beside;

    /// <summary>How far apart one rank is set from the next.</summary>
    public double RankSpacing { get; init; } = Along;

    /// <summary>The clear air kept inside a state, round what is written on it.</summary>
    public double Padding { get; init; } = Air;

    /// <summary>How wide what is written on a state runs before it wraps.</summary>
    public double Wrapping { get; init; } = Widest;

    /// <summary>The least room what is written on a state takes.</summary>
    public double LeastWidth { get; init; } = Narrowest;

    /// <summary>How much room is left above a composite state's own states for what is written on it.</summary>
    public double TitleMargin { get; init; }

    /// <summary>The clear air round a note.</summary>
    public double NoteMargin { get; init; } = Noted;

    /// <summary>The clear air round the line dividing two regions of a composite state.</summary>
    public double DividerMargin { get; init; } = Divided;

    /// <summary>How wide the bar a fork or a join is drawn as runs, and how deep it is.</summary>
    public double ForkWidth { get; init; } = Forked;

    public double ForkHeight { get; init; } = Thick;

    /// <summary>Whether a label wraps of its own accord, which <c>markdownAutoWrap: false</c> turns off.</summary>
    public bool Wraps { get; init; } = true;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static StateConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static StateConfig From(MermaidConfig config)
    {
        var states = config.Diagram("state");

        return new StateConfig
        {
            NodeSpacing = states.Number("nodeSpacing") is { } beside and >= 0 ? beside : Beside,
            RankSpacing = states.Number("rankSpacing") is { } along and >= 0 ? along : Along,
            Padding = states.Number("padding") is { } air and >= 0 ? air : Air,
            Wrapping = states.Number("wrappingWidth") is { } widest and > 0 ? widest : Widest,
            LeastWidth = states.Number("minNodeWidth") is { } narrowest and >= 0 ? narrowest : Narrowest,
            TitleMargin = states.Number("titleTopMargin") is { } margin and >= 0 ? margin : 0,
            NoteMargin = states.Number("noteMargin") is { } noted and >= 0 ? noted : Noted,
            DividerMargin = states.Number("dividerMargin") is { } divided and >= 0 ? divided : Divided,
            ForkWidth = states.Number("forkWidth") is { } forked and > 0 ? forked : Forked,
            ForkHeight = states.Number("forkHeight") is { } thick and > 0 ? thick : Thick,
            Wraps = config.Shared.Flag("markdownAutoWrap") ?? true,
        };
    }
}
