using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>
/// Base for anything that occupies time in a measure — a <see cref="Note"/>, <see cref="Rest"/> or
/// <see cref="Chord"/>. Events sharing a non-zero <see cref="BeamId"/> are beamed together; the parser
/// assigns beam ids (ABC groups by whitespace, LilyPond by its beaming rules).
///
/// Everything an engraver has to hang off an event — its ornaments, the text above it, the tuplet it
/// belongs to, the slurs opening and closing on it, the grace notes crushed in before it, the lyric
/// syllables under it — lives here rather than on <see cref="Note"/>, because a <see cref="Chord"/> or a
/// <see cref="Rest"/> carries them just the same.
/// </summary>
public abstract class MusicalEvent
{
    public Duration Duration { get; set; } = Duration.Quarter;

    /// <summary>Non-zero groups consecutive beamable events under one beam. 0 = unbeamed (draw a flag).</summary>
    public int BeamId { get; set; }

    /// <summary>Marks attached to this event (staccato dot, accent, bowing, fermata, roll …).</summary>
    public List<ArticulationKind> Articulations { get; } = [];

    /// <summary>A chord symbol printed above the staff (ABC's bare <c>"Gm7"</c>).</summary>
    public string? ChordSymbol { get; set; }

    /// <summary>Free text placed relative to the note (ABC's <c>"^Fine"</c> / <c>"_text"</c> …).</summary>
    public string? Annotation { get; set; }

    /// <summary>Where <see cref="Annotation"/> sits.</summary>
    public AnnotationPlacement AnnotationPlacement { get; set; } = AnnotationPlacement.Above;

    /// <summary>Slurs opening on this event (ABC allows nesting: <c>((AA)A)</c>).</summary>
    public int SlurOpen { get; set; }

    /// <summary>Slurs closing on this event.</summary>
    public int SlurClose { get; set; }

    /// <summary>Grace notes crushed in before this event; empty for the common case.</summary>
    public List<Note> Graces { get; } = [];

    /// <summary>True when the grace group is an acciaccatura (ABC <c>{/g}</c>) — drawn with a slash.</summary>
    public bool GraceSlashed { get; set; }

    /// <summary>Non-zero when this event belongs to a tuplet; all its members share the id.</summary>
    public int TupletId { get; set; }

    /// <summary>The number printed on the tuplet bracket (the <c>p</c> of ABC's <c>(p:q:r</c>).</summary>
    public int TupletNumber { get; set; }

    /// <summary>How many notes' worth of time the tuplet occupies (the <c>q</c>): a triplet is 3-in-the-time-of-2,
    /// so <c>TupletNumber = 3, TupletTime = 2</c>. The engraver spaces the group by <c>q/p</c>.</summary>
    public int TupletTime { get; set; }

    /// <summary>One lyric syllable per verse (ABC's stacked <c>w:</c> lines); empty when unsung.</summary>
    public List<LyricSyllable> Lyrics { get; } = [];
}

/// <summary>One syllable under one note, for one verse.</summary>
/// <param name="Text">The syllable as printed (may be empty for a melisma continuation).</param>
/// <param name="Hyphen">True when the word continues on the next note — draw a hyphen after it.</param>
/// <param name="Melisma">True when a previous syllable is held across this note (ABC's <c>_</c>) — draw an
/// extender line rather than text.</param>
public readonly record struct LyricSyllable(string Text, bool Hyphen, bool Melisma);

/// <summary>A single sounded pitch.</summary>
public sealed class Note : MusicalEvent
{
    public Pitch Pitch { get; set; }

    /// <summary>An accidental to draw explicitly (parser-decided; key-signature accidentals are not repeated here).</summary>
    public AccidentalKind Accidental { get; set; } = AccidentalKind.None;

    /// <summary>True when this note is tied into the following note of the same pitch.</summary>
    public bool TieStart { get; set; }
}

/// <summary>A silence.</summary>
public sealed class Rest : MusicalEvent
{
    /// <summary>True for ABC's <c>Z</c> — a whole bar's rest, centred in the measure.</summary>
    public bool IsWholeMeasure { get; set; }

    /// <summary>True for ABC's <c>x</c> — occupies time but prints nothing.</summary>
    public bool IsInvisible { get; set; }
}

/// <summary>Two or more simultaneous notes sharing one stem — ABC <c>[CEG]</c> / LilyPond <c>&lt;c e g&gt;</c>.
/// The chord's <see cref="MusicalEvent.Duration"/> is authoritative; the member notes carry only pitch and
/// accidental.</summary>
public sealed class Chord : MusicalEvent
{
    public List<Note> Notes { get; } = [];
}
