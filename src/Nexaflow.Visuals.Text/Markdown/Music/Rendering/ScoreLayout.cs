using System;
using System.Collections.Generic;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>The engraved geometry of a whole score: where every system, measure and note ended up.</summary>
internal sealed class ScoreLayout
{
    public double Width, Height;

    /// <summary>Ink above the first staff — just the rhythm/composer credit row. The title, subtitles and the
    /// notes under the score are <em>not</em> drawn here: the host emits them as real text so a reader can
    /// select and copy them (see <see cref="WpfScoreRenderer"/>).</summary>
    public double CreditHeight;

    /// <summary>The systems, in reading order. A system is one line of music: one staff for a single-voice
    /// tune, or all the voices' staves bracketed together for a part song.</summary>
    public List<SystemGroup> Groups { get; } = [];

    /// <summary>Every staff line, flattened out of <see cref="Groups"/> — what the painter and the hit-test walk.</summary>
    public List<SystemLayout> Systems { get; } = [];

    /// <summary>Every event in reading order — the spine selection, ties and slurs walk.</summary>
    public List<PlacedEvent> Order { get; } = [];
}

/// <summary>
/// One system: the staves that sound together, engraved as one line. A single-voice tune has one staff per
/// group and nothing to bracket. A part song has one staff per voice, and they are a <em>system</em> in the
/// real sense — the same bars at the same x, a bracket down the left, and bar lines that run through all of
/// them — because that is what tells a reader the parts are simultaneous rather than consecutive.
/// </summary>
internal sealed class SystemGroup
{
    public List<SystemLayout> Staves { get; } = [];

    /// <summary>True on the first system of the score — the one that prints the voice names in full.</summary>
    public bool ShowNames;

    public bool IsBracketed => Staves.Count > 1;
    public double TopY => Staves[0].TopLineY;
    public double BottomY => Staves[^1].BottomLineY;
}

/// <summary>One engraved staff line of one voice.</summary>
internal sealed class SystemLayout
{
    public required StaffGeometry Geom;
    public required KeySignature Key;
    public required TimeSignature Time;
    public bool ShowTime;
    public double TopLineY;
    public double LeftX, ContentStartX, RightX;
    public double ClefX, KeyStartX, TimeStartX;
    public string? SectionLabel;
    public string? StaffName;
    public List<MeasureLayout> Measures { get; } = [];
    public double BottomLineY => TopLineY + StaffHeight;

    /// <summary>Top of the block reserved above the staff.</summary>
    public double AboveTop;

    /// <summary>How far the notation itself reaches above the top line and below the bottom one — ledger heads,
    /// stems, beams, the marks stacked over them. Everything else that lives outside the staff is placed
    /// relative to <em>this</em>, not to a fixed pad: a chord symbol belongs above the music, and how high that
    /// is depends on how high the music went.</summary>
    public double AboveMusic, BelowMusic;

    public bool HasVoltaRow, HasChordRow;
    public int LyricVerses;

    public double ChordTextTop => TopLineY - AboveMusic - ChordRow;
    public double VoltaLineY   => (HasChordRow ? ChordTextTop : TopLineY - AboveMusic) - 0.7 * S;
    public double SectionTop   => AboveTop;
    public double LyricTop     => BottomLineY + BelowMusic + 0.2 * S;
}

internal sealed class MeasureLayout
{
    public required Measure Source;
    public double StartX, EndX;
    public BarlineKind StartBarline, EndBarline;
    public double SigWidth;                       // room taken by a mid-tune key/meter change at StartX
    public List<PlacedEvent> Events { get; } = [];
    public SystemLayout System = null!;
}

/// <summary>One event, placed. <see cref="HeadX"/> is the left edge of the note head; the accidental and any
/// grace notes sit in the space to its left, inside the same slot.</summary>
internal sealed class PlacedEvent
{
    public required MusicalEvent Ev;
    public double HeadX;
    public double AccX, AccW;
    public double GraceX, GraceW;
    public int Index;                             // global reading order — the selection's coordinate
    public double SlotLeft, SlotRight;
    public MeasureLayout Measure = null!;
    public SystemLayout System = null!;
}

