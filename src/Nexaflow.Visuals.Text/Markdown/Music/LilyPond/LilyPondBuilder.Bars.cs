using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using ClefKind = Nexaflow.Visuals.Text.Markdown.Music.Model.ClefKind;
using KeySignature = Nexaflow.Visuals.Text.Markdown.Music.Model.KeySignature;

namespace Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

/// <summary>
/// The three things LilyPond leaves to whoever engraves it: where the bars fall, which notes beam together, and
/// which accidentals print. All three follow from the meter and the key where a note is played, which is only
/// known once a staff has been played through.
/// </summary>
internal sealed partial class LilyPondBuilder
{
    /// <summary>One bar as it closed, with what beaming and accidentals need beside it.</summary>
    private sealed record Closed(Bar Bar, List<Sounded> Events, (int Beats, int Unit) Meter);

    /// <summary>Every staff, cut into bars and lines.</summary>
    private List<Row> Rows() => [.. _staves.SelectMany(Barred)];

    /// <summary>
    /// One staff's music cut into bars by its meter, beamed by its meter, and told which accidentals print.
    ///
    /// <para>
    /// A <c>|</c> is a check, not a line: the meter closes the bar, and a check written where it closes names
    /// the line, so the line can be pointed at like anything else somebody wrote. A line the meter implied with
    /// no check beside it names nothing, and is drawn all the same.
    /// </para>
    /// </summary>
    private List<Row> Barred(Stave stave)
    {
        var rows = new List<Row>();
        var closed = new List<Closed>();

        // What the staff opens with, until its first event says the opening is over.
        var opening = true;
        var clef = ClefKind.Treble;
        var fifths = 0;
        var openingFifths = 0;
        var meter = (Beats: 4, Unit: 4);
        var numeric = false;
        var free = false;
        var hidden = false;
        var pickup = Duration.Zero;

        Row? row = null;
        var bar = new Bar();
        var events = new List<Sounded>();
        var barRow = (Row?)null;
        var at = Duration.Zero;
        var now = Duration.Zero;

        // What waits for the next bar to start.
        KeySignature? keyChange = null;
        (int Beats, int Unit, int? Sign)? meterChange = null;
        (ISourcePart Part, string Label)? volta = null;
        Barline? open = null;
        var breaking = false;
        var unbracket = false;
        ClefKind? nextClef = null;

        Duration? Cap() => free ? null : Duration.Of(meter.Beats * 4, meter.Unit);

        Row NewRow()
        {
            if (nextClef is { } changed) { clef = changed; nextClef = null; }

            var made = new Row
            {
                Clef = clef,
                Fifths = fifths,
                Meter = free || hidden ? null : (meter.Beats, meter.Unit, Sign(meter, numeric)),
                Voice = $"{stave.Number}",
                Index = rows.Count,
                Name = stave.Name,
                Piece = stave.Piece,
            };

            rows.Add(made);
            return made;
        }

        Bar? Previous() => closed.Count > 0 ? closed[^1].Bar : null;

        void Close(Barline line)
        {
            if (bar.Events.Count == 0) return;

            bar.Closed = line;
            bar.EndsRepeat = line.Drawn.StartsWith(':');
            bar.EndsBracket = unbracket;
            unbracket = false;
            bar.Part = Across(bar.Events.Select(e => e.Part));
            (barRow ?? row!).Bars.Add(bar);
            closed.Add(new Closed(bar, events, meter));

            bar = new Bar();
            events = [];
            barRow = null;
            at = Duration.Zero;
        }

        // A line somebody wrote: where the meter has just closed a bar it is that bar's line, and anywhere else
        // it ends the bar where it is written. With nothing before it at all it opens the first bar.
        void Line(string drawn, ISourcePart part)
        {
            var line = new Barline(drawn, part);

            if (bar.Events.Count > 0) { Close(line); return; }

            if (Previous() is { } last)
            {
                // A repeat starting where one has just ended is one line with dots either side of it, which is how
                // LilyPond draws it too.
                if (last.Closed is { Part: not null } ended && ended.Drawn.StartsWith(':') && drawn.EndsWith(':'))
                    line = new Barline(":||:", ended.Part);

                last.Closed = line;
                last.EndsRepeat = line.Drawn.StartsWith(':');
                return;
            }

            open = line;
        }

        foreach (var played in stave.Stream)
        {
            switch (played)
            {
                case Sounded sounded:
                    if (opening)
                    {
                        opening = false;
                        openingFifths = fifths;
                        row = NewRow();
                        if (pickup > Duration.Zero && Cap() is { } full && pickup < full) at = full - pickup;
                    }

                    if (bar.Events.Count == 0)
                    {
                        if (breaking && row!.Bars.Count > 0) row = NewRow();
                        breaking = false;
                        barRow = row;

                        bar.KeyChange = keyChange;
                        bar.MeterChange = meterChange;
                        if (volta is { } bracket) { bar.Volta = bracket.Part; bar.VoltaLabel = bracket.Label; }
                        if (open is not null) bar.Opened = open;

                        if (keyChange is { } key) fifths = key.Fifths;
                        keyChange = null;
                        meterChange = null;
                        volta = null;
                        open = null;
                    }

                    sounded.Start = now;
                    sounded.At = at;
                    bar.Events.Add(sounded.Event);
                    events.Add(sounded);

                    at += sounded.Lasts;
                    now += sounded.Lasts;

                    if (Cap() is { } cap && at >= cap) Close(Implied);
                    break;

                case Checked check:
                    if (bar.Events.Count == 0 && Previous() is { Closed: { Part: null } line } checkedBar)
                        checkedBar.Closed = line with { Part = check.Part };
                    break;

                case Lined written:
                    Line(written.Drawn, written.Part);
                    break;

                case Bracketed ending:
                    if (bar.Events.Count > 0) Close(Implied);
                    volta = (ending.Part, ending.Label);
                    break;

                case Keyed keyed:
                    if (opening) fifths = keyed.Fifths;
                    else keyChange = KeySignature.FromFifths(keyed.Fifths);
                    break;

                case Metered metered:
                    meter = (metered.Beats, metered.Unit);
                    if (!opening) meterChange = (meter.Beats, meter.Unit, Sign(meter, numeric));
                    break;

                case Figured figured:
                    // Written after the \time it restyles as often as before it, so it reaches back to one still
                    // waiting for its bar as well as forward to every later one.
                    numeric = figured.Numeric;
                    if (meterChange is { } waiting) meterChange = (waiting.Beats, waiting.Unit, Sign((waiting.Beats, waiting.Unit), numeric));
                    break;

                case Clefed clefed:
                    if (opening) clef = clefed.Clef;
                    else nextClef = clefed.Clef;
                    break;

                case Pickup partial:
                    if (opening) pickup = partial.Length;
                    else if (Cap() is { } length && partial.Length < length) at = length - partial.Length;
                    break;

                case Cadenza cadenza:
                    free = cadenza.On;
                    break;

                case Broken:
                    breaking = true;
                    break;

                case Unbracketed:
                    if (bar.Events.Count > 0) unbracket = true;
                    else if (Previous() is { } ended) ended.EndsBracket = true;
                    break;

                case Hidden:
                    if (opening) hidden = true;
                    break;
            }
        }

        // Where the music stops, LilyPond draws a plain line.
        Close(Implied);

        Beam(closed);
        Accidentals(closed, openingFifths);
        return rows;
    }

