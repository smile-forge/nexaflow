namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// What a piece of a tune is.
/// </summary>
/// <remarks>
/// Deliberately about syntax, not meaning: a <see cref="Note"/> is a letter with marks around it, whether
/// it turns out to be a middle C or the top of a run. What it <em>means</em> — its pitch, its duration,
/// whether an accidental prints — is worked out by a pipeline stage and hung underneath it as a derived
/// part, because those are facts about the key and the bar as much as about the letter.
/// </remarks>
public static class AbcKinds
{
    /// <summary>The whole tune: its lines, in order.</summary>
    public const string Tune = "tune";

    /// <summary>One line, whatever kind — the unit that carries its own terminator.</summary>
    public const string Line = "line";

    /// <summary>An information field on a line of its own: <c>X:1</c>, <c>T:…</c>, <c>K:G</c>.</summary>
    public const string Field = "field";

    /// <summary>An information field written inside the music: <c>[K:G]</c>.</summary>
    public const string InlineField = "inline-field";

    /// <summary>A <c>w:</c> line — syllables to be aligned under the notes above.</summary>
    public const string LyricLine = "lyric-line";

    /// <summary>A note: an optional accidental, a letter, optional octave marks, an optional length.</summary>
    public const string Note = "note";

    /// <summary>A rest: <c>z</c>, <c>x</c> or <c>Z</c>, with an optional length.</summary>
    public const string Rest = "rest";

    /// <summary>Notes bracketed to sound together: <c>[CEG]2</c>.</summary>
    public const string Chord = "chord";

    /// <summary>Grace notes crushed in before what follows: <c>{gAG}</c>, <c>{/g}</c>.</summary>
    public const string Grace = "grace";

    // ── The parts a note is made of ─────────────────────────────────────────
    //
    // Leaves of their own rather than characters of one, because each is what an edit rewrites: sharpening
    // a note replaces its accidental, moving it an octave replaces its marks and its letter's case, and
    // lengthening it replaces its length. One leaf each means an edit touches one leaf.

    public const string Accidental = "accidental";
    public const string Letter = "letter";
    public const string Octave = "octave";
    public const string Length = "length";

    /// <summary>A bar line: <c>|</c>, <c>||</c>, <c>[|</c>, <c>|]</c>, <c>|:</c>, <c>:|</c>, <c>::</c>.</summary>
    public const string Barline = "barline";

    /// <summary>A repeat bracket's label, written straight after a bar line: <c>1</c>, <c>1,2</c>, <c>1-3</c>.</summary>
    public const string Volta = "volta";

    /// <summary>A tuplet marker: <c>(3</c>, <c>(3:2</c>, <c>(3:2:3</c>.</summary>
    public const string Tuplet = "tuplet";

    public const string SlurOpen = "slur-open";
    public const string SlurClose = "slur-close";

    /// <summary>A tie: the <c>-</c> between two notes of the same pitch.</summary>
    public const string Tie = "tie";

    /// <summary>A broken-rhythm marker: <c>&gt;</c>, <c>&lt;</c>, and their runs.</summary>
    public const string Broken = "broken";

    /// <summary>An ornament or bowing: <c>.</c> <c>~</c> <c>H</c> <c>T</c> <c>u</c> <c>v</c>, or <c>!trill!</c>.</summary>
    public const string Decoration = "decoration";

    /// <summary>A double-quoted run: a chord symbol, or a placed text annotation.</summary>
    public const string Annotation = "annotation";

    /// <summary>A typesetting spacer — <c>y</c> — which takes room and no time.</summary>
    public const string Spacer = "spacer";

    /// <summary>A voice overlay marker: <c>&amp;</c>.</summary>
    public const string Overlay = "overlay";

    /// <summary>A line continuation: the <c>\</c> that says the next source line is the same music line.</summary>
    public const string Continuation = "continuation";

    /// <summary>Plain text: a field's value, a syllable, whatever is inside quotes.</summary>
    public const string Text = "text";

    // ── Kinds a pipeline stage makes ────────────────────────────────────────
    //
    // None of these is written down. Each re-nests pieces that were, which is why they can exist at all
    // without the tune printing differently.

    /// <summary>Consecutive beamable events written without a space between them.</summary>
    public const string Beam = "beam";

    /// <summary>A bar: the events between two bar lines, with the lines that close it.</summary>
    public const string Measure = "measure";

    /// <summary>One voice's music, gathered out of the lines that carry it.</summary>
    public const string Voice = "voice";

    /// <summary>The events a tuplet marker covers.</summary>
    public const string TupletGroup = "tuplet-group";
}

/// <summary>What a piece of a tune is <em>to</em> the piece holding it.</summary>
public static class AbcRoles
{
    /// <summary>The letter that names a note's step.</summary>
    public const string Letter = "letter";

    /// <summary>The <c>^</c>, <c>_</c> or <c>=</c> in front of a note.</summary>
    public const string Accidental = "accidental";

    /// <summary>The <c>,</c> and <c>'</c> marks after it.</summary>
    public const string Octave = "octave";

    /// <summary>The length multiplier after it: <c>2</c>, <c>/2</c>, <c>3/2</c>, <c>/</c>.</summary>
    public const string Length = "length";

    /// <summary>What an information field was set to.</summary>
    public const string Value = "value";

    /// <summary>One note of a chord.</summary>
    public const string Note = "note";

    /// <summary>One thing that occupies time: a note, a rest, a chord.</summary>
    public const string Event = "event";

    /// <summary>A bar line, at either end of a measure.</summary>
    public const string Barline = "barline";

    /// <summary>Whether a measure runs on past the end of the line it is written on.</summary>
    public const string Continues = "continues";

    // ── Roles a stage hangs underneath a piece ──────────────────────────────
    //
    // All of these are Roles.Derived as far as the tree is concerned; these names say which fact is being
    // recorded, so a builder can ask for the one it wants.

    /// <summary>What a note actually sounds: step, alteration and octave, after the key and the bar.</summary>
    public const string Pitch = "pitch";

    /// <summary>How long an event lasts, after the unit note length and any broken rhythm.</summary>
    public const string Duration = "duration";

    /// <summary>Which accidental actually prints, which is not the same as which was written.</summary>
    public const string Printed = "printed";

    /// <summary>A syllable to sit under this event.</summary>
    public const string Lyric = "lyric";

    /// <summary>The key, meter or unit length in force here.</summary>
    public const string Context = "context";
}