/// <summary>
/// Turns a <see cref="Score"/> into a <see cref="ScoreLayout"/>: assigns each event a horizontal slot sized by
/// its duration (plus whatever it carries — an accidental, dots, grace notes, a chord symbol, a syllable),
/// groups measures into systems, and justifies them.
///
/// Justification is the part that reads wrong if you get it slightly off. Every system but the last fills the
/// chosen width exactly, so the block has a straight right edge. The last system is <em>not</em> stretched to
/// match — that would space a two-bar tail across the page — but neither is it left at its natural width, which
/// is the bug this replaced: unstretched, its notes sat visibly tighter than every line above. It is scaled by
/// the same factor its siblings were, so the note spacing is continuous and only the right edge is ragged.
/// </summary>
internal sealed class ScoreLayoutEngine(Score score, double staffSpace, double ppd)
{
    private readonly double _ppd = ppd;
    private readonly double _noteW = Smufl.Available ? Smufl.Advance(Smufl.NoteheadBlack, staffSpace) : 1.18 * S;
    private readonly double _dotW = Smufl.Available ? Smufl.Advance(Smufl.AugmentationDot, staffSpace) : 0.3 * S;

    private double _leftInset = LeftMargin;
    private double _nameWidth;

    public double NoteheadWidth => _noteW;

    public ScoreLayout Build(double availableWidth)
    {
        var layout = new ScoreLayout();

        double maxW = Math.Max(0.8 * availableWidth, 320);
        double minW = Math.Max(0.4 * availableWidth, 220);

        bool credits = !string.IsNullOrWhiteSpace(score.Composer) || !string.IsNullOrWhiteSpace(score.Rhythm);
        layout.CreditHeight = credits ? CreditSize + 6 : 0;

        // Voices that run in step are engraved as one bracketed system. Voices that don't (a source that
        // barred them differently) fall back to separate staves — better an honest stack than a false system.
        bool grand = score.Staves.Count > 1 && SameBarring();
        _nameWidth = grand ? NameColumnWidth() : 0;
        _leftInset = LeftMargin
                   + (_nameWidth > 0 ? _nameWidth + 0.6 * S : 0)
                   + (grand ? BracketWidth + 0.6 * S : 0);

        double y = layout.CreditHeight;

        if (grand) BuildGrand(layout, ref y, maxW, minW);
        else
            foreach (var staff in score.Staves)
            {
                foreach (var sys in BuildStaffSystems(staff, maxW, minW))
                {
                    var group = new SystemGroup { ShowNames = false };
                    group.Staves.Add(sys);
                    PlaceVertically(group, ref y);
                    layout.Groups.Add(group);
                }
                y += StaffGap;
            }

        double maxRight = 0;
        foreach (var g in layout.Groups)
            foreach (var sys in g.Staves)
            {
                layout.Systems.Add(sys);
                maxRight = Math.Max(maxRight, sys.RightX);
            }

        // Reading order — assigned once the systems are final, so a note's index is stable for selection.
        int idx = 0;
        foreach (var sys in layout.Systems)
            foreach (var ml in sys.Measures)
                foreach (var pe in ml.Events)
                {
                    pe.Index = idx++;
                    layout.Order.Add(pe);
                }

        layout.Width = Math.Min(Math.Max(maxRight + RightMargin, minW), Math.Max(availableWidth, minW));
        layout.Height = Math.Max(y, layout.CreditHeight + 6 * S);
        return layout;
    }

    private bool SameBarring()
    {
        int n = score.Staves[0].Measures.Count;
        if (n == 0) return false;
        foreach (var s in score.Staves)
            if (s.Measures.Count != n)
                return false;
        return true;
    }

    private double NameColumnWidth()
    {
        double w = 0;
        foreach (var s in score.Staves)
            if (!string.IsNullOrWhiteSpace(s.Name))
                w = Math.Max(w, ScoreText.Width(s.Name!, CreditSize, _ppd));
        return w;
    }

    // ── Vertical placement ──────────────────────────────────────────────────

