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
/// The half of the engraver that knows about a page: how much room each event takes, where the systems
/// break, how they are justified, and where every glyph lands.
/// </summary>
internal abstract partial class MusicBuilder
{
    /// <summary>A run of bars set on one line, with the room its notation actually needs above and below.</summary>
    private sealed class System
    {
        public required Row Row;
        public List<Bar> Bars = [];
        public double StaffTop;
        public double Above;         // how far the notation reaches above the top line
        public double Below;         // …and below the bottom one

        /// <summary>Where the first line of words sits — under the music, with air between.</summary>
        public double LyricTop;

        /// <summary>
        /// The last event drawn on this line, which is what a held syllable draws its rule back to. Kept
        /// on the system rather than the bar because a word held across a bar line is still one word.
        /// </summary>
        public Event? LastDrawn;
        public double HeadWidth;     // clef, key signature and meter at the left
        public double Right;
        public int LyricVerses;
        public bool HasChordRow;
        public bool HasVoltaRow;

        /// <summary>How many rows of marks stack above the notation, and how many below.</summary>
        public int MarksAbove;
        public int TextBelow;

        /// <summary>The piece of layout this system was drawn into, for the curves drawn over it after.</summary>
        /// <summary>
        /// Which bracketed system this staff belongs to. Staves sharing one are simultaneous: they get one
        /// bar grid, a bracket down the left, and bar lines running through them.
        /// </summary>
        public int Bracket;

        public bool FirstOfBracket;
        public bool LastOfBracket;

        /// <summary>Whether this staff prints its voice's name at the left — the first system does.</summary>
        public bool ShowName;

        // The time signature belongs to the start of the music rather than to the start of a line: an
        // engraver draws it once and then only where it changes, which a bar carries for itself.
        public bool ShowMeter;
    }

    private double _noteHead;

    /// <summary>
    /// What was drawn where, kept while a tune is engraved so the orderings can be declared once it is.
    ///
    /// <para>
    /// Gathered rather than linked as it goes because an ordering is a run: it is only complete when the
    /// last system is, and a lyric's run does not stop at a system's edge. See <see cref="Order"/>.
    /// </para>
    /// </summary>
    // ── The whole of it ─────────────────────────────────────────────────────

    // ── How much room everything takes ──────────────────────────────────────

    /// <summary>
    /// The horizontal room an event wants: the classical proportional-but-compressed curve, so a whole note
    /// is about three times an eighth rather than eight times it, with a floor of a note head plus air so a
    /// septuplet's heads cannot touch. An accidental and any dots are extra, because they are drawn beside
    /// the head rather than instead of it.
    /// </summary>
    private void Measure(Event ev, bool grouped)
    {
        ev.AccidentalWidth = ev.Accidentals.Any(a => a is not null)
            ? Smufl.Advance(Glyph(ev.Accidentals.First(a => a is not null)!.Value), S) + AccGap
            : 0;

        // Grace notes are crushed in before the head, in room of their own: they take no time and still
        // take space, which is the one place notation and arithmetic disagree.
        ev.GraceWidth = ev.Graces.Count == 0
            ? 0
            : GraceGap + (ev.Graces.Count * ((_noteHead * GraceScale) + GraceStep));

        // Tighter to its neighbour when the beam says they are one thing, wider when it does not.
        var space = grouped ? _spacing.GroupBase : _spacing.SlotBase;
        var rate = grouped ? _spacing.GroupRate : _spacing.SlotRate;

        var natural = space + (rate * Math.Sqrt(Math.Max(ev.Quarters, 0.03125)));
        var dots = ev.Dots > 0 ? DotGap + (ev.Dots * (Smufl.Advance(Smufl.AugmentationDot, S) + DotSpacing)) : 0;

        // What must physically fit: the head, and whatever is crushed in beside it.
        var fits = _noteHead + SlotFloor + ev.AccidentalWidth + ev.GraceWidth + dots;

        // <strong>The length decides the spacing.</strong> Two quavers take the same room as each other
        // however they are written — that is what makes a line of them read as even — so an accidental or
        // a grace raises a floor rather than being added on top. Adding it made the note after a sharpened
        // one sit further away than its neighbours for a reason nobody reading the music can see.
        ev.SlotWidth = Math.Max(natural, fits);

        // Text put beside a note rather than over it has to be paid for in the line, or it prints on top
        // of the next note along.
        foreach (var (text, where) in ev.Annotations)
        {
            if (where is not (AnnotationPlacement.Left or AnnotationPlacement.Right)) continue;
            ev.SlotWidth = Math.Max(ev.SlotWidth, fits + ScoreText.Width(text, ChordSize, _ppd) + (0.4 * S));
        }

        // A chord symbol has to clear the next one along, which nothing was paying for: a wide name over a
        // short note printed straight through the note after it.
        if (ev.ChordSymbol is { Length: > 0 } chord)
            ev.SlotWidth = Math.Max(ev.SlotWidth,
                                    ScoreText.Chord(chord, ChordSize, _ppd).Width + (0.5 * S));

        // A syllable is centred under its head and so charges the note only half of itself — the other half
        // is its neighbour's problem. Charging the full width made a line of long and short words lurch.
        foreach (var (_, text, _, _, _) in ev.Lyrics)
        {
            if (text.Length == 0) continue;
            var wanted = (ScoreText.Width(text, LyricSize, _ppd) / 2) + LyricGap;
            ev.SlotWidth = Math.Max(ev.SlotWidth, wanted + (_noteHead / 2));
        }
    }

