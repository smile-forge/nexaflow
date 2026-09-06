using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music.Abc.Stages;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// The engraver: reads a tune and decides where every piece of it goes.
///
/// <para>
/// The stage between the pipeline and the layout tree, and the only one that knows about a page. Everything
/// it needs to know about the <em>music</em> has already been worked out — what each note sounds, how long
/// each event lasts, what is grouped with what, where the bars are — so what is left here is engraving:
/// how much room a note takes, which way a stem points, how steeply a beam leans, where a system breaks and
/// how it is justified.
/// </para>
/// <para>
/// <strong>It never names a point in the source.</strong> It is handed a reading and takes no offsets from
/// it: each piece is told the part it was drawn from, and where that part is written is the reading's to
/// answer, worked out by a walk when somebody asks. An offset stored beside a tree is a second copy of a
/// fact the tree already holds, and the two go out of step the moment anything is edited.
/// </para>
/// <para>
/// The judgement calls are not here either. Stem direction, beam slope and note spacing live in
/// <see cref="Engraving"/> and <see cref="ScoreMetrics"/>, where they can be asserted rather than eyeballed,
/// and are shared with the engraver the <c>#%</c> block still uses.
/// </para>
/// </summary>
internal sealed partial class AbcBuilder
{
    private readonly ContentReading _reading;
    private readonly Brush _ink;
    private readonly double _ppd;

    private AbcBuilder(ContentReading reading, Brush ink, double pixelsPerDip)
    {
        _reading = reading;
        _ink = ink;
        _ppd = pixelsPerDip <= 0 ? 1.0 : pixelsPerDip;
    }

    /// <summary>Engraves a tune to fit <paramref name="width"/>, and says how big it came out.</summary>
    public static (AbcLayoutNode Root, Size Size) Build(
        ContentReading reading, double width, Brush ink, double pixelsPerDip)
    {
        var builder = new AbcBuilder(reading, ink, pixelsPerDip);
        return builder.Engrave(width);
    }

    // ── What there is to draw ───────────────────────────────────────────────

    /// <summary>One event, read off the tree and not yet placed.</summary>
    private sealed class Event
    {
        public required ContentPart Part;
        public required ContentNode Node;

        /// <summary>Where each of its heads sits, in half-spaces above the bottom line.</summary>
        public int[] Heads = [];

        /// <summary>The accidental printed before each head, or null for none.</summary>
        public int?[] Accidentals = [];

        public int BaseValue = 4;      // 1 = whole, 2 = half, 4 = quarter, 8 = eighth …
        public int Dots;
        public bool IsRest;
        public bool Invisible;
        public bool WholeBar;
        public double Quarters;

        /// <summary>The beam this was written into, if any — the group a click selects first.</summary>
        public ContentPart? Beam;

        /// <summary>The tuplet this was written into, and the number printed over it.</summary>
        public ContentPart? Tuplet;
        public int TupletNumber;

        public string? ChordSymbol;
        public List<(int Verse, string Text, bool Hyphen, bool Melisma)> Lyrics = [];

        /// <summary>Marks that hug the head, on the side the stem is not: staccato, tenuto, accent.</summary>
        public List<int> HeadMarks = [];

        /// <summary>Marks that stack clear of the staff: a fermata, an ornament, a bowing, a segno.</summary>
        public List<int> StaffMarks = [];

        /// <summary>Text placed relative to this event by the character that opened its quotes.</summary>
        public List<(string Text, AnnotationPlacement Where)> Annotations = [];

        /// <summary>True when a tie runs from this event to the next of the same pitch.</summary>
        public bool TieStart;

        /// <summary>How many slurs open on this event, and how many close on it. ABC allows nesting.</summary>
        public int SlurOpen;
        public int SlurClose;

        /// <summary>Grace notes crushed in before it, each a half-space and a written value.</summary>
        public List<(int Half, int Value)> Graces = [];

        /// <summary>True for an acciaccatura — the group is drawn with a slash through its stem.</summary>
        public bool GraceSlashed;

        // Placed, once the system is laid out.
        public double SlotWidth;
        public double AccidentalWidth;
        public double GraceWidth;
        public double X;

