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

    // Spacing.
    public const double LeftMargin   = 2.0;
    public const double RightMargin  = 6.0;
    public const double SystemGap    = 1.5 * S;
    public const double StaffGap     = 2.5 * S;     // between two voices' staves
    public const double AccGap       = 0.16 * S;    // accidental → note head
    public const double DotGap       = 0.42 * S;    // note head → first augmentation dot
    public const double DotSpacing   = 0.20 * S;    // between dots
    public const double GraceGap     = 0.35 * S;    // grace group → its main note
    public const double GraceScale   = 0.60;

    // Vertical room reserved outside the staff.
    public const double AbovePad     = 4.5 * S;     // ledger notes, articulations, beams
    public const double BelowPad     = 4.5 * S;
    public const double ChordRow     = 1.7 * S;     // chord symbols / annotations above
    public const double VoltaRow     = 1.9 * S;     // repeat brackets above
    public const double SectionRow   = 1.8 * S;     // a mid-tune T: heading
    public const double LyricRow     = 1.55 * S;    // one verse of note-aligned lyrics below

    // Type.
    public const double TitleSize    = 15;
    public const double SubtitleSize = 11.5;
    public const double CreditSize   = 11;
    public const double ChordSize    = 11;
    public const double LyricSize    = 11;
    public const double VoltaSize    = 9.5;
    public const double FooterSize   = 11;
}
