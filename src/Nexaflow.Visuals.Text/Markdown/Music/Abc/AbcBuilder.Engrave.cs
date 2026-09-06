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
/// The half of the engraver that knows about a page: how much room each event takes, where the systems
/// break, how they are justified, and where every glyph lands.
/// </summary>
internal sealed partial class AbcBuilder
{
    /// <summary>A run of bars set on one line, with the room its notation actually needs above and below.</summary>
    private sealed class System
    {
        public required Row Row;
        public List<Bar> Bars = [];
        public double StaffTop;
        public double Above;         // how far the notation reaches above the top line
        public double Below;         // …and below the bottom one
        public double HeadWidth;     // clef, key signature and meter at the left
        public double Right;
        public int LyricVerses;
        public bool HasChordRow;
    }

    private double _noteHead;

    // ── The whole of it ─────────────────────────────────────────────────────

    private (AbcLayoutNode Root, Size Size) Engrave(double width)
    {
        _noteHead = Smufl.Advance(Smufl.NoteheadBlack, S);
        if (_noteHead <= 0) _noteHead = 1.18 * S;

        var rows = Read();
        var systems = Wrap(rows, Math.Max(width, 12 * S));

        var root = new AbcLayoutNode(Rect.Empty, "score");
        if (systems.Count == 0) return (root, new Size(0, 0));

        Justify(systems, width);
        Stack(systems);

        foreach (var system in systems) Draw(root, system);

        var extent = Extent(root);
        return (root, new Size(Math.Ceiling(extent.Right + RightMargin), Math.Ceiling(extent.Bottom + S)));
    }

    // ── How much room everything takes ──────────────────────────────────────

    /// <summary>
    /// The horizontal room an event wants: the classical proportional-but-compressed curve, so a whole note
    /// is about three times an eighth rather than eight times it, with a floor of a note head plus air so a
    /// septuplet's heads cannot touch. An accidental and any dots are extra, because they are drawn beside
    /// the head rather than instead of it.
    /// </summary>
    private void Measure(Event ev)
    {
        ev.AccidentalWidth = ev.Accidentals.Any(a => a is not null)
            ? Smufl.Advance(Glyph(ev.Accidentals.First(a => a is not null)!.Value), S) + AccGap
            : 0;

        var natural = SlotBase + (SlotRate * Math.Sqrt(Math.Max(ev.Quarters, 0.03125)));
        var floor = _noteHead + SlotFloor;
        var dots = ev.Dots > 0 ? DotGap + (ev.Dots * (Smufl.Advance(Smufl.AugmentationDot, S) + DotSpacing)) : 0;

        ev.SlotWidth = Math.Max(natural, floor) + ev.AccidentalWidth + dots;

        // A syllable is centred under its head and so charges the note only half of itself — the other half
        // is its neighbour's problem. Charging the full width made a line of long and short words lurch.
        foreach (var (_, text, _, _) in ev.Lyrics)
        {
            if (text.Length == 0) continue;
            var wanted = (ScoreText.Width(text, LyricSize, _ppd) / 2) + LyricGap;
            ev.SlotWidth = Math.Max(ev.SlotWidth, wanted + (_noteHead / 2));
        }
    }

    /// <summary>What a bar wants: its events, whatever signature is printed at its head, and its bar line.</summary>
    private void Measure(Bar bar, Row row)
    {
        foreach (var ev in bar.Events) Measure(ev);

        bar.SignatureWidth = OpeningWidth(bar.Opened);
        if (bar.KeyChange is { } key) bar.SignatureWidth += KeyWidth(key, row.Clef) + (0.5 * S);
        if (bar.MeterChange is not null) bar.SignatureWidth += MeterWidth() + (0.5 * S);

        bar.Width = bar.SignatureWidth + bar.Events.Sum(e => e.SlotWidth) + BarlineWidth(bar.Closed);
    }

