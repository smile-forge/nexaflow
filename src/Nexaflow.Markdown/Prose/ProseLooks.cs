namespace Nexaflow.Markdown.Prose;

/// <summary>
/// How a <see cref="MarkdownKinds.Styled"/> run is set, as its source spelled it out: the face, the size, the weight and
/// the colours. WPF-free, so a reader of the tree that has no desktop can still ask what a run looks like; the builder is
/// what turns it into a face to draw with.
///
/// <para>
/// <strong>Sizes are relative, not absolute.</strong> <see cref="Scale"/> is against the size the source calls ordinary
/// text, so a document set in one size is drawn at the reader's size and its headings and footnotes stay as much larger or
/// smaller than that as they were written. Colours are absolute, because a writer who coloured a word red meant red.
/// </para>
/// <para>
/// A record, so two runs set alike are equal and a reader can tell where one look ends and the next begins by asking.
/// </para>
/// </summary>
public sealed record TextLook
{
    /// <summary>The family it is set in by name, or null for the reader's own.</summary>
    public string? Font { get; init; }

    /// <summary>How big, against the size the source calls ordinary text.</summary>
    public double Scale { get; init; } = 1;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool Strike { get; init; }

    /// <summary>Set in capitals whatever case it was written in.</summary>
    public bool Capitals { get; init; }

    /// <summary>Whether it is set below or above the line.</summary>
    public TextScript Script { get; init; }

    /// <summary>The colour of the letters as <c>0xAARRGGBB</c>, or null for the reader's text colour.</summary>
    public uint? Ink { get; init; }

    /// <summary>What is washed behind the letters as <c>0xAARRGGBB</c>, or null for nothing.</summary>
    public uint? Wash { get; init; }

    /// <summary>A run that says nothing about how it is set.</summary>
    public static TextLook Plain { get; } = new();
}

/// <summary>Which line a run sits on, beside the ordinary one.</summary>
public enum TextScript
{
    /// <summary>On the line.</summary>
    None,

    /// <summary>Below it and smaller.</summary>
    Sub,

    /// <summary>Above it and smaller.</summary>
    Super,
}

/// <summary>
/// How a paragraph is set, as its source spelled it out: which way its lines are set, how far in it stands, and how much
/// room is left round it. Everything is measured in the size the source calls ordinary text, for the reason
/// <see cref="TextLook.Scale"/> is.
/// </summary>
public sealed record ParagraphLook
{
    /// <summary>Which way its lines are set.</summary>
    public ParagraphAlign Align { get; init; }

    /// <summary>How far in from the start every line stands.</summary>
    public double Indent { get; init; }

    /// <summary>How far in from the end every line stops.</summary>
    public double EndIndent { get; init; }

    /// <summary>How much further in the first line starts than the rest — negative for a hanging first line.</summary>
    public double FirstLine { get; init; }

    /// <summary>The room above it.</summary>
    public double Before { get; init; }

    /// <summary>The room below it.</summary>
    public double After { get; init; }

    /// <summary>Where its tab stops are, from the start of the line, nearest first.</summary>
    public IReadOnlyList<double> Stops { get; init; } = [];

    /// <summary>How far apart the tab stops are past the last of <see cref="Stops"/>.</summary>
    public double EveryStop { get; init; } = 3;

    /// <summary>
    /// Where the next tab stop is past <paramref name="at"/>, measured from the start of the line. The paragraph's own indent
    /// is a stop too, where the first line hangs out past it — which is what puts the words of a bulleted line under one
    /// another rather than under the bullet.
    /// </summary>
    public double NextStop(double at)
    {
        const double Past = 1e-6;

        var next = double.PositiveInfinity;

        if (FirstLine < 0 && Indent > at + Past) next = Indent;

        foreach (var stop in Stops)
            if (stop > at + Past)
            {
                next = Math.Min(next, stop);
                break;
            }

        if (!double.IsPositiveInfinity(next)) return next;

        var every = EveryStop > 0 ? EveryStop : 3;

        return (Math.Floor((at + Past) / every) + 1) * every;
    }
}

/// <summary>Which way a paragraph's lines are set.</summary>
public enum ParagraphAlign
{
    /// <summary>Against the start, ragged at the end.</summary>
    Start,

    /// <summary>Centred between the two.</summary>
    Centre,

    /// <summary>Against the end, ragged at the start.</summary>
    End,

    /// <summary>Against both, the space between the words widened to fill the line — all but the last line.</summary>
    Justified,
}

/// <summary>
/// A picture whose bytes are written into the source itself rather than named — RTF's <c>\pict</c>. What it is, and how big
/// its writer asked for it to be drawn, in the size the source calls ordinary text; null where the writer did not say and
/// the picture's own size stands.
/// </summary>
/// <param name="MediaType">What the bytes are: <c>image/png</c>, <c>image/jpeg</c>, <c>image/emf</c>, <c>image/wmf</c>, <c>image/bmp</c>.</param>
/// <param name="Bytes">The picture, as written.</param>
/// <param name="Width">How wide it is drawn, or null for its own width.</param>
/// <param name="Height">How tall it is drawn, or null for its own height.</param>
public sealed record EmbeddedPicture(string MediaType, byte[] Bytes, double? Width, double? Height)
{
    /// <summary>
    /// How a Windows metafile written without a header of its own is played — what a <c>METAFILEPICT</c> says of it — or
    /// null for any other picture.
    /// </summary>
    public PictureMetafile? Metafile { get; init; }
}

/// <summary>
/// What a Windows metafile needs said about it to be played, where it carries no header saying so: its mapping mode and its
/// extent, in hundredths of a millimetre for the modes that scale.
/// </summary>
public sealed record PictureMetafile(int Mapping, int Width, int Height);