    /// <summary>What a bar wants: its events, whatever signature is printed at its head, and its bar line.</summary>
    private void Measure(Bar bar, Row row)
    {
        // Whether the note after this one is beamed to it, which is what decides how much air it takes.
        for (var at = 0; at < bar.Events.Count; at++)
        {
            var next = at + 1 < bar.Events.Count ? bar.Events[at + 1] : null;
            var grouped = bar.Events[at].Beam is { } beam && next?.Beam is { } with && ReferenceEquals(beam, with);
            Measure(bar.Events[at], grouped);
        }

        bar.SignatureWidth = OpeningWidth(bar.Opened);
        if (bar.KeyChange is { } key) bar.SignatureWidth += KeyWidth(key, row.Clef) + (0.5 * S);
        if (bar.MeterChange is { } change) bar.SignatureWidth += MeterWidth(change.Sign) + (0.5 * S);

        bar.Width = bar.SignatureWidth + LeadIn(bar.Opened) + bar.Events.Sum(e => e.SlotWidth)
                    + LeadOut(bar.Closed) + BarlineWidth(bar.Closed);
    }

    /// <summary>
    /// The air either side of a bar line — after it before the first note, and after the last note before
    /// the next one.
    ///
    /// <para>
    /// Without it a note sits against the line and reads as attached to it, which is the single clearest
    /// difference between our page and an engraver's. Fixed amounts rather than part of a note's slot,
    /// because what they separate the note from is the <em>line</em> rather than the note before or after,
    /// and a slot that grew with the note's length would put the most air around a semibreve, which needs
    /// it least.
    /// </para>
    /// <para>
    /// The lead-in is the larger of the two. A bar line is read left to right, so the eye needs the gap
    /// after it more than before it — and the note before a bar line usually has a stem the line would
    /// otherwise crowd.
    /// </para>
    /// </summary>
    private double BarLeadIn => _spacing.BarLeadIn;

    private double BarLeadOut => _spacing.BarLeadOut;

    /// <summary>
    /// The extra air either side of a bar line that stops the music rather than just counting it — a double
    /// bar, a repeat, the end.
    ///
    /// <para>
    /// A plain <c>|</c> is punctuation inside a phrase and wants no more than the lead-in. A <c>:||:</c> is
    /// a full stop and the start of a new sentence, and the eye needs to see that before it reads on. Ours
    /// gave both the same room, so a rest landing straight after a repeat sat against it and read as part
    /// of the barline rather than as the silence it is.
    /// </para>
    /// </summary>
    private double SectionAir => _spacing.SectionAir;

    /// <summary>Whether a bar line is one that stops the music: anything written with more than one mark.</summary>
    private static bool Stops(Barline? line) => line?.Drawn.Trim().Length > 1;