    /// <summary>Stacks a system's staves down the page, giving each exactly the head- and foot-room its own
    /// notation needs plus whatever rows (heading, brackets, chord symbols, lyrics) it carries.</summary>
    private void PlaceVertically(SystemGroup group, ref double y)
    {
        int verses = score.LyricVerses;
        bool anyChordRow = HasChordRow();
        bool anyVolta = HasVolta();

        foreach (var sys in group.Staves)
        {
            var (rise, drop) = InkExtent(sys);
            sys.AboveMusic = Math.Max(2.0 * S, rise + 0.9 * S);
            sys.BelowMusic = Math.Max(2.0 * S, drop + 0.9 * S);
            sys.HasVoltaRow = anyVolta;
            sys.HasChordRow = anyChordRow;
            sys.LyricVerses = verses;
            sys.AboveTop = y;

            double above = sys.AboveMusic
                         + (sys.SectionLabel is not null ? SectionRow : 0)
                         + (anyVolta ? VoltaRow : 0)
                         + (anyChordRow ? ChordRow : 0);

            sys.TopLineY = y + above;
            y = sys.BottomLineY + sys.BelowMusic + verses * LyricRow + StaffGap;
        }
        y += SystemGap - StaffGap;
    }

    /// <summary>How far a staff line's own notation reaches above its top line and below its bottom one.</summary>
    private static (double rise, double drop) InkExtent(SystemLayout sys)
    {
        double rise = 0, drop = 0;
        foreach (var ml in sys.Measures)
            foreach (var pe in ml.Events)
            {
                var (lo, hi) = Engraving.Span(pe.Ev, sys.Geom);
                bool down = Engraving.StemDown(pe.Ev, sys.Geom);
                bool stemmed = !pe.Ev.Duration.IsBreve && pe.Ev.Duration.Base >= 2;

                double up = (hi - 8) * (S / 2) + (stemmed && !down ? StemLen : 0);
                double dn = -lo * (S / 2) + (stemmed && down ? StemLen : 0);

                foreach (var gn in pe.Ev.Graces)
                    up = Math.Max(up, (sys.Geom.HalfSpacesAbove(gn.Pitch) - 8) * (S / 2) + StemLen * 0.8);

                int staffMarks = 0;
                foreach (var a in pe.Ev.Articulations)
                    if (a is not (ArticulationKind.Staccato or ArticulationKind.Tenuto))
                        staffMarks++;
                if (staffMarks > 0) up = Math.Max(up, 0) + staffMarks * 1.4 * S;
                if (pe.Ev.TupletId != 0) up = Math.Max(up, StemLen + 1.6 * S);

                rise = Math.Max(rise, up);
                drop = Math.Max(drop, dn);
            }
        return (rise, drop);
    }

    private bool HasChordRow()
    {
        foreach (var st in score.Staves)
            foreach (var m in st.Measures)
                foreach (var e in m.Events)
                    if (e.ChordSymbol is not null ||
                        (e.Annotation is not null && e.AnnotationPlacement == AnnotationPlacement.Above))
                        return true;
        return false;
    }

    private bool HasVolta()
    {
        foreach (var st in score.Staves)
            foreach (var m in st.Measures)
                if (m.Volta is not null)
                    return true;
        return false;
    }

    // ── A bracketed system ──────────────────────────────────────────────────

