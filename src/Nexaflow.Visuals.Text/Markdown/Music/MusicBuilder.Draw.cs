using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;

using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music;

/// <summary>
/// The half of the engraver that puts pieces into the tree: what each thing is drawn as, what holds it,
/// and what belongs with what.
///
/// <para>
/// Placement is already decided by <see cref="Justify"/> (<c>ev.X</c>) and <see cref="Stack"/>
/// (<c>StaffTop</c>) — this only records. It works in page coordinates, because that's what an engraver
/// thinks in; the tree stores relative coordinates, and <see cref="LayoutBuilder.Anchor"/> is the one
/// subtraction between the two.
/// </para>
/// <para>
/// <strong>A piece is finished when it is closed</strong> — so a beam is worked out before the notes
/// under it are drawn, and curves are paired before anything is drawn, rather than laid over finished
/// output afterwards.
/// </para>
/// </summary>
internal abstract partial class MusicBuilder
{
    private readonly LayoutBuilder _build = new();

    /// <summary>
    /// What was drawn where, kept while a tune is engraved so the orderings can be declared once it is.
    /// Gathered rather than linked as it goes: an ordering is a run that's only complete once the last
    /// system is, and a lyric's run doesn't stop at a system edge. See <see cref="Order"/>.
    /// </summary>
    private readonly List<(Event Event, int At)> _heads = [];
    private readonly List<(Event Event, int At)> _chords = [];
    private readonly List<(Event Event, int Verse, int At)> _sung = [];
    private readonly List<int> _sections = [];

    /// <summary>
    /// Everything on the staff a drag passes along, in engraved order: notes, rests, and the bar lines
    /// between them. A bar line is on this run because it's on the staff — left off, a drag reaching one
    /// fell back to everything written between it and took the chord over the next bar.
    /// </summary>
    private readonly List<int> _staff = [];

    // ── Page coordinates in, frames out ─────────────────────────────────────

    /// <summary>Opens a piece at a page point; null <paramref name="at"/> starts it where its holder begins.</summary>
    private int Open(string kind, ISourcePart? part = null, Point? at = null) =>
        _build.Open(kind, part, at is { } page ? In(page) : default);

    private void Close() => _build.Close();

    /// <summary>A point on the page, in the frame of the piece being built.</summary>
    private Point In(Point at)
    {
        var anchor = _build.Anchor;
        return new Point(at.X - anchor.X, at.Y - anchor.Y);
    }

    private Rect In(Rect what)
    {
        var anchor = _build.Anchor;
        return new Rect(what.X - anchor.X, what.Y - anchor.Y, what.Width, what.Height);
    }

    // ── The whole of it ─────────────────────────────────────────────────────

