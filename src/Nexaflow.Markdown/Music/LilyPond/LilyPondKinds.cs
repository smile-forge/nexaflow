namespace Nexaflow.Markdown.Music.LilyPond;

/// <summary>
/// What a piece of LilyPond is.
/// </summary>
/// <remarks>
/// About syntax, not meaning, as ABC's kinds are: a <see cref="Note"/> is a name with marks after it, whatever
/// octave <c>\relative</c> puts it in. What it sounds and how long it lasts are worked out by a stage and hung
/// underneath it, because both depend on what was written before it.
/// </remarks>
public static class LilyPondKinds
{
    /// <summary>The whole source: its definitions, its headers, and the music they come to.</summary>
    public const string File = "file";

    /// <summary>Music in braces, one thing after another: <c>{ c d e }</c>.</summary>
    public const string Sequential = "sequential";

    /// <summary>Music sounding together: <c>&lt;&lt; … &gt;&gt;</c>.</summary>
    public const string Simultaneous = "simultaneous";

    /// <summary>Notes on one stem, and the duration after them: <c>&lt;c e g&gt;4</c>.</summary>
    public const string Chord = "chord";

    /// <summary>A note: its name, octave marks, a forced accidental, a duration.</summary>
    public const string Note = "note";

    /// <summary>A rest — <c>r</c>, a whole bar's <c>R</c>, or an invisible <c>s</c> — and its duration.</summary>
    public const string Rest = "rest";

    /// <summary>The last chord again, with a duration of its own: <c>q4</c>.</summary>
    public const string ChordRepeat = "chord-repeat";

    /// <summary>
    /// A backslash command and the arguments it takes: <c>\relative c' { … }</c> holds its start pitch and the
    /// music it applies to, <c>\time 3/4</c> its fraction. A command that takes nothing — <c>\break</c>, a
    /// variable's name — is one of these holding only its name.
    /// </summary>
    public const string Command = "command";

    /// <summary>A definition: <c>melody = \relative c'' { … }</c>, or <c>title = "…"</c> in a header.</summary>
    public const string Assignment = "assignment";

    /// <summary>Something put on a note in a direction: <c>^"Fine"</c>, <c>_\fermata</c>.</summary>
    public const string Script = "script";

    /// <summary>An articulation written as punctuation — <c>-.</c> <c>-&gt;</c> <c>--</c> — or a fingering.</summary>
    public const string Articulation = "articulation";

    /// <summary>Text in double quotes, quotes included.</summary>
    public const string Quoted = "quoted";

    /// <summary>Scheme, which is read as far as where it ends and no further: <c>#(set-global-staff-size 20)</c>.</summary>
    public const string Scheme = "scheme";

    /// <summary>A word that is not a note: a context's name, a repeat's kind, a fraction.</summary>
    public const string Word = "word";

    /// <summary>A <c>|</c>, which in LilyPond checks where a bar ends rather than ending one.</summary>
    public const string BarCheck = "bar-check";

    /// <summary>A tie: the <c>~</c> after the note it runs from.</summary>
    public const string Tie = "tie";

    /// <summary>A slur's <c>(</c> or a phrasing slur's <c>\(</c>, after the note it starts on.</summary>
    public const string SlurOpen = "slur-open";

    public const string SlurClose = "slur-close";

    /// <summary>A beam asked for by hand: the <c>[</c> after the note it starts on.</summary>
    public const string BeamOpen = "beam-open";

    public const string BeamClose = "beam-close";

    /// <summary>The <c>\\</c> between two voices on one staff.</summary>
    public const string VoiceSeparator = "voice-separator";

    /// <summary>One syllable of lyrics, as written — a duration after it included.</summary>
    public const string Syllable = "syllable";

    /// <summary>What joins syllables: <c>--</c> a hyphen, <c>__</c> a held syllable, <c>_</c> a note with none.</summary>
    public const string LyricMark = "lyric-mark";

    /// <summary>
    /// A chord's name in <c>\chordmode</c> — <c>g1:7</c>, <c>d2:m7/f</c> — its root, duration and quality each a part.
    /// </summary>
    public const string ChordName = "chord-name";

    // ── The parts a note is made of ─────────────────────────────────────────
    //
    // Leaves of their own, as ABC's are, because each is what an edit rewrites: sharpening a note replaces its
    // name, moving it an octave its marks, lengthening it its duration.

    /// <summary>The note's name with its alteration: <c>c</c>, <c>fis</c>, <c>bes</c>, <c>as</c>.</summary>
    public const string NoteName = "note-name";

    /// <summary>The <c>'</c> and <c>,</c> marks after it.</summary>
    public const string Octave = "octave";

    /// <summary>A <c>!</c> or <c>?</c>, which asks for the accidental to be printed whatever is in force.</summary>
    public const string Force = "force";

    /// <summary>The duration after it: <c>4</c>, <c>8.</c>, <c>2*3</c>.</summary>
    public const string Duration = "duration";

    /// <summary>A tremolo's <c>:32</c>.</summary>
    public const string Tremolo = "tremolo";
}

/// <summary>What a piece of LilyPond is <em>to</em> the piece holding it.</summary>
public static class LilyPondRoles
{
    public const string NoteName = "note-name";
    public const string Octave = "octave";
    public const string Force = "force";
    public const string Duration = "duration";
    public const string Tremolo = "tremolo";


    /// <summary>What kind of chord a chord's name is: its <c>:m7</c>, and the <c>/f</c> of its bass.</summary>
    public const string Quality = "quality";

    /// <summary>One note of a chord.</summary>
    public const string Note = "note";

    /// <summary>
    /// What a command is given that is not the music it applies to: <c>\relative</c>'s start pitch,
    /// <c>\time</c>'s fraction, <c>\new</c>'s context name. A note written as one is not an event.
    /// </summary>
    public const string Argument = "argument";

    /// <summary>The <c>=</c> of a definition.</summary>
    public const string Assign = "assign";

    /// <summary>What a definition sets its name to.</summary>
    public const string Value = "value";

    // ── Roles a stage hangs underneath a piece ──────────────────────────────

    /// <summary>What a note actually sounds, after <c>\relative</c>, <c>\fixed</c> or <c>\transpose</c>.</summary>
    public const string Pitch = "pitch";

    /// <summary>How long an event lasts, in quarter notes, after any multiplier and any tuplet.</summary>
    public const string Sounds = "sounds";

    /// <summary>The value it is written as, before a multiplier or a tuplet changed how long it lasts.</summary>
    public const string Written = "written";
}