    /// <summary>
    /// Lays every voice out against one shared grid: the same bars, at the same x, on every staff. A measure's
    /// width is whichever voice needs the most room in it, so the bar lines line up down the page — which is
    /// the whole point of a system, and the reason this can't just stack the single-staff layout N times.
    /// </summary>
    private void BuildGrand(ScoreLayout layout, ref double y, double maxW, double minW)
    {
        var staves = score.Staves;
        int mCount = staves[0].Measures.Count;

        // The width of bar k is the widest any voice needs it to be.
        var widths = new double[mCount];
        for (int k = 0; k < mCount; k++)
            foreach (var s in staves)
                widths[k] = Math.Max(widths[k], MeasureWidth(s.Measures[k], inlineSig: false));

        double contentStart = 0;
        foreach (var s in staves)
            contentStart = Math.Max(contentStart,
                Header(StaffGeometry.For(s.Clef), s.Key.Fifths, s.ShowTime).contentStartX);

        double budget = Math.Max(6 * S, maxW - contentStart - RightMargin);
        var ranges = LineRanges(staves[0], widths, budget);

        int fullest = 0;
        foreach (var r in ranges) fullest = Math.Max(fullest, r.Count);

        var keys = new KeySignature[staves.Count];
        var times = new TimeSignature[staves.Count];
        var shown = new bool[staves.Count];
        for (int s = 0; s < staves.Count; s++) { keys[s] = staves[s].Key; times[s] = staves[s].Time; }

        for (int li = 0; li < ranges.Count; li++)
        {
            var (start, count) = ranges[li];
            var group = new SystemGroup { ShowNames = li == 0 && _nameWidth > 0 };

            for (int s = 0; s < staves.Count; s++)
            {
                var staff = staves[s];
                var first = staff.Measures[start];

                bool showTime = staff.ShowTime &&
                    (!shown[s] || first.TimeChange is not null || first.SectionLabel is not null);

                var sys = new SystemLayout
                {
                    Geom = StaffGeometry.For(staff.Clef),
                    Key = first.KeyChange ?? keys[s],
                    Time = first.TimeChange ?? times[s],
                    ShowTime = showTime,
                    LeftX = _leftInset,
                    SectionLabel = s == 0 ? first.SectionLabel : null,
                    StaffName = staff.Name,
                };
                keys[s] = sys.Key;
                times[s] = sys.Time;
                shown[s] |= showTime;

                var (clefX, keyStartX, timeStartX, _) = Header(sys.Geom, sys.Key.Fifths, sys.ShowTime);
                sys.ClefX = clefX;
                sys.KeyStartX = keyStartX;
                sys.TimeStartX = timeStartX;
                sys.ContentStartX = contentStart;

                double x = contentStart;
                for (int i = 0; i < count; i++)
                {
                    int k = start + i;
                    var m = staff.Measures[k];
                    bool inlineSig = i > 0 && (m.KeyChange is not null || m.TimeChange is not null);
                    if (i > 0)
                    {
                        if (m.KeyChange is { } kc) keys[s] = kc;
                        if (m.TimeChange is { } tc) times[s] = tc;
                    }

                    var ml = PlaceMeasure(m, x, inlineSig);
                    Stretch(ml, widths[k]);                 // …to the shared width, so the bar lines align
                    ml.System = sys;
                    foreach (var pe in ml.Events) { pe.Measure = ml; pe.System = sys; }
                    sys.Measures.Add(ml);
                    x = ml.EndX;
                }
                sys.RightX = x;
                group.Staves.Add(sys);
            }

            // Every staff in the system spans the same x, so one scale keeps them aligned.
            bool shortTail = li == ranges.Count - 1 && count < fullest;
            double scale = ScaleFor(group.Staves[0], maxW);
            if (ranges.Count == 1) scale = ScaleFor(group.Staves[0], Math.Clamp(group.Staves[0].RightX, minW, maxW));
            else if (shortTail) scale = Math.Min(MeanScale(ranges, fullest, group.Staves[0], maxW), scale);
            foreach (var sys in group.Staves) Apply(sys, scale);

            PlaceVertically(group, ref y);
            layout.Groups.Add(group);
        }
    }

    /// <summary>The stretch a full system gets — a short tail borrows it so its note spacing matches.</summary>
    private static double MeanScale(List<(int Start, int Count)> ranges, int fullest, SystemLayout tail, double maxW)
    {
        // Every full system has the same span (the widths are shared), so the tail's siblings all share one
        // scale; recovering it from the tail's own span would be wrong, so use the target directly.
        double span = tail.RightX - tail.ContentStartX;
        return span <= 1 ? 1.0 : Math.Max((maxW - tail.ContentStartX) / span, 0.5);
    }

    /// <summary>Scales a measure's contents to a target width, keeping its left edge.</summary>
    private static void Stretch(MeasureLayout ml, double target)
    {
        double natural = ml.EndX - ml.StartX;
        if (natural <= 1 || Math.Abs(target - natural) < 0.01) return;
        double k = target / natural;
        double c = ml.StartX;
        double At(double x) => c + (x - c) * k;

        foreach (var pe in ml.Events)
        {
            pe.HeadX = At(pe.HeadX);
            pe.SlotLeft = At(pe.SlotLeft);
            pe.SlotRight = At(pe.SlotRight);
            pe.AccX = pe.HeadX - pe.AccW;
            pe.GraceX = pe.AccX - pe.GraceW;
        }
        ml.EndX = c + target;
    }

    /// <summary>Groups measure <em>indices</em> into lines — the grouping every voice then shares.</summary>
    private static List<(int Start, int Count)> LineRanges(Staff lead, double[] widths, double budget)
    {
        var ranges = new List<(int, int)>();
        int start = 0;
        double used = 0;

        for (int k = 0; k < widths.Length; k++)
        {
            if (k > start && used + widths[k] > budget) { ranges.Add((start, k - start)); start = k; used = 0; }
            used += widths[k];

            if (lead.Measures[k].SystemBreak && k + 1 < widths.Length)
            {
                ranges.Add((start, k - start + 1));
                start = k + 1;
                used = 0;
            }
        }
        if (start < widths.Length) ranges.Add((start, widths.Length - start));
        return ranges;
    }