    private double LeadIn(Barline? line) => BarLeadIn + (Stops(line) ? SectionAir : 0);

    private double LeadOut(Barline? line) => BarLeadOut + (Stops(line) ? SectionAir : 0);

    private static double BarlineWidth(Barline? line) =>
        line is null ? 0 : Math.Max(0.5 * S, (line.Drawn.Length * 0.28 * S) + (0.35 * S));

    /// <summary>The room an opening bar line takes at the head of a bar — a repeat start, usually.</summary>
    private static double OpeningWidth(Barline? line) => BarlineWidth(line);

    private double KeyWidth(KeySignature key, ClefKind clef) =>
        Math.Abs(key.Fifths) == 0
            ? 0
            : Math.Min(Math.Abs(key.Fifths), 7)
              * (Smufl.Advance(key.Fifths > 0 ? Smufl.AccidentalSharp : Smufl.AccidentalFlat, S) + (0.08 * S));

    private static double MeterWidth(int? sign) =>
        sign is { } drawn ? Smufl.Advance(drawn, S) + (0.2 * S) : 2.2 * S;

    /// <summary>How far a stem reaches past its head, in half-spaces.</summary>
    private const int StemHalfSpaces = 7;

    /// <summary>
    /// How far the notation on a system actually reaches above the top line and below the bottom one, in
    /// half-spaces — heads and the stems they carry, on the side those stems point.
    ///
    /// <para>
    /// Asked per beam group rather than per note, because a group's stems all point the same way and that
    /// way is decided by the group. A note high in the staff whose group stems up still has a stem going
    /// up, and reserving for the note alone would let it through the chord symbols.
    /// </para>
    /// </summary>
    private static (int Above, int Below) Reach(System system)
    {
        var geometry = StaffGeometry.For(system.Row.Clef);
        int high = 8, low = 0;

        foreach (var group in system.Bars.SelectMany(b => b.Events)
                                    .Where(e => !e.IsRest && !e.Invisible && e.Heads.Length > 0)
                                    .GroupBy(e => e.Beam ?? (object)e))
        {
            var halves = group.SelectMany(e => e.Heads.Select(geometry.HalfSpacesAbove)).ToList();
            if (halves.Count == 0) continue;

            // A semibreve has no stem to make room for.
            var stem = group.Any(e => e.BaseValue >= 2) ? StemHalfSpaces : 0;
            var down = StemsDown(halves);

            high = Math.Max(high, halves.Max() + (down ? 0 : stem));
            low = Math.Min(low, halves.Min() - (down ? stem : 0));
        }

        // Grace notes are small and always stem up, and they sit above whatever they decorate.
        foreach (var ev in system.Bars.SelectMany(b => b.Events))
            foreach (var (half, _) in ev.Graces)
                high = Math.Max(high, geometry.HalfSpacesAbove(half) + (StemHalfSpaces / 2));

        return (high, low);
    }