    private static double BarlineWidth(ContentPart? line) =>
        line is null ? 0 : Math.Max(0.5 * S, (line.Node.Print().Length * 0.28 * S) + (0.35 * S));

    /// <summary>The room an opening bar line takes at the head of a bar — a repeat start, usually.</summary>
    private static double OpeningWidth(ContentPart? line) => BarlineWidth(line);

    private double KeyWidth(KeySignature key, ClefKind clef) =>
        Math.Abs(key.Fifths) == 0
            ? 0
            : Math.Min(Math.Abs(key.Fifths), 7)
              * (Smufl.Advance(key.Fifths > 0 ? Smufl.AccidentalSharp : Smufl.AccidentalFlat, S) + (0.08 * S));

    private double MeterWidth() => 2.2 * S;

    /// <summary>The room the clef, key signature and meter take at the head of a system.</summary>
    private double HeadWidth(Row row)
    {
        var width = LeftMargin + Smufl.Advance(StaffGeometry.For(row.Clef).ClefGlyph, S) + (0.6 * S);
        width += KeyWidth(KeySignature.FromFifths(row.Context.Fifths), row.Clef);
        if (width > 0) width += 0.4 * S;
        return width + MeterWidth() + (0.6 * S);
    }

    // ── Breaking into systems ───────────────────────────────────────────────

    /// <summary>
    /// The bars packed into systems. A source line break is honoured first — it is what ABC means by one —
    /// and a line too wide for the page is broken again at a bar line rather than being squeezed.
    ///
    /// <para>
    /// <strong>Balanced rather than greedy, and that is not a refinement.</strong> Filling each system to
    /// the brim and letting the remainder fall onto the next leaves a line holding one bar, and one bar
    /// cannot be justified to the width of four without spacing its notes absurdly — so it is left short,
    /// and the block gains a ragged edge in the middle of itself. Working out how many lines the row needs
    /// and dividing it evenly gives every system about the same amount to hold, which is what makes the
    /// justification below able to fill them all.
    /// </para>
    /// </summary>
    private List<System> Wrap(List<Row> rows, double width)
    {
        var systems = new List<System>();
        var available = width - RightMargin;

        foreach (var row in rows)
        {
            foreach (var bar in row.Bars) Measure(bar, row);

            var head = HeadWidth(row);
            var total = row.Bars.Sum(b => b.Width);
            var room = Math.Max(available - head, 4 * S);

            var lines = Math.Max(1, (int)Math.Ceiling(total / room));
            var each = total / lines;

            var current = new System { Row = row, HeadWidth = head };
            var used = 0.0;

            foreach (var bar in row.Bars)
            {
                // Start a new line once this one holds its share — unless it holds nothing yet, or there
                // are only as many bars left as there are lines left to fill.
                var left = row.Bars.Count - row.Bars.IndexOf(bar);
                var linesLeft = lines - systems.Count(s => ReferenceEquals(s.Row, row));

                if (current.Bars.Count > 0 && used + (bar.Width / 2) > each && left > linesLeft - 1)
                {
                    systems.Add(current);
                    current = new System { Row = row, HeadWidth = head };
                    used = 0;
                }

                current.Bars.Add(bar);
                used += bar.Width;
            }

            if (current.Bars.Count > 0) systems.Add(current);
        }

        return systems;
    }

