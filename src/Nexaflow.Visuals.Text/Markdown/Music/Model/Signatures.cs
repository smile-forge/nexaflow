namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>
/// A key signature expressed as a position on the circle of fifths: positive = that many sharps,
/// negative = that many flats, 0 = C major / A minor. The engraver draws the accidentals in the
/// canonical order and position; parsers map a tonic (<c>K:G</c>, <c>\key c \major</c>) to a count.
/// </summary>
public sealed class KeySignature
{
    /// <summary>Sharps (&gt;0) or flats (&lt;0) count.</summary>
    public int Fifths { get; init; }

    public static readonly KeySignature CMajor = new() { Fifths = 0 };

    public static KeySignature FromFifths(int fifths) => new() { Fifths = fifths };

    /// <summary>Order of sharps by diatonic step (F C G D A E B).</summary>
    public static readonly int[] SharpOrder = [3, 0, 4, 1, 5, 2, 6];

    /// <summary>Order of flats by diatonic step (B E A D G C F).</summary>
    public static readonly int[] FlatOrder = [6, 2, 5, 1, 4, 0, 3];
}

/// <summary>A time signature. <see cref="QuarterLengthPerMeasure"/> gives a bar's length in quarter
/// notes so parsers can split an event stream into <see cref="Measure"/>s.</summary>
public readonly record struct TimeSignature(int Numerator, int Denominator)
{
    public double QuarterLengthPerMeasure => Numerator * (4.0 / Denominator);
}
