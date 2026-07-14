using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// Vertical placement maths for one clef. A staff's bottom line is "half-space 0"; every diatonic step
/// is one half-space up. A pitch's line/space position is its <see cref="Pitch.DiatonicIndex"/> minus the
/// clef's bottom-line reference index. Also holds the clef glyph's reference line and the canonical
/// diatonic positions for key-signature accidentals (treble/bass hardcoded; C-clefs approximate to treble).
/// </summary>
internal sealed class StaffGeometry
{
    /// <summary>Diatonic index sitting on the bottom staff line for this clef.</summary>
    public int BottomLineIndex { get; }

    /// <summary>Half-spaces above the bottom line where the clef glyph's reference line sits.</summary>
    public int ClefRefHalfSpaces { get; }

    public int ClefGlyph { get; }

    private readonly int[] _sharpIndices;
    private readonly int[] _flatIndices;

    private StaffGeometry(int bottomLineIndex, int clefRefHalfSpaces, int clefGlyph,
        int[] sharpIndices, int[] flatIndices)
    {
        BottomLineIndex = bottomLineIndex;
        ClefRefHalfSpaces = clefRefHalfSpaces;
        ClefGlyph = clefGlyph;
        _sharpIndices = sharpIndices;
        _flatIndices = flatIndices;
    }

    public static StaffGeometry For(ClefKind clef) => clef switch
    {
        ClefKind.Bass  => Bass,
        ClefKind.Alto  => Alto,
        ClefKind.Tenor => Tenor,
        _              => Treble,
    };

    /// <summary>Half-spaces of a pitch above the bottom staff line (0 = bottom line, 8 = top line).</summary>
    public int HalfSpacesAbove(Pitch p) => p.DiatonicIndex - BottomLineIndex;

    /// <summary>The diatonic index at which the n-th sharp/flat of a key signature is drawn.</summary>
    public int KeyAccidentalIndex(int order, bool sharp) =>
        sharp ? _sharpIndices[order] : _flatIndices[order];

    // Treble: bottom line E4 (index 30); G-clef reference line G4 at half-space 2.
    // Key-sig positions in F C G D A E B (sharps) / B E A D G C F (flats) order.
    private static readonly StaffGeometry Treble = new(
        bottomLineIndex: 30, clefRefHalfSpaces: 2, clefGlyph: Smufl.GClef,
        sharpIndices: [38, 35, 39, 36, 33, 37, 34],   // F#5 C#5 G#5 D#5 A#4 E#5 B#4
        flatIndices:  [34, 37, 33, 36, 32, 35, 31]);  // Bb4 Eb5 Ab4 Db5 Gb4 Cb5 Fb4

    // Bass: bottom line G2 (index 18); F-clef reference line F3 at half-space 6.
    private static readonly StaffGeometry Bass = new(
        bottomLineIndex: 18, clefRefHalfSpaces: 6, clefGlyph: Smufl.FClef,
        sharpIndices: [24, 21, 25, 22, 19, 23, 20],   // F#3 C#3 G#3 D#3 A#2 E#3 B#2
        flatIndices:  [20, 23, 19, 22, 18, 21, 17]);  // Bb2 Eb3 Ab2 Db3 Gb2 Cb3 Fb2

    // Alto (C-clef, middle line C4 = index 28 at half-space 4); reuse treble-shape sig positions minus a 6th.
    private static readonly StaffGeometry Alto = new(
        bottomLineIndex: 24, clefRefHalfSpaces: 4, clefGlyph: Smufl.CClef,
        sharpIndices: [31, 28, 32, 29, 26, 30, 27],
        flatIndices:  [27, 30, 26, 29, 25, 28, 24]);

    // Tenor (C-clef, fourth line C4); one line higher than alto.
    private static readonly StaffGeometry Tenor = new(
        bottomLineIndex: 22, clefRefHalfSpaces: 6, clefGlyph: Smufl.CClef,
        sharpIndices: [29, 26, 30, 27, 24, 28, 25],
        flatIndices:  [25, 28, 24, 27, 23, 26, 22]);
}
