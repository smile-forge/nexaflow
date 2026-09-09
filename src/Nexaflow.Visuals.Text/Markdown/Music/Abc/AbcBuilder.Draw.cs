using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Abc;

/// <summary>
/// The half of the engraver that puts pieces into the tree: what each thing is drawn as, what holds it,
/// and what belongs with what.
///
/// <para>
/// Everything above this has already decided where things go — <see cref="Justify"/> settles every
/// <c>ev.X</c> and <see cref="Stack"/> every <c>StaffTop</c> — so this places nothing and only records.
/// It goes on working in page coordinates, because that is what an engraver thinks in; what the tree
/// stores is relative, and <see cref="LayoutBuilder.Anchor"/> is the one subtraction between the two.
/// </para>
/// <para>
/// <strong>A piece is finished when it is closed.</strong> That is what changed, and it is why the beam
/// is worked out before the notes under it are drawn rather than laid over them afterwards, and why the
/// curves are paired before anything is drawn rather than gathering finished notes at the end.
/// </para>
/// </summary>
internal sealed partial class AbcBuilder
{
    private readonly LayoutBuilder _build = new();

    /// <summary>
    /// What was drawn where, kept while a tune is engraved so the orderings can be declared once it is.
    ///
    /// <para>
    /// Gathered rather than linked as it goes because an ordering is a run: it is only complete when the
    /// last system is, and a lyric's run does not stop at a system's edge. See <see cref="Order"/>.
    /// </para>
    /// </summary>
    private readonly List<(Event Event, int At)> _heads = [];
    private readonly List<(Event Event, int At)> _chords = [];
    private readonly List<(Event Event, int Verse, int At)> _sung = [];
    private readonly List<int> _sections = [];

    // ── Page coordinates in, frames out ─────────────────────────────────────

    /// <summary>
    /// Opens a piece at a point on the page. Nothing for <paramref name="at"/> means the piece begins
    /// where whatever holds it begins, which is what every part of another thing's drawing wants.
    /// </summary>
    private int Open(string kind, ISourcePart? part = null, Point? at = null, bool? isInk = null) =>
        _build.Open(kind, part, at is { } page ? In(page) : default, isInk);

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