    /// <summary>A bar line the meter implied, which nobody wrote and so names nothing.</summary>
    private static Barline Implied => new("|", null);

    /// <summary>The sign a meter is printed as: common and cut time unless the figures were asked for.</summary>
    private static int? Sign((int Beats, int Unit) meter, bool numeric) =>
        numeric ? null : meter switch
        {
            (4, 4) => Smufl.TimeSigCommon,
            (2, 2) => Smufl.TimeSigCutCommon,
            _ => null,
        };

    /// <summary>
    /// Beams from the meter. Eighths group by the half-bar in common time and by the dotted quarter in a compound
    /// one; anything shorter groups by the beat. A tuplet beams as one group, a rest breaks a beam, a beam never
    /// crosses a bar line, and a beam written by hand is left as it was written.
    /// </summary>
    private static void Beam(List<Closed> bars)
    {
        foreach (var (_, events, meter) in bars)
        {
            var run = new List<Sounded>();
            var from = 0.0;

            void Flush()
            {
                if (run.Count >= 2)
                {
                    var group = Across(run.Select(s => s.Event.Part));
                    foreach (var sounded in run) sounded.Event.Beam = group;
                }

                run.Clear();
            }

            foreach (var sounded in events)
            {
                if (sounded.ByHand || !sounded.Beamed || !sounded.Event.Beamable) { Flush(); continue; }

                // A beam runs inside one tuplet or outside every tuplet, never from one into the next.
                if (run.Count > 0 && !ReferenceEquals(run[^1].Event.Tuplet, sounded.Event.Tuplet)) Flush();

                var unit = BeamUnit(meter, sounded.Event.BaseValue);
                var sameTuplet = run.Count > 0 && sounded.Event.Tuplet is { } tuplet
                                 && ReferenceEquals(run[^1].Event.Tuplet, tuplet);

                if (run.Count > 0 && !sameTuplet
                    && Math.Floor((from / unit) + 1e-9) != Math.Floor((sounded.At.Quarters / unit) + 1e-9))
                    Flush();

                if (run.Count == 0) from = sounded.At.Quarters;
                run.Add(sounded);
            }

            Flush();
        }
    }