    /// <summary>
    /// Every system but a short final one fills the same width, so the block has a straight right edge.
    /// <para>
    /// The last one is <em>not</em> stretched to match — that would space a two-bar tail across the page —
    /// but nor is it left at its natural width, which reads as a mistake: its notes sit visibly tighter
    /// than every line above. It is scaled by the same factor its siblings were, so the note spacing is
    /// continuous and only the right edge is ragged.
    /// </para>
    /// </summary>
    private static void Justify(List<System> systems, double width)
    {
        var target = width - RightMargin;
        var factors = new List<double>();

        for (var i = 0; i < systems.Count; i++)
        {
            var system = systems[i];
            var flexible = system.Bars.Sum(b => b.Events.Sum(e => e.SlotWidth));
            var fixedRoom = system.HeadWidth + system.Bars.Sum(b => b.SignatureWidth + BarlineWidth(b.Closed));
            if (flexible <= 0) continue;

            var last = i == systems.Count - 1;
            var wanted = (target - fixedRoom) / flexible;

            // A short last line takes the average of the lines above rather than a stretch of its own.
            var factor = last && factors.Count > 0
                ? Math.Min(wanted, factors.Average())
                : Math.Clamp(wanted, 0.55, 2.5);

            if (!last) factors.Add(factor);

            foreach (var bar in system.Bars)
                foreach (var ev in bar.Events)
                    ev.SlotWidth *= factor;
        }

        // Place everything now the widths are settled.
        foreach (var system in systems)
        {
            var x = system.HeadWidth;
            foreach (var bar in system.Bars)
            {
                bar.X = x;
                x += bar.SignatureWidth;

                foreach (var ev in bar.Events)
                {
                    ev.X = x + ev.AccidentalWidth;
                    x += ev.SlotWidth;
                }

                x += BarlineWidth(bar.Closed);
                bar.Width = x - bar.X;
            }

            system.Right = x;
        }
    }

    /// <summary>
    /// Stacks the systems down the page, giving each the room its own notation asks for.
    /// <para>
    /// Measured from the notation rather than fixed: how far the ledger heads, stems and beams actually
    /// reach is what decides, and everything that lives outside the staff is placed against that. A chord
    /// symbol belongs above the music, and how high that is depends on how high the music went.
    /// </para>
    /// </summary>
    private static void Stack(List<System> systems)
    {
        var y = S;

        foreach (var system in systems)
        {
            var highest = 8;
            var lowest = 0;

            foreach (var ev in system.Bars.SelectMany(b => b.Events))
            {
                foreach (var head in ev.Heads.Select(h => StaffGeometry.For(system.Row.Clef).HalfSpacesAbove(h)))
                {
                    highest = Math.Max(highest, head);
                    lowest = Math.Min(lowest, head);
                }

                system.LyricVerses = Math.Max(system.LyricVerses, ev.Lyrics.Count);
                system.HasChordRow |= ev.ChordSymbol is not null;
            }

            // A stem reaches about three and a half spaces past the head it is on, whichever way it points.
            system.Above = Math.Max(0, (highest - 8) * (S / 2)) + StemLen;
            system.Below = Math.Max(0, -lowest * (S / 2)) + StemLen;

            if (system.HasChordRow) system.Above += ChordRow;

            system.StaffTop = y + system.Above;
            y = system.StaffTop + StaffHeight + system.Below + (system.LyricVerses * LyricRow) + SystemGap;
        }
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    private void Draw(AbcLayoutNode root, System system)
    {
        var geometry = StaffGeometry.For(system.Row.Clef);
        var node = root.Adding(new AbcLayoutNode(Rect.Empty, "system"));

        Staff(node, system);
        Head(node, system, geometry);

        foreach (var bar in system.Bars) Draw(node, system, geometry, bar);

        node.Covering(Extent(node));
    }

    /// <summary>The five lines. Nobody wrote them, so they carry no part and cannot be selected.</summary>
    private void Staff(AbcLayoutNode into, System system)
    {
        for (var line = 0; line < 5; line++)
        {
            var y = system.StaffTop + (line * S);
            var bounds = new Rect(LeftMargin, y - (StaffLineThick / 2), system.Right - LeftMargin, StaffLineThick);
            var rule = into.Adding(new AbcLayoutNode(bounds, "staff-line"));
            rule.Drew(new RuleMark(bounds, null));
        }
    }

    /// <summary>The clef, the key signature and the meter, at the head of the system.</summary>
    private void Head(AbcLayoutNode into, System system, StaffGeometry geometry)
    {
        var x = LeftMargin + (0.4 * S);

        var clefY = Y(system, geometry.ClefRefHalfSpaces);
        Glyph(into, "clef", geometry.ClefGlyph, new Point(x, clefY));
        x += Smufl.Advance(geometry.ClefGlyph, S) + (0.6 * S);

        x = DrawKeySignature(into, system, geometry, x, system.Row.Context.Fifths);
        Meter(into, system, x, system.Row.Context.Beats, system.Row.Context.BeatUnit);
    }

    private double DrawKeySignature(AbcLayoutNode into, System system, StaffGeometry geometry, double x, int fifths)
    {
        var count = Math.Min(Math.Abs(fifths), 7);
        if (count == 0) return x;

        var sharp = fifths > 0;
        var glyph = sharp ? Smufl.AccidentalSharp : Smufl.AccidentalFlat;

        for (var i = 0; i < count; i++)
        {
            var half = geometry.HalfSpacesAbove(geometry.KeyAccidentalIndex(i, sharp));
            Glyph(into, "key", glyph, new Point(x, Y(system, half)));
            x += Smufl.Advance(glyph, S) + (0.08 * S);
        }

        return x + (0.4 * S);
    }

    private void Meter(AbcLayoutNode into, System system, double x, int beats, int unit)
    {
        // Figures, one above the other, centred on the third and first spaces. C and ¢ are the source
        // asking for the symbol, and a tune that wrote M:C means the symbol rather than "4/4".
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
                Glyph(into, "meter", glyph, new Point(at, y));
                at += Smufl.Advance(glyph, S);
            }
        }

