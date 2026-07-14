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
    /// <summary>A thin double bar (section break).</summary>
    Double,
    /// <summary>The thin-thick final bar line.</summary>
    Final,
    /// <summary>Start-repeat: thick-thin + two dots (<c>|:</c>).</summary>
    RepeatStart,
    /// <summary>End-repeat: two dots + thin-thick (<c>:|</c>).</summary>
    RepeatEnd,
    /// <summary>Back-to-back end+start repeat (<c>:||:</c> / <c>::</c>).</summary>
    RepeatBoth,
}

/// <summary>Requested stem direction; <see cref="Auto"/> lets the engraver choose from staff position.</summary>
public enum StemDirection { Auto, Up, Down }