    /// <summary>
    /// How long a beamed group may run, in quarter notes: LilyPond's default beaming. That is the beat, except
    /// where a meter has an exception for the value — eighths beam by the half bar in four-four, and in
    /// three-four the whole bar goes under one beam.
    /// </summary>
    private static double BeamUnit((int Beats, int Unit) meter, int value)
    {
        if (meter.Unit == 8 && meter.Beats % 3 == 0) return 1.5;

        var beat = 4.0 / meter.Unit;
        if (value >= 16) return Math.Min(beat, 1.0);
        if (meter is (3, 4) && value == 8) return 3.0;

        return meter.Beats * beat >= 4 && meter.Beats % 2 == 0 ? 2.0 : beat;
    }

    /// <summary>
    /// Which accidentals print. A LilyPond note name carries its own alteration — <c>fis</c> is F sharp whatever
    /// the key — so the source never says "print a sharp here". The ordinary rule decides: print one only where a
    /// note departs from what is in force in its bar, which starts as the key and is overridden, for that line or
    /// space, by each accidental printed. A note tied over from the bar before is not given its accidental again.
    /// </summary>
    private static void Accidentals(List<Closed> bars, int fifths)
    {
        Sounded? before = null;

        foreach (var (bar, events, _) in bars)
        {
            if (bar.KeyChange is { } key) fifths = key.Fifths;
            var inForce = new Dictionary<int, int>();

            foreach (var sounded in events)
            {
                var printed = new int?[sounded.Pitches.Length];

                for (var i = 0; i < sounded.Pitches.Length; i++)
                {
                    var pitch = sounded.Pitches[i];
                    var line = pitch.DiatonicIndex;
                    var current = inForce.TryGetValue(line, out var held) ? held : Keys.AlterFor(pitch.Step, fifths);
                    var tiedOver = before is { Event.TieStart: true } && before.Pitches.Contains(pitch);

                    if (!sounded.Forced[i] && (pitch.Alter == current || tiedOver))
                    {
                        inForce[line] = pitch.Alter;
                        continue;
                    }

                    inForce[line] = pitch.Alter;
                    printed[i] = pitch.Alter;
                }

                sounded.Event.Accidentals = printed;
                before = sounded;
            }
        }
    }
}
