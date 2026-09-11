namespace Nexaflow.Markdown.Music;

/// <summary>
/// A sounded pitch: which letter, how it is altered, and which octave.
/// </summary>
/// <param name="Step">0-6 for C D E F G A B.</param>
/// <param name="Alter">Semitones away from the natural, -2 to 2.</param>
/// <param name="Octave">Scientific octave numbering — middle C is octave 4.</param>
public readonly record struct Pitch(int Step, int Alter, int Octave)
{
    /// <summary>The note letters in staff order, from C.</summary>
    public const string Letters = "CDEFGAB";

    /// <summary>How many semitones each natural step sits above C.</summary>
    private static readonly int[] Natural = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>Where it sits on the staff, counting lines and spaces from C0. What decides its height.</summary>
    public int DiatonicIndex => (this.Octave * 7) + this.Step;

    /// <summary>How many semitones above C0 it sounds.</summary>
    public int Semitones => (this.Octave * 12) + Natural[this.Step] + this.Alter;

    /// <summary>
    /// The same pitch moved by an interval: as many letters as it spans, and as many semitones. Both, because
    /// a letter alone cannot say whether a third is major or minor, and a semitone count alone cannot say
    /// whether the result is written as an F sharp or a G flat.
    /// </summary>
    public Pitch Transposed(int steps, int semitones)
    {
        var index = this.DiatonicIndex + steps;
        var step = ((index % 7) + 7) % 7;
        var octave = (index - step) / 7;

        return new Pitch(step, this.Semitones + semitones - ((octave * 12) + Natural[step]), octave);
    }

    /// <summary>Written as a fact for the tree to carry: <c>step/alter/octave</c>.</summary>
    public override string ToString() => $"{this.Step}/{this.Alter}/{this.Octave}";

    /// <summary>Reads back what <see cref="ToString"/> wrote.</summary>
    public static Pitch Parse(string text)
    {
        var parts = text.Split('/');
        return parts.Length == 3
               && int.TryParse(parts[0], out var step)
               && int.TryParse(parts[1], out var alter)
               && int.TryParse(parts[2], out var octave)
            ? new Pitch(step, alter, octave)
            : default;
    }
}