    // ── A single staff's systems ────────────────────────────────────────────

    private List<SystemLayout> BuildStaffSystems(Staff staff, double maxW, double minW)
    {
        var geom = StaffGeometry.For(staff.Clef);
        var lines = BuildLines(staff, geom, maxW);
        var systems = new List<SystemLayout>();

        var key = staff.Key;
        var time = staff.Time;
        bool timeShown = false;

        int fullest = 0;
        foreach (var l in lines) fullest = Math.Max(fullest, l.Count);

        foreach (var line in lines)
        {
            bool showTime = staff.ShowTime &&
                (!timeShown || line[0].TimeChange is not null || line[0].SectionLabel is not null);

            var sys = new SystemLayout
            {
                Geom = geom,
                Key = line[0].KeyChange ?? key,
                Time = line[0].TimeChange ?? time,
                ShowTime = showTime,
                LeftX = _leftInset,
                SectionLabel = line[0].SectionLabel,
                StaffName = staff.Name,
            };
            key = sys.Key;
            time = sys.Time;
            timeShown |= showTime;

            var (clefX, keyStartX, timeStartX, contentStartX) = Header(geom, sys.Key.Fifths, sys.ShowTime);
            sys.ClefX = clefX;
            sys.KeyStartX = keyStartX;
            sys.TimeStartX = timeStartX;
            sys.ContentStartX = contentStartX;

            double x = contentStartX;
            for (int i = 0; i < line.Count; i++)
            {
                var m = line[i];
                bool inlineSig = i > 0 && (m.KeyChange is not null || m.TimeChange is not null);
                if (i > 0)
                {
                    if (m.KeyChange is { } kc) key = kc;
                    if (m.TimeChange is { } tc) time = tc;
                }
                var ml = PlaceMeasure(m, x, inlineSig);
                ml.System = sys;
                foreach (var pe in ml.Events) { pe.Measure = ml; pe.System = sys; }
                sys.Measures.Add(ml);
                x = ml.EndX;
            }
            sys.RightX = x;
            systems.Add(sys);
        }

        Justify(systems, lines, fullest, maxW, minW);
        return systems;
    }

    private static void Justify(List<SystemLayout> systems, List<List<Measure>> lines, int fullest,
        double maxW, double minW)
    {
        if (systems.Count == 0) return;

        if (systems.Count == 1)
        {
            var only = systems[0];
            Apply(only, ScaleFor(only, Math.Clamp(only.RightX, minW, maxW)));
            return;
        }

        double sum = 0;
        int n = 0;
        for (int i = 0; i < systems.Count; i++)
        {
            if (i == systems.Count - 1 && lines[i].Count < fullest) continue;   // short tail — scaled below
            sum += ScaleFor(systems[i], maxW);
            n++;
        }

        double mean = n > 0 ? sum / n : 1.0;
        for (int i = 0; i < systems.Count; i++)
        {
            bool shortTail = i == systems.Count - 1 && lines[i].Count < fullest;
            double s = shortTail ? Math.Min(mean, ScaleFor(systems[i], maxW)) : ScaleFor(systems[i], maxW);
            Apply(systems[i], s);
        }
    }

    private static double ScaleFor(SystemLayout sys, double targetRight)
    {
        double span = sys.RightX - sys.ContentStartX;
        double target = targetRight - sys.ContentStartX;
        if (span <= 1 || target <= 0) return 1.0;
        return Math.Max(target / span, 0.5);                   // a floor keeps a very dense line legible
    }

    private static void Apply(SystemLayout sys, double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.002) return;
        double c = sys.ContentStartX;
        double At(double x) => c + (x - c) * scale;

