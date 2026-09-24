namespace Nexaflow.Markdown.Mermaid.Sequence;

/// <summary>
/// What a sequence diagram's front matter asks for, under <c>config: sequence:</c>: how far apart the lifelines stand, how far
/// down one message is from the next, how big the words are drawn, how wide a bar and a frame's tab are, and whether the
/// participants are repeated at the bottom.
///
/// Mermaid's own defaults, every one of them applied — the words are measured rather than guessed, so <c>width</c> is how wide
/// a participant grows before its name wraps and <c>height</c> how deep one is at least. <c>useMaxWidth</c>,
/// <c>arrowMarkerAbsolute</c>, <c>theme</c> and <c>look</c> name a renderer and a drawing style, and the font families and
/// weights name fonts a page has and a layout tree does not.
/// </summary>
public sealed record SequenceConfig
{
    /// <summary>How wide the bar saying a participant is working is drawn.</summary>
    public const double Working = 10;

    /// <summary>The clear air kept round the drawing, across and down.</summary>
    public const double AcrossMargin = 50;
    public const double DownMargin = 10;

    /// <summary>The least space between one participant's box and the next.</summary>
    public const double Apart = 50;

    /// <summary>How wide a participant grows before its name wraps, and how deep one is at least.</summary>
    public const double Wide = 150;
    public const double Deep = 50;

    /// <summary>The air a frame keeps round the messages inside it, and the air round the words in its tab.</summary>
    public const double Framing = 10;
    public const double Tabbing = 5;

    /// <summary>The air a note keeps round its words.</summary>
    public const double Noting = 10;

    /// <summary>How far down one message is from the next.</summary>
    public const double Down = 35;

    /// <summary>How much of a message's own room is left under the last one.</summary>
    public const double Under = 1;

    /// <summary>How big a participant's name, a note and what a message says are drawn.</summary>
    public const double NameSize = 14;
    public const double NoteSize = 14;
    public const double SaidSize = 16;

    /// <summary>The air kept either side of words that wrap.</summary>
    public const double Padding = 10;

    /// <summary>How big the tab in the corner of a frame is.</summary>
    public const double TabWide = 50;
    public const double TabDeep = 20;

    /// <summary>Where words are set in the room they are given.</summary>
    public const string Centred = "center";

    public static SequenceConfig Default { get; } = new();

    /// <summary>How wide the bar on a lifeline is — <c>activationWidth</c>.</summary>
    public double Bar { get; init; } = Working;

    /// <summary>The clear air kept round the drawing — <c>diagramMarginX</c> and <c>diagramMarginY</c>.</summary>
    public double Across { get; init; } = AcrossMargin;
    public double Downward { get; init; } = DownMargin;

    /// <summary>The least space between one participant's box and the next — <c>actorMargin</c>.</summary>
    public double Between { get; init; } = Apart;

    /// <summary>How wide a participant grows before its name wraps — <c>width</c>.</summary>
    public double Widest { get; init; } = Wide;

    /// <summary>How deep a participant's box is at least — <c>height</c>.</summary>
    public double Tallest { get; init; } = Deep;

    /// <summary>The air a frame keeps round what is inside it — <c>boxMargin</c>.</summary>
    public double Framed { get; init; } = Framing;

    /// <summary>The air round the words in a frame's tab — <c>boxTextMargin</c>.</summary>
    public double Tabbed { get; init; } = Tabbing;

    /// <summary>The air a note keeps round its words — <c>noteMargin</c>.</summary>
    public double Noted { get; init; } = Noting;

    /// <summary>How far down one message is from the next — <c>messageMargin</c>.</summary>
    public double Apartness { get; init; } = Down;

    /// <summary>Where what a message says is set over its line — <c>messageAlign</c>.</summary>
    public string Aligned { get; init; } = Centred;

    /// <summary>Where a note's words are set in it — <c>noteAlign</c>.</summary>
    public string NoteAligned { get; init; } = Centred;

    /// <summary>Whether the participants are drawn again at the bottom — <c>mirrorActors</c>.</summary>
    public bool Mirrored { get; init; } = true;

    /// <summary>How much of a message's own room is left under the last one — <c>bottomMarginAdj</c>.</summary>
    public double Bottom { get; init; } = Under;

