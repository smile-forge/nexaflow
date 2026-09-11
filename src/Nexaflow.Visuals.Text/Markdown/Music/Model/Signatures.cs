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

    public static KeySignature FromFifths(int fifths) => new() { Fifths = fifths };

    /// <summary>Order of sharps by diatonic step (F C G D A E B).</summary>
    /// <summary>Order of flats by diatonic step (B E A D G C F).</summary>
}
