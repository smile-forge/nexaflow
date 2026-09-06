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

        // Placed, once the system is laid out.
        public double SlotWidth;
        public double AccidentalWidth;
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

                if (bar.Events.Count > 0 || bar.Closed is not null) row.Bars.Add(bar);
            }

            if (row.Bars.Count > 0) { rows.Add(row); started = true; }
        }

        return rows;
    }

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
                // A chord symbol is written before the note it belongs to, so it is held until one arrives.
                _pendingChordSymbol = ChordSymbolOf(piece.Node.Text);
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
        _pendingChordSymbol = null;
        return ev;
    }

    /// <summary>The accidental written in front of a note, in semitones, or null where none was.</summary>
    private static int? Written(ContentPart note) =>
        note.Part(AbcRoles.Accidental)?.Node.Text is { Length: > 0 } mark
            ? mark[0] switch { '^' => mark.Length, '_' => -mark.Length, _ => 0 }
            : null;

    /// <summary>What a chord symbol says, or null where the quotes held a placed annotation instead.</summary>
    private static string? ChordSymbolOf(string quoted)
    {
        if (quoted.Length < 2) return null;
        var text = quoted[1..^1];
        return text.Length == 0 || text[0] is '^' or '_' or '<' or '>' or '@' ? null : text;
    }

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
