namespace Nexaflow.Markdown.Music.Abc;

/// <summary>
/// What a piece of a tune is.
/// </summary>
/// <remarks>
/// Deliberately about syntax, not meaning: a <see cref="Note"/> is a letter with marks around it, whether
/// it turns out to be a middle C or the top of a run. What it <em>means</em> — its pitch, its duration,
/// whether an accidental prints — is worked out by a pipeline stage and said in the tune's own nodes
/// (<see cref="AbcEventNode"/> and its kin), because those are facts about the key and the bar as much as
/// about the letter. What is written is split as far as it goes, so no stage takes characters apart.
/// </remarks>
public static class AbcKinds
{
    /// <summary>The whole tune: its lines, in order.</summary>
    public const string Tune = "tune";

    /// <summary>One line, whatever kind — the unit that carries its own terminator.</summary>
    public const string Line = "line";

    /// <summary>
    /// A line with no music on it: empty, or nothing but a comment.
    ///
    /// <para>
    /// Told apart from <see cref="Line"/> because everything downstream asks "has the music started" and
    /// answers it by looking for the first one. A tune whose file opens with a <c>%%wordsfont</c> line —
    /// which is common, and which ABC readers ignore — otherwise has its music start at line zero, so
    /// every <c>T:</c>, <c>C:</c> and <c>R:</c> after it is read as belonging to the middle of the tune
    /// and none of them is printed. The title simply does not appear, on a file that looks perfectly
    /// ordinary.
    /// </para>
    /// </summary>
    public const string Blank = "blank";

    /// <summary>An information field on a line of its own: <c>X:1</c>, <c>T:…</c>, <c>K:G</c>.</summary>
    public const string Field = "field";

    /// <summary>An information field written inside the music: <c>[K:G]</c>.</summary>
    public const string InlineField = "inline-field";

    /// <summary>A <c>w:</c> line — syllables to be aligned under the notes above.</summary>
    public const string LyricLine = "lyric-line";

    /// <summary>
    /// One syllable of a <c>w:</c> line — the characters a reader would select if they picked one word
    /// out of a verse.
    ///
    /// <para>
    /// A lyric line used to be one leaf holding the whole verse, which meant a syllable existed only as a
    /// worked-out fact hung under the note it is sung on: no width, no place in the source, and so
    /// nothing to select or edit. Splitting the line into its syllables is what gives each one characters
    /// of its own to point at. The line still prints exactly as it was written — the pieces are adjacent
    /// and in order, so printing them in order is the line.
    /// </para>
    /// </summary>
    public const string Syllable = "syllable";

    /// <summary>
    /// What separates two syllables and says how they join: a space, a hyphen, a held <c>_</c>, a skipped
    /// <c>*</c>, a bar jump <c>|</c>.
    /// </summary>
    public const string LyricMark = "lyric-mark";

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
    // Parts of their own rather than characters of one, because each is what an edit rewrites: sharpening
    // a note replaces its accidental, moving it an octave replaces its marks and its letter's case, and
    // lengthening it replaces its length — its numbers and its slashes. One part each means an edit touches
    // one part.

    public const string Accidental = "accidental";
    public const string Letter = "letter";
    public const string Octave = "octave";
    public const string Length = "length";

    /// <summary>A bar line: <c>|</c>, <c>||</c>, <c>[|</c>, <c>|]</c>, <c>|:</c>, <c>:|</c>, <c>::</c>.</summary>
    public const string Barline = "barline";

    /// <summary>A repeat bracket's label, written straight after a bar line: <c>1</c>, <c>1,2</c>, <c>1-3</c>.</summary>
    public const string Volta = "volta";

    /// <summary>A tuplet marker — <c>(3</c>, <c>(3:2</c>, <c>(3:2:3</c> — as its bracket, and each number in the role its place gives it.</summary>
    public const string Tuplet = "tuplet";

    public const string SlurOpen = "slur-open";
    public const string SlurClose = "slur-close";

    /// <summary>A tie: the <c>-</c> between two notes of the same pitch.</summary>
    public const string Tie = "tie";

    /// <summary>A broken-rhythm marker: <c>&gt;</c>, <c>&lt;</c>, and their runs.</summary>
    public const string Broken = "broken";

