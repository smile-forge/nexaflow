namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>
/// A concrete pitch. <paramref name="Step"/> is the diatonic letter (0=C,1=D,2=E,3=F,4=G,5=A,6=B),
/// <paramref name="Alter"/> the chromatic offset in semitones (-2..+2), and <paramref name="Octave"/>
/// the scientific octave (middle C = C4). The engraver places notes on the staff by
/// <see cref="DiatonicIndex"/> (letter+octave only) — the chromatic <paramref name="Alter"/> never moves
/// a note vertically, it only selects an accidental glyph.
/// </summary>
public readonly record struct Pitch(int Step, int Alter, int Octave)
{
    /// <summary>A monotonic diatonic staff coordinate: C4 = 28, D4 = 29, … Each whole number is one
    /// staff position (line→space→line). Used directly for vertical placement.</summary>
    public int DiatonicIndex => Octave * 7 + Step;

    /// <summary>Letter names indexed by <see cref="Step"/>.</summary>
    public static readonly char[] StepLetters = ['C', 'D', 'E', 'F', 'G', 'A', 'B'];

    public char Letter => StepLetters[((Step % 7) + 7) % 7];

    public override string ToString()
    {
        string acc = Alter switch { -2 => "bb", -1 => "b", 1 => "#", 2 => "##", _ => "" };
        return $"{Letter}{acc}{Octave}";
    }
}
