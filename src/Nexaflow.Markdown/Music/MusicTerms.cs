namespace Nexaflow.Markdown.Music;

/// <summary>The clef a staff is drawn with.</summary>
public enum ClefKind { Treble, Bass, Alto, Tenor }

/// <summary>
/// Where text written against a note sits — ABC's <c>"^text"</c>, <c>"_text"</c>, <c>"&lt;text"</c> and <c>"&gt;text"</c>, and
/// LilyPond's <c>^"text"</c> and <c>_"text"</c>.
/// </summary>
public enum AnnotationPlacement { Above, Below, Left, Right }

/// <summary>How a meter is printed: as its figures, or as the sign it was written as.</summary>
public enum MeterSign { Figures, Common, Cut }

/// <summary>
/// A mark written on a note, whatever notation spelled it: an articulation, which hugs the head, or an ornament, a fermata, a
/// bowing or a navigation sign, which stands clear of the staff (<see cref="MusicMarks.HugsHead"/>).
/// </summary>
public enum MusicMark
{
    Staccato,
    Tenuto,
    Accent,
    Marcato,
    Fermata,
    Trill,
    Turn,
    Mordent,
    LowerMordent,
    UpBow,
    DownBow,
    Segno,
    Coda,
}

/// <summary>What a mark is to the note it is written on.</summary>
public static class MusicMarks
{
    /// <summary>
    /// Whether a mark hugs the head, on the side the stem is not — an articulation — rather than stacking clear of the staff.
    /// </summary>
    public static bool HugsHead(this MusicMark mark) =>
        mark is MusicMark.Staccato or MusicMark.Tenuto or MusicMark.Accent or MusicMark.Marcato;
}