        public bool Beamable => BaseValue >= 8 && !IsRest;
    }

    /// <summary>One bar, and the lines that opened and closed it.</summary>
    private sealed class Bar
    {
        public required ContentPart Part;
        public ContentPart? Opened;
        public ContentPart? Closed;
        public List<Event> Events = [];
        public double Width;
        public double X;

        /// <summary>Room taken at the head of this bar by a key or meter written in the middle of a tune.</summary>
        public double SignatureWidth;
        public KeySignature? KeyChange;
        public (int Beats, int Unit)? MeterChange;

        /// <summary>The repeat bracket opening at this bar — ABC's <c>|1</c> or <c>[2</c> — and its label.</summary>
        public ContentPart? Volta;
        public string? VoltaLabel;

        /// <summary>True when the line closing this bar ends a repeat, which is where a bracket stops.</summary>
        public bool EndsRepeat;
    }

    /// <summary>One line of music, which is where a system may break.</summary>
    private sealed class Row
    {
        public List<Bar> Bars = [];
        public AbcContext Context;
        public ClefKind Clef = ClefKind.Treble;
    }

    // ── Reading it ──────────────────────────────────────────────────────────

    /// <summary>
    /// The tune as rows of bars of events, read off the parse tree.
    /// <para>
    /// A walk rather than a query, because the order matters: what a system holds is the bars in the order
    /// they were written, and a mid-tune key change belongs to the bar it takes effect at.
    /// </para>
    /// </summary>
    private List<Row> Read()
    {
        var rows = new List<Row>();
        var key = (KeySignature?)null;
        var meter = ((int Beats, int Unit)?)null;
        var started = false;

        foreach (var line in _reading.Root.Children)
        {
            if (line.Kind == AbcKinds.Field)
            {
                // A field between lines changes what the next system opens with, and the change is printed
                // at the head of the bar it reaches. Only once the music has started, though: the header's
                // own K: and M: are what every system opens with, and printing them again at the head of
                // the first bar sets the meter twice on the first line of every tune.
                if (!started) continue;

                var name = line.Part(Roles.Name)?.Text ?? "";
                var value = line.Part(AbcRoles.Value)?.Node.Text ?? "";

                if (name.StartsWith('K')) key = KeyOf(value);
                if (name.StartsWith('M')) meter = AbcTheory.Meter(value);
                continue;
            }

            if (line.Kind != AbcKinds.Line) continue;

            var context = ResolveContext.Of(line.Node);
            var row = new Row { Context = context, Clef = ClefOf(line) };

            foreach (var piece in line.Children)
            {
                if (piece.Kind != AbcKinds.Measure) continue;

                var bar = new Bar { Part = piece, KeyChange = key, MeterChange = meter };
                key = null;
                meter = null;

                foreach (var inside in piece.Children)
                {
                    if (inside.Role == Roles.Open) { bar.Opened = inside; continue; }
                    if (inside.Role == Roles.Close) { bar.Closed = inside; continue; }
                    Gather(inside, bar, null);
                }

                bar.EndsRepeat = Printed(bar.Closed).Contains(':');
                if (bar.Events.Count > 0 || bar.Closed is not null) row.Bars.Add(bar);
            }

            Brackets(row);
            if (row.Bars.Count > 0) { rows.Add(row); started = true; }
        }

        return rows;
    }

    /// <summary>
    /// Which bar each repeat bracket opens at.
    /// <para>
    /// A number is written straight after a bar line — <c>|1</c> — so it belongs to the bar that line
    /// opens rather than to the one it closed. Which is a fact about two bars and is settled here, once
    /// the row is read, rather than by every reader that meets a number.
    /// </para>
    /// </summary>
    private static void Brackets(Row row)
    {
        for (var at = 0; at < row.Bars.Count; at++)
        {
            if (Volta(row.Bars[at].Opened) is { } opening)
            {
                row.Bars[at].Volta = opening.Part;
                row.Bars[at].VoltaLabel = opening.Label;
            }

            if (Volta(row.Bars[at].Closed) is not { } closing || at + 1 >= row.Bars.Count) continue;

            row.Bars[at + 1].Volta = closing.Part;
            row.Bars[at + 1].VoltaLabel = closing.Label;
        }
    }