        double Width(string digits) => digits.Sum(d => Smufl.Advance(Smufl.TimeSig0 + (d - '0'), S));
        static string Digits(int value) => value.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Draw(AbcLayoutNode into, System system, StaffGeometry geometry, Bar bar)
    {
        var node = into.Adding(new AbcLayoutNode(Rect.Empty, "measure", bar.Part));

        var x = bar.X;
        if (bar.Opened is not null) x = Barline(node, system, bar.Opened, x);

        // A mid-tune key or meter change, printed where it takes effect.
        if (bar.KeyChange is { } key) x = DrawKeySignature(node, system, geometry, x, key.Fifths);
        if (bar.MeterChange is { } meter) Meter(node, system, x, meter.Beats, meter.Unit);

        // Beamed runs are nodes of their own, because a beam is what a reader points at first: clicking one
        // note of a beamed pair means the pair. Everything else hangs straight off the bar.
        var drawn = new Dictionary<Event, AbcLayoutNode>();
        var groups = new List<(ContentPart Part, AbcLayoutNode Node, List<Event> Events)>();

        AbcLayoutNode holder = node;
        ContentPart? beam = null;
        List<Event>? run = null;

        foreach (var ev in bar.Events)
        {
            if (!ReferenceEquals(ev.Beam, beam))
            {
                beam = ev.Beam;
                if (beam is not null && ev.Beamable)
                {
                    holder = node.Adding(new AbcLayoutNode(Rect.Empty, "beam", beam));
                    run = [];
                    groups.Add((beam, holder, run));
                }
                else
                {
                    holder = node;
                    run = null;
                }
            }

            drawn[ev] = Draw(holder, system, geometry, ev);
            if (ev.Beamable) run?.Add(ev);
        }

        foreach (var (_, group, events) in groups) Beam(group, system, geometry, events, drawn);

        // Anything not beamed wears its own stem and flag.
        foreach (var ev in bar.Events)
        {
            if (ev.IsRest || ev.BaseValue <= 1) continue;
            if (groups.Any(g => g.Events.Contains(ev))) continue;
            Stem(drawn[ev], system, geometry, ev, flags: true);
        }

        Tuplets(node, system, geometry, bar);
        Barline(node, system, bar);

        foreach (var child in node.Children) node.Covering(child.Bounds);
        if (node.Bounds.IsEmpty) node.Covering(new Rect(bar.X, system.StaffTop, bar.Width, StaffHeight));
    }

