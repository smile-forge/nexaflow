namespace Nexaflow.Markdown.Scores.Rendering;

/// <summary>
/// SMuFL (Standard Music Font Layout) codepoints in the bundled Bravura font: which glyph each thing a score draws is. What the
/// glyphs look like, how far each reaches and how it is drawn are the font's, and so the layer's an engraver draws through.
/// </summary>
internal static class Smufl
{
    // ── Codepoints ──────────────────────────────────────────────────────────
    public const int GClef = 0xE050;
    public const int FClef = 0xE062;
    public const int CClef = 0xE05C;

    public const int NoteheadDoubleWhole = 0xE0A0;
    public const int NoteheadWhole       = 0xE0A2;
    public const int NoteheadHalf        = 0xE0A3;
    public const int NoteheadBlack       = 0xE0A4;

    public const int Flag8thUp    = 0xE240, Flag8thDown  = 0xE241;
    public const int Flag16thUp   = 0xE242, Flag16thDown = 0xE243;
    public const int Flag32ndUp   = 0xE244, Flag32ndDown = 0xE245;
    public const int Flag64thUp   = 0xE246, Flag64thDown = 0xE247;

    public const int AccidentalFlat        = 0xE260;
    public const int AccidentalNatural     = 0xE261;
    public const int AccidentalSharp       = 0xE262;
    public const int AccidentalDoubleSharp = 0xE263;
    public const int AccidentalDoubleFlat  = 0xE264;

    public const int RestDoubleWhole = 0xE4E2;
    public const int RestWhole       = 0xE4E3;
    public const int RestHalf        = 0xE4E4;
    public const int RestQuarter     = 0xE4E5;
    public const int Rest8th         = 0xE4E6;
    public const int Rest16th        = 0xE4E7;
    public const int Rest32nd        = 0xE4E8;

    public const int TimeSig0 = 0xE080;          // 0..9 are contiguous: TimeSig0 + digit
    public const int TimeSigCommon    = 0xE08A;  // the C of common time…
    public const int TimeSigCutCommon = 0xE08B;  // …and the struck C of cut time
    public const int AugmentationDot = 0xE1E7;

    // Articulations. Each has an "Above" and a "Below" form; the Below glyph is the next codepoint up.
    public const int ArticAccentAbove   = 0xE4A0;
    public const int ArticStaccatoAbove = 0xE4A2;
    public const int ArticTenutoAbove   = 0xE4A4;
    public const int ArticMarcatoAbove  = 0xE4AC;

    public const int FermataAbove         = 0xE4C0;
    public const int OrnamentTrill        = 0xE566;
    public const int OrnamentTurn         = 0xE567;
    public const int OrnamentMordent      = 0xE56C;
    public const int OrnamentLowerMordent = 0xE56D;

    public const int StringsDownBow = 0xE610;
    public const int StringsUpBow   = 0xE612;

    public const int Segno = 0xE047;
    public const int Coda  = 0xE048;

    /// <summary>Tuplet digits — a dedicated set, narrower than the time-signature figures.</summary>
    public const int TupletDigit0 = 0xE880;
}