    private (LayoutTree Tree, Size Size) Engrave(double width)
    {
        _noteHead = Smufl.Advance(Smufl.NoteheadBlack, S);
        if (_noteHead <= 0) _noteHead = 1.18 * S;

        var rows = Read();
        var systems = Wrap(rows, Math.Max(width, 12 * S));

        if (systems.Count > 0) Justify(systems, width);

        // The words are set to the width the music actually took rather than to the page it was offered. A
        // tune shorter than the window is engraved narrower than it, and a title centred on the window would
        // sit off to one side of the music it names.
        //
        // Asked of where the systems were justified to rather than of the ink they turned out to make. It
        // is the same number to within an overhanging slur, and it is available before anything is drawn —
        // which it has to be, because the heading is what the music starts under and a piece's anchor is
        // fixed when it opens.
        var header = AbcHeader.Of(_reading);
        var paper = systems.Count == 0 ? width : systems.Max(s => s.Right) + RightMargin;

        _build.Open("score");

        var top = Heading(header, paper);

        // …and the music is stacked below the heading rather than laid out and moved down afterwards.
        // `Stack` already took the offset to start from; nothing else had ever passed it one.
        if (systems.Count > 0) Stack(systems, top);

        _build.Open("music");

        if (systems.Count > 0)
        {
            Pair(systems);
            foreach (var system in systems) Draw(system);

            // A bracket joins staves that are already placed — it cannot be drawn until every staff has
            // landed, which is why it comes after the loop rather than inside it.
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

        Open("system", at: new Point(0, system.StaffTop), isInk: false);

        Staff(system);
        Head(system, geometry);

        foreach (var bars in Sections(system.Bars))
        {
            _sections.Add(Open("section", Spanning(bars), new Point(bars[0].X, system.StaffTop)));
            foreach (var bar in bars) Draw(system, geometry, bar);
            Close();
        }

        Voltas(system);

        // The curves that reach further than any one bar, drawn on the line — which is the smallest thing
        // that can hold both their ends.
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
            var glyphs = ScoreText.Build(name, CreditSize, _ppd);
            var at = new Point(x, system.StaffTop + (StaffHeight / 2) - (glyphs.Height / 2));

            Open("voice", at: at, isInk: false);
            _build.Draw(new TextMark(glyphs, default, null));
            Close();

            x += glyphs.Width + (0.6 * S);
        }

        var clefY = Y(system, geometry.ClefRefHalfSpaces);
        Glyph("clef", geometry.ClefGlyph, new Point(x, clefY));
        x += Smufl.Advance(geometry.ClefGlyph, S) + (0.6 * S);

        x = DrawKeySignature(system, geometry, x, system.Row.Context.Fifths);
        if (system.ShowMeter) Meter(system, x, system.Row.Context.Beats, system.Row.Context.BeatUnit);
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

    private void Meter(System system, double x, int beats, int unit, int? asked = null)
    {
        // A sign where the tune wrote one — `M:C` and `M:C|` are asking for the symbol rather than for
        // the figures they happen to count as.
        if ((asked ?? MeterSign) is { } sign)
        {
            Glyph("meter", sign, new Point(x, Y(system, 4)));
            return;
        }

        // Otherwise figures, one above the other, centred on the third and first spaces.
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

    /// <summary>
    /// The children of a bar that stand for events: a beamed run is one of them, and anything else is one
    /// of its own. What a curve's ends are counted in when it gathers two of them.
    /// </summary>
    private static List<(ContentPart? Beam, List<Event> Events)> Runs(Bar bar)
    {
        var runs = new List<(ContentPart? Beam, List<Event> Events)>();

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

        var x = bar.X;
        if (bar.Opened is not null) x = Barline(system, bar.Opened, x);

        // A mid-tune key or meter change, printed where it takes effect.
        if (bar.KeyChange is { } key) x = DrawKeySignature(system, geometry, x, key.Fifths);
        if (bar.MeterChange is { } meter) Meter(system, x, meter.Beats, meter.Unit, meter.Sign);

        var runs = Runs(bar);
        var sets = Gathering(bar, beam: null);

        for (var slot = 0; slot < runs.Count; slot++)
        {
            foreach (var curve in Opening(sets, slot)) Open(curve.Kind + "-set", isInk: false);

            var (beam, events) = runs[slot];

            // Beamed runs are pieces of their own: the beam is drawn on the group and the notes it joins
            // hang under it. Everything else hangs straight off the bar.
            if (beam is not null)
            {
                Open("beam", beam, new Point(events[0].X, system.StaffTop));
                Beamed(system, geometry, bar, events);
                Close();
            }
            else
            {
                foreach (var ev in events) Note(system, geometry, ev, flags: true);
            }

            foreach (var curve in Closing(sets, slot)) { Arced(curve); Close(); }
        }

        // The chord named over a note and the words sung under it hang off the bar, beside the note
        // rather than inside it. None of the three is drawn within another - a chord symbol sits in the
        // air above the staff and a syllable in the lyric row below - and containment is supposed to say
        // where ink went and how much room it takes. Hung off the note they made it a piece with children,
        // which a pointer descends straight past, so half the notes in a tune could not be clicked at all
        // and the press landed on the bar.
        //
        // Nor are the three wrapped in a box of their own. That box would have to live inside the beam
        // group when the note is beamed, and it would drag the group's rectangle down through the lyric
        // row - a beam that reports itself as seventy pixels tall because of a word. What groups the three
        // is the moment `Order` declares, which is a way to step between them and not a thing anybody
        // draws.
        //
        // In a pass of their own because a note may be inside a beam group when it is drawn and these are
        // never inside one. A piece is finished when it is closed, so there is no writing back into the
        // bar from within something it holds.
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
        Barline(system, bar);

        if (_build.Reached.IsEmpty)
            _build.Covers(In(new Rect(bar.X, system.StaffTop, bar.Width, StaffHeight)));

        Close();
    }

    /// <summary>
    /// A beamed run: the beam worked out first, then the notes drawn with their stems already reaching it.
    ///
    /// <para>
    /// The order is the whole of what changed. A beam laid over finished notes has to write a stem back
    /// into each of them, and a note is finished when it is closed — so the group's direction, slope and
    /// height are settled before the first head goes down, which is what an engraver does anyway.
    /// </para>
    /// </summary>
    private void Beamed(System system, StaffGeometry geometry, Bar bar, List<Event> events)
    {
        var beamed = events.Where(e => e.Beamable).ToList();
        var plan = Plan(system, geometry, beamed);

        foreach (var ev in events)
        {
            var at = plan is null ? -1 : beamed.IndexOf(ev);

            if (plan is null || at < 0)
            {
                Note(system, geometry, ev, flags: true);
                continue;
            }

            Note(system, geometry, ev, flags: false,
                 toY: plan.Reach + (plan.Slope * (plan.Xs[at] - plan.Xs[0])) + (plan.Down ? BeamThick : 0),
                 stemsDown: plan.Down);
        }

        if (plan is not null) BeamBars(plan, beamed);
        _ = bar;
    }

    /// <summary>Where a beam over a group lands: which way it points, how it leans, and how many bars.</summary>
    private sealed record BeamPlan(bool Down, double Reach, double Slope, IReadOnlyList<double> Xs, int Levels);

    /// <summary>
    /// The beam over a written group.
    /// <para>
    /// One direction for the whole group — the note reaching furthest from the middle line decides — and a
    /// slope that only leans where the group's contour genuinely leans. A run that climbs, falls and climbs
    /// again beams flat, because a first-to-last slope drawn through a zig-zag asserts a direction the music
    /// does not have.
    /// </para>
    /// </summary>
    private BeamPlan? Plan(System system, StaffGeometry geometry, List<Event> events)
    {
        if (events.Count < 2) return null;

        var halves = events.SelectMany(e => e.Heads.Select(geometry.HalfSpacesAbove)).ToList();
        if (halves.Count == 0) return null;

        var down = StemsDown(halves);

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

        // The beam has to clear every stem in the group, so it is placed against the note that needs it most.
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

                Open("beam-bar", at: new Point(Math.Min(x0, x1), Math.Min(y0, y1)), isInk: false);

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

    /// <summary>
    /// Which notes each level of beam runs between. The primary beam spans the whole group; a secondary one
    /// runs only where both neighbours are short enough, and a lone short note gets a stub.
    /// </summary>
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
    /// One event, as a note (or a rest) with its drawing hanging under it: the head, the accidental, the
    /// ledger lines, the dots, the stem.
    ///
    /// <para>
    /// Each of those is a piece rather than a mark on the note, which is what makes the note a
    /// <em>parent</em>. A pointer lands on whichever piece it is over and climbs to the first thing the
    /// source named - so a press on a stem or a ledger line means the note, said once rather than
    /// special-cased. It is also the unit a drag would move: one piece, and its whole drawing with it,
    /// because everything inside is measured from the note's own anchor.
    /// </para>
    /// </summary>
    private void Note(System system, StaffGeometry geometry, Event ev, bool flags,
                      double? toY = null, bool? stemsDown = null)
    {
        var at = Open(ev.IsRest ? "rest" : "note", ev.Part, new Point(ev.X, system.StaffTop));

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

        if (!ev.IsRest && !ev.Invisible) _heads.Add((ev, at));
    }

    /// <summary>
    /// Grace notes: cue-size heads crushed in before the main one, beamed where there are several and
    /// slashed for an acciaccatura.
    /// <para>
    /// They belong to the note they precede — which is why they hang off its piece of layout rather than
    /// standing beside it. Selecting the note takes them with it, which is what a reader means.
    /// </para>
    /// </summary>
    private void Graces(System system, StaffGeometry geometry, Event ev)
    {
        if (ev.Graces.Count == 0) return;

        Open("graces", isInk: false);

        var width = _noteHead * GraceScale;
        var step = width + GraceStep;
        var x = ev.X - ev.AccidentalWidth - ev.GraceWidth + GraceGap;

        // Where the beam sits, settled before anything is drawn: every stem in a beamed group has to
        // reach the same line, so the highest note in the group decides for all of them. Drawing each
        // stem to its own length and then laying a beam over the shortest left the others hanging short
        // of it, which is why the group came out unattached.
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

            // A grace note always stems up, whatever it sits on: the group is read as an ornament of the
            // note after it rather than as music of its own, and a run of them reads as one gesture.
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

        // …and the slur to the note it ornaments, which is what says the two are one gesture rather than
        // a very short note followed by another.
        // From the middle of the group, not its last note: what the slur joins to the main note is the
        // ornament as a whole.
        var middle = (firstStem ?? lastHead) + (((lastHead + width) - (firstStem ?? lastHead)) / 2);
        GraceSlur(system, geometry, ev, middle, heads.Max(h => h.Y));

        if (ev.GraceSlashed && firstStem is { } slashAt)
        {
            // The slash of an acciaccatura, through the stem of the first grace note.
            var one = In(new Point(slashAt - (0.5 * S), top + (0.9 * S)));
            var other = In(new Point(slashAt + (0.5 * S), top - (0.2 * S)));

            var slash = new LineGeometry(one, other);
            slash.Freeze();
            _build.Draw(new GeometryMark(slash, null, null, StemThick * 1.6));
        }

        Close();
    }

    /// <summary>
    /// The marks on an event: the ones that hug the head, and the ones that stack clear of the staff.
    /// <para>
    /// Two rules rather than one, because they answer different questions. A staccato dot means <em>this
    /// note</em> and so sits against it, on the side the stem is not; a fermata means <em>this moment</em>
    /// and so sits above the staff whatever the note is doing, where the eye reads it along the line.
    /// </para>
    /// </summary>
    private void Marks(System system, StaffGeometry geometry, Event ev)
    {
        if (ev.HeadMarks.Count == 0 && ev.StaffMarks.Count == 0) return;

        var halves = ev.Heads.Length > 0
            ? ev.Heads.Select(geometry.HalfSpacesAbove).ToList()
            : [Engraving.MiddleLine];

        var down = StemsDown(halves);
        var at = down ? halves.Max() + 2 : halves.Min() - 2;

        foreach (var glyph in ev.HeadMarks)
        {
            Glyph("articulation", glyph,
                  new Point(ev.X + ((_noteHead - Smufl.Advance(glyph, S, MarkScale)) / 2), Y(system, at)),
                  MarkScale);

            at += down ? 2 : -2;
        }

        // Above the staff and above whatever the notation already reached, so a run of high notes pushes
        // its fermatas up with it rather than colliding.
        //
        // `Above` is the room the whole system reserved, so the row this lands on clears the highest note
        // on the line — but a mark sits over *its own* note, and on a line with one high note everything
        // else was being lifted to clear a note nowhere near it while the high note's own mark sat on the
        // head. Taking the higher of the two is what makes a mark clear the note it belongs to.
        var over = ev.Heads.Length == 0
            ? system.StaffTop
            : Y(system, geometry.HalfSpacesAbove(ev.Heads.Max())) - MarkRow;

        var y = Math.Min(
            system.StaffTop - system.Above + (system.HasVoltaRow ? VoltaRow : 0)
                + (system.HasChordRow ? ChordRow : 0) + MarkRow,
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

            var glyphs = ScoreText.Build(text, ChordSize, _ppd);

            var at = where switch
            {
                AnnotationPlacement.Above => new Point(ev.X, system.StaffTop - system.Above + (system.HasVoltaRow ? VoltaRow : 0)),
                AnnotationPlacement.Below => new Point(
                    ev.X,
                    system.StaffTop + StaffHeight + system.Below - (system.TextBelow * ChordRow) + (below++ * ChordRow)),
                AnnotationPlacement.Left => new Point(ev.X - glyphs.Width - (0.3 * S), system.StaffTop + S),
                _ => new Point(ev.X + _noteHead + (0.3 * S), system.StaffTop + S),
            };

            Open("annotation", at: at, isInk: false);
            _build.Draw(new TextMark(glyphs, default, null));
            Close();
        }
    }

    /// <summary>
    /// The stem, and a flag when the note is not beamed. Stems point away from the middle line; a note
    /// sitting on it stems up, which is what ABC engravers do.
    /// </summary>
    /// <param name="stemsDown">
    /// Which way the whole group points, where this note is in one. <strong>A beamed note does not get to
    /// decide for itself.</strong> The note reaching furthest from the middle line decides for all of them,
    /// and a note that worked its own direction out would attach its stem to the other side of its head and
    /// run it away from the beam — which draws as a stray line across the staff and is how this was found.
    /// </param>
    private void Stem(System system, StaffGeometry geometry, Event ev, bool flags,
                      double? toY = null, bool? stemsDown = null)
    {
        if (ev.IsRest || ev.Heads.Length == 0 || ev.BaseValue <= 1) return;

        var halves = ev.Heads.Select(geometry.HalfSpacesAbove).ToList();
        var down = stemsDown ?? StemsDown(halves);

        var fromY = Y(system, down ? halves.Max() : halves.Min());
        var endY = toY ?? (down ? Y(system, halves.Min()) + StemLen : Y(system, halves.Max()) - StemLen);
        var x = down ? ev.X + (StemThick / 2) : ev.X + _noteHead - (StemThick / 2);

        var bounds = new Rect(x - (StemThick / 2), Math.Min(fromY, endY), StemThick, Math.Abs(endY - fromY));

        Open("stem", at: bounds.TopLeft, isInk: false);

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
        Open(kind, at: new Point(x, y), isInk: false);
        Rule(x, y, width, height);
        Close();
    }

    private void Dots(System system, double x, int half, int dots)
    {
        // A dot never sits on a line: a note on one takes its dots in the space above.
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

        var glyphs = ScoreText.Chord(text, ChordSize, _ppd);
        var at = new Point(ev.X, system.StaffTop - system.Above);

        // Its own piece, naming the `"Am"` that was typed. A chord is a thing a reader picks out on its
        // own — to read down the changes, to copy them, to retype one — and it cannot be any of that while
        // it is a mark drawn on the note underneath it.
        var chord = Open("chord", ev.ChordPart, at);
        _build.Draw(new TextMark(glyphs, default, null));
        Close();

        _chords.Add((ev, chord));
    }

    /// <summary>
    /// The words under one note, and the line under a word held across it.
    ///
    /// <para>
    /// A held syllable — ABC's <c>_</c>, and what a tie means for the words — is not sung again on the
    /// next note, so what belongs under that note is not a syllable but the fact that the last one is
    /// still going. Engravers draw that as a rule running from the end of the word to the note that ends
    /// the hold. Drawn a note at a time, from the one before to this one, so a run of held notes comes
    /// out as one unbroken line without anything needing to know how long the run is.
    /// </para>
    /// </summary>
    private void Lyrics(System system, Event ev, Event? before)
    {
        foreach (var (verse, _, text, hyphen, melisma, part) in ev.Lyrics)
        {
            var y = system.LyricTop + (verse * LyricRow);

            if (melisma)
            {
                Held(ev, before, verse, y);
                continue;
            }

            if (text.Length == 0) continue;

            var glyphs = ScoreText.Build(hyphen ? text + "-" : text, LyricSize, _ppd);
            var at = new Point(ev.X + (_noteHead / 2) - (glyphs.Width / 2), y);

            // Drawn under the note and written a line away, and the layout says the first while the part
            // says the second. The two trees are free to look nothing alike, which is the only reason a
            // syllable can be picked out of a verse without the note coming with it.
            var sung = Open("syllable", part, at);
            _build.Draw(new TextMark(glyphs, default, null));
            Close();

            _sung.Add((ev, verse, sung));
        }
    }

    /// <summary>The rule under a note whose word was sung on an earlier one.</summary>
    private void Held(Event ev, Event? before, int verse, double y)
    {
        var to = ev.X + (_noteHead / 2);

        // From where the word it is holding actually ended, so the line starts clear of the letters
        // rather than through them. A held note with nothing before it on this line — the run carried
        // over a system break — starts at the head.
        var from = before is null ? ev.X : (before.X + (_noteHead / 2));
        if (before?.Lyrics.FirstOrDefault(l => l.Verse == verse) is { Text.Length: > 0 } sung)
            from += (ScoreText.Width(sung.Text, LyricSize, _ppd) / 2) + (0.3 * S);

        if (to - from < 0.2 * S) return;

        var at = y + (LyricSize * 0.78);
        var rule = new Rect(from, at - (MelismaThick / 2), to - from, MelismaThick);

        Open("melisma", at: rule.TopLeft, isInk: false);
        _build.Draw(new GeometryMark(new RectangleGeometry(In(rule)), _ink, null, 0));
        Close();
    }

    /// <summary>
    /// The slur from a grace group to the note it ornaments.
    ///
    /// <para>
    /// Without it a grace is a small note standing beside a big one and nothing on the page says the two
    /// belong together — which is the whole meaning of the notation.
    /// </para>
    /// <para>
    /// It hangs below, from the underside of the grace head to the underside of the head it runs to.
    /// Above is where the stems are — a grace always stems up — so a curve drawn over the top crosses
    /// them, which is why it read as upside down.
    /// </para>
    /// </summary>
    private void GraceSlur(System system, StaffGeometry geometry, Event ev, double from, double fromY)
    {
        if (ev.Heads.Length == 0) return;

        var to = ev.X + (_noteHead / 2);
        if (to - from < 0.3 * S) return;

        // The lowest head at each end, because the curve hangs under both.
        var toY = Y(system, geometry.HalfSpacesAbove(ev.Heads.Min())) + CurveClear;
        Arc(new Point(from, fromY + CurveClear), new Point(to, toY), above: false, "grace-slur");
    }

    /// <summary>
    /// The number over a tuplet, centred on the notes it covers.
    /// <para>
    /// Without it a triplet is three eighths that do not add up, and a reader has no way to know the tune
    /// is not simply wrong. It goes on the side the stems point, clear of the beam — which is where a
    /// reader looks for it, and is the opposite of where a mark on a head goes.
    /// </para>
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
                var down = StemsDown(halves);
                var glyphs = ScoreText.Build(
                    bar.Events[at].TupletNumber.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
                    VoltaSize, _ppd, style: FontStyles.Italic);

                var left = events[0].X;
                var right = events[^1].X + _noteHead;
                var y = down
                    ? Y(system, halves.Min()) + StemLen
                    : Y(system, halves.Max()) - StemLen - glyphs.Height;

                var where = new Point(((left + right) / 2) - (glyphs.Width / 2), y);

                Open("tuplet", at: where, isInk: false);
                _build.Draw(new TextMark(glyphs, default, null));
                Close();
            }

            at = to + 1;
        }
    }

