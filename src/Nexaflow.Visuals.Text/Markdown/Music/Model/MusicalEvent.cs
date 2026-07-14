using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>
/// Base for anything that occupies time in a measure — a <see cref="Note"/>, <see cref="Rest"/> or
/// <see cref="Chord"/>. Events sharing a non-zero <see cref="BeamId"/> are beamed together; the parser
/// assigns beam ids (ABC groups by whitespace, LilyPond by its beaming rules).
/// </summary>
public abstract class MusicalEvent
{
    public Duration Duration { get; set; } = Duration.Quarter;

    /// <summary>Non-zero groups consecutive beamable events under one beam. 0 = unbeamed (draw a flag).</summary>
    public int BeamId { get; set; }
}

/// <summary>A single sounded pitch.</summary>
public sealed class Note : MusicalEvent
{
    public Pitch Pitch { get; set; }

    /// <summary>An accidental to draw explicitly (parser-decided; key-signature accidentals are not repeated here).</summary>
    public AccidentalKind Accidental { get; set; } = AccidentalKind.None;

    /// <summary>True when this note is tied into the next event (parsed; v1 renderer may not draw the tie).</summary>
    public bool TieStart { get; set; }
}

/// <summary>A silence.</summary>
public sealed class Rest : MusicalEvent { }

/// <summary>Two or more simultaneous notes sharing one stem. Parsed for ABC <c>[CEG]</c> / LilyPond
/// <c>&lt;c e g&gt;</c>; the v1 engraver renders the lowest note and records the chord for a later pass.</summary>
public sealed class Chord : MusicalEvent
{
    public List<Note> Notes { get; } = [];
}