    /// <summary>One event: its accidentals, its heads, its ledger lines, its dots, and what is sung on it.</summary>
    private AbcLayoutNode Draw(AbcLayoutNode into, System system, StaffGeometry geometry, Event ev)
    {
        var node = into.Adding(new AbcLayoutNode(Rect.Empty, ev.IsRest ? "rest" : "note", ev.Part));

        if (ev.Invisible)
        {
            node.Covering(new Rect(ev.X, system.StaffTop, Math.Max(ev.SlotWidth, 1), StaffHeight));
            return node;
        }

        if (ev.IsRest)
        {
            var half = ev.BaseValue == 1 ? 6 : 4;
            var glyph = RestGlyph(ev.WholeBar ? 1 : ev.BaseValue);
            var x = ev.WholeBar ? ev.X + ((ev.SlotWidth - Smufl.Advance(glyph, S)) / 2) : ev.X;
            Mark(node, glyph, new Point(x, Y(system, half)));
            Dots(node, system, x + Smufl.Advance(glyph, S), 4, ev.Dots);
            Lyrics(node, system, ev);
            return node;
        }

        var head = HeadGlyph(ev.BaseValue);

        // The accidental goes in the room measured for it, immediately left of the head.
        for (var i = 0; i < ev.Heads.Length; i++)
        {
            if (ev.Accidentals.Length <= i || ev.Accidentals[i] is not { } alter) continue;
            var glyph = Glyph(alter);
            Mark(node, glyph, new Point(ev.X - ev.AccidentalWidth, Y(system, geometry.HalfSpacesAbove(ev.Heads[i]))));
        }

        foreach (var pitch in ev.Heads)
        {
            var half = geometry.HalfSpacesAbove(pitch);
            Mark(node, head, new Point(ev.X, Y(system, half)));
            Ledgers(node, system, ev.X, half);
        }

        Dots(node, system, ev.X + _noteHead, geometry.HalfSpacesAbove(ev.Heads[^1]), ev.Dots);
        ChordSymbol(node, system, ev);
        Lyrics(node, system, ev);
        return node;
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
    private void Stem(AbcLayoutNode node, System system, StaffGeometry geometry, Event ev, bool flags,
                      double? toY = null, bool? stemsDown = null)
    {
        if (ev.IsRest || ev.Heads.Length == 0) return;

        var halves = ev.Heads.Select(geometry.HalfSpacesAbove).ToList();
        var down = stemsDown ?? StemsDown(halves);

        var fromY = Y(system, down ? halves.Max() : halves.Min());
        var endY = toY ?? (down ? Y(system, halves.Min()) + StemLen : Y(system, halves.Max()) - StemLen);
        var x = down ? ev.X + (StemThick / 2) : ev.X + _noteHead - (StemThick / 2);

        var stem = new RectangleGeometry(new Rect(x - (StemThick / 2), Math.Min(fromY, endY),
                                                  StemThick, Math.Abs(endY - fromY)));
        stem.Freeze();
        node.Drew(GeometryMark.Filled(stem));
        node.Covering(stem.Bounds);

        if (!flags || ev.BaseValue < 8) return;

        var flag = FlagGlyph(ev.BaseValue, down);
        if (flag != 0) Mark(node, flag, new Point(x, endY));
    }

    /// <summary>
    /// The beam over a written group, and the stems that reach it.
    /// <para>
    /// One direction for the whole group — the note reaching furthest from the middle line decides — and a
    /// slope that only leans where the group's contour genuinely leans. A run that climbs, falls and climbs
    /// again beams flat, because a first-to-last slope drawn through a zig-zag asserts a direction the music
    /// does not have.
    /// </para>
    /// </summary>
    private void Beam(AbcLayoutNode group, System system, StaffGeometry geometry, List<Event> events,
                      Dictionary<Event, AbcLayoutNode> drawn)
    {
        if (events.Count == 0) return;

        if (events.Count == 1)
        {
            Stem(drawn[events[0]], system, geometry, events[0], flags: true);
            group.Covering(drawn[events[0]].Bounds);
            return;
        }

        var halves = events.SelectMany(e => e.Heads.Select(geometry.HalfSpacesAbove)).ToList();
        var down = StemsDown(halves);

        var xs = new List<double>();
        var outer = new List<double>();

        foreach (var ev in events)
        {
            var own = ev.Heads.Select(geometry.HalfSpacesAbove).ToList();
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

        var beams = events.Max(e => Flags(e.BaseValue));

        for (var level = 0; level < beams; level++)
        {
            var offset = level * (BeamThick + BeamGap) * (down ? -1 : 1);
            var span = SpanOf(events, level);

            foreach (var (from, to) in span)
            {
                var x0 = xs[from];
                var x1 = from == to ? x0 + BeamStub * (from == 0 ? 1 : -1) : xs[to];
                var y0 = reach + (slope * (x0 - xs[0])) + offset;
                var y1 = reach + (slope * (x1 - xs[0])) + offset;

                var bar = new PathGeometry();
                var figure = new PathFigure { StartPoint = new Point(x0, y0), IsClosed = true, IsFilled = true };
                figure.Segments.Add(new LineSegment(new Point(x1, y1), false));
                figure.Segments.Add(new LineSegment(new Point(x1, y1 + BeamThick), false));
                figure.Segments.Add(new LineSegment(new Point(x0, y0 + BeamThick), false));
                bar.Figures.Add(figure);
                bar.Freeze();

                var piece = group.Adding(new AbcLayoutNode(bar.Bounds, "beam-bar"));
                piece.Drew(GeometryMark.Filled(bar));
            }
        }

        for (var i = 0; i < events.Count; i++)
            Stem(drawn[events[i]], system, geometry, events[i], flags: false,
                 toY: reach + (slope * (xs[i] - xs[0])) + (down ? BeamThick : 0), stemsDown: down);

        foreach (var child in group.Children) group.Covering(child.Bounds);
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

    // ── The small pieces ────────────────────────────────────────────────────

    private void Ledgers(AbcLayoutNode node, System system, double x, int half)
    {
        for (var line = 10; line <= half; line += 2) Ledger(node, system, x, line);
        for (var line = -2; line >= half; line -= 2) Ledger(node, system, x, line);
    }

    private void Ledger(AbcLayoutNode node, System system, double x, int half)
    {
        var bounds = new Rect(x - LedgerExt, Y(system, half) - (LedgerThick / 2),
                              _noteHead + (2 * LedgerExt), LedgerThick);
        node.Drew(new RuleMark(bounds, null));
        node.Covering(bounds);
    }

    private void Dots(AbcLayoutNode node, System system, double x, int half, int dots)
    {
        // A dot never sits on a line: a note on one takes its dots in the space above.
        var at = half % 2 == 0 ? half + 1 : half;
        var cursor = x + DotGap;

        for (var i = 0; i < dots; i++)
        {
            Mark(node, Smufl.AugmentationDot, new Point(cursor, Y(system, at)));
            cursor += Smufl.Advance(Smufl.AugmentationDot, S) + DotSpacing;
        }
    }

    private void ChordSymbol(AbcLayoutNode node, System system, Event ev)
    {
        if (ev.ChordSymbol is not { Length: > 0 } text) return;

        var glyphs = ScoreText.Build(text, ChordSize, _ppd);
        var at = new Point(ev.X, system.StaffTop - system.Above);
        node.Drew(new TextMark(glyphs, at, null));
        node.Covering(new Rect(at, new Size(glyphs.Width, glyphs.Height)));
    }

    private void Lyrics(AbcLayoutNode node, System system, Event ev)
    {
        foreach (var (verse, text, hyphen, _) in ev.Lyrics)
        {
            if (text.Length == 0) continue;

            var glyphs = ScoreText.Build(hyphen ? text + "-" : text, LyricSize, _ppd);
            var at = new Point(ev.X + (_noteHead / 2) - (glyphs.Width / 2),
                               system.StaffTop + StaffHeight + system.Below + (verse * LyricRow));

            node.Drew(new TextMark(glyphs, at, null));
            node.Covering(new Rect(at, new Size(glyphs.Width, glyphs.Height)));
        }
    }

    /// <summary>
    /// The number over a tuplet, centred on the notes it covers.
    /// <para>
    /// Without it a triplet is three eighths that do not add up, and a reader has no way to know the tune
    /// is not simply wrong. It goes on the side the stems are not, so it never collides with a beam.
    /// </para>
    /// </summary>
    private void Tuplets(AbcLayoutNode into, System system, StaffGeometry geometry, Bar bar)
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
                    ? Y(system, halves.Max()) - StemLen - glyphs.Height
                    : Y(system, halves.Min()) + StemLen;

                var node = into.Adding(new AbcLayoutNode(Rect.Empty, "tuplet"));
                var where = new Point(((left + right) / 2) - (glyphs.Width / 2), y);
                node.Drew(new TextMark(glyphs, where, null));
                node.Covering(new Rect(where, new Size(glyphs.Width, glyphs.Height)));
            }

            at = to + 1;
        }
    }

