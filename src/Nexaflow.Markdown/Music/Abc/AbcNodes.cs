using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// An information field as its stage leaves it (<see cref="Stages.ResolveFields"/>): what its value says for the letter it is
/// written under — a key and a clef for <c>K:</c>, a meter and the sign it was written as for <c>M:</c>, a unit note length for
/// <c>L:</c>, a voice, its name and its clef for <c>V:</c>. What a field says nothing about is null. It prints as the field written.
/// </summary>
internal sealed class AbcFieldNode : ContentNode
{
    internal AbcFieldNode(ContentNode written, char letter, ClefKind? clef, int? fifths, (int Beats, int Unit)? meter,
                          MeterSign sign, Duration? unit, string? voice, string? voiceName)
        : base(written)
    {
        this.Letter = letter;
        this.Clef = clef;
        this.Fifths = fifths;
        this.Meter = meter;
        this.Sign = sign;
        this.Unit = unit;
        this.Voice = voice;
        this.VoiceName = voiceName;
    }

    /// <summary>The field's letter, as written — ABC tells some fields apart by case.</summary>
    public char Letter { get; }

    /// <summary>The clef it asks for — a bare name on <c>K:</c> and <c>V:</c>, and <c>clef=</c> anywhere.</summary>
    public ClefKind? Clef { get; }

    /// <summary>The key a <c>K:</c> sets, as sharps (positive) or flats (negative).</summary>
    public int? Fifths { get; }

    /// <summary>The meter an <c>M:</c> sets.</summary>
    public (int Beats, int Unit)? Meter { get; }

    /// <summary>How an <c>M:</c>'s meter is printed: as figures, or as the <c>C</c> or <c>C|</c> it was written as.</summary>
    public MeterSign Sign { get; }

    /// <summary>The unit note length an <c>L:</c> sets.</summary>
    public Duration? Unit { get; }

    /// <summary>The voice a <c>V:</c> names — its first word.</summary>
    public string? Voice { get; }

    /// <summary>What a <c>V:</c> asks for its voice to be labelled — <c>name="Soprano"</c>.</summary>
    public string? VoiceName { get; }

    protected override ContentNode Reshaped(ContentNode shape) =>
        new AbcFieldNode(shape, this.Letter, this.Clef, this.Fifths, this.Meter, this.Sign, this.Unit, this.Voice, this.VoiceName);
}

/// <summary>
/// A line of music as its stage leaves it (<see cref="Stages.ResolveContext"/>): the key, meter, unit note length and voice in force
/// as it starts. It prints as the line written.
/// </summary>
internal sealed class AbcLineNode : ContentNode
{
    internal AbcLineNode(ContentNode written, AbcContext context) : base(written) => this.Context = context;

    public AbcContext Context { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new AbcLineNode(shape, this.Context);
}

/// <summary>
/// A tuplet marker gathered with the events it covers (<see cref="Stages.GroupTuplets"/>): how many notes it holds, in the time of
/// how many. It prints as the marker and the events written.
/// </summary>
internal sealed class AbcTupletNode : ContentNode
{
    internal AbcTupletNode(ContentNode written, int notes, int time) : base(written)
    {
        this.Notes = notes;
        this.Time = time;
    }

    /// <summary>How many notes the group holds — the number printed over it.</summary>
    public int Notes { get; }

    /// <summary>How many notes' worth of time it takes.</summary>
    public int Time { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new AbcTupletNode(shape, this.Notes, this.Time);
}

/// <summary>One syllable sung on an event, and where it was written — the <c>w:</c> line and which piece of it.</summary>
/// <param name="Verse">Which verse it is, counting the <c>w:</c> lines under one line of music from nought.</param>
/// <param name="Line">Which of the tune's lines the <c>w:</c> line is.</param>
/// <param name="At">Which piece of that line's words it is.</param>
internal readonly record struct AbcSung(int Verse, int Line, int At, string Text, bool Hyphen, bool Melisma);

/// <summary>
/// A note, a chord or a rest as its stages leave it: what a note sounds and which accidental is written in front of it, which rest
/// a rest is (<see cref="Stages.ResolveNotes"/>), how long the event lasts and what value it is written as, and the syllables sung
/// on it (<see cref="Stages.AlignLyrics"/>). It prints as the event written.
/// </summary>
internal sealed class AbcEventNode : ContentNode
{
    internal AbcEventNode(ContentNode written, Pitch? pitch, int? printed, Duration lasts, Duration writtenAs,
                          AbcRestKind? rest = null, IReadOnlyList<AbcSung>? sung = null)
        : base(written)
    {
        this.Pitch = pitch;
        this.Printed = printed;
        this.Lasts = lasts;
        this.WrittenAs = writtenAs;
        this.Rest = rest;
        this.Sung = sung ?? [];
    }

    /// <summary>What a note sounds, after the key and the bar — null for a chord and a rest, and for a note nothing sounds.</summary>
    public Pitch? Pitch { get; }

    /// <summary>The accidental written in front of a note, in semitones — null where none was.</summary>
    public int? Printed { get; }

    /// <summary>How long it lasts, after the unit note length, any broken rhythm and any tuplet.</summary>
    public Duration Lasts { get; }

    /// <summary>
    /// The value it is <em>written</em> as, which is not always how long it lasts. A triplet eighth sounds for a third of a
    /// quarter and is drawn as an eighth — the number over the group says the rest.
    /// </summary>
    public Duration WrittenAs { get; }

    /// <summary>Which rest it is — null for a note and a chord.</summary>
    public AbcRestKind? Rest { get; }

    /// <summary>The syllables sung on it, a verse at a time.</summary>
    public IReadOnlyList<AbcSung> Sung { get; }

    /// <summary>The same event, with one more syllable sung on it.</summary>
    internal AbcEventNode Singing(AbcSung sung) =>
        new(this, this.Pitch, this.Printed, this.Lasts, this.WrittenAs, this.Rest, [.. this.Sung, sung]);

    /// <summary>What the stages have said of an event so far — nothing, where they have said nothing yet.</summary>
    internal static AbcEventNode Of(ContentNode node) =>
        node as AbcEventNode ?? new AbcEventNode(node, null, null, Duration.Zero, Duration.Zero);

    protected override ContentNode Reshaped(ContentNode shape) =>
        new AbcEventNode(shape, this.Pitch, this.Printed, this.Lasts, this.WrittenAs, this.Rest, this.Sung);
}

/// <summary>Which rest a rest is: one that is seen (<c>z</c>), one that only takes time (<c>x</c>), or whole bars of it (<c>Z</c>).</summary>
internal enum AbcRestKind
{
    Seen,
    Unseen,
    Bars,
}

/// <summary>
/// A bar line closing a measure, as its stage leaves it (<see cref="Stages.GroupBars"/>): whether it is a repeat line. It prints as
/// the line written.
/// </summary>
internal sealed class AbcBarlineNode : ContentNode
{
    internal AbcBarlineNode(ContentNode written, bool repeats) : base(written) => this.Repeats = repeats;

    /// <summary>
    /// Whether it is a repeat line — dots on either side of it, <c>:|</c>, <c>|:</c>, <c>::</c> — which is where a section of the
    /// tune ends, and with it any ending written before it.
    /// </summary>
    public bool Repeats { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new AbcBarlineNode(shape, this.Repeats);
}