    /// <summary>
    /// Whether a message to a participant itself turns square rather than bowing — <c>rightAngles</c>. Square unless the front
    /// matter asks otherwise, which is where Nexaflow parts from Mermaid: a square loop reads as a step taken, where a bow is easily
    /// taken for a line on its way somewhere else.
    /// </summary>
    public bool Square { get; init; } = true;

    /// <summary>Whether every message is numbered without <c>autonumber</c> being written — <c>showSequenceNumbers</c>.</summary>
    public bool Numbered { get; init; }

    /// <summary>Whether a participant nothing reaches is left undrawn — <c>hideUnusedParticipants</c>.</summary>
    public bool HideUnused { get; init; }

    /// <summary>How big a participant's name is drawn — <c>actorFontSize</c>.</summary>
    public double NameText { get; init; } = NameSize;

    /// <summary>How big a note's words are drawn — <c>noteFontSize</c>.</summary>
    public double NoteText { get; init; } = NoteSize;

    /// <summary>How big what a message says is drawn — <c>messageFontSize</c>.</summary>
    public double SaidText { get; init; } = SaidSize;

    /// <summary>Whether words wrap of their own accord — <c>wrap</c>.</summary>
    public bool Wraps { get; init; }

    /// <summary>The air kept either side of words that wrap — <c>wrapPadding</c>.</summary>
    public double WrapAir { get; init; } = Padding;

    /// <summary>How big the tab in the corner of a frame is — <c>labelBoxWidth</c> and <c>labelBoxHeight</c>.</summary>
    public double TabWidth { get; init; } = TabWide;
    public double TabHeight { get; init; } = TabDeep;

    /// <summary>
    /// How wide words run before they wrap. Mermaid wraps none of its own accord — a message says what it says on one line and
    /// the lifelines stand far enough apart to hold it — so this is as wide as anything ever gets until <c>wrap</c> asks for it.
    /// </summary>
    public double Wrapping => Wraps ? Math.Max(20, Widest - (WrapAir * 2)) : Widest * 20;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static SequenceConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static SequenceConfig From(MermaidConfig config)
    {
        var sequence = config.Diagram("sequence");

        return new SequenceConfig
        {
            Bar = sequence.Number("activationWidth") is { } bar and >= 0 ? bar : Working,
            Across = sequence.Number("diagramMarginX") is { } across and >= 0 ? across : AcrossMargin,
            Downward = sequence.Number("diagramMarginY") is { } downward and >= 0 ? downward : DownMargin,
            Between = sequence.Number("actorMargin") is { } between and >= 0 ? between : Apart,
            Widest = sequence.Number("width") is { } widest and > 0 ? widest : Wide,
            Tallest = sequence.Number("height") is { } tallest and >= 0 ? tallest : Deep,
            Framed = sequence.Number("boxMargin") is { } framed and >= 0 ? framed : Framing,
            Tabbed = sequence.Number("boxTextMargin") is { } tabbed and >= 0 ? tabbed : Tabbing,
            Noted = sequence.Number("noteMargin") is { } noted and >= 0 ? noted : Noting,
            Apartness = sequence.Number("messageMargin") is { } apart and >= 0 ? apart : Down,
            Aligned = sequence.Value("messageAlign") ?? Centred,
            NoteAligned = sequence.Value("noteAlign") ?? Centred,
            Mirrored = sequence.Flag("mirrorActors") ?? true,
            Bottom = sequence.Number("bottomMarginAdj") is { } bottom and >= 0 ? bottom : Under,
            Square = sequence.Flag("rightAngles") ?? true,
            Numbered = sequence.Flag("showSequenceNumbers") ?? false,
            HideUnused = sequence.Flag("hideUnusedParticipants") ?? false,
            NameText = sequence.Number("actorFontSize") is { } named and > 0 ? named : NameSize,
            NoteText = sequence.Number("noteFontSize") is { } noting and > 0 ? noting : NoteSize,
            SaidText = sequence.Number("messageFontSize") is { } said and > 0 ? said : SaidSize,
            Wraps = sequence.Flag("wrap") ?? false,
            WrapAir = sequence.Number("wrapPadding") is { } air and >= 0 ? air : Padding,
            TabWidth = sequence.Number("labelBoxWidth") is { } tab and >= 0 ? tab : TabWide,
            TabHeight = sequence.Number("labelBoxHeight") is { } deep and >= 0 ? deep : TabDeep,
        };
    }
}
