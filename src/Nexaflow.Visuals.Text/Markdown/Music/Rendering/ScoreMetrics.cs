namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// Every dimension the engraver uses, in one place, expressed as a multiple of the staff space — the unit
/// classical engraving is measured in, and the unit SMuFL fonts are designed against. Change <see cref="S"/>
/// and the whole score scales; change nothing else and the proportions stay right.
/// </summary>
internal static class ScoreMetrics
{
    /// <summary>The staff space (px): the gap between two staff lines. Everything else is a multiple of it.</summary>
    public const double S = 8.0;

    public const double StaffHeight = 4 * S;        // 4 spaces between 5 lines

    // Stems and beams (SMuFL "engravingDefaults" proportions).
    public const double StemLen     = 3.5 * S;
    public const double MinStem     = 2.4 * S;
    public const double StemThick   = 0.12 * S;
    public const double BeamThick   = 0.5 * S;
    public const double BeamGap     = 0.28 * S;     // between stacked beams
    public const double MaxBeamRise = 1.25 * S;     // a beam never climbs more than this across its group
    public const double MaxBeamSlope = 0.25;        // …nor steeper than 1:4
    public const double BeamStub    = 1.05 * S;     // a lone secondary beam (the 16th of a broken pair)

    // Lines.
    public const double StaffLineThick = 0.11 * S;
    public const double ThinBarline    = 0.16 * S;
    public const double ThickBarline   = 0.5 * S;
    public const double LedgerThick    = 0.16 * S;
    public const double LedgerExt      = 0.32 * S;  // how far a ledger line reaches past the head

    // Spacing. The horizontal room a note takes is `SlotBase + SlotRate × √(quarter-lengths)` — the classical
    // proportional-but-compressed curve, so a whole note is roughly three times an eighth rather than eight
    // times it. SlotFloor is the collision guard: no note head may come closer to the next than this, which is
    // what stops a septuplet's heads from touching.
    public const double SlotBase  = 1.0 * S;
    public const double SlotRate  = 2.35 * S;

    // …except between two notes of one beam group, which keep the tighter pair. A beam is a statement that
    // these notes belong together, and the way that is said on paper is that they sit closer to each other
    // than to what is either side of the group. Opening the spacing everywhere undoes the beam's own
    // meaning: four quavers evenly spaced across a bar are four quavers, not a group of four.
    public const double GroupBase = 0.85 * S;
    public const double GroupRate = 2.1 * S;
    public const double SlotFloor = 0.5 * S;        // added to the note-head width
    public const double TupletFloor = 0.55;         // …and a tuplet never compresses below this fraction

    public const double LeftMargin   = 2.0;
    public const double RightMargin  = 6.0;
    public const double SystemGap    = 1.5 * S;
    public const double StaffGap     = 2.5 * S;     // between two voices' staves
    public const double AccGap       = 0.16 * S;    // accidental → note head
    public const double DotGap       = 0.42 * S;    // note head → first augmentation dot
    public const double DotSpacing   = 0.20 * S;    // between dots
    public const double GraceGap     = 0.45 * S;    // grace group → its main note
    public const double GraceStep    = 0.5 * S;     // …and between two grace note heads
    public const double GraceScale   = 0.60;
    public const double LyricGap     = 0.5 * S;     // between two syllables under adjacent notes

    /// <summary>The bracket down the left of a multi-voice system.</summary>
    public const double BracketWidth = 0.5 * S;

    // Vertical room reserved outside the staff. The head- and foot-room a staff actually gets is measured from
    // its own notation (SystemLayout.AboveMusic/BelowMusic); these are only the floor and the rows.
    public const double ChordRow     = 1.7 * S;     // chord symbols / annotations above
    public const double VoltaRow     = 1.9 * S;     // repeat brackets above
    public const double SectionRow   = 1.8 * S;     // a mid-tune T: heading
    public const double LyricRow     = 1.55 * S;    // one verse of note-aligned lyrics below

    /// <summary>
    /// The air between the lowest thing the notation drew and the top of the first line of words.
    /// <para>
    /// Words sat directly under the reach of the music, which for a phrase of low notes put the tops of
    /// the letters a pixel under the ledger lines. Read at a glance the two ran together and the lyric
    /// looked like part of the staff. It is a clearance rather than part of <c>LyricRow</c> because it is
    /// paid once, under the music, not again between every verse.
    /// </para>
    /// </summary>
    public const double LyricClear   = 0.9 * S;

    /// <summary>
    /// The air between what opens a line — clef, key, meter — and the first note of it.
    /// <para>
    /// It used to be carried by the gap after the time signature, so a tune that printed no meter had no
    /// gap at all and its first note sat against the key signature. The opening of a line is a statement
    /// about the whole line rather than part of the first bar, and it needs to read as one.
    /// </para>
    /// </summary>
    public const double HeadGap      = 0.9 * S;

    /// <summary>How thick the line under a held syllable is drawn.</summary>
    public const double MelismaThick = 0.09 * S;
    public const double MarkRow      = 1.35 * S;    // one row of fermatas / ornaments / bowings above

    // Type.
    public const double TitleSize    = 15;
    public const double SubtitleSize = 11.5;
    public const double CreditSize   = 11;
    public const double ChordSize    = 11;
    public const double LyricSize    = 11;
    public const double VoltaSize    = 9.5;
    public const double FooterSize   = 11;

    /// <summary>Articulations and ornaments are drawn a size down from the notes — at full staff scale a
    /// Bravura accent reads as big as the note head it belongs to.</summary>
    public const double MarkScale = 0.78;

    // Curves. A tie or a slur is a filled crescent rather than a stroked arc: thin where it meets a note
    // head and thickest in the middle, which is what an engraved one looks like and what a stroked Bézier
    // of even thickness never does.
    public const double CurveThick = 0.22 * S;      // the crescent at its widest
    public const double CurveRise  = 0.9 * S;       // how far a short curve bows away from the notes
    public const double CurveMaxRise = 2.2 * S;     // …and how far the longest one is allowed to
    public const double CurveClear = 0.55 * S;      // between a curve and the head it springs from

    /// <summary>The repeat bracket's down-tick at each end.</summary>
    public const double VoltaTick = 0.9 * S;
}
