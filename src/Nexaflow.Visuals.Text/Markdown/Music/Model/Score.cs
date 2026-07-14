using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Music.Model;

/// <summary>
/// The shared intermediate representation both notation parsers target and the single engraver consumes.
/// A score is its front matter plus one or more <see cref="Staff"/>s; v1 renders each staff as its own
/// single-line system (a future grand-staff pass will bracket simultaneous staves). Parsers append
/// human-readable <see cref="Warnings"/> for constructs they could not represent, which the renderer
/// surfaces to the reader.
///
/// The front-matter properties mirror where an engraver prints each one: <see cref="Title"/> and
/// <see cref="Subtitles"/> above the first system, <see cref="Rhythm"/> at its top left, the
/// <see cref="Composer"/> (with <see cref="Origin"/> in brackets after it) at its top right, and
/// <see cref="Notes"/>/<see cref="Source"/>/<see cref="Transcription"/>/<see cref="Verses"/> underneath
/// the last one.
/// </summary>
public sealed class Score
{
    /// <summary>The tune title (ABC's first <c>T:</c>).</summary>
    public string? Title { get; set; }

    /// <summary>Further <c>T:</c> lines in the header — printed under the title in a smaller face.</summary>
    public List<string> Subtitles { get; } = [];

    /// <summary>Printed at the top right (ABC <c>C:</c>).</summary>
    public string? Composer { get; set; }

    /// <summary>Printed in brackets after the composer (ABC <c>O:</c>).</summary>
    public string? Origin { get; set; }

    /// <summary>Printed in italics at the top left (ABC <c>R:</c>).</summary>
    public string? Rhythm { get; set; }

    /// <summary>Printed under the score as "Source: …" (ABC <c>S:</c>).</summary>
    public string? Source { get; set; }

    /// <summary>Printed under the score as "Transcription: …" (ABC <c>Z:</c>).</summary>
    public string? Transcription { get; set; }

    /// <summary>Printed under the score as "Notes: …" (ABC <c>N:</c>).</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Verse text printed under the score as-is (ABC's upper-case <c>W:</c>), as opposed to the
    /// note-aligned syllables of <c>w:</c> which live on the events.</summary>
    public List<string> Verses { get; } = [];

    public List<Staff> Staves { get; } = [];

    /// <summary>Unsupported/ignored source constructs, surfaced under the rendered score.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>How many verses of note-aligned lyrics the events carry (0 when the tune is unsung).</summary>
    public int LyricVerses
    {
        get
        {
            int n = 0;
            foreach (var s in Staves)
                foreach (var m in s.Measures)
                    foreach (var e in m.Events)
                        if (e.Lyrics.Count > n) n = e.Lyrics.Count;
            return n;
        }
    }

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

/// <summary>One staff line of music: the clef, key and time signature it <em>opens</em> with, followed by its
/// measures. A tune that changes key or meter part-way records that on the <see cref="Measure"/> it happens
/// at, so the engraver can print the change in place and carry it into the next system's header.</summary>
public sealed class Staff
{
    public ClefKind Clef { get; set; } = ClefKind.Treble;
    public KeySignature Key { get; set; } = KeySignature.CMajor;
    public TimeSignature Time { get; set; } = new(4, 4);

    /// <summary>False for free meter (ABC's <c>M:none</c> or a bare <c>M:</c>) — the staff prints no time
    /// signature, though <see cref="Time"/> still holds the 4/4 the parser reckons note lengths against.</summary>
    public bool ShowTime { get; set; } = true;

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

    /// <summary>A key change taking effect at this measure (a mid-tune ABC <c>K:</c>), else null.</summary>
    public KeySignature? KeyChange { get; set; }

    /// <summary>A meter change taking effect at this measure (a mid-tune ABC <c>M:</c>), else null.</summary>
    public TimeSignature? TimeChange { get; set; }

    /// <summary>The label of a repeat bracket opening here — ABC's <c>|1</c> / <c>[2</c>, so usually "1" or
    /// "2". The bracket runs to the measure that ends the repeat (or to the next bracket).</summary>
    public string? Volta { get; set; }

    /// <summary>A section heading printed above this measure — a mid-tune ABC <c>T:</c>. Forces a system
    /// break so the heading has a line to sit on.</summary>
    public string? SectionLabel { get; set; }
}
