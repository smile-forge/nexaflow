using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Rtf;

/// <summary>What the characters inside a group are for.</summary>
internal enum RtfDestination
{
    /// <summary>The document's text: drawn.</summary>
    Text,

    /// <summary>Something no reader of the text needs — read past.</summary>
    Skip,

    /// <summary>Settings that are read and never drawn: a nested row's definition.</summary>
    Settings,

    /// <summary>The font table.</summary>
    Fonts,

    /// <summary>The colour table.</summary>
    Colours,

    /// <summary>What a field is — <c>HYPERLINK "…"</c> — rather than what it shows.</summary>
    Instruction,

    /// <summary>A picture's bytes and what they are.</summary>
    Picture,

    /// <summary>A drawing shape's description: read for what the shape shows, and never drawn itself.</summary>
    Shape,

    /// <summary>The name of one of a shape's properties, which says what the value after it is.</summary>
    Property,
}

/// <summary>
/// Everything a group of RTF sets, which its close undoes: how the characters are set, how the paragraph is, and what the
/// characters are for. A group starts as a copy of the one holding it.
/// </summary>
internal sealed class RtfGroup
{
    public RtfDestination Destination { get; set; }

    /// <summary>Whether a <c>\*</c> said the next control word names a destination a reader may skip.</summary>
    public bool Starred { get; set; }

    /// <summary>Where in the paragraph being read this group's first piece is.</summary>
    public int Begins { get; set; }

    /// <summary>Which paragraph that was, counted from the first — so a group can tell whether it is still being read.</summary>
    public int Line { get; set; }

    // ── The characters ──────────────────────────────────────────────────────

    public int Font { get; set; } = -1;

    /// <summary>In half-points, as RTF counts it.</summary>
    public int Size { get; set; } = RtfReading.OrdinarySize;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underline { get; set; }

    public bool Strike { get; set; }

    public bool Capitals { get; set; }

    /// <summary><c>\v</c>: written, and drawn by nothing.</summary>
    public bool Hidden { get; set; }

    public TextScript Script { get; set; }

    /// <summary>The colour table entry the letters are in, or -1 for the reader's own colour.</summary>
    public int Ink { get; set; } = -1;

    /// <summary>A highlighter's colour (<c>\highlight</c>), which wins over a background.</summary>
    public int Highlight { get; set; }

    /// <summary>A background colour (<c>\cb</c>, <c>\chcbpat</c>).</summary>
    public int Background { get; set; }

    /// <summary>How many characters stand in for each <c>\u</c> for readers without Unicode.</summary>
    public int Fallback { get; set; } = 1;

    /// <summary>How many of those are still to be read past.</summary>
    public int Skipping { get; set; }

    // ── The paragraph ───────────────────────────────────────────────────────

    public ParagraphAlign Align { get; set; }

    /// <summary>Every distance in twips, as RTF counts them.</summary>
    public int Indent { get; set; }

    public int EndIndent { get; set; }

    public int FirstLine { get; set; }

    public int Before { get; set; }

    public int After { get; set; }

    public int[] Stops { get; set; } = [];

    /// <summary>How deep in tables the paragraph is: nought outside one.</summary>
    public int Depth { get; set; }

    // ── What it belongs to ──────────────────────────────────────────────────

    /// <summary>The field this group is inside, if any.</summary>
    public RtfField? Field { get; set; }

    /// <summary>The link the words of this group lead along: nought where they lead nowhere.</summary>
    public int Link { get; set; }

    /// <summary>The picture being read, if this group is in one.</summary>
    public RtfPicture? Picture { get; set; }

    /// <summary>Whether this is the group that opened <see cref="Picture"/>, so its close is where the picture ends.</summary>
    public bool OpensPicture { get; set; }

    /// <summary>Whether this group is inside a drawing shape's description.</summary>
    public bool Shaped { get; set; }

    /// <summary>What a group inside this one starts as.</summary>
    public RtfGroup Inner()
    {
        var inner = (RtfGroup)MemberwiseClone();

        // What was skipping ends where a group opens, and a starred word names the destination of the group it opens.
        inner.Skipping = 0;
        inner.Starred = false;
        inner.OpensPicture = false;

        return inner;
    }

    /// <summary><c>\plain</c>: the characters as the document sets them where nothing says otherwise.</summary>
    public void Plain(int font)
    {
        Font = font;
        Size = RtfReading.OrdinarySize;
        Bold = Italic = Underline = Strike = Capitals = Hidden = false;
        Script = TextScript.None;
        Ink = -1;
        Highlight = Background = 0;
    }

    /// <summary><c>\pard</c>: the paragraph as the document sets one where nothing says otherwise.</summary>
    public void Ordinary()
    {
        Align = ParagraphAlign.Start;
        Indent = EndIndent = FirstLine = Before = After = 0;
        Stops = [];
        Depth = 0;
    }

    /// <summary>A copy of this group, to be read on from where this one stands.</summary>
    public RtfGroup Copy() => (RtfGroup)MemberwiseClone();

    /// <summary>
    /// Whether <paramref name="other"/> sets everything this one does — leaving out where in the paragraph being read each
    /// began, which says nothing about what follows.
    /// </summary>
    public bool Alike(RtfGroup other) =>
        Destination == other.Destination && Starred == other.Starred
        && Font == other.Font && Size == other.Size && Bold == other.Bold && Italic == other.Italic
        && Underline == other.Underline && Strike == other.Strike && Capitals == other.Capitals && Hidden == other.Hidden
        && Script == other.Script && Ink == other.Ink && Highlight == other.Highlight && Background == other.Background
        && Fallback == other.Fallback && Skipping == other.Skipping
        && Align == other.Align && Indent == other.Indent && EndIndent == other.EndIndent && FirstLine == other.FirstLine
        && Before == other.Before && After == other.After && Stops.AsSpan().SequenceEqual(other.Stops) && Depth == other.Depth
        && ReferenceEquals(Field, other.Field) && Link == other.Link && ReferenceEquals(Picture, other.Picture)
        && OpensPicture == other.OpensPicture && Shaped == other.Shaped;
}

/// <summary>A field: what it is, read from its instruction, and the link its result leads along where it is one.</summary>
internal sealed class RtfField(int number)
{
    /// <summary>What tells this field's link apart from every other one in the document.</summary>
    public int Number { get; } = number;

    /// <summary>The instruction as written, decoded.</summary>
    public System.Text.StringBuilder Instruction { get; } = new();
}
