using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music.Abc.Stages;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// ABC's reading: turns a tune's lines into rows of bars of events for the shared engraver. The pipeline has
/// already worked out the music itself (pitches, durations, grouping, bar positions); what's left here is
/// translation — voice, key/meter changes, lyric alignment, decoration marks.
/// </summary>
internal sealed class AbcBuilder : MusicBuilder
{
    private readonly (int Start, int Length)? _shownAsWritten;

    /// <param name="shownAsWritten">A stretch to show as typed characters rather than engraved music — the piece being edited.</param>
    /// <param name="spacing">Null uses the engraver's normal spacing; pass another only to compare two engravings without the comparison being about spacing.</param>
    public AbcBuilder(string abc, double width, Brush ink, double pixelsPerDip,
                      (int Start, int Length)? shownAsWritten = null,
                      ScoreSpacing? spacing = null)
        : base(abc, width, ink, pixelsPerDip, spacing) =>
        _shownAsWritten = shownAsWritten;

    /// <summary>Reads and engraves a tune in one call — most callers want nothing else from a laid-out builder.</summary>
    public static Laid Build(string abc, double width, Brush ink, double pixelsPerDip,
                             (int Start, int Length)? shownAsWritten = null, ScoreSpacing? spacing = null) =>
        new AbcBuilder(abc, width, ink, pixelsPerDip, shownAsWritten, spacing).Lay();

    protected override Tune ReadTune()
    {
        // What can't be drawn and what's being typed are both settled before this, returned as parts that say so.
        var reading = ContentReading.Of(AbcPipeline.Read(Source, Draws, _shownAsWritten));
        return new Tune(Rows(reading), AbcHeader.Of(reading), reading);
    }

    /// <summary>Whether the engraver can draw a named decoration — asked of the builder, not a table, since drawability is a fact about the engraver.</summary>
    internal static bool Draws(string decoration) => Decorations.Contains(decoration.ToLowerInvariant());

    private static readonly HashSet<string> Decorations =
    [
        "staccato", "tenuto", "accent", "emphasis", "marcato", "upbow", "downbow", "fermata", "trill",
        "roll", "turn", "uppermordent", "pralltriller", "lowermordent", "mordent", "segno", "coda",
        ">", "^",
    ];

    // ── Reading it ──────────────────────────────────────────────────────────

    /// <summary>
    /// Which piece of its verse each syllable was written as, held until verses are read — a <c>w:</c> line
    /// comes after the music it's sung under, so an event's verse doesn't exist yet when it's built.
    /// See <see cref="Sung"/>.
    /// </summary>
    private readonly Dictionary<Event, int[]> _unsung = new();