    /// <summary>
    /// The line that closes a bar. It carries a part, because somebody wrote it: a reader can point at one,
    /// and a selection of every note in a bar has to cover it before it can grow into the bar.
    /// </summary>
    private void Barline(System system, Bar bar)
    {
        if (bar.Closed is null) return;
        Barline(system, bar.Closed, bar.X + bar.Width - BarlineWidth(bar.Closed) + (0.25 * S));
    }

    /// <summary>Draws one bar line at <paramref name="from"/>, and says where it ended.</summary>
    private double Barline(System system, ContentPart line, double from)
    {
        var written = line.Node.Print();
        var x = from;
        var top = system.StaffTop;
        var height = StaffHeight;

        Open("barline", line, new Point(from, top));

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
                    // The two dots of a repeat, in the second and third spaces.
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
    /// What says two staves are played together rather than one after another: a bracket down their left,
    /// and bar lines running through the gap between them.
    ///
    /// <para>
    /// Both are drawn after every staff has been placed, because both are about the space <em>between</em>
    /// two staves and neither exists until both have a position. The bar lines are taken off the topmost
    /// staff of the group, which is only correct because the group shares one bar grid — where the voices
    /// disagreed about where the bars are, nothing was bracketed and nothing is drawn.
    /// </para>
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

            // Clamped to the left edge rather than placed a bracket's width outside it: the margin is two
            // pixels, so a bracket drawn where it belongs is half off the page.
            var x = Math.Max(0, LeftMargin - BracketWidth);

            Open("bracket", at: new Point(x, top), isInk: false);

            Rule(x, top, BracketWidth, bottom - top);

            // A short hook at each end, which is what tells the eye the line is a bracket and not the
            // start of a bar.
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
    /// The numbered bracket over a repeat: <c>|1 … :|2 …</c>.
    /// <para>
    /// It runs from the bar the number was written on to the bar that ends the repeat, or to the next
    /// number, or to the end of the system — whichever comes first. Which is three answers to one
    /// question and is why this is worked out here rather than drawn bar by bar.
    /// </para>
    /// </summary>
    private void Voltas(System system)
    {
        for (var at = 0; at < system.Bars.Count; at++)
        {
            if (system.Bars[at].Volta is not { } part) continue;

            var to = at;
            while (to < system.Bars.Count && !system.Bars[to].EndsRepeat
                   && (to == at || system.Bars[to].Volta is null)) to++;

            var last = Math.Min(to, system.Bars.Count - 1);
            var left = system.Bars[at].X;
            var right = system.Bars[last].X + system.Bars[last].Width;
            var y = system.StaffTop - system.Above;

            Open("volta", part, new Point(left, y));

            Rule(left, y, right - left, StaffLineThick * 1.6);
            Rule(left, y, StaffLineThick * 1.6, VoltaTick);

            // Closed on the right where what it covers stops — a repeat end, or a line that ends the
            // music. Left open otherwise, which is how a bracket says it carries on into the next line.
            if (system.Bars[last].EndsRepeat || Closes(system.Bars[last].Closed))
                Rule(right - (StaffLineThick * 1.6), y, StaffLineThick * 1.6, VoltaTick);

            if (system.Bars[at].VoltaLabel is { Length: > 0 } label)
            {
                var glyphs = ScoreText.Build(label + ".", VoltaSize, _ppd);
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
        Open(kind, at: baseline, isInk: false);
        Mark(codepoint, baseline, scale);
        Close();
    }

    /// <summary>
    /// Records a glyph on the piece being built. Drawn as a filled outline rather than as text,
    /// deliberately: WPF's text pipeline gamma-corrects glyph coverage and visibly fattens a music font's
    /// thin strokes.
    /// </summary>
    private void Mark(int codepoint, Point baseline, double scale = 1.0)
    {
        var at = In(baseline);

        if (Smufl.Outline(codepoint, at, S, scale) is not { } outline)
        {
            // No font. A hollow head of about the right size keeps the tune readable rather than blank.
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
    /// A tie or a slur, and where its two ends can be gathered — worked out before anything is drawn.
    ///
    /// <para>
    /// It has to be worked out first now. A piece is finished when it is closed, so a set cannot gather
    /// notes that have already been drawn: it is opened around them as they are emitted. Everything the
    /// pairing needs — which events, on which systems, in which bars — is settled by
    /// <see cref="Justify"/> and <see cref="Stack"/>, both of which run before the tree is touched.
    /// </para>
    /// </summary>
    private sealed class Curve
    {
        public required Event From;
        public required Event To;
        public required System FromSystem;
        public required System ToSystem;
        public required string Kind;

        /// <summary>The bar whose children it gathers, or null for one drawn on the line.</summary>
        public Bar? In;

        /// <summary>The beamed group inside that bar, where both ends are in one.</summary>
        public ContentPart? Beam;

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

        // Slurs are matched with a stack because ABC allows them to nest: `((AA)A)` is two slurs, and the
        // one that closes first is the one that opened last.
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
    /// One curve, and the smallest thing that can hold both its ends.
    ///
    /// <para>
    /// Two notes of one beamed group are gathered inside it; two things in one bar are gathered there.
    /// Anything reaching further — across a bar line, or across a system break, where it is not one curve
    /// at all but two — is drawn on the line, which is what the re-parenting version fell back to when it
    /// found no common ground.
    /// </para>
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

    /// <summary>Which child of a bar an event is drawn in — a beamed run counts as one.</summary>
    private static int Slot(Bar bar, Event of)
    {
        var at = -1;
        ContentPart? beam = null;
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
    /// Which of the pairs can actually be a piece, and which have to hang on the line.
    ///
    /// <para>
    /// A set is a parent, and two parents cannot each hold half of the other's children — so where two
    /// curves overlap without one containing the other, the second one drawn hangs instead. Widest first,
    /// so a slur covering another gathers it rather than colliding with it.
    /// </para>
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
        }

        static bool Crosses(Curve a, Curve b) =>
            (b.First < a.First && b.Last >= a.First && b.Last < a.Last)
            || (b.First > a.First && b.First <= a.Last && b.Last > a.Last);
    }

    /// <summary>The sets gathered at one level of one bar, widest first so they nest as they are opened.</summary>
    private List<Curve> Gathering(Bar bar, ContentPart? beam) =>
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
        // Opposite the stems, which is the whole rule: a stem leaving the head upward is what the curve
        // has to keep clear of, so it bows underneath, and the other way round for a down stem. This had
        // the sense inverted, so a pair of low notes — stems up — got a tie arched over the stems it was
        // supposed to avoid. Where the two ends disagree it goes above, which is the side with room.
        var above = StemsDown(Halves(curve.From, curve.FromSystem))
                    || StemsDown(Halves(curve.To, curve.ToSystem));

        Arc(new Point(curve.From.X + (_noteHead / 2), Springs(curve.From, curve.FromSystem, above)),
            new Point(curve.To.X + (_noteHead / 2), Springs(curve.To, curve.ToSystem, above)),
            above, curve.Kind);
    }

    /// <summary>
    /// The curves that reach further than a bar, each on the line it is drawn on.
    ///
    /// <para>
    /// A curve that crosses a break is two curves — out to the right edge, and in from the left of the
    /// next — which is what an engraver does. That the two halves are one thing is a fact about what was
    /// written, and the parse tree is where that fact lives; on the page they are two arcs on two lines,
    /// and the layout is a picture of the page.
    /// </para>
    /// </summary>
    private void Hanging(System system)
    {
        foreach (var curve in _curves)
        {
            if (curve.In is not null) continue;

            var above = StemsDown(Halves(curve.From, curve.FromSystem))
                        || StemsDown(Halves(curve.To, curve.ToSystem));

            var start = new Point(curve.From.X + (_noteHead / 2), Springs(curve.From, curve.FromSystem, above));
            var end = new Point(curve.To.X + (_noteHead / 2), Springs(curve.To, curve.ToSystem, above));

            if (ReferenceEquals(curve.FromSystem, curve.ToSystem))
            {
                if (!ReferenceEquals(curve.FromSystem, system)) continue;

                Open(curve.Kind + "-set", at: start, isInk: false);
                Arc(start, end, above, curve.Kind);
                Close();
                continue;
            }

            if (ReferenceEquals(curve.FromSystem, system))
            {
                Open(curve.Kind + "-set", at: start, isInk: false);
                Arc(start, new Point(system.Right - (0.5 * S), start.Y), above, curve.Kind);
                Close();
            }
            else if (ReferenceEquals(curve.ToSystem, system))
            {
                Open(curve.Kind + "-set", at: end, isInk: false);
                Arc(new Point(system.HeadWidth - (0.5 * S), end.Y), end, above, curve.Kind);
                Close();
            }
        }
    }

    /// <summary>Where a curve leaves a note: clear of the head, on the side away from the stems.</summary>
    private static double Springs(Event ev, System system, bool above)
    {
        var halves = Halves(ev, system);
        return above ? Y(system, halves.Max()) - CurveClear : Y(system, halves.Min()) + CurveClear;
    }

    private static List<int> Halves(Event ev, System system)
    {
        var geometry = StaffGeometry.For(system.Row.Clef);
        return ev.Heads.Length > 0
            ? [.. ev.Heads.Select(geometry.HalfSpacesAbove)]
            : [Engraving.MiddleLine];
    }

    /// <summary>
    /// The crescent itself: two Béziers with the same ends, bowing by different amounts, so it is thin
    /// where it meets a head and thickest in the middle. A stroked arc of even thickness reads as a
    /// drawing of a slur rather than as one.
    ///
    /// <para>
    /// <strong>It carries no part.</strong> Nobody typed it — the <c>-</c> and the <c>(</c> that asked for
    /// it are marks on the notes either side, and the arc between them is how those are drawn. Giving it
    /// one looked tempting and is wrong twice over: it would offer itself as a caret stop spanning both
    /// notes, and being shallower than either it would win, so the caret would take the whole arc's height
    /// wherever a tie began.
    /// </para>
    /// </summary>
    private void Arc(Point from, Point to, bool above, string kind)
    {
        var span = Math.Abs(to.X - from.X);
        if (span < 1) return;

        Open(kind, at: new Point(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y)), isInk: false);

        var one = In(from);
        var other = In(to);

        var rise = Math.Clamp(CurveRise + (span * 0.06), CurveRise, CurveMaxRise) * (above ? -1 : 1);
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
    /// Declares what selection may step through: each layer along its own length, and everything sounding
    /// at one moment as a stack.
    ///
    /// <para>
    /// <strong>The layers run the length of the tune, not of a system.</strong> A verse carries on across
    /// a line break — that is what a verse <em>is</em> — and so does the music. Stopping a run at the edge
    /// of a system would be describing the page rather than the piece.
    /// </para>
    /// <para>
    /// The stack is what sounds together: the chord named over a note, the note, and the syllables sung on
    /// it, top to bottom as they are drawn. Between them the two orderings say everything a reader means
    /// by dragging — along a verse, down through the parts at a moment, or a block of both.
    /// </para>
    /// <para>
    /// It is also, deliberately, the structure an animation would need. Notes appearing as they are
    /// played is the note layer in order; notes then words then chords is the layers in turn; a bar at a
    /// time with all three is the bars, each with its stacks. All three are walks over what is declared
    /// here, so none of them would need this rebuilt.
    /// </para>
    /// </summary>
    private void Order()
    {
        // Along each layer, in the order they were engraved — which is the order they are read.
        //
        // A run is the layout's own order with the lines ignored, and that is the whole of what declaring
        // one adds. Containment already puts the notes of a bar in order and the bars of a line in order,
        // and stepping off the end of one falls into the next by itself. What containment cannot say is
        // that the last note of a line and the first of the next are neighbours — a line break is a fact
        // about the paper rather than about the tune. The chords and the verses are that same order with
        // everything that is not a chord, or not a syllable of that verse, left out.
        _build.Runs(_sections, vertical: false);
        _build.Runs([.. _heads.Select(h => h.At)], vertical: false);
        _build.Runs([.. _chords.Select(c => c.At)], vertical: false);

        foreach (var verse in _sung.GroupBy(s => s.Verse).OrderBy(g => g.Key))
            _build.Runs([.. verse.Select(v => v.At)], vertical: false);

        // …and down through everything that sounds at one moment.
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
}