        foreach (var ml in sys.Measures)
        {
            ml.StartX = At(ml.StartX);
            ml.EndX = At(ml.EndX);
            foreach (var pe in ml.Events)
            {
                // Stretch the gaps, not the glyphs: the head moves, and its accidental / grace notes keep
                // their measured widths and stay clamped to it.
                pe.HeadX = At(pe.HeadX);
                pe.SlotLeft = At(pe.SlotLeft);
                pe.SlotRight = At(pe.SlotRight);
                pe.AccX = pe.HeadX - pe.AccW;
                pe.GraceX = pe.AccX - pe.GraceW;
            }
        }
        sys.RightX = At(sys.RightX);
    }

    private List<List<Measure>> BuildLines(Staff staff, StaffGeometry geom, double maxW)
    {
        double budget = maxW - Header(geom, staff.Key.Fifths, showTime: true).contentStartX - RightMargin;
        if (budget < 6 * S) budget = 6 * S;

        var lines = new List<List<Measure>>();
        var run = new List<Measure>();

        foreach (var m in staff.Measures)
        {
            if (m.SectionLabel is not null && run.Count > 0) { AddRun(lines, run, budget); run = []; }
            run.Add(m);
            if (m.SystemBreak) { AddRun(lines, run, budget); run = []; }
        }
        if (run.Count > 0) AddRun(lines, run, budget);
        if (lines.Count == 0 && staff.Measures.Count > 0) lines.Add([.. staff.Measures]);
        return lines;
    }

    private void AddRun(List<List<Measure>> lines, List<Measure> run, double budget)
    {
        double natural = 0;
        foreach (var m in run) natural += MeasureWidth(m, inlineSig: false);
        if (run.Count <= 1 || natural <= budget * 1.35) { lines.Add(run); return; }

        var cur = new List<Measure>();
        double used = 0;
        foreach (var m in run)
        {
            double w = MeasureWidth(m, inlineSig: false);
            if (cur.Count > 0 && used + w > budget) { lines.Add(cur); cur = []; used = 0; }
            cur.Add(m);
            used += w;
        }
        if (cur.Count > 0) lines.Add(cur);
    }

    // ── Horizontal placement ────────────────────────────────────────────────

    /// <summary>Header (clef + key + time) glyph positions, computed once and shared by layout and drawing so
    /// the notes align with them. The gap after the clef is deliberate — key-signature accidentals sit clear of
    /// it, matching engraving convention.</summary>
    internal (double clefX, double keyStartX, double timeStartX, double contentStartX) Header(
        StaffGeometry geom, int fifths, bool showTime)
    {
        double clefX = _leftInset + 0.5 * S;
        double clefAdv = Smufl.Available ? Smufl.Advance(geom.ClefGlyph, S) : 2.6 * S;
        double keyStartX = clefX + clefAdv + 0.9 * S;
        double keyEndX = keyStartX + KeyWidth(fifths);
        double timeStartX = keyEndX + 0.3 * S;
        double contentStartX = (showTime ? timeStartX + 2.7 * S : keyEndX) + 0.7 * S;
        return (clefX, keyStartX, timeStartX, contentStartX);
    }

    internal static double KeyWidth(int fifths)
    {
        int n = Math.Min(Math.Abs(fifths), 7);
        return n == 0 ? 0 : n * 1.05 * S + 0.3 * S;
    }

    private static double LeadPad(Measure m) =>
        m.StartBarline is BarlineKind.RepeatStart or BarlineKind.HeavyLight ? 1.9 * S : 0.7 * S;

    private static double TrailPad(Measure m) =>
        m.EndBarline is BarlineKind.Final or BarlineKind.RepeatEnd or BarlineKind.RepeatBoth ? 2.2 * S : 1.0 * S;

    private static double SigWidthOf(Measure m, bool inlineSig)
    {
        if (!inlineSig) return 0;
        double w = 0;
        if (m.KeyChange is { } k) w += KeyWidth(k.Fifths) + 0.6 * S;
        if (m.TimeChange is not null) w += 2.7 * S + 0.5 * S;
        return w;
    }

    private double MeasureWidth(Measure m, bool inlineSig)
    {
        double w = LeadPad(m) + SigWidthOf(m, inlineSig) + TrailPad(m);
        for (int i = 0; i < m.Events.Count; i++)
            w += EventSlot(m.Events[i], i + 1 < m.Events.Count ? m.Events[i + 1] : null);
        return w;
    }

    private MeasureLayout PlaceMeasure(Measure m, double startX, bool inlineSig)
    {
        var ml = new MeasureLayout
        {
            Source = m,
            StartX = startX,
            StartBarline = m.StartBarline,
            EndBarline = m.EndBarline,
            SigWidth = SigWidthOf(m, inlineSig),
        };

        double x = startX + LeadPad(m) + ml.SigWidth;
        for (int i = 0; i < m.Events.Count; i++)
        {
            var ev = m.Events[i];
            double slot = EventSlot(ev, i + 1 < m.Events.Count ? m.Events[i + 1] : null);
            double graceW = GraceWidth(ev);
            double accW = AccWidth(ev);

            ml.Events.Add(new PlacedEvent
            {
                Ev = ev,
                GraceX = x,
                GraceW = graceW,
                AccX = x + graceW,
                AccW = accW,
                HeadX = x + graceW + accW,
                SlotLeft = x,
                SlotRight = x + slot,
            });
            x += slot;
        }
        ml.EndX = x + TrailPad(m);
        return ml;
    }

    /// <summary>
    /// The horizontal room one event needs: a duration-driven core (compressed if it's in a tuplet), plus
    /// whatever hangs off it, and never less than a note head's width plus air.
    ///
    /// A syllable widens the slot only by <em>half</em> of itself plus half of its neighbour's, because lyrics
    /// are centred under their note heads — charging each note the full width of its own syllable made a line
    /// of long and short words lurch, which is what "the note spacing is all over the place" was.
    /// </summary>
    private double EventSlot(MusicalEvent ev, MusicalEvent? next)
    {
        double core = SlotBase + SlotRate * Math.Sqrt(Math.Max(0.125, ev.Duration.QuarterLength));
        if (ev.TupletNumber > 1 && ev.TupletTime > 0)
            core *= Math.Max(TupletFloor, (double)ev.TupletTime / ev.TupletNumber);

        double w = GraceWidth(ev) + AccWidth(ev) + core + DotsWidth(ev);
        w = Math.Max(w, _noteW + SlotFloor);

        if (ev.ChordSymbol is { } cs)
            w = Math.Max(w, ScoreText.Width(cs, ChordSize, _ppd) + 0.6 * S);

        double lyric = LyricHalf(ev) + LyricHalf(next) + LyricGap;
        if (lyric > LyricGap) w = Math.Max(w, lyric);

        return w;
    }

    /// <summary>Half the widest syllable this event carries — its share of the gap to its neighbour.</summary>
    private double LyricHalf(MusicalEvent? ev)
    {
        if (ev is null) return 0;
        double widest = 0;
        foreach (var syl in ev.Lyrics)
            if (syl.Text.Length > 0)
                widest = Math.Max(widest, ScoreText.Width(syl.Text, LyricSize, _ppd));
        return widest / 2;
    }

    internal double AccWidth(MusicalEvent ev)
    {
        var acc = AccidentalOf(ev);
        if (acc == AccidentalKind.None) return 0;
        return Smufl.Advance(AccidentalGlyph(acc), S) + AccGap;
    }

    /// <summary>The widest accidental the event carries — a chord's accidentals share one column.</summary>
    internal static AccidentalKind AccidentalOf(MusicalEvent ev) => ev switch
    {
        Note n => n.Accidental,
        Chord c => WidestAccidental(c),
        _ => AccidentalKind.None,
    };

    private static AccidentalKind WidestAccidental(Chord c)
    {
        var best = AccidentalKind.None;
        foreach (var n in c.Notes)
            if (n.Accidental != AccidentalKind.None && (best == AccidentalKind.None || Rank(n.Accidental) > Rank(best)))
                best = n.Accidental;
        return best;

        static int Rank(AccidentalKind k) => k switch
        {
            AccidentalKind.DoubleFlat => 3,
            AccidentalKind.DoubleSharp => 2,
            AccidentalKind.Flat or AccidentalKind.Sharp or AccidentalKind.Natural => 1,
            _ => 0,
        };
    }

    internal static int AccidentalGlyph(AccidentalKind k) => k switch
    {
        AccidentalKind.Sharp       => Smufl.AccidentalSharp,
        AccidentalKind.Flat        => Smufl.AccidentalFlat,
        AccidentalKind.DoubleSharp => Smufl.AccidentalDoubleSharp,
        AccidentalKind.DoubleFlat  => Smufl.AccidentalDoubleFlat,
        _                          => Smufl.AccidentalNatural,
    };

    internal double DotsWidth(MusicalEvent ev) =>
        ev.Duration.Dots == 0 ? 0 : DotGap + ev.Duration.Dots * (_dotW + DotSpacing);

    internal double GraceWidth(MusicalEvent ev) =>
        ev.Graces.Count == 0 ? 0 : ev.Graces.Count * (_noteW * GraceScale + GraceStep) + GraceGap;
}