    private (LayoutTree Tree, Size Size) Engrave(Tune tune, double width)
    {
        _noteHead = Smufl.Advance(Smufl.NoteheadBlack, S);
        if (_noteHead <= 0) _noteHead = 1.18 * S;

        var rows = tune.Rows;
        var systems = Wrap(rows, Math.Max(width, 12 * S));

        if (systems.Count > 0) Justify(systems, width);

        // Set to the width the music actually took, not the page offered — else a tune shorter than the
        // window engraves narrower than it and a title centred on the window sits off to one side of the
        // music it names. Taken from where systems were justified to (known before anything is drawn,
        // which it must be — a piece's anchor is fixed when it opens) rather than the ink they made.
        var header = tune.Header;
        var paper = systems.Count == 0 ? width : systems.Max(s => s.Right) + RightMargin;

        _build.Open("score");

        var top = Heading(header, paper);

        // Music is stacked below the heading rather than laid out then moved down; `Stack` takes the offset directly.
        if (systems.Count > 0) Stack(systems, top);

        _build.Open("music");

        if (systems.Count > 0)
        {
            Pair(systems);
            foreach (var system in systems) Draw(system);

            // A bracket joins already-placed staves, so it can't be drawn until every staff has landed.
            Brackets(systems);
        }

        _build.Close();

        Verses(header, paper, _build.Reached.IsEmpty ? 0 : _build.Reached.Bottom);

        _build.Close();

        Order();
        var tree = _build.Seal();

        var extent = tree.Root.Box;
        if (extent.IsEmpty || (extent.Width <= 0 && extent.Height <= 0)) return (tree, new Size(0, 0));

        return (tree, new Size(Math.Ceiling(extent.Right + RightMargin), Math.Ceiling(extent.Bottom + S)));
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    private void Draw(System system)
    {
        var geometry = StaffGeometry.For(system.Row.Clef);

        Open("system", at: new Point(0, system.StaffTop));

        Staff(system);
        Head(system, geometry);

        foreach (var bars in Sections(system.Bars))
        {
            _sections.Add(Open("section", Spanning(bars), new Point(bars[0].X, system.StaffTop)));
            _build.Reserves(0, StaffHeight);
            foreach (var bar in bars) Draw(system, geometry, bar);
            Close();
        }

        Voltas(system);

        // Curves reaching further than one bar are drawn on the line, the smallest thing holding both their ends.
        Hanging(system);

        Close();
    }

    /// <summary>The five lines. Nobody wrote them, so they carry no part and cannot be selected.</summary>
    private void Staff(System system)
    {
        for (var line = 0; line < 5; line++)
        {
            var y = system.StaffTop + (line * S);
            Ruled("staff-line", LeftMargin, y - (StaffLineThick / 2), system.Right - LeftMargin, StaffLineThick);
        }
    }

    /// <summary>The clef, the key signature and the meter, at the head of the system.</summary>
    private void Head(System system, StaffGeometry geometry)
    {
        var x = LeftMargin + (0.4 * S);

        if (system.ShowName && system.Row.Name is { Length: > 0 } name)
        {
            var glyphs = ScoreText.Build(name, CreditSize);
            var at = new Point(x, system.StaffTop + (StaffHeight / 2) - (glyphs.Height / 2));

            Open("voice", at: at);
            _build.Draw(new TextMark(glyphs, default, null));
            Close();

            x += glyphs.Width + (0.6 * S);
        }

        var clefY = Y(system, geometry.ClefRefHalfSpaces);
        Glyph("clef", geometry.ClefGlyph, new Point(x, clefY));
        x += Smufl.Advance(geometry.ClefGlyph, S) + (0.6 * S);

        x = DrawKeySignature(system, geometry, x, system.Row.Fifths);
        if (system.ShowMeter && system.Row.Meter is { } meter) Meter(system, x, meter.Beats, meter.Unit, meter.Sign);
    }

    private double DrawKeySignature(System system, StaffGeometry geometry, double x, int fifths)
    {
        var count = Math.Min(Math.Abs(fifths), 7);
        if (count == 0) return x;

        var sharp = fifths > 0;
        var glyph = sharp ? Smufl.AccidentalSharp : Smufl.AccidentalFlat;

        for (var i = 0; i < count; i++)
        {
            var half = geometry.HalfSpacesAbove(geometry.KeyAccidentalIndex(i, sharp));
            Glyph("key", glyph, new Point(x, Y(system, half)));
            x += Smufl.Advance(glyph, S) + (0.08 * S);
        }

        return x + (0.4 * S);
    }

    private void Meter(System system, double x, int beats, int unit, int? sign)
    {
        // `M:C` / `M:C|` ask for the symbol, not the figures they happen to count as.
        if (sign is { } symbol)
        {
            Glyph("meter", symbol, new Point(x, Y(system, 4)));
            return;
        }

        // Figures, one above the other, centred on the third and first spaces.
        var top = Digits(beats);
        var bottom = Digits(unit);
        var width = Math.Max(Width(top), Width(bottom));

        Row(top, Y(system, 6));
        Row(bottom, Y(system, 2));

        void Row(string digits, double y)
        {
            var at = x + ((width - Width(digits)) / 2);
            foreach (var digit in digits)
            {
                var glyph = Smufl.TimeSig0 + (digit - '0');
                Glyph("meter", glyph, new Point(at, y));
                at += Smufl.Advance(glyph, S);
            }
        }

        double Width(string digits) => digits.Sum(d => Smufl.Advance(Smufl.TimeSig0 + (d - '0'), S));
        static string Digits(int value) => value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── A bar ───────────────────────────────────────────────────────────────

    /// <summary>The bar's children as drawn: a beamed run counts as one, anything else stands alone.</summary>
    private static List<(ISourcePart? Beam, List<Event> Events)> Runs(Bar bar)
    {
        var runs = new List<(ISourcePart? Beam, List<Event> Events)>();

        foreach (var ev in bar.Events)
        {
            if (runs.Count > 0 && ev.Beam is not null && ReferenceEquals(ev.Beam, runs[^1].Beam))
                runs[^1].Events.Add(ev);
            else
                runs.Add((ev.Beam, [ev]));
        }

        return runs;
    }

    private void Draw(System system, StaffGeometry geometry, Bar bar)
    {
        Open("measure", bar.Part, new Point(bar.X, system.StaffTop));
        _build.Reserves(0, StaffHeight);

        var x = bar.X;
        if (bar.Opened is not null) x = DrawBarline(system, bar.Opened, x);

        // A mid-tune key or meter change, printed where it takes effect.
        if (bar.KeyChange is { } key) x = DrawKeySignature(system, geometry, x, key.Fifths);
        if (bar.MeterChange is { } meter) Meter(system, x, meter.Beats, meter.Unit, meter.Sign);

        var runs = Runs(bar);
        var sets = Gathering(bar, beam: null);

        for (var slot = 0; slot < runs.Count; slot++)
        {
            foreach (var curve in Opening(sets, slot)) Open(curve.Kind + "-set");

            var (beam, events) = runs[slot];

            // Beamed runs are pieces of their own — the beam is drawn on the group, notes hang under it.
            if (beam is not null)
            {
                Open("beam", Notes(events) ?? beam, new Point(events[0].X, system.StaffTop));
                _build.Reserves(0, StaffHeight);
                Beamed(system, geometry, bar, beam, events);
                Close();
            }
            else
            {
                foreach (var ev in events) Note(system, geometry, ev, flags: true);
            }

            foreach (var curve in Closing(sets, slot)) { Arced(curve); Close(); }
        }

        // Chord symbols and lyrics hang off the bar, beside the note rather than inside it — containment
        // says where ink went and how much room it takes, and nesting them under the note would make
        // pointer hit-testing descend straight past them to the note (unclickable) and would drag a beam
        // group's reported height down through the lyric row. `Order` groups the three logically without
        // drawing a container. Run as a separate pass because a note may sit inside a beam group when
        // drawn, and a piece is finished (unwritable) once closed.
        foreach (var ev in bar.Events)
        {
            if (!ev.Invisible)
            {
                if (!ev.IsRest) ChordSymbol(system, ev);
                Lyrics(system, ev, system.LastDrawn);
            }

            system.LastDrawn = ev;
        }

        Tuplets(system, geometry, bar);
        DrawBarline(system, bar);

        if (_build.Reached.IsEmpty)
            _build.Covers(In(new Rect(bar.X, system.StaffTop, bar.Width, StaffHeight)));

        Close();
    }

    /// <summary>
    /// A beamed run: the beam is worked out first, then notes are drawn with stems already reaching it — a
    /// note is finished once closed, so a beam laid over finished notes couldn't write a stem back into
    /// each. A curve joining two notes of this group is gathered here rather than at the bar, since they
    /// share this parent and nothing else can span them without lifting them out of it.
    /// </summary>
    private void Beamed(System system, StaffGeometry geometry, Bar bar, ISourcePart beam, List<Event> events)
    {
        var beamed = events.Where(e => e.Beamable).ToList();
        var plan = Plan(system, geometry, beamed);
        var sets = Gathering(bar, beam);

        for (var at = 0; at < events.Count; at++)
        {
            foreach (var curve in Opening(sets, at)) Open(curve.Kind + "-set");

            var ev = events[at];
            var on = plan is null ? -1 : beamed.IndexOf(ev);

            if (plan is null || on < 0)
            {
                Note(system, geometry, ev, flags: true);
            }
            else
            {
                Note(system, geometry, ev, flags: false,
                     toY: plan.Reach + (plan.Slope * (plan.Xs[on] - plan.Xs[0])) + (plan.Down ? BeamThick : 0),
                     stemsDown: plan.Down);
            }

            foreach (var curve in Closing(sets, at)) { Arced(curve); Close(); }
        }

        if (plan is not null) BeamBars(plan, beamed);
    }

    /// <summary>Where a beam over a group lands: which way it points, how it leans, and how many bars.</summary>
    private sealed record BeamPlan(bool Down, double Reach, double Slope, IReadOnlyList<double> Xs, int Levels);

    /// <summary>
    /// The beam over a written group: one direction for the whole group (the note furthest from the middle
    /// line decides), and a slope that only leans where the contour genuinely leans — a run that climbs,
    /// falls and climbs again beams flat rather than asserting a direction via a first-to-last slope.
    /// </summary>
    private BeamPlan? Plan(System system, StaffGeometry geometry, List<Event> events)
    {
        if (events.Count < 2) return null;

        var halves = events.SelectMany(e => e.Heads.Select(geometry.HalfSpacesAbove)).ToList();
        if (halves.Count == 0) return null;

        var down = Engraving.StemDown(halves);

        var xs = new List<double>();
        var outer = new List<double>();

        foreach (var ev in events)
        {
            var own = ev.Heads.Select(geometry.HalfSpacesAbove).ToList();
            if (own.Count == 0) return null;

            xs.Add(down ? ev.X + (StemThick / 2) : ev.X + _noteHead - (StemThick / 2));
            outer.Add(Y(system, down ? own.Min() : own.Max()));
        }

        var slope = Engraving.BeamSlope(xs, outer);

        // Placed against the note that needs it most, since the beam must clear every stem in the group.
        var reach = down ? double.MinValue : double.MaxValue;
        for (var i = 0; i < events.Count; i++)
        {
            var wanted = outer[i] + (down ? StemLen : -StemLen) - (slope * (xs[i] - xs[0]));
            reach = down ? Math.Max(reach, wanted) : Math.Min(reach, wanted);
        }

        return new BeamPlan(down, reach, slope, xs, events.Max(e => Flags(e.BaseValue)));
    }

    private void BeamBars(BeamPlan plan, List<Event> events)
    {
        for (var level = 0; level < plan.Levels; level++)
        {
            var offset = level * (BeamThick + BeamGap) * (plan.Down ? -1 : 1);

            foreach (var (from, to) in SpanOf(events, level))
            {
                var x0 = plan.Xs[from];
                var x1 = from == to ? x0 + BeamStub * (from == 0 ? 1 : -1) : plan.Xs[to];
                var y0 = plan.Reach + (plan.Slope * (x0 - plan.Xs[0])) + offset;
                var y1 = plan.Reach + (plan.Slope * (x1 - plan.Xs[0])) + offset;

                Open("beam-bar", at: new Point(Math.Min(x0, x1), Math.Min(y0, y1)));

                var one = In(new Point(x0, y0));
                var other = In(new Point(x1, y1));

                var bar = new PathGeometry();
                var figure = new PathFigure { StartPoint = one, IsClosed = true, IsFilled = true };
                figure.Segments.Add(new LineSegment(other, false));
                figure.Segments.Add(new LineSegment(new Point(other.X, other.Y + BeamThick), false));
                figure.Segments.Add(new LineSegment(new Point(one.X, one.Y + BeamThick), false));
                bar.Figures.Add(figure);
                bar.Freeze();

                _build.Draw(GeometryMark.Filled(bar));
                Close();
            }
        }
    }

    /// <summary>Which notes each beam level spans: the primary spans the whole group; a secondary runs only where both neighbours are short enough, and a lone short note gets a stub.</summary>
    private static List<(int From, int To)> SpanOf(List<Event> events, int level)
    {
        if (level == 0) return [(0, events.Count - 1)];

        var spans = new List<(int, int)>();
        var start = -1;

        for (var i = 0; i < events.Count; i++)
        {
            var wants = Flags(events[i].BaseValue) > level;

            if (wants && start < 0) start = i;
            if (wants) continue;

            if (start >= 0) { spans.Add((start, i - 1)); start = -1; }
        }

        if (start >= 0) spans.Add((start, events.Count - 1));
        return spans;
    }

    // ── An event ────────────────────────────────────────────────────────────

    /// <summary>
    /// One event as a note (or rest), with head, accidental, ledger lines, dots and stem each drawn as a
    /// child piece rather than a mark — making the note a parent: a pointer over any child climbs to the
    /// source part the note names, so a press on a stem or ledger line means the note without special-casing.
    /// </summary>
    private void Note(System system, StaffGeometry geometry, Event ev, bool flags,
                      double? toY = null, bool? stemsDown = null)
    {
        var at = Open(ev.IsRest ? "rest" : "note", ev.Part, new Point(ev.X, system.StaffTop));
        _build.Reserves(0, StaffHeight);   // a caret against a note is the height of the staff it stands on

        if (ev.Invisible)
        {
            _build.Covers(In(new Rect(ev.X, system.StaffTop, Math.Max(ev.SlotWidth, 1), StaffHeight)));
            Close();
            return;
        }

        if (ev.IsRest)
        {
            var half = ev.BaseValue == 1 ? 6 : 4;
            var glyph = RestGlyph(ev.WholeBar ? 1 : ev.BaseValue);
            var x = ev.WholeBar ? ev.X + ((ev.SlotWidth - Smufl.Advance(glyph, S)) / 2) : ev.X;

            Glyph("rest-glyph", glyph, new Point(x, Y(system, half)));
            Dots(system, x + Smufl.Advance(glyph, S), 4, ev.Dots);
            Close();

            _staff.Add(at);   // on the staff as much as a note is; in no stack, since nothing is sung on a rest
            return;
        }

        var head = HeadGlyph(ev.BaseValue);

        // The accidental goes in the room measured for it, immediately left of the head.
        for (var i = 0; i < ev.Heads.Length; i++)
        {
            if (ev.Accidentals.Length <= i || ev.Accidentals[i] is not { } alter) continue;
            Glyph("accidental", Glyph(alter),
                  new Point(ev.X - ev.AccidentalWidth, Y(system, geometry.HalfSpacesAbove(ev.Heads[i]))));
        }

        foreach (var pitch in ev.Heads)
        {
            var half = geometry.HalfSpacesAbove(pitch);
            Glyph("head", head, new Point(ev.X, Y(system, half)));
            Ledgers(system, ev.X, half);
        }

        if (ev.Graces.Count > 0) Graces(system, geometry, ev);

        Stem(system, geometry, ev, flags, toY, stemsDown);

        Dots(system, ev.X + _noteHead, geometry.HalfSpacesAbove(ev.Heads[^1]), ev.Dots);
        Marks(system, geometry, ev);
        Annotations(system, ev);

        Close();

        _staff.Add(at);
        _heads.Add((ev, at));
    }

    /// <summary>
    /// Grace notes: cue-size heads crushed in before the main one, beamed where there are several and
    /// slashed for an acciaccatura. They hang off the main note's layout piece (not beside it), so
    /// selecting the note takes them with it.
    /// </summary>
    private void Graces(System system, StaffGeometry geometry, Event ev)
    {
        if (ev.Graces.Count == 0) return;

        Open("graces");

        var width = _noteHead * GraceScale;
        var step = width + GraceStep;
        var x = ev.X - ev.AccidentalWidth - ev.GraceWidth + GraceGap;

        // Settled before anything is drawn: every stem in the group must reach the same line, so the
        // highest note decides for all of them.
        var heads = ev.Graces
            .Select(g => geometry.HalfSpacesAbove(g.Half))
            .Select(half => (Half: half, Y: Y(system, half)))
            .ToList();

        var beamed = heads.Count > 1;
        var top = heads.Min(h => h.Y) - (StemLen * GraceScale);

        double? firstStem = null;
        double? lastStem = null;
        var lastHead = x;

        foreach (var (half, y) in heads)
        {
            Mark(Smufl.NoteheadBlack, new Point(x, y), GraceScale);
            for (var line = 10; line <= half; line += 2) Ledger(system, x, line, width);
            for (var line = -2; line >= half; line -= 2) Ledger(system, x, line, width);

            // A grace note always stems up, whatever it sits on — it's read as an ornament, not music of its own.
            var stemTop = beamed ? top : y - (StemLen * GraceScale);
            var stemX = x + width - (StemThick / 2);
            Rule(stemX - (StemThick / 2), stemTop, StemThick, y - stemTop);

            firstStem ??= stemX;
            lastStem = stemX;
            lastHead = x;

            x += step;
        }

        if (beamed && firstStem is { } from && lastStem is { } to)
            Rule(from, top, to - from, BeamThick * GraceScale);

        // Slur to the note it ornaments, from the middle of the group (not the last note) since it joins
        // the ornament as a whole to the main note.
        var middle = (firstStem ?? lastHead) + (((lastHead + width) - (firstStem ?? lastHead)) / 2);
        GraceSlur(system, geometry, ev, middle, heads.Max(h => h.Y));

        if (ev.GraceSlashed && firstStem is { } slashAt)
        {
            // The acciaccatura slash, through the first grace note's stem.
            var one = In(new Point(slashAt - (0.5 * S), top + (0.9 * S)));
            var other = In(new Point(slashAt + (0.5 * S), top - (0.2 * S)));

            var slash = new LineGeometry(one, other);
            slash.Freeze();
            _build.Draw(new GeometryMark(slash, null, null, StemThick * 1.6));
        }

        Close();
    }

    /// <summary>
    /// The marks on an event: head marks (staccato, tenuto — hug the head, opposite the stem, meaning
    /// "this note") and staff marks (fermata — stack clear of the staff, meaning "this moment").
    /// </summary>
    private void Marks(System system, StaffGeometry geometry, Event ev)
    {
        if (ev.HeadMarks.Count == 0 && ev.StaffMarks.Count == 0) return;

        var halves = ev.Heads.Length > 0
            ? ev.Heads.Select(geometry.HalfSpacesAbove).ToList()
            : [Engraving.MiddleLine];

        var down = Engraving.StemDown(halves);
        var at = down ? halves.Max() + 2 : halves.Min() - 2;

        foreach (var glyph in ev.HeadMarks)
        {
            Glyph("articulation", glyph,
                  new Point(ev.X + ((_noteHead - Smufl.Advance(glyph, S, MarkScale)) / 2), Y(system, at)),
                  MarkScale);

            at += down ? 2 : -2;
        }

        // Above the staff and above whatever's already been reached, so a run of high notes pushes its
        // fermatas up rather than colliding. `Above` is room reserved for the whole system's highest note,
        // which over-lifts marks on notes nowhere near it — taking the higher of the two makes a mark
        // clear the note it actually belongs to.
        var over = ev.Heads.Length == 0
            ? system.StaffTop
            : Y(system, geometry.HalfSpacesAbove(ev.Heads.Max())) - MarkRow;

        var y = Math.Min(
            system.StaffTop - system.Above + (system.HasVoltaRow ? VoltaRow : 0)
                + (system.HasChordRow ? TextRow : 0) + MarkRow,
            over);

        foreach (var glyph in ev.StaffMarks)
        {
            Glyph("articulation", glyph,
                  new Point(ev.X + ((_noteHead - Smufl.Advance(glyph, S, MarkScale)) / 2), y),
                  MarkScale);

            y += MarkRow;
        }
    }

    /// <summary>Text put where the quotes said to put it, rather than over the note like a chord name.</summary>
    private void Annotations(System system, Event ev)
    {
        var below = 0;

        foreach (var (text, where) in ev.Annotations)
        {
            if (text.Length == 0) continue;

            var glyphs = ScoreText.Build(text, ChordSize);

            var at = where switch
            {
                AnnotationPlacement.Above => new Point(ev.X, system.StaffTop - system.Above + (system.HasVoltaRow ? VoltaRow : 0)),
                AnnotationPlacement.Below => new Point(
                    ev.X,
                    system.StaffTop + StaffHeight + system.Below - (system.TextBelow * TextRow) + (below++ * TextRow)),
                AnnotationPlacement.Left => new Point(ev.X - glyphs.Width - (0.3 * S), system.StaffTop + S),
                _ => new Point(ev.X + _noteHead + (0.3 * S), system.StaffTop + S),
            };

            Open("annotation", at: at);
            _build.Draw(new TextMark(glyphs, default, null));
            Close();
        }
    }

    /// <summary>
    /// The stem, and a flag when the note is not beamed. Stems point away from the middle line; a note on
    /// it stems up (ABC convention).
    /// </summary>
    /// <param name="stemsDown">
    /// The whole beamed group's direction, where this note is in one — <strong>a beamed note never picks its
    /// own</strong>. Letting it do so attaches the stem to the wrong side of the head and draws as a stray
    /// line off the beam.
    /// </param>
    private void Stem(System system, StaffGeometry geometry, Event ev, bool flags,
                      double? toY = null, bool? stemsDown = null)
    {
        if (ev.IsRest || ev.Heads.Length == 0 || ev.BaseValue <= 1) return;

        var halves = ev.Heads.Select(geometry.HalfSpacesAbove).ToList();
        var down = stemsDown ?? Engraving.StemDown(halves);

        var fromY = Y(system, down ? halves.Max() : halves.Min());
        var endY = toY ?? (down ? Y(system, halves.Min()) + StemLen : Y(system, halves.Max()) - StemLen);
        var x = down ? ev.X + (StemThick / 2) : ev.X + _noteHead - (StemThick / 2);

        var bounds = new Rect(x - (StemThick / 2), Math.Min(fromY, endY), StemThick, Math.Abs(endY - fromY));

        Open("stem", at: bounds.TopLeft);

        var stem = new RectangleGeometry(In(bounds));
        stem.Freeze();
        _build.Draw(GeometryMark.Filled(stem));

        Close();

        if (!flags || ev.BaseValue < 8) return;

        var flag = FlagGlyph(ev.BaseValue, down);
        if (flag != 0) Glyph("flag", flag, new Point(x, endY));
    }

    // ── The small pieces ────────────────────────────────────────────────────

    private void Ledgers(System system, double x, int half)
    {
        for (var line = 10; line <= half; line += 2) Ledger(system, x, line);
        for (var line = -2; line >= half; line -= 2) Ledger(system, x, line);
    }

    private void Ledger(System system, double x, int half, double? head = null)
    {
        var width = head ?? _noteHead;
        Ruled("ledger", x - LedgerExt, Y(system, half) - (LedgerThick / 2),
              width + (2 * LedgerExt), LedgerThick);
    }

    /// <summary>A filled rectangle on the piece being built — a staff line, a stem, a beam, a ledger.</summary>
    private void Rule(double x, double y, double width, double height) =>
        _build.Draw(new RuleMark(In(new Rect(x, y, Math.Max(width, 0), Math.Max(height, 0))), null));

    /// <summary>…and the same on a piece of its own.</summary>
    private void Ruled(string kind, double x, double y, double width, double height)
    {
        Open(kind, at: new Point(x, y));
        Rule(x, y, width, height);
        Close();
    }

    private void Dots(System system, double x, int half, int dots)
    {
        // A dot never sits on a line — a note on one takes its dots in the space above.
        var at = half % 2 == 0 ? half + 1 : half;
        var cursor = x + DotGap;

        for (var i = 0; i < dots; i++)
        {
            Glyph("dot", Smufl.AugmentationDot, new Point(cursor, Y(system, at)));
            cursor += Smufl.Advance(Smufl.AugmentationDot, S) + DotSpacing;
        }
    }

    private void ChordSymbol(System system, Event ev)
    {
        if (ev.ChordSymbol is not { Length: > 0 } text) return;

        var glyphs = ScoreText.Chord(text, ChordSize);
        var at = new Point(ev.X, system.StaffTop - system.Above);

        // Its own piece rather than a mark on the note, since a chord symbol is something a reader picks out on its own.
        var chord = Open("chord", ev.ChordPart, at);
        var (top, height) = Letters(ScoreText.Chord("Hg", ChordSize));
        _build.Reserves(top, height);
        _build.Draw(new TextMark(glyphs, default, null));
        Close();

        _chords.Add((ev, chord));
    }

    /// <summary>
    /// The words under one note, and the line under a word held across it. A held syllable (ABC's <c>_</c>,
    /// or a tie) isn't re-sung, so it's drawn as a rule from the word's end to the holding note — a note at
    /// a time, so a run of held notes comes out as one unbroken line without knowing the run's length.
    /// </summary>
    private void Lyrics(System system, Event ev, Event? before)
    {
        foreach (var (verse, text, hyphen, melisma, part) in ev.Lyrics)
        {
            var y = system.LyricTop + (verse * LyricRow);

            if (melisma)
            {
                Held(ev, before, verse, y);
                continue;
            }

            if (text.Length == 0) continue;

            var glyphs = ScoreText.Build(hyphen ? text + "-" : text, LyricSize);
            var at = new Point(ev.X + (_noteHead / 2) - (glyphs.Width / 2), y);

            // Layout position (under the note) and source part (written a line away) deliberately diverge —
            // that's what lets a syllable be picked out of a verse without the note coming with it.
            var sung = Open("syllable", part, at);
            var (top, height) = Letters(ScoreText.Build("Hg", LyricSize));
            _build.Reserves(top, height);
            _build.Draw(new TextMark(glyphs, default, null));
            Close();

            _sung.Add((ev, verse, sung));
        }
    }

    /// <summary>The rule under a note whose word was sung on an earlier one.</summary>
    private void Held(Event ev, Event? before, int verse, double y)
    {
        var to = ev.X + (_noteHead / 2);

        // Starts where the held word actually ended, clear of its letters. A run carried over a system
        // break has nothing before it on this line, so it starts at the head instead.
        var from = before is null ? ev.X : (before.X + (_noteHead / 2));
        if (before?.Lyrics.FirstOrDefault(l => l.Verse == verse) is { Text.Length: > 0 } sung)
            from += (ScoreText.Width(sung.Text, LyricSize) / 2) + (0.3 * S);

        if (to - from < 0.2 * S) return;

        var at = y + (LyricSize * 0.78);
        var rule = new Rect(from, at - (MelismaThick / 2), to - from, MelismaThick);

        Open("melisma", at: rule.TopLeft);
        _build.Draw(new GeometryMark(new RectangleGeometry(In(rule)), _ink, null, 0));
        Close();
    }

    /// <summary>
    /// The slur from a grace group to the note it ornaments — without it nothing on the page says the two
    /// belong together. Hangs below, since a grace always stems up and a curve above would cross the stems.
    /// </summary>
    private void GraceSlur(System system, StaffGeometry geometry, Event ev, double from, double fromY)
    {
        if (ev.Heads.Length == 0) return;

        var to = ev.X + (_noteHead / 2);
        if (to - from < 0.3 * S) return;

        // The lowest head at each end, since the curve hangs under both.
        var toY = Y(system, geometry.HalfSpacesAbove(ev.Heads.Min())) + CurveClear;
        Arc(new Point(from, fromY + CurveClear), new Point(to, toY), above: false, "grace-slur");
    }

    /// <summary>
    /// The number over a tuplet, centred on the notes it covers — without it a triplet reads as three
    /// eighths that don't add up. Placed on the stem side, clear of the beam.
    /// </summary>
    private void Tuplets(System system, StaffGeometry geometry, Bar bar)
    {
        var at = 0;

        while (at < bar.Events.Count)
        {
            var tuplet = bar.Events[at].Tuplet;
            if (tuplet is null) { at++; continue; }

            var to = at;
            while (to + 1 < bar.Events.Count && ReferenceEquals(bar.Events[to + 1].Tuplet, tuplet)) to++;

            var events = bar.Events.GetRange(at, to - at + 1);
            var halves = events.SelectMany(e => e.Heads.Select(geometry.HalfSpacesAbove)).ToList();

            if (halves.Count > 0 && bar.Events[at].TupletNumber > 1)
            {
                var down = Engraving.StemDown(halves);
                var glyphs = ScoreText.Build(
                    bar.Events[at].TupletNumber.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
                    VoltaSize, style: FontStyles.Italic);

                var left = events[0].X;
                var right = events[^1].X + _noteHead;
                var y = down
                    ? Y(system, halves.Min()) + StemLen
                    : Y(system, halves.Max()) - StemLen - glyphs.Height;

                var where = new Point(((left + right) / 2) - (glyphs.Width / 2), y);

                Open("tuplet", at: where);
                _build.Draw(new TextMark(glyphs, default, null));
                Close();
            }

            at = to + 1;
        }
    }

    /// <summary>The line closing a bar. Carries a part since somebody wrote it — a selection covering every note in a bar must include it before growing into the bar.</summary>
    private void DrawBarline(System system, Bar bar)
    {
        if (bar.Closed is null) return;
        DrawBarline(system, bar.Closed, bar.X + bar.Width - BarlineWidth(bar.Closed) + (0.25 * S));
    }

    /// <summary>Draws one bar line at <paramref name="from"/>, and says where it ended.</summary>
    private double DrawBarline(System system, Barline line, double from)
    {
        var written = line.Drawn;
        var x = from;
        var top = system.StaffTop;
        var height = StaffHeight;

        var piece = Open("barline", line.Part, new Point(from, top));

        // Only on the staff's run where somebody wrote it — a meter-implied line names nothing.
        if (line.Part is not null) _staff.Add(piece);

        foreach (var mark in written)
        {
            switch (mark)
            {
                case '|':
                    Rule(x, top, ThinBarline, height);
                    x += ThinBarline + (0.22 * S);
                    continue;

                case '[':
                case ']':
                    Rule(x, top, ThickBarline, height);
                    x += ThickBarline + (0.22 * S);
                    continue;

                case ':':
                    // Repeat dots, in the second and third spaces.
                    Dot(x, Y(system, 3));
                    Dot(x, Y(system, 5));
                    x += (0.5 * S) + (0.22 * S);
                    continue;
            }
        }

        if (_build.Reached.IsEmpty) _build.Covers(In(new Rect(x, top, ThinBarline, height)));

        Close();
        return x;

        void Dot(double at, double y)
        {
            var size = 0.32 * S;
            var dot = new EllipseGeometry(In(new Rect(at, y - (size / 2), size, size)));
            dot.Freeze();
            _build.Draw(GeometryMark.Filled(dot));
        }
    }

    // ── Bracketed systems ───────────────────────────────────────────────────

    /// <summary>
    /// What says two staves play together rather than one after another: a bracket down their left, and
    /// bar lines through the gap between them. Both drawn after every staff is placed, since both concern
    /// the space between two staves. Bar lines are taken off the topmost staff, which only works because
    /// the group shares one bar grid — if the voices disagreed on bar positions, nothing is bracketed.
    /// </summary>
    private void Brackets(List<System> systems)
    {
        for (var at = 0; at < systems.Count; at++)
        {
            if (systems[at].Bracket == 0 || !systems[at].FirstOfBracket) continue;

            var last = at;
            while (last + 1 < systems.Count
                   && systems[last + 1].Bracket == systems[at].Bracket
                   && !systems[last].LastOfBracket) last++;

            if (last == at) continue;

            var top = systems[at].StaffTop;
            var bottom = systems[last].StaffTop + StaffHeight;

            // Clamped to the left edge: the margin is only two pixels, so a correctly-placed bracket would be half off the page.
            var x = Math.Max(0, LeftMargin - BracketWidth);

            Open("bracket", at: new Point(x, top));

            Rule(x, top, BracketWidth, bottom - top);

            // A short hook at each end tells the eye this is a bracket, not a bar line.
            Rule(x, top, BracketWidth * 2, StaffLineThick * 2);
            Rule(x, bottom - (StaffLineThick * 2), BracketWidth * 2, StaffLineThick * 2);

            Through(systems, at, last);

            Close();
        }
    }

    /// <summary>The bar lines continued down the gaps between the staves of one bracketed system.</summary>
    private void Through(List<System> systems, int from, int to)
    {
        foreach (var bar in systems[from].Bars)
        {
            if (bar.Closed is null) continue;
            var x = bar.X + bar.Width - BarlineWidth(bar.Closed) + (0.25 * S);

            for (var at = from; at < to; at++)
                Rule(x,
                     systems[at].StaffTop + StaffHeight,
                     ThinBarline,
                     systems[at + 1].StaffTop - (systems[at].StaffTop + StaffHeight));
        }
    }

    // ── Repeat brackets ─────────────────────────────────────────────────────

    /// <summary>
    /// The numbered bracket over a repeat: <c>|1 … :|2 …</c>. Runs from the bar the number was written on
    /// to whichever comes first of: the repeat's end, the next number, or the system's end.
    /// </summary>
    private void Voltas(System system)
    {
        for (var at = 0; at < system.Bars.Count; at++)
        {
            if (system.Bars[at].Volta is not { } part) continue;

            var to = at;
            while (to < system.Bars.Count && !system.Bars[to].EndsRepeat && !system.Bars[to].EndsBracket
                   && (to == at || system.Bars[to].Volta is null)) to++;

            var last = Math.Min(to, system.Bars.Count - 1);
            var left = system.Bars[at].X;
            var right = system.Bars[last].X + system.Bars[last].Width;
            var y = system.StaffTop - system.Above;

            Open("volta", part, new Point(left, y));

            Rule(left, y, right - left, StaffLineThick * 1.6);
            Rule(left, y, StaffLineThick * 1.6, VoltaTick);

            // Closed on the right where it covers a repeat end or the music's end; open otherwise, meaning it carries into the next line.
            if (system.Bars[last].EndsRepeat || Closes(system.Bars[last].Closed))
                Rule(right - (StaffLineThick * 1.6), y, StaffLineThick * 1.6, VoltaTick);

            if (system.Bars[at].VoltaLabel is { Length: > 0 } label)
            {
                var glyphs = ScoreText.Build(label + ".", VoltaSize);
                var where = In(new Point(left + (0.45 * S), y + (0.28 * S)));
                _build.Draw(new TextMark(glyphs, where, null));
            }

            Close();
        }
    }

    // ── Glyphs ──────────────────────────────────────────────────────────────

    /// <summary>A glyph on a piece of its own — the ordinary case, since each is part of a thing's drawing.</summary>
    private void Glyph(string kind, int codepoint, Point baseline, double scale = 1.0)
    {
        Open(kind, at: baseline);
        Mark(codepoint, baseline, scale);
        Close();
    }

    /// <summary>Records a glyph as a filled outline, not text — WPF's text pipeline gamma-corrects glyph coverage and visibly fattens a music font's thin strokes.</summary>
    private void Mark(int codepoint, Point baseline, double scale = 1.0)
    {
        var at = In(baseline);

        if (Smufl.Outline(codepoint, at, S, scale) is not { } outline)
        {
            // No font: a hollow head of about the right size keeps the tune readable rather than blank.
            var fallback = new EllipseGeometry(new Point(at.X + (_noteHead / 2), at.Y),
                                               _noteHead / 2, S * 0.35);
            fallback.Freeze();
            _build.Draw(new GeometryMark(fallback, null, null, 0.12 * S));
            return;
        }

        _build.Draw(GeometryMark.Filled(outline));
    }

    // ── Ties and slurs ──────────────────────────────────────────────────────

    /// <summary>
    /// A tie or a slur, and where its two ends can be gathered — worked out before anything is drawn,
    /// because a piece is finished once closed: a set can't gather notes already drawn, only be opened
    /// around them as they're emitted. What pairing needs is settled by <see cref="Justify"/> and
    /// <see cref="Stack"/>, both of which run before the tree is touched.
    /// </summary>
    private sealed class Curve
    {
        public required Event From;
        public required Event To;
        public required System FromSystem;
        public required System ToSystem;
        public required string Kind;

        /// <summary>Which side it bows on — a fact about the stems it must clear, not about either note.</summary>
        public bool Above;

        /// <summary>How many curves it encloses, so an outer curve draws clear of those inside it.</summary>
        public int Nesting;

        /// <summary>The bar whose children it gathers, or null for one drawn on the line.</summary>
        public Bar? In;

        /// <summary>The beamed group inside that bar, where both ends are in one.</summary>
        public ISourcePart? Beam;

        /// <summary>The children it spans, inclusive, within whatever gathers it.</summary>
        public int First = -1;
        public int Last = -1;
    }

    private readonly List<Curve> _curves = [];

    private void Pair(List<System> systems)
    {
        _curves.Clear();

        var placed = new List<(Event Event, System System, Bar Bar)>();
        foreach (var system in systems)
            foreach (var bar in system.Bars)
                foreach (var ev in bar.Events)
                    placed.Add((ev, system, bar));

        for (var at = 0; at < placed.Count - 1; at++)
            if (placed[at].Event.TieStart) Joined(placed, at, at + 1, "tie");

        // A stack, since ABC allows slurs to nest: `((AA)A)` is two slurs, and the one that closes first opened last.
        var open = new Stack<int>();

        for (var at = 0; at < placed.Count; at++)
        {
            for (var i = 0; i < placed[at].Event.SlurOpen; i++) open.Push(at);

            for (var i = 0; i < placed[at].Event.SlurClose && open.Count > 0; i++)
            {
                var from = open.Pop();
                if (from != at) Joined(placed, from, at, "slur");
            }
        }

        Nest();
    }

    /// <summary>
    /// One curve, and the smallest thing that can hold both its ends: a beamed group, or a bar. Anything
    /// reaching further — across a bar line or a system break (two curves, not one) — is drawn on the line.
    /// </summary>
    private void Joined(List<(Event Event, System System, Bar Bar)> placed, int from, int to, string kind)
    {
        var one = placed[from];
        var other = placed[to];

        var curve = new Curve
        {
            From = one.Event,
            To = other.Event,
            FromSystem = one.System,
            ToSystem = other.System,
            Kind = kind,

                // Opposite the stems: a curve bows away from wherever the stem leaves the head. Where the two ends disagree it goes above, the side with room.
                Above = Engraving.StemDown(Stems(one)) || Engraving.StemDown(Stems(other)),
            };

        if (ReferenceEquals(one.Bar, other.Bar))
        {
            if (one.Event.Beam is { } beam && ReferenceEquals(beam, other.Event.Beam))
            {
                var notes = one.Bar.Events.Where(e => ReferenceEquals(e.Beam, beam)).ToList();
                curve.In = one.Bar;
                curve.Beam = beam;
                curve.First = notes.IndexOf(one.Event);
                curve.Last = notes.IndexOf(other.Event);
            }
            else
            {
                curve.In = one.Bar;
                curve.First = Slot(one.Bar, one.Event);
                curve.Last = Slot(one.Bar, other.Event);
            }
        }

        _curves.Add(curve);
    }

    /// <summary>
    /// The half-spaces a curve's end has to clear: its beamed group's, where it is in one.
    /// <strong>The group decides, not the note</strong> — a beamed note doesn't choose its own stem
    /// direction, so a curve keeps clear of the stem actually drawn, not the one the note alone would pick.
    /// </summary>
    private static List<int> Stems((Event Event, System System, Bar Bar) at)
    {
        var geometry = StaffGeometry.For(at.System.Row.Clef);

        var run = at.Event.Beam is { } beam
            ? at.Bar.Events.Where(e => ReferenceEquals(e.Beam, beam) && e.Beamable).ToList()
            : [];

        // The same test the beam makes: fewer than two, and every note stems for itself.
        var events = run.Count > 1 && run.Contains(at.Event) ? run : [at.Event];
        var halves = events.SelectMany(e => e.Heads.Select(geometry.HalfSpacesAbove)).ToList();

        return halves.Count > 0 ? halves : [Engraving.MiddleLine];
    }

    /// <summary>Which child of a bar an event is drawn in — a beamed run counts as one.</summary>
    private static int Slot(Bar bar, Event of)
    {
        var at = -1;
        ISourcePart? beam = null;
        var first = true;

        foreach (var ev in bar.Events)
        {
            if (first || ev.Beam is null || !ReferenceEquals(ev.Beam, beam)) at++;

            beam = ev.Beam;
            first = false;

            if (ReferenceEquals(ev, of)) return at;
        }

        return -1;
    }

    /// <summary>
    /// Which pairs can be a piece, and which have to hang on the line. A set is a parent, and two parents
    /// can't each hold half the other's children — so where two curves overlap without containment, the
    /// second drawn hangs instead. Widest first, so a slur covering another gathers it.
    /// </summary>
    private void Nest()
    {
        foreach (var group in _curves.Where(c => c.In is not null).GroupBy(c => (c.In, c.Beam)))
        {
            var taken = new List<Curve>();

            foreach (var curve in group.OrderByDescending(c => c.Last - c.First).ThenBy(c => c.First))
            {
                if (curve.First < 0 || curve.Last < 0 || curve.First >= curve.Last
                    || taken.Any(t => Crosses(t, curve)))
                {
                    curve.In = null;
                    curve.Beam = null;
                    continue;
                }

                taken.Add(curve);
                    }

                    // How many each encloses, so an outer curve draws clear of those inside it rather than at the same rise.
                    foreach (var curve in taken)
                        curve.Nesting = taken.Count(inner => !ReferenceEquals(inner, curve)
                                                             && inner.First >= curve.First && inner.Last <= curve.Last);
                }

        static bool Crosses(Curve a, Curve b) =>
            (b.First < a.First && b.Last >= a.First && b.Last < a.Last)
            || (b.First > a.First && b.First <= a.Last && b.Last > a.Last);
    }

    /// <summary>The sets gathered at one level of one bar, widest first so they nest as they are opened.</summary>
    private List<Curve> Gathering(Bar bar, ISourcePart? beam) =>
        [.. _curves
            .Where(c => ReferenceEquals(c.In, bar) && ReferenceEquals(c.Beam, beam))
            .OrderByDescending(c => c.Last - c.First)
            .ThenBy(c => c.First)];

    private static IEnumerable<Curve> Opening(List<Curve> sets, int slot) =>
        sets.Where(c => c.First == slot);

    /// <summary>In the reverse of the order they opened, so the innermost closes first.</summary>
    private static IEnumerable<Curve> Closing(List<Curve> sets, int slot) =>
        sets.Where(c => c.Last == slot).Reverse();

    /// <summary>The arc of a set gathered inside a bar, drawn into the set once its members are in it.</summary>
    private void Arced(Curve curve)
    {
        var clear = curve.Nesting * CurveNest;

        Arc(new Point(curve.From.X + (_noteHead / 2), Springs(curve.From, curve.FromSystem, curve.Above, clear)),
            new Point(curve.To.X + (_noteHead / 2), Springs(curve.To, curve.ToSystem, curve.Above, clear)),
            curve.Above, curve.Kind, clear);
    }

    /// <summary>
    /// The curves that reach further than a bar, each drawn on its own line. A curve crossing a system
    /// break is two arcs — out to the right edge, in from the left of the next — since the layout is a
    /// picture of the page; that the two halves are one thing is a fact the parse tree keeps instead.
    /// </summary>
    private void Hanging(System system)
    {
        foreach (var curve in _curves)
        {
            if (curve.In is not null) continue;

            var above = curve.Above;

            var start = new Point(curve.From.X + (_noteHead / 2), Springs(curve.From, curve.FromSystem, above));
            var end = new Point(curve.To.X + (_noteHead / 2), Springs(curve.To, curve.ToSystem, above));

            if (ReferenceEquals(curve.FromSystem, curve.ToSystem))
            {
                if (!ReferenceEquals(curve.FromSystem, system)) continue;

                Open(curve.Kind + "-set", at: start);
                Arc(start, end, above, curve.Kind);
                Close();
                continue;
            }

            if (ReferenceEquals(curve.FromSystem, system))
            {
                Open(curve.Kind + "-set", at: start);
                Arc(start, new Point(system.Right - (0.5 * S), start.Y), above, curve.Kind);
                Close();
            }
            else if (ReferenceEquals(curve.ToSystem, system))
            {
                Open(curve.Kind + "-set", at: end);
                Arc(new Point(system.HeadWidth - (0.5 * S), end.Y), end, above, curve.Kind);
                Close();
            }
        }
    }

    /// <summary>Where a curve leaves a note: clear of the head, opposite the stems, clear again of whatever it encloses.</summary>
    private static double Springs(Event ev, System system, bool above, double clear = 0)
    {
        var halves = Halves(ev, system);

        return above
            ? Y(system, halves.Max()) - CurveClear - clear
            : Y(system, halves.Min()) + CurveClear + clear;
    }

    private static List<int> Halves(Event ev, System system)
    {
        var geometry = StaffGeometry.For(system.Row.Clef);
        return ev.Heads.Length > 0
            ? [.. ev.Heads.Select(geometry.HalfSpacesAbove)]
            : [Engraving.MiddleLine];
    }

    /// <summary>
    /// The crescent itself: two Béziers with the same ends, bowing by different amounts so it's thin at
    /// the heads and thickest in the middle — an evenly-stroked arc reads as a drawing of a slur, not one.
    /// <strong>It carries no part.</strong> Nobody typed it directly; giving it one would offer a shallow
    /// caret stop spanning both notes that wins over the actual notes wherever a tie begins.
    /// </summary>
    private void Arc(Point from, Point to, bool above, string kind, double clear = 0)
    {
        var span = Math.Abs(to.X - from.X);
        if (span < 1) return;

        Open(kind, at: new Point(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y)));

        var one = In(from);
        var other = In(to);

        var rise = (Math.Clamp(CurveRise + (span * 0.06), CurveRise, CurveMaxRise) + clear) * (above ? -1 : 1);
        var middle = new Point((one.X + other.X) / 2, ((one.Y + other.Y) / 2) + rise);
        var inner = new Point(middle.X, middle.Y - (CurveThick * (above ? -1 : 1)));

        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = one, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new QuadraticBezierSegment(middle, other, false));
        figure.Segments.Add(new QuadraticBezierSegment(inner, one, false));
        path.Figures.Add(figure);
        path.Freeze();

        _build.Draw(GeometryMark.Filled(path));

        Close();
    }

