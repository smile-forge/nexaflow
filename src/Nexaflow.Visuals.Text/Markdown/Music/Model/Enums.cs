namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>The clef a staff is drawn with. v1 renders <see cref="Treble"/> and <see cref="Bass"/>;
/// <see cref="Alto"/>/<see cref="Tenor"/> are modelled for a future C-clef renderer.</summary>
public enum ClefKind { Treble, Bass, Alto, Tenor }

/// <summary>An explicit accidental drawn to the left of a note head (independent of the key signature).</summary>
public enum AccidentalKind { None, Sharp, Flat, Natural, DoubleSharp, DoubleFlat }

/// <summary>How a bar is terminated (or begun, for repeats).</summary>
public enum BarlineKind
{
    /// <summary>A normal single bar line.</summary>
    Single,
    /// <summary>A thin double bar — a section break (<c>||</c>).</summary>
    Double,
    /// <summary>The thin-thick final bar line (<c>|]</c>).</summary>
    Final,
    /// <summary>The thick-thin section start (<c>[|</c>).</summary>
    HeavyLight,
    /// <summary>Start-repeat: thick-thin + two dots (<c>|:</c>).</summary>
    RepeatStart,
    /// <summary>End-repeat: two dots + thin-thick (<c>:|</c>).</summary>
    RepeatEnd,
    /// <summary>Back-to-back end+start repeat (<c>:||:</c> / <c>::</c>).</summary>
    RepeatBoth,
}

/// <summary>Requested stem direction; <see cref="Auto"/> lets the engraver choose from staff position.</summary>
public enum StemDirection { Auto, Up, Down }

/// <summary>
/// A mark attached to one event. ABC writes these either as shorthand (<c>.</c> staccato, <c>~</c> roll,
/// <c>u</c> up-bow, <c>v</c> down-bow, <c>H</c> fermata, <c>T</c> trill …) or in <c>!name!</c> form. The
/// engraver splits them into two families: <em>note marks</em> (<see cref="Staccato"/>, <see cref="Tenuto"/>,
/// <see cref="Accent"/>, <see cref="Marcato"/> and the bowings) hug the note head on the side away from the
/// stem; <em>staff marks</em> (the rest) stack above the staff.
/// </summary>
public enum ArticulationKind
{
    Staccato, Tenuto, Accent, Marcato, UpBow, DownBow,
    Fermata, Trill, Roll, Turn, Mordent, LowerMordent, Segno, Coda,
}

/// <summary>Where a text annotation (ABC's <c>"^text"</c> / <c>"_text"</c> / <c>"&lt;text"</c> /
/// <c>"&gt;text"</c>) sits relative to its note. A bare <c>"text"</c> is a chord symbol, not an annotation.</summary>
public enum AnnotationPlacement { Above, Below, Left, Right }