    /// <summary>The tune as rows of bars of events. A walk, not a query — order matters: a mid-tune key change belongs to the bar it takes effect at.</summary>
    private List<Row> Rows(ContentReading reading)
    {
        var rows = new List<Row>();
        var key = (KeySignature?)null;
        var meter = ((int Beats, int Unit, int? Sign)?)null;
        var started = false;
        _unsung.Clear();

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

        // The `w:` lines sung under each row, verse 0 first — kept so a syllable can later get the characters it was actually written with.
        var verses = new Dictionary<Row, List<ContentPart>>();

        // The row a trailing backslash left open, if the line before this one ended with one.
        Row? continuing = null;

        // …and the row a `w:` line belongs under, which is simply the last one read.
        Row? sungUnder = null;

        foreach (var line in reading.Root.Children)
        {
            if (line.Kind == AbcKinds.Field)
            {
                var name = line.Part(Roles.Name)?.Text ?? "";
                if (name.StartsWith('M') && AbcTheory.Meter(line.Part(AbcRoles.Value)?.Node.Text ?? "") is not null)
                {
                    meterWritten = true;
                    if (!started) meterSign = Sign(line.Part(AbcRoles.Value)?.Node.Text ?? "");
                }
                var value = line.Part(AbcRoles.Value)?.Node.Text ?? "";

                // Read unconditionally (not under the `started` guard below) — a part song's V: is in the header, before any music starts.
                if (name.StartsWith('V'))
                {
                    var (id, label) = VoiceName(value);
                    if (label is not null) names[id] = label;
                    if (ClefIn(name, value) is { } asked) clefs[id] = asked;
                    continue;
                }

                if (name.StartsWith('K') && ClefIn(name, value) is { } keyed) keyClef = keyed;

                // A mid-tune key/meter change prints at the head of the bar it reaches — only once music has
                // started, or the header's own K:/M: would double-print on the first line.
                if (!started) continue;

                if (name.StartsWith('K')) key = KeyOf(value);
                if (name.StartsWith('M') && AbcTheory.Meter(value) is { } wrote)
                    meter = (wrote.Beats, wrote.Unit, Sign(value));
                continue;
            }

            if (line.Kind == AbcKinds.LyricLine)
            {
                // Sung under whichever row was last read — the same pairing the aligning stage made.
                if (line.Part(AbcRoles.Value) is { } sung && sungUnder is not null)
                {
                    if (!verses.TryGetValue(sungUnder, out var written)) verses[sungUnder] = written = [];
                    written.Add(sung);
                }

                continue;
            }

            if (line.Kind != AbcKinds.Line) continue;

            var context = ResolveContext.Of(line.Node);

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
                bar.EndsRepeat = Printed(closed).Contains(':');
                if (bar.Events.Count > 0 || closed is not null) row.Bars.Add(bar);
            }

            // Held back while open, so brackets are worked out over the whole row and it's added exactly once.
            if (Continues(line)) { continuing = row; continue; }

            continuing = null;
            sungUnder = row;
            Brackets(row);
            if (row.Bars.Count > 0 && !rows.Contains(row)) { rows.Add(row); started = true; }
        }

        Sung(rows, verses);

        // Printed only where the tune wrote a meter, as the sign it opened with.
        foreach (var row in rows)
            row.Meter = meterWritten && row.Meter is { } counted ? (counted.Beats, counted.Unit, meterSign) : null;

