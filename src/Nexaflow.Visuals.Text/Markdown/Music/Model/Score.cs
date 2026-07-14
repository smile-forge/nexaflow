using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>
/// The shared intermediate representation both notation parsers target and the single engraver consumes.
/// A score is a title/composer plus one or more <see cref="Staff"/>s; v1 renders each staff as its own
/// single-line system (a future grand-staff pass will bracket simultaneous staves). Parsers append
/// human-readable <see cref="Warnings"/> for constructs they could not represent, which the renderer
/// surfaces to the reader.
/// </summary>
public sealed class Score
{
    public string? Title { get; set; }
    public string? Composer { get; set; }

    public List<Staff> Staves { get; } = [];

    /// <summary>Unsupported/ignored source constructs, surfaced under the rendered score.</summary>
    public List<string> Warnings { get; } = [];

    public bool IsEmpty
    {
        get
        {
            foreach (var s in Staves)
                if (s.Measures.Count > 0)
                    return false;
            return true;
        }
    }
}

/// <summary>One staff line of music: a clef, key and time signature followed by its measures.</summary>
public sealed class Staff
{
    public ClefKind Clef { get; set; } = ClefKind.Treble;
    public KeySignature Key { get; set; } = KeySignature.CMajor;
    public TimeSignature Time { get; set; } = new(4, 4);

    /// <summary>Optional label drawn at the left of the first system (e.g. a LilyPond voice/staff name).</summary>
    public string? Name { get; set; }

    public List<Measure> Measures { get; } = [];
}

/// <summary>A bar: an ordered list of events plus its opening and closing bar lines (repeats live here).</summary>
public sealed class Measure
{
    public List<MusicalEvent> Events { get; } = [];

    public BarlineKind StartBarline { get; set; } = BarlineKind.Single;
    public BarlineKind EndBarline { get; set; } = BarlineKind.Single;

    /// <summary>A notation-requested system break after this measure (e.g. an ABC source line ending, or a
    /// LilyPond <c>\break</c>). The engraver honours it, then still wraps further if the line is too wide.</summary>
    public bool SystemBreak { get; set; }
}