    /// <summary>
    /// The line that closes a bar. It carries a part, because somebody wrote it: a reader can point at one,
    /// and a selection of every note in a bar has to cover it before it can grow into the bar.
    /// </summary>
    private void Barline(AbcLayoutNode into, System system, Bar bar)
    {
        if (bar.Closed is null) return;
        Barline(into, system, bar.Closed, bar.X + bar.Width - BarlineWidth(bar.Closed) + (0.25 * S));
    }

    /// <summary>Draws one bar line at <paramref name="from"/>, and says where it ended.</summary>
    private double Barline(AbcLayoutNode into, System system, ContentPart line, double from)
    {
        var written = line.Node.Print();
        var x = from;
        var top = system.StaffTop;
        var height = StaffHeight;

        var node = into.Adding(new AbcLayoutNode(Rect.Empty, "barline", line));

        foreach (var mark in written)
        {
            switch (mark)
            {
                case '|':
                    Rule(node, x, top, ThinBarline, height);
                    x += ThinBarline + (0.22 * S);
                    continue;

                case '[':
                case ']':
                    Rule(node, x, top, ThickBarline, height);
                    x += ThickBarline + (0.22 * S);
                    continue;

                case ':':
                    // The two dots of a repeat, in the second and third spaces.
                    Dot(node, x, Y(system, 3));
                    Dot(node, x, Y(system, 5));
                    x += (0.5 * S) + (0.22 * S);
                    continue;
            }
        }

        if (node.Bounds.IsEmpty) node.Covering(new Rect(x, top, ThinBarline, height));
        return x;

        void Rule(AbcLayoutNode on, double at, double y, double thickness, double tall)
        {
            var bounds = new Rect(at, y, thickness, tall);
            on.Drew(new RuleMark(bounds, null));
            on.Covering(bounds);
        }

        void Dot(AbcLayoutNode on, double at, double y)
        {
            var size = 0.32 * S;
            var bounds = new Rect(at, y - (size / 2), size, size);
            var dot = new EllipseGeometry(bounds);
            dot.Freeze();
            on.Drew(GeometryMark.Filled(dot));
            on.Covering(bounds);
        }
    }

