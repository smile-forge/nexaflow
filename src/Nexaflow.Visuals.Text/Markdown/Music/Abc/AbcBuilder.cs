using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// ABC's walk: a tune's lines, in the order written, as rows of bars of events for the shared engraver. The stages have already
/// said what everything means — what each field sets, what is in force on each line, what each note sounds and how long it
/// lasts, which syllable is sung on it, what each mark and each quoted run is — so what is left here is the walk itself: which
/// voice a line belongs to, where a key or meter written mid-tune takes effect, and what is written before an event going with
/// it.
/// </summary>
internal sealed class AbcBuilder : MusicBuilder
{
    internal AbcBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) { }

    /// <inheritdoc/>
    /// <remarks>What cannot be drawn and what is being typed were both settled while it was read, as parts saying so.</remarks>
    protected override Tune ReadTune() => new(Rows(Reading), AbcHeader.Of(Reading), Reading);

    // ── Walking it ──────────────────────────────────────────────────────────

    /// <summary>The tune as rows of bars of events. A walk, not a query — order matters: a mid-tune key change belongs to the bar it takes effect at.</summary>
    private List<Row> Rows(ContentReading reading)
    {
        var rows = new List<Row>();
        var key = (KeySignature?)null;
        var meter = ((int Beats, int Unit, int? Sign)?)null;
        var started = false;

        // Assuming 4/4 by default and *printing* it are different claims — printing one nobody wrote puts a meter on a tune that has none.
        var meterWritten = false;

        // `C`/`C|` draw as a sign, not figures. Kept because computing the meter loses this — `M:C|` and
        // `M:2/2` count the same but shouldn't print the same.
        int? meterSign = null;

        // How many lines each voice has written — two voices' n-th lines sound together, and ABC leaves the reader to count that.
        var lines = new Dictionary<string, int>();
        var names = new Dictionary<string, string>();
        var clefs = new Dictionary<string, ClefKind>();

        // The clef a K: asks for, which a voice that names none of its own is written in.
        ClefKind? keyClef = null;

        // The row a trailing backslash left open, if the line before this one ended with one.
        Row? continuing = null;

        foreach (var line in reading.Root.Children)
        {
            if (line.Kind == AbcKinds.Field)
            {
                if (line.Node is AbcFieldNode field) Field(field);
                continue;
            }

            if (line.Kind != AbcKinds.Line) continue;

            var context = (line.Node as AbcLineNode)?.Context ?? AbcContext.Default;

            // A trailing backslash means the tune carries on — where the source wraps isn't where the
            // engraver should break, else a bar-per-line tune (to line up chord symbols) becomes forty single-bar staves.
            Row row;
            if (continuing is { } open && open.Voice == context.Voice)
            {
                row = open;
            }
            else
            {
                row = new Row
                {
                    Fifths = context.Fifths,
                    Meter = (context.Beats, context.BeatUnit, null),
                    Clef = ClefOf(line) ?? (clefs.TryGetValue(context.Voice, out var voiced) ? voiced : keyClef ?? ClefKind.Treble),
                    Voice = context.Voice,
                    Index = lines.TryGetValue(context.Voice, out var seen) ? seen : 0,
                    Name = names.GetValueOrDefault(context.Voice),
                };

                lines[context.Voice] = row.Index + 1;
            }

            foreach (var piece in line.Children)
            {
                if (piece.Kind != AbcKinds.Measure) continue;

                var bar = new Bar { Part = piece, KeyChange = key, MeterChange = meter };
                key = null;
                meter = null;

                ContentPart? opened = null, closed = null;
                foreach (var inside in piece.Children)
                {
                    if (inside.Role == Roles.Open) { opened = inside; continue; }
                    if (inside.Role == Roles.Close) { closed = inside; continue; }
                    Gather(inside, bar, null);
                }

                bar.Opened = Line(opened);
                bar.Closed = Line(closed);
                bar.EndsRepeat = closed?.Node is AbcBarlineNode { Repeats: true };
                if (bar.Events.Count > 0 || closed is not null) row.Bars.Add(bar);
            }

            // Held back while open, so brackets are worked out over the whole row and it's added exactly once.
            if (Continues(line)) { continuing = row; continue; }

            continuing = null;
            Brackets(row);
            if (row.Bars.Count > 0 && !rows.Contains(row)) { rows.Add(row); started = true; }
        }

        // Printed only where the tune wrote a meter, as the sign it opened with.
        foreach (var row in rows)
            row.Meter = meterWritten && row.Meter is { } counted ? (counted.Beats, counted.Unit, meterSign) : null;

        return rows;

        // A field on a line of its own, as the walk reaches it.
        void Field(AbcFieldNode field)
        {
            if (field.Letter == 'M' && field.Meter is not null)
            {
                meterWritten = true;
                if (!started) meterSign = Glyph(field.Sign);
            }

            // Read unconditionally (not under the `started` guard below) — a part song's V: is in the header, before any music starts.
            if (field.Letter == 'V')
            {
                var id = field.Voice ?? "";
                if (field.VoiceName is { } label) names[id] = label;
                if (field.Clef is { } asked) clefs[id] = asked;
                return;
            }

            if (field.Letter == 'K' && field.Clef is { } keyed) keyClef = keyed;

            // A mid-tune key/meter change prints at the head of the bar it reaches — only once music has
            // started, or the header's own K:/M: would double-print on the first line.
            if (!started) return;

            if (field.Letter == 'K') key = field.Fifths is { } fifths ? KeySignature.FromFifths(fifths) : null;
            if (field.Letter == 'M' && field.Meter is { } wrote) meter = (wrote.Beats, wrote.Unit, Glyph(field.Sign));
        }
    }

    /// <summary>
    /// A bar line as the engraver draws it: exactly as it was written, because ABC spells its bar lines mark
    /// by mark and that spelling is the one the engraver reads.
    /// </summary>
    private static Barline? Line(ContentPart? written) =>
        written is null ? null : new Barline(Spelled(written), written);

    /// <summary>How a bar line is spelled: its line alone, without the ending a volta number after it opens.</summary>
    private static string Spelled(ContentPart written) => written.Part(Roles.Name)?.Text ?? written.Text;

    /// <summary>Whether a music line ends with a backslash — trivia to the parser, but it tells the engraver where a system may not break.</summary>
    private static bool Continues(ContentPart line) =>
        line.SelfAndDescendants().Any(p => p.Kind == AbcKinds.Continuation);

    /// <summary>Which bar each repeat bracket opens at: a number after a bar line (<c>|1</c>) belongs to the bar it opens, not the one it closed.</summary>
    private static void Brackets(Row row)
    {
        for (var at = 0; at < row.Bars.Count; at++)
        {
            if (Volta(row.Bars[at].Opened?.Part as ContentPart) is { } opening)
            {
                row.Bars[at].Volta = opening.Part;
                row.Bars[at].VoltaLabel = opening.Label;
            }

            if (Volta(row.Bars[at].Closed?.Part as ContentPart) is not { } closing || at + 1 >= row.Bars.Count)
                continue;

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
                // The marker says nothing about where the notes stop — only the stage's group knows.
                var notes = (piece.Node as AbcTupletNode)?.Notes ?? 0;
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
                if (piece.Node is MusicAnnotationNode said) Quoted(said, piece);
                return;

            case AbcKinds.Decoration:
                if (piece.Node is MusicMarkNode marked) _pendingMarks.Add(marked.Mark);
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
    private ContentPart? _pendingChordPart;

    private Event Note(ContentPart note, ContentPart? beam)
    {
        var read = note.Node as AbcEventNode;
        var (value, dots) = Value(Written(read).Quarters);

        return Taking(Sings(new Event
        {
            Part = note,
            Heads = [(read?.Pitch ?? default).DiatonicIndex],
            Accidentals = [read?.Printed],
            BaseValue = value,
            Dots = dots,
            Quarters = (read?.Lasts ?? Duration.Zero).Quarters,
            Beam = beam,
        }, read));
    }

    private Event Chord(ContentPart chord, ContentPart? beam)
    {
        var members = chord.Children.Where(c => c.Kind == AbcKinds.Note && c.Part(AbcRoles.Letter) is not null)
                                    .Select(c => c.Node as AbcEventNode)
                                    .OrderBy(m => (m?.Pitch ?? default).DiatonicIndex)
                                    .ToList();

        var read = chord.Node as AbcEventNode;
        var (value, dots) = Value(Written(read).Quarters);

        return Taking(Sings(new Event
        {
            Part = chord,
            Heads = [.. members.Select(m => (m?.Pitch ?? default).DiatonicIndex)],
            Accidentals = [.. members.Select(m => m?.Printed)],
            BaseValue = value,
            Dots = dots,
            Quarters = (read?.Lasts ?? Duration.Zero).Quarters,
            Beam = beam,
        }, read));
    }

    private Event Rest(ContentPart rest, ContentPart? beam)
    {
        var read = rest.Node as AbcEventNode;
        var (value, dots) = Value(Written(read).Quarters);

        return Taking(new Event
        {
            Part = rest,
            IsRest = true,
            Invisible = read?.Rest == AbcRestKind.Unseen,
            WholeBar = read?.Rest == AbcRestKind.Bars,
            BaseValue = value,
            Dots = dots,
            Quarters = (read?.Lasts ?? Duration.Zero).Quarters,
            Beam = beam,
        });
    }

    /// <summary>The value an event is written as — what its head is chosen from.</summary>
    private static Duration Written(AbcEventNode? read) => read?.WrittenAs ?? Duration.Zero;

    /// <summary>The syllables the stages said are sung on an event, each with the characters it was written as.</summary>
    private Event Sings(Event ev, AbcEventNode? read)
    {
        if (read is null) return ev;

        ev.Lyrics = [.. read.Sung.Select(sung => (sung.Verse, sung.Text, sung.Hyphen, sung.Melisma, Syllable(sung)))];
        return ev;
    }

    /// <summary>Where a syllable was written: its piece of the <c>w:</c> line it came from.</summary>
    private ISourcePart? Syllable(AbcSung sung)
    {
        var lines = Reading.Root.Children;
        if (sung.Line < 0 || sung.Line >= lines.Count || lines[sung.Line].Part(AbcRoles.Value) is not { } words) return null;

        return sung.At >= 0 && sung.At < words.Children.Count ? words.Children[sung.At] : null;
    }

    private Event Taking(Event ev)
    {
        ev.ChordSymbol = _pendingChordSymbol;
        ev.ChordPart = _pendingChordPart;
        ev.Annotations.AddRange(_pendingAnnotations);
        foreach (var mark in _pendingMarks) Marking(ev, mark);
        ev.Graces.AddRange(_pendingGraces);
        ev.GraceSlashed = _pendingGraceSlash;
        ev.SlurOpen = _pendingSlurs;

        _pendingChordSymbol = null;
        _pendingChordPart = null;
        _pendingAnnotations.Clear();
        _pendingMarks.Clear();
        _pendingGraces.Clear();
        _pendingGraceSlash = false;
        _pendingSlurs = 0;

        return ev;
    }

    // Everything written before an event belongs to it, and is held until it arrives. A list rather than
    // a field because a note may wear several: `.~!trill!A` is three marks on one head.
    private readonly List<(string Text, AnnotationPlacement Where)> _pendingAnnotations = [];
    private readonly List<MusicMark> _pendingMarks = [];
    private readonly List<(int Half, int Value)> _pendingGraces = [];
    private bool _pendingGraceSlash;
    private int _pendingSlurs;

    /// <summary>A quoted run, as the stages said it: the name of a chord, or text put where its placement says.</summary>
    private void Quoted(MusicAnnotationNode said, ContentPart piece)
    {
        if (said.Placement is not { } where) { _pendingChordSymbol = said.Said; _pendingChordPart = piece; return; }

        _pendingAnnotations.Add((said.Said, where));
    }

    /// <summary>The grace notes crushed in before the next event — grace-ness is about where they were written, not what they are.</summary>
    private void Graces(ContentPart group)
    {
        // ABC spells the two out explicitly: `{g}` appoggiatura, `{/g}` acciaccatura — nothing to infer.
        // A slash straight after the brace is read as the group's name: an acciaccatura rather than an appoggiatura.
        _pendingGraceSlash = group.Part(Roles.Name) is not null;

        foreach (var member in group.Children)
        {
            if (member.Kind != AbcKinds.Note || member.Part(AbcRoles.Letter) is null) continue;

            var read = member.Node as AbcEventNode;
            var (value, _) = Value(Written(read).Quarters);
            _pendingGraces.Add(((read?.Pitch ?? default).DiatonicIndex, Math.Max(value, 8)));
        }
    }

    /// <summary>The glyph a meter written as a sign is printed with, or null for one printed as figures.</summary>
    private static int? Glyph(MeterSign sign) => sign switch
    {
        MeterSign.Common => Smufl.TimeSigCommon,
        MeterSign.Cut => Smufl.TimeSigCutCommon,
        _ => null,
    };

    /// <summary>The clef a line asks for inline, or null — falling back to the voice's <c>V:</c>, then <c>K:</c>, then treble.</summary>
    private static ClefKind? ClefOf(ContentPart line)
    {
        foreach (var piece in line.SelfAndDescendants())
            if (piece.Node is AbcFieldNode { Clef: { } asked }) return asked;

        return null;
    }
}