    /// <summary>An ornament or bowing: a one-character shorthand — <c>.</c> <c>~</c> <c>H</c> <c>T</c> <c>u</c> <c>v</c> — or a name between bangs, <c>!trill!</c>, held as its bangs and its name.</summary>
    public const string Decoration = "decoration";

    /// <summary>A double-quoted run — a chord symbol, or placed text — as its quotes, the character that places it where one does, and its words.</summary>
    public const string Annotation = "annotation";

    /// <summary>A typesetting spacer — <c>y</c> — which takes room and no time.</summary>
    public const string Spacer = "spacer";

    /// <summary>A voice overlay marker: <c>&amp;</c>.</summary>
    public const string Overlay = "overlay";

    /// <summary>A line continuation: the <c>\</c> that says the next source line is the same music line.</summary>
    public const string Continuation = "continuation";

    /// <summary>Plain text: a field's value, a syllable, whatever is inside quotes.</summary>
    public const string Text = "text";

    // ── The words of a K:, M:, L: or V: value ───────────────────────────────

    /// <summary>A word of a field's value standing for itself: <c>bass</c>, <c>Lydian</c>, <c>C|</c>, <c>none</c>, a voice's name.</summary>
    public const string Word = "word";

    /// <summary>A <c>key=value</c> setting in a field's value — <c>clef=bass</c>, <c>name="Soprano"</c>: its name, the equals sign and what it is set to.</summary>
    public const string Setting = "setting";

    /// <summary>The key a <c>K:</c> opens with: its tonic, the sharps or flats written on it, and the letters of its mode written straight after.</summary>
    public const string Key = "key";

    /// <summary>The letter a key is named after.</summary>
    public const string Tonic = "tonic";

    /// <summary>A mode written straight after a key's tonic: the <c>m</c> of <c>Dm</c>, the <c>mix</c> of <c>Amix</c>.</summary>
    public const string Mode = "mode";

    /// <summary>The figures of a meter or a unit length — <c>6/8</c>, <c>(2+3)/8</c>, <c>1/16</c>: its numbers and the marks between them.</summary>
    public const string Figures = "figures";

    /// <summary>One number of a meter's or a unit length's figures.</summary>
    public const string Number = "number";

    // ── Kinds a pipeline stage makes ────────────────────────────────────────
    //
    // None of these is written down. Each re-nests pieces that were, which is why they can exist at all
    // without the tune printing differently.

    /// <summary>Consecutive beamable events written without a space between them.</summary>
    public const string Beam = "beam";

    /// <summary>A bar: the events between two bar lines, with the lines that close it.</summary>
    public const string Measure = "measure";

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

    /// <summary>A key's mode, written straight after its tonic.</summary>
    public const string Mode = "mode";

    /// <summary>The <c>,</c> and <c>'</c> marks after it.</summary>
    public const string Octave = "octave";

    /// <summary>The length multiplier after it — <c>2</c>, <c>/2</c>, <c>3/2</c>, <c>/</c> — as its numbers and its slashes.</summary>
    public const string Length = "length";

    /// <summary>What an information field was set to.</summary>
    public const string Value = "value";

    /// <summary>One note of a chord.</summary>
    public const string Note = "note";

    /// <summary>One thing that occupies time: a note, a rest, a chord.</summary>
    public const string Event = "event";

    /// <summary>How many notes a tuplet marker puts together: the <c>p</c> of <c>(p:q:r</c>.</summary>
    public const string Tupled = "tupled";

    /// <summary>In the time of how many: the <c>q</c> of <c>(p:q:r</c>.</summary>
    public const string InTimeOf = "in-time-of";

    /// <summary>Over how many of the events after it: the <c>r</c> of <c>(p:q:r</c>.</summary>
    public const string Covers = "covers";

    /// <summary>The character a quoted run opens with to say where its words go: <c>^</c>, <c>_</c>, <c>&lt;</c>, <c>&gt;</c> or <c>@</c>.</summary>
    public const string Placement = "placement";

    /// <summary>The <c>~</c> that joins two words into one syllable.</summary>
    public const string Joined = "joined";

    /// <summary>The backslash that keeps the hyphen after it inside a syllable.</summary>
    public const string Escape = "escape";
}
