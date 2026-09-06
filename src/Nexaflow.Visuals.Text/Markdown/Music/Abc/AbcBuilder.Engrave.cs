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
        public bool HasVoltaRow;

        /// <summary>How many rows of marks stack above the notation, and how many below.</summary>
        public int MarksAbove;
        public int TextBelow;

        /// <summary>The piece of layout this system was drawn into, for the curves drawn over it after.</summary>
        public AbcLayoutNode? Node;

        /// <summary>
        /// Which bracketed system this staff belongs to. Staves sharing one are simultaneous: they get one
        /// bar grid, a bracket down the left, and bar lines running through them.
        /// </summary>
        public int Bracket;

        public bool FirstOfBracket;
        public bool LastOfBracket;

        /// <summary>Whether this staff prints its voice's name at the left — the first system does.</summary>
        public bool ShowName;
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

        // A bracket joins staves that are already placed, and a curve may run from one system to the
        // next — neither can be drawn until every staff has landed.
        Brackets(root, systems);
        Curves(systems);

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

        // Grace notes are crushed in before the head, in room of their own: they take no time and still
        // take space, which is the one place notation and arithmetic disagree.
        ev.GraceWidth = ev.Graces.Count == 0
            ? 0
            : GraceGap + (ev.Graces.Count * ((_noteHead * GraceScale) + GraceStep));

        var natural = SlotBase + (SlotRate * Math.Sqrt(Math.Max(ev.Quarters, 0.03125)));
        var floor = _noteHead + SlotFloor;
        var dots = ev.Dots > 0 ? DotGap + (ev.Dots * (Smufl.Advance(Smufl.AugmentationDot, S) + DotSpacing)) : 0;

        ev.SlotWidth = Math.Max(natural, floor) + ev.AccidentalWidth + ev.GraceWidth + dots;

        // Text put beside a note rather than over it has to be paid for in the line, or it prints on top
        // of the next note along.
        foreach (var (text, where) in ev.Annotations)
        {
            if (where is not (AnnotationPlacement.Left or AnnotationPlacement.Right)) continue;
            ev.SlotWidth += ScoreText.Width(text, ChordSize, _ppd) + (0.4 * S);
        }

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
        // A voice's name is printed to the left of its clef, so it has to be paid for before the clef is
        // placed — otherwise every staff in a part song starts at a different x and the grid is gone.
        var named = row is { Index: 0, Name: { Length: > 0 } name }
            ? ScoreText.Width(name, CreditSize, _ppd) + (0.6 * S)
            : 0;

        var width = LeftMargin + named + Smufl.Advance(StaffGeometry.For(row.Clef).ClefGlyph, S) + (0.6 * S);
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
        var bracket = 0;

        // The parts that sound together, in the order they were written. ABC writes one voice's line after
        // another and leaves the reader to count, so this is the counting.
        foreach (var together in rows.GroupBy(r => (r.Voice.Length == 0 ? "" : "v", r.Index))
                                     .OrderBy(g => rows.IndexOf(g.First())))
        {
            var parts = together.ToList();
            foreach (var row in parts)
                foreach (var bar in row.Bars) Measure(bar, row);

            var shared = Share(parts);
            var head = parts.Max(HeadWidth);
            var from = systems.Count;

            foreach (var row in parts) Break(systems, row, head, available, bracket);

            // Only what shares a bar grid is bracketed. A bracket drawn over voices that disagree about
            // where the bars are would run a line through music that is not simultaneous.
            if (shared) Bracketed(systems, from, parts.Count);
            bracket++;
        }

        return systems;
    }

    /// <summary>
    /// One bar grid for the whole bracket: bar <em>n</em> is as wide as the widest voice's bar
    /// <em>n</em>, so the lines run straight down and a reader can read across the parts.
    /// <para>
    /// Only where the voices agree about where the bars are. Where they do not — which real tunebooks do,
    /// and which is nobody's mistake — they are left at their own widths and stack honestly rather than
    /// being forced into a grid that would misalign every bar after the first difference.
    /// </para>
    /// </summary>
    private static bool Share(List<Row> parts)
    {
        if (parts.Count < 2) return false;

        var bars = parts[0].Bars.Count;
        if (bars == 0 || parts.Any(p => p.Bars.Count != bars)) return false;

        for (var at = 0; at < bars; at++)
        {
            var widest = parts.Max(p => p.Bars[at].Width);
            foreach (var part in parts) part.Bars[at].Width = widest;
        }

        return true;
    }

    /// <summary>Marks the run of staves just laid out as one bracketed system.</summary>
    private static void Bracketed(List<System> systems, int from, int voices)
    {
        // Only where every voice broke the same way. A bracket that joined staves holding different bars
        // would draw a line through music that is not simultaneous.
        var made = systems.Count - from;
        if (voices < 2 || made % voices != 0) return;

        var lines = made / voices;

        for (var line = 0; line < lines; line++)
            for (var voice = 0; voice < voices; voice++)
            {
                var at = from + (voice * lines) + line;
                systems[at].Bracket = line + 1;
                systems[at].FirstOfBracket = voice == 0;
                systems[at].LastOfBracket = voice == voices - 1;
            }

        // Staves that sound together have to be drawn together, which means ordering them line by line
        // rather than voice by voice.
        var reordered = new List<System>(made);
        for (var line = 0; line < lines; line++)
            for (var voice = 0; voice < voices; voice++)
                reordered.Add(systems[from + (voice * lines) + line]);

        for (var i = 0; i < made; i++) systems[from + i] = reordered[i];
    }

    /// <summary>One voice's line, broken into as many systems as the page needs.</summary>
    private void Break(List<System> systems, Row row, double head, double available, int bracket)
    {
        {
            var total = row.Bars.Sum(b => b.Width);
            var room = Math.Max(available - head, 4 * S);

            var lines = Math.Max(1, (int)Math.Ceiling(total / room));
            var each = total / lines;

            var current = new System { Row = row, HeadWidth = head, ShowName = row.Index == 0 };
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
            _ = bracket;
        }
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
                    // Left to right within the slot: grace notes, the accidental, then the head.
                    ev.X = x + ev.GraceWidth + ev.AccidentalWidth;
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
                system.HasChordRow |= ev.ChordSymbol is not null
                                      || ev.Annotations.Any(a => a.Where == AnnotationPlacement.Above);
                system.MarksAbove = Math.Max(system.MarksAbove, ev.StaffMarks.Count);
                system.TextBelow = Math.Max(system.TextBelow,
                                            ev.Annotations.Count(a => a.Where == AnnotationPlacement.Below));

                foreach (var (half, _) in ev.Graces)
                {
                    var at = StaffGeometry.For(system.Row.Clef).HalfSpacesAbove(half);
                    highest = Math.Max(highest, at);
                    lowest = Math.Min(lowest, at);
                }
            }

            system.HasVoltaRow = system.Bars.Any(b => b.Volta is not null);

            // A stem reaches about three and a half spaces past the head it is on, whichever way it points.
            system.Above = Math.Max(0, (highest - 8) * (S / 2)) + StemLen;
            system.Below = Math.Max(0, -lowest * (S / 2)) + StemLen;

            // Everything outside the staff is measured from the notation rather than from a fixed pad: a
            // chord symbol belongs above the music, and how high that is depends on how high the music
            // went. Stacked in the order they are read outward from the staff.
            system.Above += system.MarksAbove * MarkRow;
            if (system.HasChordRow) system.Above += ChordRow;
            if (system.HasVoltaRow) system.Above += VoltaRow;

            system.Below += system.TextBelow * ChordRow;

            system.StaffTop = y + system.Above;

            // A staff that sounds with the next one is set close to it; a new system gets a full gap. The
            // difference is what tells a reader whether two lines are played together or one after another,
            // and it is the only thing that does.
            var gap = system.Bracket > 0 && !system.LastOfBracket ? StaffGap : SystemGap;
            y = system.StaffTop + StaffHeight + system.Below + (system.LyricVerses * LyricRow) + gap;
        }
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    private void Draw(AbcLayoutNode root, System system)
    {
        var geometry = StaffGeometry.For(system.Row.Clef);
        var node = root.Adding(new AbcLayoutNode(Rect.Empty, "system"));
        system.Node = node;

        Staff(node, system);
        Head(node, system, geometry);

        foreach (var bar in system.Bars) Draw(node, system, geometry, bar);

        Voltas(node, system);
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

        if (system.ShowName && system.Row.Name is { Length: > 0 } name)
        {
            var glyphs = ScoreText.Build(name, CreditSize, _ppd);
            var at = new Point(x, system.StaffTop + (StaffHeight / 2) - (glyphs.Height / 2));

            var node = into.Adding(new AbcLayoutNode(new Rect(at, new Size(glyphs.Width, glyphs.Height)), "voice"));
            node.Drew(new TextMark(glyphs, at, null));
            x += glyphs.Width + (0.6 * S);
        }

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

                // Whether the group beams is decided by what is IN it, not by what happens to start it.
                // `c2ec` is written as one run and beams its two eighths under the quarter that opens it;
                // asking the first event whether it could be beamed left those two wearing flags.
                if (beam is not null)
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

        Graces(node, system, geometry, ev);
        Dots(node, system, ev.X + _noteHead, geometry.HalfSpacesAbove(ev.Heads[^1]), ev.Dots);
        Marks(node, system, geometry, ev);
        ChordSymbol(node, system, ev);
        Annotations(node, system, ev);
        Lyrics(node, system, ev);
        return node;
    }

    /// <summary>
    /// Grace notes: cue-size heads crushed in before the main one, beamed where there are several and
    /// slashed for an acciaccatura.
    /// <para>
    /// They belong to the note they precede — which is why they hang off its piece of layout rather than
    /// standing beside it. Selecting the note takes them with it, which is what a reader means.
    /// </para>
    /// </summary>
    private void Graces(AbcLayoutNode node, System system, StaffGeometry geometry, Event ev)
    {
        if (ev.Graces.Count == 0) return;

        var width = _noteHead * GraceScale;
        var step = width + GraceStep;
        var x = ev.X - ev.AccidentalWidth - ev.GraceWidth + GraceGap;

        double? firstStem = null;
        double? lastStem = null;
        var top = double.MaxValue;

        foreach (var (pitch, _) in ev.Graces)
        {
            var half = geometry.HalfSpacesAbove(pitch);
            var y = Y(system, half);

            Mark(node, Smufl.NoteheadBlack, new Point(x, y), GraceScale);
            for (var line = 10; line <= half; line += 2) Ledger(node, system, x, line, width);
            for (var line = -2; line >= half; line -= 2) Ledger(node, system, x, line, width);

            // A grace note always stems up, whatever it sits on: the group is read as an ornament of the
            // note after it rather than as music of its own, and a run of them reads as one gesture.
            var stemTop = y - (StemLen * GraceScale);
            var stemX = x + width - (StemThick / 2);
            Rule(node, stemX - (StemThick / 2), stemTop, StemThick, y - stemTop);

            firstStem ??= stemX;
            lastStem = stemX;
            top = Math.Min(top, stemTop);

            x += step;
        }

        if (ev.Graces.Count > 1 && firstStem is { } from && lastStem is { } to)
            Rule(node, from, top, to - from, BeamThick * GraceScale);

        if (!ev.GraceSlashed || firstStem is not { } slashAt) return;

        // The slash of an acciaccatura, through the stem of the first grace note.
        var slash = new LineGeometry(new Point(slashAt - (0.5 * S), top + (0.9 * S)),
                                     new Point(slashAt + (0.5 * S), top - (0.2 * S)));
        slash.Freeze();
        node.Drew(new GeometryMark(slash, null, null, StemThick * 1.6));
        node.Covering(slash.Bounds);
    }

    /// <summary>
    /// The marks on an event: the ones that hug the head, and the ones that stack clear of the staff.
    /// <para>
    /// Two rules rather than one, because they answer different questions. A staccato dot means <em>this
    /// note</em> and so sits against it, on the side the stem is not; a fermata means <em>this moment</em>
    /// and so sits above the staff whatever the note is doing, where the eye reads it along the line.
    /// </para>
    /// </summary>
    private void Marks(AbcLayoutNode node, System system, StaffGeometry geometry, Event ev)
    {
        if (ev.HeadMarks.Count == 0 && ev.StaffMarks.Count == 0) return;

        var halves = ev.Heads.Length > 0
            ? ev.Heads.Select(geometry.HalfSpacesAbove).ToList()
            : [Engraving.MiddleLine];

        var down = StemsDown(halves);
        var at = down ? halves.Max() + 2 : halves.Min() - 2;

        foreach (var glyph in ev.HeadMarks)
        {
            var ink = Smufl.Ink(glyph, S, MarkScale);
            Mark(node, glyph,
                 new Point(ev.X + ((_noteHead - Smufl.Advance(glyph, S, MarkScale)) / 2), Y(system, at)),
                 MarkScale);

            at += down ? 2 : -2;
            _ = ink;
        }

        // Above the staff and above whatever the notation already reached, so a run of high notes pushes
        // its fermatas up with it rather than colliding.
        var y = system.StaffTop - system.Above + (system.HasVoltaRow ? VoltaRow : 0)
                + (system.HasChordRow ? ChordRow : 0) + MarkRow;

        foreach (var glyph in ev.StaffMarks)
        {
            Mark(node, glyph,
                 new Point(ev.X + ((_noteHead - Smufl.Advance(glyph, S, MarkScale)) / 2), y),
                 MarkScale);

            y += MarkRow;
        }
    }

    /// <summary>Text put where the quotes said to put it, rather than over the note like a chord name.</summary>
    private void Annotations(AbcLayoutNode node, System system, Event ev)
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

            node.Drew(new TextMark(glyphs, at, null));
            node.Covering(new Rect(at, new Size(glyphs.Width, glyphs.Height)));
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

    private void Ledger(AbcLayoutNode node, System system, double x, int half, double? head = null)
    {
        var width = head ?? _noteHead;
        Rule(node, x - LedgerExt, Y(system, half) - (LedgerThick / 2), width + (2 * LedgerExt), LedgerThick);
    }

    /// <summary>A filled rectangle recorded on a piece — a staff line, a stem, a beam, a ledger.</summary>
    private static void Rule(AbcLayoutNode node, double x, double y, double width, double height)
    {
        var bounds = new Rect(x, y, Math.Max(width, 0), Math.Max(height, 0));
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
    /// is not simply wrong. It goes on the side the stems point, clear of the beam — which is where a
    /// reader looks for it, and is the opposite of where a mark on a head goes.
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
                    ? Y(system, halves.Min()) + StemLen
                    : Y(system, halves.Max()) - StemLen - glyphs.Height;

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

    /// <summary>Whether a bar line ends the music rather than merely dividing it.</summary>
    private static bool Closes(ContentPart? line) =>
        line?.Node.Print() is { } written && (written.Contains(']') || written == "||");

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
    private static void Brackets(AbcLayoutNode root, List<System> systems)
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
            var node = root.Adding(new AbcLayoutNode(Rect.Empty, "bracket"));

            // Clamped to the left edge rather than placed a bracket's width outside it: the margin is two
            // pixels, so a bracket drawn where it belongs is half off the page.
            var x = Math.Max(0, LeftMargin - BracketWidth);
            Rule(node, x, top, BracketWidth, bottom - top);

            // A short hook at each end, which is what tells the eye the line is a bracket and not the
            // start of a bar.
            Rule(node, x, top, BracketWidth * 2, StaffLineThick * 2);
            Rule(node, x, bottom - (StaffLineThick * 2), BracketWidth * 2, StaffLineThick * 2);

            Through(node, systems, at, last);
        }
    }

    /// <summary>The bar lines continued down the gaps between the staves of one bracketed system.</summary>
    private static void Through(AbcLayoutNode node, List<System> systems, int from, int to)
    {
        foreach (var bar in systems[from].Bars)
        {
            if (bar.Closed is null) continue;
            var x = bar.X + bar.Width - BarlineWidth(bar.Closed) + (0.25 * S);

            for (var at = from; at < to; at++)
                Rule(node,
                     x,
                     systems[at].StaffTop + StaffHeight,
                     ThinBarline,
                     systems[at + 1].StaffTop - (systems[at].StaffTop + StaffHeight));
        }
    }

    // ── Curves ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ties and slurs, drawn last because a curve can run from one system to the next and neither end can
    /// be placed until both systems are.
    ///
    /// <para>
    /// A curve that crosses a break is drawn as two pieces — out to the right edge, and in from the left —
    /// which is what an engraver does and what the layout model already allows: two pieces may share one
    /// part, as a fraction's box and its bar do.
    /// </para>
    /// <para>
    /// <strong>A curve carries no part.</strong> Nobody typed it — the <c>-</c> and the <c>(</c> that
    /// asked for it are marks on the notes either side, and the arc between them is how those are drawn.
    /// Giving it one looked tempting and is wrong twice over: it would offer itself as a caret stop
    /// spanning both notes, and being a shallower node than either it would win, so the caret would take
    /// the whole arc's height wherever a tie began.
    /// </para>
    /// </summary>
    private void Curves(List<System> systems)
    {
        var placed = new List<(Event Event, System System)>();
        foreach (var system in systems)
            foreach (var ev in system.Bars.SelectMany(b => b.Events))
                placed.Add((ev, system));

        Ties(placed);
        Slurs(placed);
    }

    private void Ties(List<(Event Event, System System)> placed)
    {
        for (var at = 0; at < placed.Count - 1; at++)
        {
            if (!placed[at].Event.TieStart) continue;
            Curve(placed[at], placed[at + 1], "tie");
        }
    }

    /// <summary>
    /// Slurs, matched with a stack because ABC allows them to nest: <c>((AA)A)</c> is two slurs, and the
    /// one that closes first is the one that opened last.
    /// </summary>
    private void Slurs(List<(Event Event, System System)> placed)
    {
        var open = new Stack<int>();

        for (var at = 0; at < placed.Count; at++)
        {
            for (var i = 0; i < placed[at].Event.SlurOpen; i++) open.Push(at);

            for (var i = 0; i < placed[at].Event.SlurClose && open.Count > 0; i++)
            {
                var from = open.Pop();
                if (from == at) continue;
                Curve(placed[from], placed[at], "slur");
            }
        }
    }

    /// <summary>
    /// One curve from one event to another, in as many pieces as there are systems between them.
    /// </summary>
    private void Curve((Event Event, System System) from, (Event Event, System System) to, string kind)
    {
        if (from.System.Node is null || to.System.Node is null) return;

        var above = !StemsDown(Halves(from.Event, from.System)) || StemsDown(Halves(to.Event, to.System));

        var start = new Point(from.Event.X + (_noteHead / 2), Springs(from, above));
        var end = new Point(to.Event.X + (_noteHead / 2), Springs(to, above));

        if (ReferenceEquals(from.System, to.System))
        {
            Draw(from.System.Node, start, end, above, kind);
            return;
        }

        // Broken over a system end: out to the right margin, and in from the left of the next.
        Draw(from.System.Node, start, new Point(from.System.Right - (0.5 * S), start.Y), above, kind);
        Draw(to.System.Node, new Point(to.System.HeadWidth - (0.5 * S), end.Y), end, above, kind);
    }

    /// <summary>Where a curve leaves a note: clear of the head, on the side away from the stems.</summary>
    private double Springs((Event Event, System System) at, bool above)
    {
        var halves = Halves(at.Event, at.System);
        return above
            ? Y(at.System, halves.Max()) - CurveClear
            : Y(at.System, halves.Min()) + CurveClear;
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
    /// </summary>
    private static void Draw(AbcLayoutNode into, Point from, Point to, bool above, string kind)
    {
        var span = Math.Abs(to.X - from.X);
        if (span < 1) return;

        var rise = Math.Clamp(CurveRise + (span * 0.06), CurveRise, CurveMaxRise) * (above ? -1 : 1);
        var middle = new Point((from.X + to.X) / 2, ((from.Y + to.Y) / 2) + rise);
        var inner = new Point(middle.X, middle.Y - (CurveThick * (above ? -1 : 1)));

        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = from, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new QuadraticBezierSegment(middle, to, false));
        figure.Segments.Add(new QuadraticBezierSegment(inner, from, false));
        path.Figures.Add(figure);
        path.Freeze();

        var node = into.Adding(new AbcLayoutNode(path.Bounds, kind));
        node.Drew(GeometryMark.Filled(path));
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
    private void Voltas(AbcLayoutNode into, System system)
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

            var node = into.Adding(new AbcLayoutNode(Rect.Empty, "volta", part));

            Rule(node, left, y, right - left, StaffLineThick * 1.6);
            Rule(node, left, y, StaffLineThick * 1.6, VoltaTick);

            // Closed on the right where what it covers stops — a repeat end, or a line that ends the
            // music. Left open otherwise, which is how a bracket says it carries on into the next line.
            if (system.Bars[last].EndsRepeat || Closes(system.Bars[last].Closed))
                Rule(node, right - (StaffLineThick * 1.6), y, StaffLineThick * 1.6, VoltaTick);

            if (system.Bars[at].VoltaLabel is not { Length: > 0 } label) continue;

            var glyphs = ScoreText.Build(label + ".", VoltaSize, _ppd);
            var where = new Point(left + (0.45 * S), y + (0.28 * S));
            node.Drew(new TextMark(glyphs, where, null));
            node.Covering(new Rect(where, new Size(glyphs.Width, glyphs.Height)));
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