    /// <summary>
    /// The room the clef, key signature and — on the first system only — the meter take at the head.
    /// <para>
    /// Two answers rather than one, because a later system draws no time signature and must not leave a
    /// gap where one would have gone. Successive systems are free to differ; it is the staves *within* a
    /// system that have to agree, and they all ask this the same way.
    /// </para>
    /// </summary>
    private double HeadWidth(Row row, bool meter)
    {
        // A voice's name is printed to the left of its clef, so it has to be paid for before the clef is
        // placed — otherwise every staff in a part song starts at a different x and the grid is gone.
        var named = row is { Index: 0, Name: { Length: > 0 } name }
            ? ScoreText.Width(name, CreditSize, _ppd) + (0.6 * S)
            : 0;

        var width = LeftMargin + named + Smufl.Advance(StaffGeometry.For(row.Clef).ClefGlyph, S) + (0.6 * S);
        width += KeyWidth(KeySignature.FromFifths(row.Fifths), row.Clef);
        if (width > 0) width += 0.4 * S;
        if (meter && row.Meter is { } shown) width += MeterWidth(shown.Sign);

        // The same air whether or not a meter was printed. Hanging it off the meter meant a tune without
        // one opened with its first note against the key signature.
        return width + HeadGap;
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
        foreach (var together in rows.GroupBy(r => (r.Piece, r.Voice.Length == 0 ? "" : "v", r.Index))
                                     .OrderBy(g => rows.IndexOf(g.First())))
        {
            var parts = together.ToList();
            foreach (var row in parts)
                foreach (var bar in row.Bars) Measure(bar, row);

            var shared = Share(parts);
            var opening = parts.Max(r => HeadWidth(r, meter: true));
            var later = parts.Max(r => HeadWidth(r, meter: false));
            var from = systems.Count;

            foreach (var row in parts) Break(systems, row, opening, later, available, bracket);

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
    private void Break(List<System> systems, Row row, double opening, double later, double available, int bracket)
    {
        {
            var head = row.Index == 0 ? opening : later;
            var total = row.Bars.Sum(b => b.Width);
            var room = Math.Max(available - head, 4 * S);

            var lines = Math.Max(1, (int)Math.Ceiling(total / room));
            var each = total / lines;

            var current = new System
            {
                Row = row, HeadWidth = head, ShowName = row.Index == 0,
                ShowMeter = row.Index == 0 && row.Meter is not null,
            };
            var used = 0.0;

            foreach (var bar in row.Bars)
            {
                // Start a new line once this one holds its share — unless it holds nothing yet, or there
                // are only as many bars left as there are lines left to fill.
                var left = row.Bars.Count - row.Bars.IndexOf(bar);
                var linesLeft = lines - systems.Count(s => ReferenceEquals(s.Row, row));

                var balanced = used + (bar.Width / 2) > each && left > linesLeft - 1;

                // …and always once it will not fit, whatever the balancing thinks. `lines` is an estimate
                // made before any bar was placed, and when it comes out too large every break is suppressed
                // by the clause above — there are always fewer bars left than lines to fill. The system then
                // runs off the page, because justification will compress a line but not by an unbounded
                // amount. Fitting is not something a balance is allowed to trade away.
                var overflows = used + bar.Width > room;

                if (current.Bars.Count > 0 && (balanced || overflows))
                {
                    systems.Add(current);
                    current = new System { Row = row, HeadWidth = later };
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
    /// <summary>How little of the width given a block of music may take before it stops reading as one.</summary>
    private const double LeastLine = 0.25;

    /// <summary>How much of it a line has to want before it is treated as wanting all of it.</summary>
    private const double Fills = 0.5;

    /// <summary>The room a system needs whatever the spacing does — its head, its signatures, its bar lines.</summary>
    private double Fixed(System system) =>
        system.HeadWidth
        + system.Bars.Sum(b => b.SignatureWidth + LeadIn(b.Opened) + LeadOut(b.Closed) + BarlineWidth(b.Closed));

    /// <summary>
    /// The one width every system on this page is set to.
    ///
    /// <para>
    /// <strong>One width, not a minimum applied line by line.</strong> A floor imposed per system lifts
    /// only the lines that fall under it, so a line whose clef and bar lines already exceed the floor
    /// stays wider than its neighbours and the block comes out ragged — which is exactly what a floor was
    /// supposed to prevent. Deciding the width once and setting every line to it is what makes them agree.
    /// </para>
    /// <para>
    /// Normally that width is the page — a line that had to be wrapped was wrapped <em>to</em> the page,
    /// so setting it to anything else would undo the choice the breaker just made.
    /// </para>
    /// <para>
    /// A tune whose lines were never wrapped is the other case: every line ends where the writer ended it,
    /// so the page is not what decided them and stretching each across it would space two bars over a
    /// room they were never meant to fill. Those are set to whatever the widest of them wants — which is
    /// what makes them agree with each other — and never to less than a quarter of the page, below which
    /// a block of music stops looking like one at all.
    /// </para>
    /// </summary>
    private (double Width, bool Shared) Page(List<System> systems, double target)
    {
        // A row that came out as more than one system was wrapped to this width, so the width is what
        // decided it and the line is set to fill it.
        if (systems.GroupBy(s => s.Row).Any(row => row.Count() > 1)) return (target, false);

        var widest = systems
            .Where(s => s.Bars.Sum(b => b.Events.Sum(e => e.SlotWidth)) > 0)
            .Select(s => Fixed(s) + s.Bars.Sum(b => b.Events.Sum(e => e.SlotWidth)))
            .DefaultIfEmpty(0)
            .Max();

        // Nothing wrapped, so the writer's line breaks stand — but a line that already wants most of the
        // page is a full line that happens to end where it was told, and leaving it short of the edge
        // reads as the music being cut off rather than as a choice. Only a tune that wants markedly less
        // than the page keeps its own width.
        return widest >= Fills * target
            ? (target, false)
            : (Math.Min(target, Math.Max(LeastLine * target, widest)), true);
    }

    private void Justify(List<System> systems, double width)
    {
        var (page, shared) = Page(systems, width - RightMargin);
        var factors = new List<double>();

        for (var i = 0; i < systems.Count; i++)
        {
            var system = systems[i];
            var flexible = system.Bars.Sum(b => b.Events.Sum(e => e.SlotWidth));
            var fixedRoom = Fixed(system);
            if (flexible <= 0) continue;

            var last = i == systems.Count - 1;
            var wanted = (page - fixedRoom) / flexible;

            // A width chosen for the block is one every line is meant to reach, so it is taken as given —
            // no clamp, and no exception for the last line. Both of those exist to stop a page-width line
            // being stretched absurdly, and neither applies when the width came from the music itself.
            // Leaving them on is what left five one-bar lines at five different lengths: each stopped
            // where the clamp put it rather than where the block asked.
            var factor = shared
                ? Math.Max(0.2, wanted)
                : last && factors.Count > 0
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
                x += bar.SignatureWidth + LeadIn(bar.Opened);

                foreach (var ev in bar.Events)
                {
                    // Left to right within the slot: grace notes, the accidental, then the head.
                    ev.X = x + ev.GraceWidth + ev.AccidentalWidth;
                    x += ev.SlotWidth;
                }

                // The air before the bar line, which was paid for when the bar was measured and then never
                // spent here — so the last note of every bar sat against the line it was supposed to be
                // clear of, and every system came out shorter than the room it had asked for.
                x += LeadOut(bar.Closed) + BarlineWidth(bar.Closed);
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
    private void Stack(List<System> systems, double below)
    {
        var y = below + S;

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

            // A stem reaches about three and a half spaces past the head it is on — but only on the side
            // it points, which is what this used to ignore. Adding it to both sides reserved a whole stem's
            // length above a system whose stems all point down, and the chord symbols sat on top of the
            // room nothing was using.
            var (reaches, sinks) = Reach(system);
            system.Above = Math.Max(0, (Math.Max(highest, reaches) - 8) * (S / 2));
            system.Below = Math.Max(0, -Math.Min(lowest, sinks) * (S / 2));

            // Everything outside the staff is measured from the notation rather than from a fixed pad: a
            // chord symbol belongs above the music, and how high that is depends on how high the music
            // went. Stacked in the order they are read outward from the staff.
            system.Above += system.MarksAbove * MarkRow;
            if (system.HasChordRow) system.Above += TextRow;
            if (system.HasVoltaRow) system.Above += VoltaRow;

            system.Below += system.TextBelow * TextRow;

            system.StaffTop = y + system.Above;

            // Clear of whatever the music reached down to, which is why it is measured from Below rather
            // than from the staff: a phrase of low notes pushes its words down with it.
            system.LyricTop = system.StaffTop + StaffHeight + system.Below
                              + (system.LyricVerses > 0 ? LyricClear : 0);

            // A staff that sounds with the next one is set close to it; a new system gets a full gap. The
            // difference is what tells a reader whether two lines are played together or one after another,
            // and it is the only thing that does.
            var gap = system.Bracket > 0 && !system.LastOfBracket ? StaffGap : SystemGap;
            y = system.LyricTop + (system.LyricVerses * LyricRow) + gap;
        }
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>
    /// The bars of a line, grouped into the sections a musician would name: the A part, the B part, the
    /// bars inside a repeat.
    ///
    /// <para>
    /// A level between the bar and the line, because it is a level a reader thinks in. Selection grows
    /// outward through whatever the tree holds, so a tune whose layout goes note, bar, line can be
    /// widened from a note to a bar and then to a whole line - skipping the unit anybody would actually
    /// want, which is the section. Nothing has to teach selection about it: it is a node with a stretch
    /// of source, and growing to the nearest thing that covers what is chosen is what selection already
    /// does.
    /// </para>
    /// <para>
    /// A plain <c>|</c> divides bars and nothing else; everything heavier - a double bar, a final bar, a
    /// repeat either way round - is where the music turns, and is what this breaks on. A line whose bars
    /// are all divided by plain bar lines is one section, which is the honest answer rather than a level
    /// that appears only sometimes.
    /// </para>
    /// </summary>
    private static List<List<Bar>> Sections(List<Bar> bars)
    {
        var sections = new List<List<Bar>>();
        var current = new List<Bar>();

        foreach (var bar in bars)
        {
            if (current.Count > 0 && (bar.Opened is not null || bar.Volta is not null))
            {
                sections.Add(current);
                current = [];
            }

            current.Add(bar);

            if (bar.EndsRepeat || Heavier(bar.Closed))
            {
                sections.Add(current);
                current = [];
            }
        }

        if (current.Count > 0) sections.Add(current);
        return sections;
    }

    /// <summary>Whether a bar line is more than the plain divider between two bars.</summary>
    private static bool Heavier(Barline? line) =>
        line is not null && line.Drawn.Trim() is not ("|" or "");

    /// <summary>
    /// The stretch of source a run of bars covers, from the first character anything in it was written with to
    /// the last — or nothing, where nothing in it was written at all.
    /// </summary>
    private static ISourcePart? Spanning(List<Bar> bars)
    {
        var start = int.MaxValue;
        var end = int.MinValue;

        void Take(ISourcePart? part)
        {
            if (part is null) return;
            start = Math.Min(start, part.Start);
            end = Math.Max(end, part.End());
        }

        foreach (var bar in bars)
        {
            Take(bar.Part);
            Take(bar.Opened?.Part);
            Take(bar.Closed?.Part);
            foreach (var ev in bar.Events) Take(ev.Part);
        }

        return start > end ? null : new SourceSpan(start, end - start);
    }

    // ── The small pieces ────────────────────────────────────────────────────

    /// <summary>Whether a bar line ends the music rather than merely dividing it.</summary>
    private static bool Closes(Barline? line) =>
        line?.Drawn is { } written && (written.Contains(']') || written == "||");

    // ── Bracketed systems ───────────────────────────────────────────────────

    // ── Curves ──────────────────────────────────────────────────────────────

    // ── Repeat brackets ─────────────────────────────────────────────────────

    // ── Glyphs and geometry ─────────────────────────────────────────────────

    /// <summary>
    /// Which way a stem points: away from the middle line, with the note reaching furthest from it
    /// deciding for the whole group. The tie — a note on the middle line, or a group reaching equally far
    /// both ways — goes down, which is a convention borrowed from the corpus rather than a rule. See
    /// <see cref="Engraving.StemDown"/>, which this is the half-space form of.
    /// </summary>
    private static bool StemsDown(IReadOnlyList<int> halves) =>
        halves.Max() - Engraving.MiddleLine >= Engraving.MiddleLine - halves.Min();

    /// <summary>Where a half-space above the bottom staff line lands on the page.</summary>
    private static double Y(System system, int half) => system.StaffTop + StaffHeight - (half * (S / 2));

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

    /// <summary>
    /// One row of words above or below the staff — chord names, and text put above or below a note: tall enough
    /// for a line of either face set in it, with air before the staff. A fixed height undercut the words it held;
    /// the row was shorter than a line of the body face, so text over the staff sat on its top line.
    /// </summary>
    private double TextRow => _textRow ??= Math.Max(ChordRow,
        Math.Max(ScoreText.Build("Hg", ChordSize, _ppd).Height, ScoreText.Chord("Hg", ChordSize, _ppd).Height) + (0.5 * S));

    private double? _textRow;
}