    /// <summary>The repeat-bracket number written on a bar line, or nothing.</summary>
    private static (ContentPart Part, string Label)? Volta(ContentPart? line)
    {
        if (line is null) return null;

        foreach (var piece in line.SelfAndDescendants())
            if (piece.Kind == AbcKinds.Volta)
                return (piece, piece.Node.Text);

        return null;
    }

    /// <summary>What a part was written as, or nothing where there is no part.</summary>
    private static string Printed(ContentPart? part) => part?.Node.Print() ?? "";

    /// <summary>Everything in a bar that takes time, in written order, with the groups it was written into.</summary>
    private void Gather(ContentPart piece, Bar bar, ContentPart? beam)
    {
        switch (piece.Kind)
        {
            case AbcKinds.Beam:
                foreach (var child in piece.Children) Gather(child, bar, piece);
                return;

            case AbcKinds.TupletGroup:
            {
                // The marker is written before the notes and says nothing about where they stop, so what
                // it covers is only knowable from the group the pipeline made of it.
                var (notes, _) = Nexaflow.Markdown.Music.Abc.Stages.GroupTuplets.Of(piece.Node);
                var from = bar.Events.Count;

                foreach (var child in piece.Children) Gather(child, bar, beam);

                for (var i = from; i < bar.Events.Count; i++)
                {
                    bar.Events[i].Tuplet = piece;
                    bar.Events[i].TupletNumber = notes;
                }

                return;
            }

            case AbcKinds.Note when piece.Part(AbcRoles.Letter) is not null:
                bar.Events.Add(Note(piece, beam));
                return;

            case AbcKinds.Chord:
                bar.Events.Add(Chord(piece, beam));
                return;

            case AbcKinds.Rest:
                bar.Events.Add(Rest(piece, beam));
                return;

            case AbcKinds.Annotation:
                // Written before the note it belongs to, so it is held until one arrives.
                Quoted(piece.Node.Text);
                return;

            case AbcKinds.Decoration:
                Decorate(piece.Node.Text);
                return;

            case AbcKinds.Grace:
                Graces(piece);
                return;

            case AbcKinds.SlurOpen:
                _pendingSlurs++;
                return;

            case AbcKinds.SlurClose:
                if (bar.Events.Count > 0) bar.Events[^1].SlurClose++;
                return;

            case AbcKinds.Tie:
                // A tie is written after the note it runs from, so it is the one already in hand.
                if (bar.Events.Count > 0) bar.Events[^1].TieStart = true;
                return;

            default:
                if (piece.Children.Count > 0)
                    foreach (var child in piece.Children) Gather(child, bar, beam);
                return;
        }
    }

    private string? _pendingChordSymbol;

    private Event Note(ContentPart note, ContentPart? beam)
    {
        var pitch = ResolveNotes.PitchOf(note.Node) ?? default;
        var (value, dots) = Value(ResolveNotes.WrittenOf(note.Node).Quarters);

        return Taking(new Event
        {
            Part = note,
            Node = note.Node,
            Heads = [pitch.DiatonicIndex],
            Accidentals = [Written(note)],
            BaseValue = value,
            Dots = dots,
            Quarters = ResolveNotes.LengthOf(note.Node).Quarters,
            Beam = beam,
            Lyrics = [.. AlignLyrics.Of(note.Node)],
        });
    }

    private Event Chord(ContentPart chord, ContentPart? beam)
    {
        var members = chord.Children.Where(c => c.Kind == AbcKinds.Note && c.Part(AbcRoles.Letter) is not null)
                                    .ToList();

        var quarters = ResolveNotes.LengthOf(chord.Node).Quarters;
        var (value, dots) = Value(ResolveNotes.WrittenOf(chord.Node).Quarters);

        return Taking(new Event
        {
            Part = chord,
            Node = chord.Node,
            Heads = [.. members.Select(m => (ResolveNotes.PitchOf(m.Node) ?? default).DiatonicIndex).OrderBy(i => i)],
            Accidentals = [.. members.OrderBy(m => (ResolveNotes.PitchOf(m.Node) ?? default).DiatonicIndex)
                                     .Select(Written)],
            BaseValue = value,
            Dots = dots,
            Quarters = quarters,
            Beam = beam,
            Lyrics = [.. AlignLyrics.Of(chord.Node)],
        });
    }