    // ── What belongs with what ──────────────────────────────────────────────

    /// <summary>
    /// Declares what selection may step through: each layer along its own length (notes, chords, each
    /// verse), and everything sounding at one moment as a stack (chord, note, syllables — top to bottom).
    ///
    /// <para>
    /// <strong>Layers run the length of the tune, not of a system</strong> — a verse (and the music)
    /// carries on across a line break, so stopping a run at a system edge would describe the page rather
    /// than the piece.
    /// </para>
    /// <para>
    /// Also, deliberately, the structure a playback animation would need — notes in order, layers in turn,
    /// or bar by bar with stacks — all walks over what's declared here.
    /// </para>
    /// </summary>
    private void Order()
    {
        // Along each layer, in engraved (= read) order. A run adds what containment can't say: that the
        // last note of a line and the first of the next are neighbours, since a line break is a fact about
        // the paper, not the tune. The staff's run keeps its rests and bar lines too, since a drag along it passes over them.
        _build.Runs(_sections, vertical: false);
        _build.Runs(_staff, vertical: false);
        _build.Runs([.. _chords.Select(c => c.At)], vertical: false);

        foreach (var verse in _sung.GroupBy(s => s.Verse).OrderBy(g => g.Key))
            _build.Runs([.. verse.Select(v => v.At)], vertical: false);

        // Down through everything that sounds at one moment.
        var chords = _chords.ToDictionary(c => c.Event, c => c.At);
        var sung = _sung.GroupBy(s => s.Event).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Verse).ToList());

        foreach (var (ev, head) in _heads)
        {
            var stack = new List<int>();
            if (chords.TryGetValue(ev, out var chord)) stack.Add(chord);
            stack.Add(head);
            if (sung.TryGetValue(ev, out var verses)) stack.AddRange(verses.Select(v => v.At));

            _build.Runs(stack, vertical: true);
        }
    }

    /// <summary>
    /// Where a text line's letters actually sit inside its layout box — top of a capital to bottom of a
    /// descender, excluding leading. Needed because a wash or caret using the whole box reaches into the
    /// line above/below given how tightly lyrics are set.
    /// </summary>
    private static (double Top, double Height) Letters(FormattedText sample)
    {
        var bottom = sample.Height + sample.OverhangAfter;
        return (bottom - sample.Extent, sample.Extent);
    }

    /// <summary>
    /// What a beamed run names: its notes, first to last — not the group as read, which can begin at a
    /// chord symbol before the first note (<c>"D"GFGA</c>) and would otherwise drag the chord into a
    /// note-only selection.
    /// </summary>
    private static ISourcePart? Notes(List<Event> events) => SourcePartExtensions.Across(events.Select(ev => ev.Part));
}
