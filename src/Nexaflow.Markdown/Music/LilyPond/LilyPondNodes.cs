using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Music.LilyPond;

/// <summary>
/// A note, a rest, a chord, a repeated chord, a chord's name or a <c>\skip</c> as its stages leave it: how long it is written and
/// how long it lasts (<see cref="Stages.ResolveDurations"/>), what it sounds (<see cref="Stages.ResolvePitches"/>), and — for a
/// chord's name — the name a lead sheet spells it as (<see cref="Stages.SpellChords"/>). All of it is true of the event wherever
/// it is played. It prints as the event written.
/// </summary>
internal sealed class LilyPondEventNode : ContentNode
{
    private LilyPondEventNode(ContentNode written, Duration? writtenAs, Duration? sounds, IReadOnlyList<Pitch> pitches, string? chord)
        : base(written)
    {
        this.Written = writtenAs;
        this.Sounds = sounds;
        this.Pitches = pitches;
        this.Chord = chord;
    }

    /// <summary>The value it is written as, which decides its head and its flags — null where it was never timed.</summary>
    public Duration? Written { get; }

    /// <summary>How long it lasts, after its multiplier and any tuplet it is inside — null where it was never timed.</summary>
    public Duration? Sounds { get; }

    /// <summary>How long it lasts, or nothing where it was never timed.</summary>
    public Duration Lasts => this.Sounds ?? Duration.Zero;

    /// <summary>The value it is written as, or — where none was said — how long it lasts.</summary>
    public Duration WrittenAs => this.Written ?? this.Lasts;

    /// <summary>What it sounds: a note's pitch, or every pitch of the chord a repeated chord repeats.</summary>
    public IReadOnlyList<Pitch> Pitches { get; }

    /// <summary>A chord's name spelled as a lead sheet spells it — null for anything else.</summary>
    public string? Chord { get; }

    /// <summary>What the stages have said of an event so far — nothing, where they have said nothing yet.</summary>
    internal static LilyPondEventNode Of(ContentNode node) =>
        node as LilyPondEventNode ?? new LilyPondEventNode(node, null, null, [], null);

    internal LilyPondEventNode Timed(Duration written, Duration sounds) => new(this, written, sounds, this.Pitches, this.Chord);

    internal LilyPondEventNode Pitched(IReadOnlyList<Pitch> pitches) => new(this, this.Written, this.Sounds, pitches, this.Chord);

    internal LilyPondEventNode Spelled(string chord) => new(this, this.Written, this.Sounds, this.Pitches, chord);

    protected override ContentNode Reshaped(ContentNode shape) =>
        new LilyPondEventNode(shape, this.Written, this.Sounds, this.Pitches, this.Chord);
}

/// <summary>What a <c>\new</c> or a <c>\context</c> makes.</summary>
public enum LilyPondContext
{
    /// <summary>A staff, or a voice on one — music to play.</summary>
    Staff,

    /// <summary>A group of staves — a score, a piano staff, a choir.</summary>
    Group,

    /// <summary>Words to sing.</summary>
    Lyrics,

    /// <summary>Chord names.</summary>
    Chords,

    /// <summary>Anything else, which is not drawn.</summary>
    Other,
}

/// <summary>
/// A command as its stage leaves it (<see cref="Stages.ResolveCommands"/>): what its arguments say for its name — the clef a
/// <c>\clef</c> sets, the key a <c>\key</c> does, the meter of a <c>\time</c>, the pickup of a <c>\partial</c>, the bar line a
/// <c>\bar</c> draws, the number a tuplet prints, what a <c>\repeat</c> does and how many times, what a <c>\new</c> makes and
/// what it is called, the instrument a <c>\set</c> or a <c>\with</c> names, the ending a <c>\volta</c> labels, the voice a
/// <c>\lyricsto</c> sings to, and whether an <c>\omit</c> hides the meter. What a command says nothing about is null. It prints as
/// the command written.
/// </summary>
internal sealed class LilyPondCommandNode : ContentNode
{
    internal LilyPondCommandNode(ContentNode written) : base(written) { }

    private LilyPondCommandNode(ContentNode written, LilyPondCommandNode said) : base(written)
    {
        this.Clef = said.Clef;
        this.Fifths = said.Fifths;
        this.Meter = said.Meter;
        this.Pickup = said.Pickup;
        this.Bar = said.Bar;
        this.TupletNumber = said.TupletNumber;
        this.Repeat = said.Repeat;
        this.Times = said.Times;
        this.Context = said.Context;
        this.Id = said.Id;
        this.Instrument = said.Instrument;
        this.Label = said.Label;
        this.HidesMeter = said.HidesMeter;
    }

    public ClefKind? Clef { get; init; }

    /// <summary>The key, as sharps (positive) or flats (negative).</summary>
    public int? Fifths { get; init; }

    public (int Beats, int Unit)? Meter { get; init; }

    /// <summary>How much of a bar the music before the first bar line takes.</summary>
    public Duration? Pickup { get; init; }

    /// <summary>
    /// The bar line in the spelling the engraver draws — ABC's, where each mark is a stroke: <c>|</c> thin, <c>[</c> and
    /// <c>]</c> thick, <c>:</c> the dots of a repeat.
    /// </summary>
    public string? Bar { get; init; }

    /// <summary>The number a tuplet prints over what it holds.</summary>
    public int? TupletNumber { get; init; }

    /// <summary>What a <c>\repeat</c> does: <c>volta</c>, <c>unfold</c>, <c>percent</c>, <c>tremolo</c>.</summary>
    public string? Repeat { get; init; }

    /// <summary>How many times a <c>\repeat</c> plays what it holds.</summary>
    public int? Times { get; init; }

    /// <summary>What a <c>\new</c> or a <c>\context</c> makes.</summary>
    public LilyPondContext? Context { get; init; }

    /// <summary>The name a <c>\new</c> gives what it makes, or the voice a <c>\lyricsto</c> sings to.</summary>
    public string? Id { get; init; }

    /// <summary>The instrument a staff is named for, by a <c>\set</c>, a <c>\with</c>, or the <c>\with</c> of a <c>\new</c>.</summary>
    public string? Instrument { get; init; }

    /// <summary>What a <c>\volta</c> labels its ending.</summary>
    public string? Label { get; init; }

    /// <summary>Whether an <c>\omit</c> or a <c>\hide</c> hides the meter.</summary>
    public bool HidesMeter { get; init; }

    protected override ContentNode Reshaped(ContentNode shape) => new LilyPondCommandNode(shape, this);
}