    private Event Rest(ContentPart rest, ContentPart? beam)
    {
        var letter = rest.Part(Roles.Name)?.Node.Text ?? "z";
        var quarters = ResolveNotes.LengthOf(rest.Node).Quarters;
        var (value, dots) = Value(ResolveNotes.WrittenOf(rest.Node).Quarters);

        return Taking(new Event
        {
            Part = rest,
            Node = rest.Node,
            IsRest = true,
            Invisible = letter == "x",
            WholeBar = letter == "Z",
            BaseValue = value,
            Dots = dots,
            Quarters = quarters,
            Beam = beam,
        });
    }

    private Event Taking(Event ev)
    {
        ev.ChordSymbol = _pendingChordSymbol;
        ev.Annotations.AddRange(_pendingAnnotations);
        ev.HeadMarks.AddRange(_pendingHeadMarks);
        ev.StaffMarks.AddRange(_pendingStaffMarks);
        ev.Graces.AddRange(_pendingGraces);
        ev.GraceSlashed = _pendingGraceSlash;
        ev.SlurOpen = _pendingSlurs;

        _pendingChordSymbol = null;
        _pendingAnnotations.Clear();
        _pendingHeadMarks.Clear();
        _pendingStaffMarks.Clear();
        _pendingGraces.Clear();
        _pendingGraceSlash = false;
        _pendingSlurs = 0;

        return ev;
    }

    // Everything written before an event belongs to it, and is held until it arrives. A list rather than
    // a field because a note may wear several: `.~!trill!A` is three marks on one head.
    private readonly List<(string Text, AnnotationPlacement Where)> _pendingAnnotations = [];
    private readonly List<int> _pendingHeadMarks = [];
    private readonly List<int> _pendingStaffMarks = [];
    private readonly List<(int Half, int Value)> _pendingGraces = [];
    private bool _pendingGraceSlash;
    private int _pendingSlurs;

    /// <summary>
    /// A double-quoted run: a bare one names a chord, and one led by a placement character is text put
    /// where that character says.
    /// </summary>
    private void Quoted(string quoted)
    {
        if (quoted.Length < 2) return;
        var text = quoted[1..^1];
        if (text.Length == 0) return;

        var where = text[0] switch
        {
            '^' or '@' => AnnotationPlacement.Above,
            '_' => AnnotationPlacement.Below,
            '<' => AnnotationPlacement.Left,
            '>' => AnnotationPlacement.Right,
            _ => (AnnotationPlacement?)null,
        };

        if (where is null) { _pendingChordSymbol = text; return; }

        _pendingAnnotations.Add((text[1..], where.Value));
    }

    /// <summary>
    /// A decoration, sorted by where it goes. An articulation hugs the head on the side away from the
    /// stem; an ornament, a fermata, a bowing or a navigation sign stacks clear of the staff. Which is a
    /// fact about the mark rather than about the note, so it is decided once, here.
    /// </summary>
    private void Decorate(string written)
    {
        var name = written.Length > 2 && written[0] == '!' && written[^1] == '!'
            ? written[1..^1].Trim().ToLowerInvariant()
            : written;

        switch (name)
        {
            case "." or "staccato": _pendingHeadMarks.Add(Smufl.ArticStaccatoAbove); return;
            case "tenuto": _pendingHeadMarks.Add(Smufl.ArticTenutoAbove); return;
            case "L" or "accent" or "emphasis" or ">": _pendingHeadMarks.Add(Smufl.ArticAccentAbove); return;
            case "marcato" or "^": _pendingHeadMarks.Add(Smufl.ArticMarcatoAbove); return;

            case "H" or "fermata": _pendingStaffMarks.Add(Smufl.FermataAbove); return;
            case "T" or "trill": _pendingStaffMarks.Add(Smufl.OrnamentTrill); return;
            case "~" or "roll" or "turn": _pendingStaffMarks.Add(Smufl.OrnamentTurn); return;
            case "P" or "uppermordent" or "pralltriller": _pendingStaffMarks.Add(Smufl.OrnamentMordent); return;
            case "M" or "lowermordent" or "mordent": _pendingStaffMarks.Add(Smufl.OrnamentLowerMordent); return;
            case "u" or "upbow": _pendingStaffMarks.Add(Smufl.StringsUpBow); return;
            case "v" or "downbow": _pendingStaffMarks.Add(Smufl.StringsDownBow); return;
            case "S" or "segno": _pendingStaffMarks.Add(Smufl.Segno); return;
            case "O" or "coda": _pendingStaffMarks.Add(Smufl.Coda); return;
        }
    }