        return rows;
    }

    /// <summary>
    /// Hands every syllable the characters it was written with — done after the whole tune is read, since a
    /// <c>w:</c> line comes after the music it's sung under and its verses don't exist yet when built.
    /// </summary>
    private void Sung(List<Row> rows, Dictionary<Row, List<ContentPart>> verses)
    {
        foreach (var row in rows)
        {
            if (!verses.TryGetValue(row, out var written)) continue;

            foreach (var bar in row.Bars)
                foreach (var ev in bar.Events)
                {
                    if (!_unsung.TryGetValue(ev, out var pieces)) continue;

                    for (var i = 0; i < ev.Lyrics.Count && i < pieces.Length; i++)
                    {
                        var sung = ev.Lyrics[i];
                        if (sung.Verse >= written.Count) continue;

                        var children = written[sung.Verse].Children;
                        if (pieces[i] < 0 || pieces[i] >= children.Count) continue;

                        ev.Lyrics[i] = sung with { Part = children[pieces[i]] };
                    }
                }
        }
    }

    /// <summary>
    /// A bar line as the engraver draws it: exactly as it was written, because ABC spells its bar lines mark
    /// by mark and that spelling is the one the engraver reads.
    /// </summary>
    private static Barline? Line(ContentPart? written) =>
        written is null ? null : new Barline(written.Node.Print(), written);

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

    /// <summary>What a part was written as, or nothing where there is no part.</summary>
    private static string Printed(ContentPart? part) => part?.Node.Print() ?? "";

    /// <summary>
    /// A <c>V:</c> field's id and the name it asks to be labelled with — <c>V:1 clef=treble name="Soprano"</c>.
    /// </summary>
    private static (string Id, string? Name) VoiceName(string field)
    {
        var id = field.Trim().Split([' ', '\t'], 2)[0];

        var at = field.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = field.IndexOf("nm=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return (id, null);

        var value = field[(field.IndexOf('=', at) + 1)..].TrimStart();
        if (value.StartsWith('"'))
        {
            var close = value.IndexOf('"', 1);
            return (id, close > 0 ? value[1..close] : null);
        }

        var word = value.Split([' ', '\t'], 2)[0].Trim();
        return (id, word.Length > 0 ? word : null);
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
                // The marker says nothing about where the notes stop — only the pipeline's group knows.
                var (notes, _) = GroupTuplets.Of(piece.Node);
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
                Quoted(piece);
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
    private ContentPart? _pendingChordPart;

    private Event Note(ContentPart note, ContentPart? beam)
    {
        var pitch = ResolveNotes.PitchOf(note.Node) ?? default;
        var (value, dots) = Value(ResolveNotes.WrittenOf(note.Node).Quarters);

        return Taking(Sings(new Event
        {
            Part = note,
            Heads = [pitch.DiatonicIndex],
            Accidentals = [Written(note)],
            BaseValue = value,
            Dots = dots,
            Quarters = ResolveNotes.LengthOf(note.Node).Quarters,
            Beam = beam,
        }, note.Node));
    }

    private Event Chord(ContentPart chord, ContentPart? beam)
    {
        var members = chord.Children.Where(c => c.Kind == AbcKinds.Note && c.Part(AbcRoles.Letter) is not null)
                                    .ToList();

        var quarters = ResolveNotes.LengthOf(chord.Node).Quarters;
        var (value, dots) = Value(ResolveNotes.WrittenOf(chord.Node).Quarters);

        return Taking(Sings(new Event
        {
            Part = chord,
            Heads = [.. members.Select(m => (ResolveNotes.PitchOf(m.Node) ?? default).DiatonicIndex).OrderBy(i => i)],
            Accidentals = [.. members.OrderBy(m => (ResolveNotes.PitchOf(m.Node) ?? default).DiatonicIndex)
                                     .Select(Written)],
            BaseValue = value,
            Dots = dots,
            Quarters = quarters,
            Beam = beam,
        }, chord.Node));
    }

    private Event Rest(ContentPart rest, ContentPart? beam)
    {
        var letter = rest.Part(Roles.Name)?.Node.Text ?? "z";
        var quarters = ResolveNotes.LengthOf(rest.Node).Quarters;
        var (value, dots) = Value(ResolveNotes.WrittenOf(rest.Node).Quarters);

        return Taking(new Event
        {
            Part = rest,
            IsRest = true,
            Invisible = letter == "x",
            WholeBar = letter == "Z",
            BaseValue = value,
            Dots = dots,
            Quarters = quarters,
            Beam = beam,
        });
    }

    /// <summary>The syllables the aligning stage hung on an event; where each was written is held until verses are read.</summary>
    private Event Sings(Event ev, ContentNode node)
    {
        var lyrics = AlignLyrics.Of(node).ToList();
        ev.Lyrics = [.. lyrics.Select(l => (l.Verse, l.Text, l.Hyphen, l.Melisma, (ISourcePart?)null))];
        if (lyrics.Count > 0) _unsung[ev] = [.. lyrics.Select(l => l.At)];
        return ev;
    }

    private Event Taking(Event ev)
    {
        ev.ChordSymbol = _pendingChordSymbol;
        ev.ChordPart = _pendingChordPart;
        ev.Annotations.AddRange(_pendingAnnotations);
        ev.HeadMarks.AddRange(_pendingHeadMarks);
        ev.StaffMarks.AddRange(_pendingStaffMarks);
        ev.Graces.AddRange(_pendingGraces);
        ev.GraceSlashed = _pendingGraceSlash;
        ev.SlurOpen = _pendingSlurs;

        _pendingChordSymbol = null;
        _pendingChordPart = null;
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

    /// <summary>A double-quoted run: bare names a chord; led by a placement character, it's text put where that character says.</summary>
    private void Quoted(ContentPart piece)
    {
        var quoted = piece.Node.Text;
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

        if (where is null) { _pendingChordSymbol = text; _pendingChordPart = piece; return; }

        _pendingAnnotations.Add((text[1..], where.Value));
    }

    /// <summary>A decoration, sorted by where it goes: articulations hug the head opposite the stem; ornaments, fermatas, bowings and navigation signs stack clear of the staff.</summary>
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

    /// <summary>The grace notes crushed in before the next event — grace-ness is about where they were written, not what they are.</summary>
    private void Graces(ContentPart group)
    {
        // ABC spells the two out explicitly: `{g}` appoggiatura, `{/g}` acciaccatura — nothing to infer.
        _pendingGraceSlash = group.Node.Print().StartsWith("{/", StringComparison.Ordinal);

        foreach (var member in group.Children)
        {
            if (member.Kind != AbcKinds.Note || member.Part(AbcRoles.Letter) is null) continue;

            var pitch = ResolveNotes.PitchOf(member.Node) ?? default;
            var (value, _) = Value(ResolveNotes.WrittenOf(member.Node).Quarters);
            _pendingGraces.Add((pitch.DiatonicIndex, Math.Max(value, 8)));
        }
    }

    /// <summary>The sign a meter was written as, where it was written as one rather than as figures.</summary>
    private static int? Sign(string value) => value.Trim() switch
    {
        "C" => Smufl.TimeSigCommon,
        "C|" => Smufl.TimeSigCutCommon,
        _ => null,
    };

    /// <summary>The accidental written in front of a note, in semitones, or null where none was.</summary>
    private static int? Written(ContentPart note) =>
        note.Part(AbcRoles.Accidental)?.Node.Text is { Length: > 0 } mark
            ? mark[0] switch { '^' => mark.Length, '_' => -mark.Length, _ => 0 }
            : null;

    private static KeySignature? KeyOf(string field) =>
        AbcTheory.Fifths(field) is { } fifths ? KeySignature.FromFifths(fifths) : null;

    /// <summary>The clef a line asks for inline, or null — falling back to the voice's <c>V:</c>, then <c>K:</c>, then treble.</summary>
    private static ClefKind? ClefOf(ContentPart line)
    {
        foreach (var piece in line.SelfAndDescendants())
        {
            if (piece.Kind is not (AbcKinds.InlineField or AbcKinds.Field)) continue;
            if (ClefIn(piece.Part(Roles.Name)?.Text ?? "", piece.Part(AbcRoles.Value)?.Node.Text ?? "") is { } asked) return asked;
        }

        return null;
    }

    /// <summary>
    /// The clef a field asks for, or null. <c>clef=</c> is optional on <c>K:</c>/<c>V:</c> (<c>K:F bass</c> =
    /// <c>K:F clef=bass</c>), so a bare name counts there too; elsewhere only an explicit <c>clef=</c> does.
    /// </summary>
    private static ClefKind? ClefIn(string field, string value)
    {
        var voice = field.StartsWith('V');
        var bare = voice || field.StartsWith('K');

        foreach (var word in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(voice ? 1 : 0))
        {
            var written = word.StartsWith("clef=", StringComparison.OrdinalIgnoreCase);
            if (!written && !bare) continue;

            var name = (written ? word[5..] : word).Trim('"').ToLowerInvariant();
            if (name.StartsWith("bass", StringComparison.Ordinal)) return ClefKind.Bass;
            if (name.StartsWith("alto", StringComparison.Ordinal)) return ClefKind.Alto;
            if (name.StartsWith("tenor", StringComparison.Ordinal)) return ClefKind.Tenor;
            if (name.StartsWith("treble", StringComparison.Ordinal)) return ClefKind.Treble;
        }

        return null;
    }
}