    // ── Glyphs and geometry ─────────────────────────────────────────────────

    /// <summary>
    /// Which way a stem points: away from the middle line, with the note reaching furthest from it
    /// deciding for the whole group. A note sitting <em>on</em> the middle line stems up — the tie breaks
    /// upward, which is what ABC engravers do.
    /// </summary>
    private static bool StemsDown(IReadOnlyList<int> halves) =>
        halves.Max() - Engraving.MiddleLine > Engraving.MiddleLine - halves.Min();

    /// <summary>Where a half-space above the bottom staff line lands on the page.</summary>
    private static double Y(System system, int half) => system.StaffTop + StaffHeight - (half * (S / 2));

    private void Glyph(AbcLayoutNode into, string kind, int codepoint, Point baseline)
    {
        var node = into.Adding(new AbcLayoutNode(Rect.Empty, kind));
        Mark(node, codepoint, baseline);
    }

    /// <summary>
    /// Records a glyph on a piece. Drawn as a filled outline rather than as text, deliberately: WPF's text
    /// pipeline gamma-corrects glyph coverage and visibly fattens a music font's thin strokes.
    /// </summary>
    private void Mark(AbcLayoutNode node, int codepoint, Point baseline, double scale = 1.0)
    {
        if (Smufl.Outline(codepoint, baseline, S, scale) is not { } outline)
        {
            // No font. A hollow head of about the right size keeps the tune readable rather than blank.
            var fallback = new EllipseGeometry(new Point(baseline.X + (_noteHead / 2), baseline.Y),
                                               _noteHead / 2, S * 0.35);
            fallback.Freeze();
            node.Drew(new GeometryMark(fallback, null, null, 0.12 * S));
            node.Covering(fallback.Bounds);
            return;
        }

        node.Drew(GeometryMark.Filled(outline));
        node.Covering(outline.Bounds);
    }