    /// <summary>
    /// The grace notes crushed in before the next event. Their pitches come off the tree the same way a
    /// real note's does; what makes them grace notes is where they were written, not what they are.
    /// </summary>
    private void Graces(ContentPart group)
    {
        _pendingGraceSlash = group.Part(Roles.Name)?.Node.Text == "/";

        foreach (var member in group.Children)
        {
            if (member.Kind != AbcKinds.Note || member.Part(AbcRoles.Letter) is null) continue;

            var pitch = ResolveNotes.PitchOf(member.Node) ?? default;
            var (value, _) = Value(ResolveNotes.WrittenOf(member.Node).Quarters);
            _pendingGraces.Add((pitch.DiatonicIndex, Math.Max(value, 8)));
        }

        // A lone grace note is conventionally slashed, whether or not the source asked.
        if (_pendingGraces.Count == 1) _pendingGraceSlash = true;
    }

    /// <summary>The accidental written in front of a note, in semitones, or null where none was.</summary>
    private static int? Written(ContentPart note) =>
        note.Part(AbcRoles.Accidental)?.Node.Text is { Length: > 0 } mark
            ? mark[0] switch { '^' => mark.Length, '_' => -mark.Length, _ => 0 }
            : null;

    /// <summary>
    /// A written note value and its dots, from a length in quarter notes. Chosen rather than computed,
    /// because notation has a fixed set of shapes and a length that is not one of them has to be drawn as
    /// the nearest that is.
    /// </summary>
    private static (int Value, int Dots) Value(double quarters)
    {
        if (quarters <= 0) return (4, 0);

        var best = (Value: 4, Dots: 0);
        var bestError = double.MaxValue;

        foreach (var value in (int[])[1, 2, 4, 8, 16, 32, 64])
            for (var dots = 0; dots <= 3; dots++)
            {
                var length = 4.0 / value * (2.0 - Math.Pow(0.5, dots));
                var error = Math.Abs(length - quarters);
                if (error >= bestError) continue;

                bestError = error;
                best = (value, dots);
            }

        // A breve is twice a whole note and has no place in the loop above, which counts downward from one.
        if (Math.Abs(quarters - 8) < Math.Abs(4.0 / best.Value * (2.0 - Math.Pow(0.5, best.Dots)) - quarters))
            return (0, 0);

        return best;
    }

    private static KeySignature? KeyOf(string field) =>
        AbcTheory.Fifths(field) is { } fifths ? KeySignature.FromFifths(fifths) : null;

    /// <summary>
    /// The clef a line asks for, or the one its range suggests. A voice that names none and never climbs
    /// above middle C is a bass part, and printing it in treble buries it in ledger lines.
    /// </summary>
    private static ClefKind ClefOf(ContentPart line)
    {
        foreach (var piece in line.SelfAndDescendants())
        {
            if (piece.Kind != AbcKinds.InlineField && piece.Kind != AbcKinds.Field) continue;
            var value = piece.Part(AbcRoles.Value)?.Node.Text ?? "";
            if (value.Contains("clef=bass", StringComparison.OrdinalIgnoreCase)) return ClefKind.Bass;
            if (value.Contains("clef=alto", StringComparison.OrdinalIgnoreCase)) return ClefKind.Alto;
            if (value.Contains("clef=tenor", StringComparison.OrdinalIgnoreCase)) return ClefKind.Tenor;
        }

        return ClefKind.Treble;
    }
}
