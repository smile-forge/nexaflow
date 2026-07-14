using System;
using System.Collections.Generic;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>The engraved geometry of a whole score: where every system, measure and note ended up.</summary>
internal sealed class ScoreLayout
{
    public double Width, Height;
    public double TitleHeight;                    // ink above the first system (title, subtitles, credits)
    public double CreditY;                        // baseline of the rhythm/composer row
    public double FooterY;                        // top of the notes/verses block under the last system
    public List<SystemLayout> Systems { get; } = [];

    /// <summary>Every event in reading order — the spine selection, ties and slurs walk.</summary>
    public List<PlacedEvent> Order { get; } = [];
}

/// <summary>One engraved line of one staff.</summary>
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
    public List<MeasureLayout> Measures { get; } = [];
    public double BottomLineY => TopLineY + StaffHeight;

    /// <summary>Top of the block reserved above the staff. The rows stack downward from here — section
    /// heading, then repeat brackets, then chord symbols — and the staff's own head-room follows.</summary>
    public double AboveTop;
    public bool HasVoltaRow, HasChordRow;
    public int LyricVerses;

    public double VoltaTop => AboveTop + (SectionLabel is not null ? SectionRow : 0);
    public double ChordTop => VoltaTop + (HasVoltaRow ? VoltaRow : 0);
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

    public double NoteheadWidth => _noteW;

    public ScoreLayout Build(double availableWidth)
    {
        var layout = new ScoreLayout();

        double maxW = Math.Max(0.8 * availableWidth, 320);
        double minW = Math.Max(0.4 * availableWidth, 220);

        layout.TitleHeight = MeasureFrontMatter(layout);

        int verses = score.LyricVerses;
        bool anyChordRow = HasChordRow();
        bool anyVolta = HasVolta();

        double y = layout.TitleHeight;
        double maxRight = 0;

        foreach (var staff in score.Staves)
        {
            var geom = StaffGeometry.For(staff.Clef);
            foreach (var sys in BuildSystems(staff, geom, maxW, minW))
            {
                sys.HasVoltaRow = anyVolta;
                sys.HasChordRow = anyChordRow;
                sys.LyricVerses = verses;
                sys.AboveTop = y;

                double above = AbovePad
                             + (sys.SectionLabel is not null ? SectionRow : 0)
                             + (anyVolta ? VoltaRow : 0)
                             + (anyChordRow ? ChordRow : 0);

                sys.TopLineY = y + above;
                layout.Systems.Add(sys);
                maxRight = Math.Max(maxRight, sys.RightX);

                y = sys.BottomLineY + BelowPad + verses * LyricRow + SystemGap;
            }
            y += StaffGap;
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

        layout.FooterY = y;
        y += MeasureFooter();

        layout.Width = Math.Min(Math.Max(maxRight + RightMargin, minW), Math.Max(availableWidth, minW));
        layout.Height = Math.Max(y, layout.TitleHeight + AbovePad + StaffHeight + BelowPad);
        return layout;
    }

    // ── Front matter and footer ─────────────────────────────────────────────

    private double MeasureFrontMatter(ScoreLayout layout)
    {
        double h = 2;
        if (!string.IsNullOrWhiteSpace(score.Title)) h += TitleSize + 6;
        foreach (var _ in score.Subtitles) h += SubtitleSize + 3;

        bool credits = !string.IsNullOrWhiteSpace(score.Composer) || !string.IsNullOrWhiteSpace(score.Rhythm);
        layout.CreditY = h;
        if (credits) h += CreditSize + 5;
        return h;
    }

    private double MeasureFooter()
    {
        int lines = score.Notes.Count + score.Verses.Count;
        if (!string.IsNullOrWhiteSpace(score.Source)) lines++;
        if (!string.IsNullOrWhiteSpace(score.Transcription)) lines++;
        return lines == 0 ? 0 : 6 + lines * (FooterSize + 4);
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

    // ── Systems ─────────────────────────────────────────────────────────────

    /// <summary>Breaks a staff into systems, places every measure, then justifies. The running key and meter are
    /// carried across measures here — a mid-tune change alters the header of every system after it.</summary>
    private List<SystemLayout> BuildSystems(Staff staff, StaffGeometry geom, double maxW, double minW)
    {
        var lines = BuildLines(staff, geom, maxW);
        var systems = new List<SystemLayout>();

        var key = staff.Key;
        var time = staff.Time;
        bool timeShown = false;

        int fullest = 0;
        foreach (var l in lines) fullest = Math.Max(fullest, l.Count);

        foreach (var line in lines)
        {
            // The signatures a system opens with are whatever is in force at its first measure. The meter prints
            // once, on the first system — and again on any system that opens on a meter change.
            var sys = new SystemLayout
            {
                Geom = geom,
                Key = line[0].KeyChange ?? key,
                Time = line[0].TimeChange ?? time,
                ShowTime = staff.ShowTime && (!timeShown || line[0].TimeChange is not null),
                LeftX = LeftMargin,
                SectionLabel = line[0].SectionLabel,
            };
            key = sys.Key;
            time = sys.Time;
            timeShown |= sys.ShowTime;

            var (clefX, keyStartX, timeStartX, contentStartX) = Header(geom, sys.Key.Fifths, sys.ShowTime);
            sys.ClefX = clefX;
            sys.KeyStartX = keyStartX;
            sys.TimeStartX = timeStartX;
            sys.ContentStartX = contentStartX;

            double x = contentStartX;
            for (int i = 0; i < line.Count; i++)
            {
                var m = line[i];
                // The system header already prints the change that lands on its first measure.
                bool inlineSig = i > 0 && (m.KeyChange is not null || m.TimeChange is not null);
                if (i > 0)
                {
                    if (m.KeyChange is { } kc) key = kc;
                    if (m.TimeChange is { } tc) time = tc;
                }
                var ml = PlaceMeasure(m, x, geom, inlineSig);
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

    /// <summary>Fills every system but a short final one to <paramref name="maxW"/>; the short one keeps the
    /// note spacing of its siblings by taking their average stretch (see the class remarks).</summary>
    private static void Justify(List<SystemLayout> systems, List<List<Measure>> lines, int fullest,
        double maxW, double minW)
    {
        if (systems.Count == 0) return;

        if (systems.Count == 1)
        {
            var only = systems[0];
            double natural = only.RightX;
            double fill = Math.Clamp(natural, minW, maxW);
            Apply(only, ScaleFor(only, fill));
            return;
        }

        double sum = 0;
        int n = 0;
        for (int i = 0; i < systems.Count; i++)
        {
            bool last = i == systems.Count - 1;
            if (last && lines[i].Count < fullest) continue;     // genuinely short tail — scaled below
            double s = ScaleFor(systems[i], maxW);
            sum += s;
            n++;
        }

        double mean = n > 0 ? sum / n : 1.0;
        for (int i = 0; i < systems.Count; i++)
        {
            bool last = i == systems.Count - 1;
            bool shortTail = last && lines[i].Count < fullest;
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
                // Stretch the gaps, not the glyphs: the head moves, and its accidental / grace notes / dots
                // keep their measured widths and stay clamped to it.
                pe.HeadX = At(pe.HeadX);
                pe.SlotLeft = At(pe.SlotLeft);
                pe.SlotRight = At(pe.SlotRight);
                pe.AccX = pe.HeadX - pe.AccW;
                pe.GraceX = pe.AccX - pe.GraceW;
            }
        }
        sys.RightX = At(sys.RightX);
    }

    /// <summary>Groups a staff's measures into systems. A notation-requested break (an ABC source-line end) and
    /// a section heading are hard boundaries; each resulting run is kept on ONE line when it fits the width
    /// budget (allowing mild compression, which preserves the source's phrasing), and only wrapped by width when
    /// it genuinely doesn't.</summary>
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
        double clefX = LeftMargin + 0.5 * S;
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

    private double SigWidth(Measure m, bool inlineSig)
    {
        if (!inlineSig) return 0;
        double w = 0;
        if (m.KeyChange is { } k) w += KeyWidth(k.Fifths) + 0.6 * S;
        if (m.TimeChange is not null) w += 2.7 * S + 0.5 * S;
        return w;
    }

    private double MeasureWidth(Measure m, bool inlineSig)
    {
        double w = LeadPad(m) + SigWidth(m, inlineSig) + TrailPad(m);
        foreach (var ev in m.Events) w += EventSlot(ev);
        return w;
    }

    private MeasureLayout PlaceMeasure(Measure m, double startX, StaffGeometry geom, bool inlineSig)
    {
        var ml = new MeasureLayout
        {
            Source = m,
            StartX = startX,
            StartBarline = m.StartBarline,
            EndBarline = m.EndBarline,
            SigWidth = SigWidth(m, inlineSig),
        };

        double x = startX + LeadPad(m) + ml.SigWidth;
        foreach (var ev in m.Events)
        {
            double slot = EventSlot(ev);
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

    /// <summary>The horizontal room one event needs: a duration-driven core (compressed if it's in a tuplet),
    /// plus whatever hangs off it, plus enough not to collide with its own chord symbol or syllable.</summary>
    private double EventSlot(MusicalEvent ev)
    {
        double core = 1.0 * S + 0.85 * S * Math.Sqrt(Math.Max(0.25, ev.Duration.QuarterLength));
        if (ev.TupletNumber > 1 && ev.TupletTime > 0)
            core *= (double)ev.TupletTime / ev.TupletNumber;    // 3 notes into 2 notes' worth of space

        // …but never tighter than a note head plus air, or a septuplet's heads would overlap each other.
        core = Math.Max(core, _noteW + 0.22 * S);

        double w = GraceWidth(ev) + AccWidth(ev) + core + DotsWidth(ev);

        if (ev.ChordSymbol is { } cs)
            w = Math.Max(w, ScoreText.Width(cs, ChordSize, _ppd) + 0.6 * S);
        foreach (var syl in ev.Lyrics)
            if (syl.Text.Length > 0)
                w = Math.Max(w, ScoreText.Width(syl.Text, LyricSize, _ppd) + 0.5 * S);

        return w;
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
        ev.Graces.Count == 0 ? 0 : ev.Graces.Count * (_noteW * GraceScale + 0.3 * S) + GraceGap;
}