    private static int HeadGlyph(int value) => value switch
    {
        0 => Smufl.NoteheadDoubleWhole,
        1 => Smufl.NoteheadWhole,
        2 => Smufl.NoteheadHalf,
        _ => Smufl.NoteheadBlack,
    };

    private static int RestGlyph(int value) => value switch
    {
        0 => Smufl.RestDoubleWhole,
        1 => Smufl.RestWhole,
        2 => Smufl.RestHalf,
        4 => Smufl.RestQuarter,
        8 => Smufl.Rest8th,
        16 => Smufl.Rest16th,
        _ => Smufl.Rest32nd,
    };

    private static int FlagGlyph(int value, bool down) => value switch
    {
        8 => down ? Smufl.Flag8thDown : Smufl.Flag8thUp,
        16 => down ? Smufl.Flag16thDown : Smufl.Flag16thUp,
        32 => down ? Smufl.Flag32ndDown : Smufl.Flag32ndUp,
        64 => down ? Smufl.Flag64thDown : Smufl.Flag64thUp,
        _ => 0,
    };

    /// <summary>How many beams or flags a value wears: one for an eighth, two for a sixteenth, and so on.</summary>
    private static int Flags(int value) => value switch
    {
        8 => 1,
        16 => 2,
        32 => 3,
        64 => 4,
        _ => 0,
    };

    private static int Glyph(int alter) => alter switch
    {
        >= 2 => Smufl.AccidentalDoubleSharp,
        1 => Smufl.AccidentalSharp,
        -1 => Smufl.AccidentalFlat,
        <= -2 => Smufl.AccidentalDoubleFlat,
        _ => Smufl.AccidentalNatural,
    };

    /// <summary>How much of the page a piece and everything under it actually covers.</summary>
    private static Rect Extent(ILayoutNode root)
    {
        var union = Rect.Empty;
        foreach (var node in root.SelfAndDescendants())
            if (!node.Bounds.IsEmpty)
                union.Union(node.Bounds);

        return union.IsEmpty ? new Rect(0, 0, 0, 0) : union;
    }
}
