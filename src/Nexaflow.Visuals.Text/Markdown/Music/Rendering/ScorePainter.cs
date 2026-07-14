using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Music.Model;
using static Nexaflow.Visuals.Text.Markdown.Music.Rendering.ScoreMetrics;
using MDuration = Nexaflow.Visuals.Text.Markdown.Music.Model.Duration;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// Draws a laid-out <see cref="ScoreLayout"/>. Everything here is geometry and glyphs — no measuring, no
/// wrapping, no decisions about where a note goes; that all happened in <see cref="ScoreLayoutEngine"/>. The
/// only judgement left is the engraving kind: which way a stem points, how a beam sits over its group, which
/// side of a note head a mark hugs.
///
/// Ink is the palette's text brush throughout — a score is text, and a theme retints it with everything else.
/// </summary>
internal sealed class ScorePainter(Score score, ScoreLayout layout, Brush ink, double ppd)
{
    private readonly Dictionary<double, Pen> _pens = [];
    private readonly double _noteW = Smufl.Available ? Smufl.Advance(Smufl.NoteheadBlack, S) : 1.18 * S;

    private Pen Pen(double thickness)
    {
        if (_pens.TryGetValue(thickness, out var p)) return p;
        p = new Pen(ink, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        p.Freeze();
        _pens[thickness] = p;
        return p;
    }

    public void Paint(DrawingContext dc)
    {
        PaintFrontMatter(dc);
        foreach (var sys in layout.Systems) PaintSystem(dc, sys);
        PaintTiesAndSlurs(dc);
        PaintTuplets(dc);
        PaintFooter(dc);
    }

    // ── Front matter ────────────────────────────────────────────────────────

    private void PaintFrontMatter(DrawingContext dc)
    {
        double y = 2;
        if (!string.IsNullOrWhiteSpace(score.Title))
        {
            ScoreText.Draw(dc, score.Title!, new Point(layout.Width / 2, y), TitleSize, ink, ppd,
                TextAlignment.Center, FontWeights.SemiBold);
            y += TitleSize + 6;
        }
        foreach (var sub in score.Subtitles)
        {
            ScoreText.Draw(dc, sub, new Point(layout.Width / 2, y), SubtitleSize, ink, ppd, TextAlignment.Center);
            y += SubtitleSize + 3;
        }

        // The credit row sits immediately above the first staff: rhythm on the left in italics, composer on
        // the right with the origin bracketed after it.
        if (!string.IsNullOrWhiteSpace(score.Rhythm))
            ScoreText.Draw(dc, score.Rhythm!, new Point(LeftMargin, layout.CreditY), CreditSize, ink, ppd,
                TextAlignment.Left, style: FontStyles.Italic);

        if (!string.IsNullOrWhiteSpace(score.Composer))
        {
            string credit = string.IsNullOrWhiteSpace(score.Origin) ? score.Composer! : $"{score.Composer} ({score.Origin})";
            ScoreText.Draw(dc, credit, new Point(layout.Width - RightMargin, layout.CreditY), CreditSize, ink, ppd,
                TextAlignment.Right);
        }
    }

    private void PaintFooter(DrawingContext dc)
    {
        double y = layout.FooterY + 6;
        void Line(string s)
        {
            ScoreText.Draw(dc, s, new Point(LeftMargin, y), FooterSize, ink, ppd);
            y += FooterSize + 4;
        }

        foreach (var n in score.Notes) Line($"Notes: {n}");
        if (!string.IsNullOrWhiteSpace(score.Source)) Line($"Source: {score.Source}");
        if (!string.IsNullOrWhiteSpace(score.Transcription)) Line($"Transcription: {score.Transcription}");
        foreach (var v in score.Verses) Line(v);
    }

    // ── A system ────────────────────────────────────────────────────────────

    private void PaintSystem(DrawingContext dc, SystemLayout sys)
    {
        var linePen = Pen(StaffLineThick);

        if (sys.SectionLabel is not null)
            ScoreText.Draw(dc, sys.SectionLabel, new Point(sys.LeftX, sys.AboveTop), SubtitleSize, ink, ppd,
                TextAlignment.Left, FontWeights.SemiBold);

        for (int k = 0; k <= 4; k++)
        {
            double ly = sys.TopLineY + k * S;
            dc.DrawLine(linePen, new Point(sys.LeftX, ly), new Point(sys.RightX, ly));
        }

        DrawGlyph(dc, sys.Geom.ClefGlyph, sys.ClefX, sys.BottomLineY - sys.Geom.ClefRefHalfSpaces * (S / 2));
        DrawKeySignature(dc, sys, sys.KeyStartX, sys.Key);
        if (sys.ShowTime) DrawTimeSignature(dc, sys, sys.TimeStartX, sys.Time);

        for (int j = 0; j < sys.Measures.Count; j++)
        {
            var ml = sys.Measures[j];

            if (j == 0 && ml.StartBarline is BarlineKind.RepeatStart or BarlineKind.HeavyLight)
                DrawBarline(dc, ml.StartX, sys, ml.StartBarline);

            // A key or meter change that lands mid-system prints just inside the bar it takes effect at.
            if (ml.SigWidth > 0)
            {
                double sx = ml.StartX + 0.5 * S;
                if (ml.Source.KeyChange is { } kc)
                {
                    DrawKeySignature(dc, sys, sx, kc);
                    sx += ScoreLayoutEngine.KeyWidth(kc.Fifths) + 0.6 * S;
                }
                if (ml.Source.TimeChange is { } tc) DrawTimeSignature(dc, sys, sx, tc);
            }

            PaintMeasure(dc, ml, sys);

            // Where a bar ends and the next one opens a repeat, one glyph serves both.
            var end = ml.EndBarline;
            if (j + 1 < sys.Measures.Count && sys.Measures[j + 1].StartBarline == BarlineKind.RepeatStart)
                end = end == BarlineKind.RepeatEnd ? BarlineKind.RepeatBoth : BarlineKind.RepeatStart;
            DrawBarline(dc, ml.EndX, sys, end);
        }

        PaintVoltas(dc, sys);
    }

    private void PaintMeasure(DrawingContext dc, MeasureLayout ml, SystemLayout sys)
    {
        var beam = new List<PlacedEvent>();
        int beamId = 0;

        void Flush()
        {
            if (beam.Count >= 2) DrawBeamGroup(dc, beam, sys);
            else if (beam.Count == 1) DrawStemAndFlag(dc, beam[0], sys);
            beam.Clear();
        }

        foreach (var pe in ml.Events)
        {
            if (pe.Ev is Rest rest)
            {
                Flush();
                if (!rest.IsInvisible) DrawRest(dc, rest, pe, ml, sys);
                PaintAttachments(dc, pe, sys);
                continue;
            }

            DrawGraces(dc, pe, sys);
            DrawHeads(dc, pe, sys);

            if (pe.Ev.BeamId != 0)
            {
                if (beam.Count > 0 && beamId != pe.Ev.BeamId) Flush();
                beamId = pe.Ev.BeamId;
                beam.Add(pe);
            }
            else
            {
                Flush();
                DrawStemAndFlag(dc, pe, sys);
            }

            PaintAttachments(dc, pe, sys);
        }
        Flush();
    }

    /// <summary>The marks that ride on an event but aren't part of its note: articulations, a chord symbol,
    /// a text annotation, its lyric syllables.</summary>
    private void PaintAttachments(DrawingContext dc, PlacedEvent pe, SystemLayout sys)
    {
        DrawArticulations(dc, pe, sys);
        DrawChordSymbolAndAnnotation(dc, pe, sys);
        DrawLyrics(dc, pe, sys);
    }

    // ── Staff furniture ─────────────────────────────────────────────────────

    private void DrawKeySignature(DrawingContext dc, SystemLayout sys, double startX, KeySignature key)
    {
        int fifths = key.Fifths;
        if (fifths == 0) return;
        bool sharp = fifths > 0;
        int n = Math.Min(Math.Abs(fifths), 7);
        int glyph = sharp ? Smufl.AccidentalSharp : Smufl.AccidentalFlat;
        double x = startX;
        for (int i = 0; i < n; i++)
        {
            int hs = sys.Geom.KeyAccidentalIndex(i, sharp) - sys.Geom.BottomLineIndex;
            DrawGlyph(dc, glyph, x, sys.BottomLineY - hs * (S / 2));
            x += 1.05 * S;
        }
    }

    private void DrawTimeSignature(DrawingContext dc, SystemLayout sys, double startX, TimeSignature time)
    {
        DrawDigits(dc, time.Numerator, startX, sys.BottomLineY - 3 * S);
        DrawDigits(dc, time.Denominator, startX, sys.BottomLineY - 1 * S);
    }

    private void DrawDigits(DrawingContext dc, int value, double startX, double baselineY)
    {
        string s = value.ToString(CultureInfo.InvariantCulture);
        double total = 0;
        foreach (char ch in s) total += Smufl.Advance(Smufl.TimeSig0 + (ch - '0'), S);
        double x = startX + 1.1 * S - total / 2;
        foreach (char ch in s)
            x += DrawGlyph(dc, Smufl.TimeSig0 + (ch - '0'), x, baselineY);
    }

    private void DrawBarline(DrawingContext dc, double x, SystemLayout sys, BarlineKind kind)
    {
        double top = sys.TopLineY, bot = sys.BottomLineY;
        var thin = Pen(ThinBarline);
        var thick = Pen(ThickBarline);
        void V(Pen p, double px) => dc.DrawLine(p, new Point(px, top), new Point(px, bot));

        switch (kind)
        {
            case BarlineKind.Double:
                V(thin, x - 0.7 * S);
                V(thin, x);
                break;
            case BarlineKind.Final:
                V(thin, x - 0.9 * S);
                V(thick, x - ThickBarline / 2);
                break;
            case BarlineKind.HeavyLight:
                V(thick, x + ThickBarline / 2);
                V(thin, x + 0.9 * S);
                break;
            case BarlineKind.RepeatStart:
                V(thick, x + ThickBarline / 2);
                V(thin, x + 0.9 * S);
                RepeatDots(dc, x + 1.5 * S, sys);
                break;
            case BarlineKind.RepeatEnd:
                RepeatDots(dc, x - 1.5 * S, sys);
                V(thin, x - 0.9 * S);
                V(thick, x - ThickBarline / 2);
                break;
            case BarlineKind.RepeatBoth:
                RepeatDots(dc, x - 1.7 * S, sys);
                V(thin, x - 1.0 * S);
                V(thick, x);
                V(thin, x + 1.0 * S);
                RepeatDots(dc, x + 1.7 * S, sys);
                break;
            default:
                V(thin, x);
                break;
        }
    }

    private void RepeatDots(DrawingContext dc, double x, SystemLayout sys)
    {
        double r = 0.17 * S;
        dc.DrawEllipse(ink, null, new Point(x, sys.BottomLineY - 1.5 * S), r, r);
        dc.DrawEllipse(ink, null, new Point(x, sys.BottomLineY - 2.5 * S), r, r);
    }

    /// <summary>Repeat brackets. One runs from the measure that opens it to the measure that ends the repeat
    /// (or to the one before the next bracket), and it closes with a down-tick only when the repeat sends the
    /// reader back — an open-ended bracket is the last time through.</summary>
    private void PaintVoltas(DrawingContext dc, SystemLayout sys)
    {
        var pen = Pen(ThinBarline);
        for (int i = 0; i < sys.Measures.Count; i++)
        {
            if (sys.Measures[i].Source.Volta is not { } label) continue;

            int j = i;
            while (j < sys.Measures.Count - 1 &&
                   sys.Measures[j].EndBarline is not (BarlineKind.RepeatEnd or BarlineKind.RepeatBoth or BarlineKind.Final) &&
                   sys.Measures[j + 1].Source.Volta is null)
                j++;

            bool closed = sys.Measures[j].EndBarline is BarlineKind.RepeatEnd or BarlineKind.RepeatBoth;
            double x0 = sys.Measures[i].StartX;
            double x1 = sys.Measures[j].EndX;
            double y = sys.ChordTop + VoltaRow - 0.35 * S;

            dc.DrawLine(pen, new Point(x0, y), new Point(x1, y));
            dc.DrawLine(pen, new Point(x0, y), new Point(x0, y + 0.9 * S));
            if (closed) dc.DrawLine(pen, new Point(x1, y), new Point(x1, y + 0.9 * S));

            ScoreText.Draw(dc, label, new Point(x0 + 0.45 * S, y + 0.05 * S), VoltaSize, ink, ppd);
            i = j;
        }
    }

    // ── Heads, stems, beams ─────────────────────────────────────────────────

    private double Y(SystemLayout sys, int halfSpaces) => sys.BottomLineY - halfSpaces * (S / 2);

    private static (int lo, int hi) Span(MusicalEvent ev, StaffGeometry g) => Engraving.Span(ev, g);

    private static bool StemDown(IReadOnlyList<PlacedEvent> group, StaffGeometry g)
    {
        var evs = new MusicalEvent[group.Count];
        for (int i = 0; i < group.Count; i++) evs[i] = group[i].Ev;
        return Engraving.StemDown(evs, g);
    }

    private double StemX(PlacedEvent pe, bool down) =>
        down ? pe.HeadX + StemThick / 2 : pe.HeadX + _noteW - StemThick / 2;

    private void DrawHeads(DrawingContext dc, PlacedEvent pe, SystemLayout sys)
    {
        bool down = StemDown([pe], sys.Geom);
        int glyph = HeadGlyph(pe.Ev.Duration);

        var notes = pe.Ev switch
        {
            Note n => (IReadOnlyList<Note>)[n],
            Chord c => c.Notes,
            _ => [],
        };
        if (notes.Count == 0) return;

        // Two notes a step apart cannot share a side of the stem — one is displaced across it. Work outward
        // from the head the stem attaches to, so the displaced note lands on the far side.
        var order = new List<Note>(notes);
        if (down) order.Reverse();                       // a down stem hangs from the top note

        int prevHs = int.MinValue;
        bool displaced = false;
        double accX = pe.AccX;

        foreach (var n in order)
        {
            int hs = sys.Geom.HalfSpacesAbove(n.Pitch);
            displaced = prevHs != int.MinValue && Math.Abs(hs - prevHs) == 1 && !displaced;
            prevHs = hs;

            double x = pe.HeadX + (displaced ? (down ? -_noteW + StemThick : _noteW - StemThick) : 0);
            double cy = Y(sys, hs);

            DrawLedgers(dc, x, cy, hs, sys);
            if (n.Accidental != AccidentalKind.None)
                DrawGlyph(dc, ScoreLayoutEngine.AccidentalGlyph(n.Accidental), accX, cy);

            if (Smufl.Available) DrawGlyph(dc, glyph, x, cy);
            else DrawFallbackHead(dc, pe.Ev.Duration, x, cy);
        }

        DrawDots(dc, pe, sys, notes);
    }

    private static int HeadGlyph(MDuration d) => d.IsBreve ? Smufl.NoteheadDoubleWhole
        : d.Base switch { 1 => Smufl.NoteheadWhole, 2 => Smufl.NoteheadHalf, _ => Smufl.NoteheadBlack };

    private void DrawFallbackHead(DrawingContext dc, MDuration d, double x, double cy)
    {
        var c = new Point(x + _noteW / 2, cy);
        if (d.Base <= 2) dc.DrawEllipse(null, Pen(0.14 * S), c, 0.62 * S, 0.46 * S);
        else dc.DrawEllipse(ink, null, c, 0.62 * S, 0.46 * S);
    }

    private void DrawLedgers(DrawingContext dc, double x, double cy, int hs, SystemLayout sys)
    {
        var pen = Pen(LedgerThick);
        double x0 = x - LedgerExt, x1 = x + _noteW + LedgerExt;
        if (hs < 0)
            for (int e = -2; e >= hs; e -= 2)
                dc.DrawLine(pen, new Point(x0, Y(sys, e)), new Point(x1, Y(sys, e)));
        else if (hs > 8)
            for (int e = 10; e <= hs; e += 2)
                dc.DrawLine(pen, new Point(x0, Y(sys, e)), new Point(x1, Y(sys, e)));
    }

    /// <summary>Augmentation dots sit in a space, to the right of the head — clear of it, and clear of the
    /// wide whole/breve heads in particular, which is what "the dot is too close to the hole" was about.</summary>
    private void DrawDots(DrawingContext dc, PlacedEvent pe, SystemLayout sys, IReadOnlyList<Note> notes)
    {
        int dots = pe.Ev.Duration.Dots;
        if (dots == 0) return;

        double headW = Smufl.Available ? Smufl.Advance(HeadGlyph(pe.Ev.Duration), S) : _noteW;
        foreach (var n in notes)
        {
            int hs = sys.Geom.HalfSpacesAbove(n.Pitch);
            double cy = Y(sys, hs);
            if (hs % 2 == 0) cy -= S / 2;                // a note on a line puts its dot in the space above
            double x = pe.HeadX + headW + DotGap;
            for (int d = 0; d < dots; d++)
            {
                if (Smufl.Available) x += Smufl.Draw(dc, Smufl.AugmentationDot, new Point(x, cy), S, ink) + DotSpacing;
                else { dc.DrawEllipse(ink, null, new Point(x + 0.15 * S, cy), 0.12 * S, 0.12 * S); x += 0.5 * S; }
            }
        }
    }

    private void DrawStemAndFlag(DrawingContext dc, PlacedEvent pe, SystemLayout sys)
    {
        var d = pe.Ev.Duration;
        if (d.IsBreve || d.Base < 2) return;             // breve and whole note carry no stem

        bool down = StemDown([pe], sys.Geom);
        var (lo, hi) = Span(pe.Ev, sys.Geom);
        double baseY = down ? Y(sys, hi) : Y(sys, lo);   // the head the stem grows from
        double refY = down ? Y(sys, lo) : Y(sys, hi);    // the head it must clear
        double tipY = down ? refY + StemLen : refY - StemLen;
        double x = StemX(pe, down);

        dc.DrawLine(Pen(StemThick), new Point(x, baseY), new Point(x, tipY));

        int flags = d.FlagCount;
        if (flags <= 0 || !Smufl.Available) return;
        int glyph = flags switch
        {
            1 => down ? Smufl.Flag8thDown : Smufl.Flag8thUp,
            2 => down ? Smufl.Flag16thDown : Smufl.Flag16thUp,
            3 => down ? Smufl.Flag32ndDown : Smufl.Flag32ndUp,
            _ => down ? Smufl.Flag64thDown : Smufl.Flag64thUp,
        };
        DrawGlyph(dc, glyph, x - (down ? StemThick / 2 : StemThick / 2), tipY);
    }

    /// <summary>Beams a group: stems to a common line, then the primary beam plus any secondary beams (and the
    /// stub a lone sixteenth of a broken pair gets). The slope rule lives in <see cref="Engraving.BeamSlope"/>.</summary>
    private void DrawBeamGroup(DrawingContext dc, List<PlacedEvent> g, SystemLayout sys)
    {
        int n = g.Count;
        bool down = StemDown(g, sys.Geom);

        var x = new double[n];
        var baseY = new double[n];      // the head each stem grows from
        var outerY = new double[n];     // the head each stem must clear — the beam rides on these
        var flags = new int[n];

        for (int i = 0; i < n; i++)
        {
            var (lo, hi) = Span(g[i].Ev, sys.Geom);
            x[i] = StemX(g[i], down);
            baseY[i] = down ? Y(sys, hi) : Y(sys, lo);
            outerY[i] = down ? Y(sys, lo) : Y(sys, hi);
            flags[i] = Math.Max(1, g[i].Ev.Duration.FlagCount);
        }

        double slope = Engraving.BeamSlope(x, outerY);

        // Seat the beam so the note that reaches furthest gets exactly a full-length stem; the rest are longer.
        double a = down ? double.MinValue : double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double want = (down ? outerY[i] + StemLen : outerY[i] - StemLen) - slope * (x[i] - x[0]);
            a = down ? Math.Max(a, want) : Math.Min(a, want);
        }
        double BeamY(double px) => a + slope * (px - x[0]);

        for (int i = 0; i < n; i++)
            dc.DrawLine(Pen(StemThick), new Point(x[i], baseY[i]), new Point(x[i], BeamY(x[i])));

        int levels = 1;
        foreach (int f in flags) levels = Math.Max(levels, f);

        for (int b = 0; b < levels; b++)
        {
            if (b == 0) { Bar(dc, x[0], x[n - 1], BeamY, down, 0); continue; }

            int i = 0;
            while (i < n)
            {
                if (flags[i] <= b) { i++; continue; }
                int j = i;
                while (j + 1 < n && flags[j + 1] > b) j++;

                if (j > i) Bar(dc, x[i], x[j], BeamY, down, b);
                else
                {
                    // A lone secondary beam — the sixteenth of a broken pair. It points back at the note it
                    // is grouped with, which is the previous one unless it opens the group.
                    double sx = i == 0 ? x[i] : x[i] - BeamStub;
                    double ex = i == 0 ? x[i] + BeamStub : x[i];
                    Bar(dc, sx, ex, BeamY, down, b);
                }
                i = j + 1;
            }
        }
    }

    private void Bar(DrawingContext dc, double x0, double x1, Func<double, double> beamY, bool down, int level)
    {
        double off = level * (BeamThick + BeamGap);
        double y0 = down ? beamY(x0) - off - BeamThick : beamY(x0) + off;
        double y1 = down ? beamY(x1) - off - BeamThick : beamY(x1) + off;

        var fig = new PathFigure { StartPoint = new Point(x0, y0), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(x1, y1), true));
        fig.Segments.Add(new LineSegment(new Point(x1, y1 + BeamThick), true));
        fig.Segments.Add(new LineSegment(new Point(x0, y0 + BeamThick), true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        dc.DrawGeometry(ink, null, geo);
    }

    // ── Grace notes ─────────────────────────────────────────────────────────

    private void DrawGraces(DrawingContext dc, PlacedEvent pe, SystemLayout sys)
    {
        if (pe.Ev.Graces.Count == 0) return;

        double gw = _noteW * GraceScale;
        double step = gw + 0.3 * S;
        double x = pe.GraceX;
        double stemLen = StemLen * 0.75;
        var pen = Pen(StemThick * 0.85);

        var stems = new List<(double x, double y)>();
        foreach (var gn in pe.Ev.Graces)
        {
            int hs = sys.Geom.HalfSpacesAbove(gn.Pitch);
            double cy = Y(sys, hs);
            if (Smufl.Available) Smufl.Draw(dc, Smufl.NoteheadBlack, new Point(x, cy), S, ink, GraceScale);
            else dc.DrawEllipse(ink, null, new Point(x + gw / 2, cy), 0.38 * S, 0.3 * S);

            double sx = x + gw - StemThick / 2;
            dc.DrawLine(pen, new Point(sx, cy), new Point(sx, cy - stemLen));
            stems.Add((sx, cy - stemLen));
            x += step;
        }

        if (stems.Count >= 2)
        {
            // Beam the group with one thin bar rather than flagging each note.
            double top = double.MaxValue;
            foreach (var s in stems) top = Math.Min(top, s.y);
            var thick = BeamThick * GraceScale;
            dc.DrawRectangle(ink, null, new Rect(stems[0].x, top, stems[^1].x - stems[0].x, thick));
        }
        else if (stems.Count == 1 && Smufl.Available)
        {
            Smufl.Draw(dc, Smufl.Flag8thUp, new Point(stems[0].x, stems[0].y), S, ink, GraceScale);
        }

        if (pe.Ev.GraceSlashed && stems.Count > 0)
        {
            var (sx, sy) = stems[0];
            dc.DrawLine(Pen(StemThick), new Point(sx - 0.5 * S, sy + 1.1 * S), new Point(sx + 0.7 * S, sy + 0.1 * S));
        }
    }

    // ── Rests ───────────────────────────────────────────────────────────────

    private void DrawRest(DrawingContext dc, Rest rest, PlacedEvent pe, MeasureLayout ml, SystemLayout sys)
    {
        var d = rest.Duration;
        int glyph = d.IsBreve ? Smufl.RestDoubleWhole
            : d.Base switch
              {
                  1 => Smufl.RestWhole,
                  2 => Smufl.RestHalf,
                  4 => Smufl.RestQuarter,
                  8 => Smufl.Rest8th,
                  16 => Smufl.Rest16th,
                  _ => Smufl.Rest32nd,
              };
        double y = d.Base switch
        {
            1 => sys.BottomLineY - 3 * S,      // a whole rest hangs from the fourth line
            _ => sys.BottomLineY - 2 * S,      // everything else centres on the middle line
        };
        if (d.IsBreve) y = sys.BottomLineY - 3 * S;

        // A whole-bar rest is centred between the bar lines rather than sitting in its slot.
        double x = rest.IsWholeMeasure ? (ml.StartX + ml.EndX) / 2 - S / 2 : pe.HeadX;

        if (Smufl.Available) DrawGlyph(dc, glyph, x, y);
        else dc.DrawRectangle(ink, null, new Rect(x, sys.BottomLineY - 2.2 * S, 0.9 * S, 0.5 * S));
    }

    // ── Marks and text on an event ──────────────────────────────────────────

    private void DrawArticulations(DrawingContext dc, PlacedEvent pe, SystemLayout sys)
    {
        if (pe.Ev.Articulations.Count == 0) return;

        bool down = StemDown([pe], sys.Geom);
        var (lo, hi) = Span(pe.Ev, sys.Geom);
        double cx = pe.HeadX + _noteW / 2;

        // A mark that belongs to the note hugs it on the side the stem isn't; everything else stacks above
        // the staff, clear of any high notes.
        double nearY = down ? Y(sys, hi) - 1.25 * S : Y(sys, lo) + 1.25 * S;
        double staffY = Math.Min(sys.TopLineY, Y(sys, hi)) - 1.6 * S;

        foreach (var art in pe.Ev.Articulations)
        {
            bool onNote = art is ArticulationKind.Staccato or ArticulationKind.Tenuto;
            int glyph = ArticulationGlyph(art, above: onNote ? down : true);

            if (onNote)
            {
                DrawGlyph(dc, glyph, cx - Smufl.Advance(glyph, S) / 2, nearY);
                nearY += down ? -1.0 * S : 1.0 * S;
            }
            else
            {
                DrawGlyph(dc, glyph, cx - Smufl.Advance(glyph, S) / 2, staffY);
                staffY -= 1.5 * S;
            }
        }
    }

    private static int ArticulationGlyph(ArticulationKind k, bool above) => k switch
    {
        ArticulationKind.Staccato => Smufl.ArticStaccatoAbove + (above ? 0 : 1),
        ArticulationKind.Tenuto   => Smufl.ArticTenutoAbove + (above ? 0 : 1),
        ArticulationKind.Accent   => Smufl.ArticAccentAbove,
        ArticulationKind.Marcato  => Smufl.ArticMarcatoAbove,
        ArticulationKind.UpBow    => Smufl.StringsUpBow,
        ArticulationKind.DownBow  => Smufl.StringsDownBow,
        ArticulationKind.Fermata  => Smufl.FermataAbove,
        ArticulationKind.Trill    => Smufl.OrnamentTrill,
        ArticulationKind.Roll or ArticulationKind.Turn => Smufl.OrnamentTurn,
        ArticulationKind.Mordent  => Smufl.OrnamentMordent,
        ArticulationKind.LowerMordent => Smufl.OrnamentLowerMordent,
        ArticulationKind.Segno    => Smufl.Segno,
        ArticulationKind.Coda     => Smufl.Coda,
        _ => Smufl.ArticStaccatoAbove,
    };

    private void DrawChordSymbolAndAnnotation(DrawingContext dc, PlacedEvent pe, SystemLayout sys)
    {
        if (pe.Ev.ChordSymbol is { } cs)
            ScoreText.Draw(dc, cs, new Point(pe.HeadX, sys.ChordTop), ChordSize, ink, ppd,
                TextAlignment.Left, FontWeights.SemiBold);

        if (pe.Ev.Annotation is not { } an) return;

        double cx = pe.HeadX + _noteW / 2;
        switch (pe.Ev.AnnotationPlacement)
        {
            case AnnotationPlacement.Below:
                ScoreText.Draw(dc, an, new Point(cx, sys.BottomLineY + 1.6 * S), ChordSize, ink, ppd, TextAlignment.Center);
                break;
            case AnnotationPlacement.Left:
                ScoreText.Draw(dc, an, new Point(pe.HeadX - 0.4 * S, sys.TopLineY + S), ChordSize, ink, ppd, TextAlignment.Right);
                break;
            case AnnotationPlacement.Right:
                ScoreText.Draw(dc, an, new Point(pe.HeadX + _noteW + 0.4 * S, sys.TopLineY + S), ChordSize, ink, ppd);
                break;
            default:
                ScoreText.Draw(dc, an, new Point(pe.HeadX, sys.ChordTop), ChordSize, ink, ppd,
                    TextAlignment.Left, style: FontStyles.Italic);
                break;
        }
    }

    private void DrawLyrics(DrawingContext dc, PlacedEvent pe, SystemLayout sys)
    {
        if (pe.Ev.Lyrics.Count == 0) return;
        double cx = pe.HeadX + _noteW / 2;

        for (int v = 0; v < pe.Ev.Lyrics.Count; v++)
        {
            var syl = pe.Ev.Lyrics[v];
            double y = sys.BottomLineY + BelowPad - 1.7 * S + v * LyricRow;

            if (syl.Melisma)
            {
                dc.DrawLine(Pen(StaffLineThick), new Point(cx - 0.6 * S, y + LyricSize * 0.75),
                    new Point(cx + 0.6 * S, y + LyricSize * 0.75));
                continue;
            }
            if (syl.Text.Length == 0) continue;

            ScoreText.Draw(dc, syl.Text, new Point(cx, y), LyricSize, ink, ppd, TextAlignment.Center);
            if (syl.Hyphen)
            {
                double w = ScoreText.Width(syl.Text, LyricSize, ppd);
                ScoreText.Draw(dc, "-", new Point(cx + w / 2 + 0.35 * S, y), LyricSize, ink, ppd);
            }
        }
    }

    // ── Spanners ────────────────────────────────────────────────────────────

    /// <summary>Ties join two heads of the same pitch; slurs arch over everything between the note that opened
    /// them and the note that closed it. Both are drawn from the reading-order spine, so they cross bar lines
    /// (and, as a stub at each end, system breaks) the way the notation intends.</summary>
    private void PaintTiesAndSlurs(DrawingContext dc)
    {
        var order = layout.Order;
        var open = new Stack<PlacedEvent>();

        for (int i = 0; i < order.Count; i++)
        {
            var pe = order[i];

            for (int k = 0; k < pe.Ev.SlurOpen; k++) open.Push(pe);
            for (int k = 0; k < pe.Ev.SlurClose && open.Count > 0; k++) DrawSlur(dc, open.Pop(), pe);

            foreach (var n in TiedNotes(pe.Ev))
            {
                var to = FindTieTarget(order, i, n.Pitch);
                if (to is not null) DrawTie(dc, pe, to, n.Pitch);
            }
        }
    }

    private static IEnumerable<Note> TiedNotes(MusicalEvent ev)
    {
        switch (ev)
        {
            case Note n when n.TieStart:
                yield return n;
                break;
            case Chord c:
                foreach (var cn in c.Notes)
                    if (cn.TieStart)
                        yield return cn;
                break;
        }
    }

    private static PlacedEvent? FindTieTarget(List<PlacedEvent> order, int from, Pitch pitch)
    {
        for (int j = from + 1; j < order.Count; j++)
        {
            var ev = order[j].Ev;
            if (ev is Rest) continue;
            foreach (var n in ev switch { Note n2 => (IReadOnlyList<Note>)[n2], Chord c => c.Notes, _ => [] })
                if (n.Pitch.DiatonicIndex == pitch.DiatonicIndex)
                    return order[j];
            return null;                    // the very next sounding event isn't the same pitch — no tie
        }
        return null;
    }

    private void DrawTie(DrawingContext dc, PlacedEvent from, PlacedEvent to, Pitch pitch)
    {
        var sys = from.System;
        int hs = sys.Geom.HalfSpacesAbove(pitch);
        double y = Y(sys, hs);
        bool under = !StemDown([from], sys.Geom);          // a tie curves away from the stem
        double x0 = from.HeadX + _noteW + 0.15 * S;
        double x1 = ReferenceEquals(to.System, sys) ? to.HeadX - 0.15 * S : from.Measure.EndX - 0.3 * S;
        if (x1 <= x0 + 0.4 * S) x1 = x0 + 1.2 * S;

        Arc(dc, x0, x1, y + (under ? 0.55 * S : -0.55 * S), under, 0.8 * S);

        // Across a system break the second half of the tie leads into the note on the next line.
        if (!ReferenceEquals(to.System, sys))
        {
            double y2 = Y(to.System, to.System.Geom.HalfSpacesAbove(pitch));
            bool under2 = !StemDown([to], to.System.Geom);
            Arc(dc, to.HeadX - 1.3 * S, to.HeadX - 0.15 * S,
                y2 + (under2 ? 0.55 * S : -0.55 * S), under2, 0.8 * S);
        }
    }

    /// <summary>A slur bows away from the stems, so it never crosses them: over a passage of down-stemmed notes
    /// it arches above, under an up-stemmed one it hangs below. The span votes, and it clears the outermost note
    /// on whichever side it lands.</summary>
    private void DrawSlur(DrawingContext dc, PlacedEvent from, PlacedEvent to)
    {
        var sys = from.System;
        if (!ReferenceEquals(to.System, sys)) to = LastOn(sys) ?? to;

        double x0 = from.HeadX + _noteW / 2;
        double x1 = to.HeadX + _noteW / 2;
        if (x1 < x0) return;
        if (x1 - x0 < 1.5 * S) x1 = x0 + 1.5 * S;

        var span = new List<PlacedEvent>();
        foreach (var pe in layout.Order)
            if (ReferenceEquals(pe.System, sys) && pe.HeadX >= from.HeadX - 0.1 && pe.HeadX <= to.HeadX + 0.1)
                span.Add(pe);
        if (span.Count == 0) span.Add(from);

        int stemsUp = 0;
        double top = double.MaxValue, bot = double.MinValue;
        foreach (var pe in span)
        {
            if (!StemDown([pe], sys.Geom)) stemsUp++;
            var (lo, hi) = Span(pe.Ev, sys.Geom);
            top = Math.Min(top, Y(sys, hi));
            bot = Math.Max(bot, Y(sys, lo));
        }

        bool under = stemsUp * 2 >= span.Count;          // mostly up-stemmed → the slur goes below
        double y = under
            ? Math.Max(bot, sys.BottomLineY) + 0.9 * S
            : Math.Min(top, sys.TopLineY) - 0.9 * S;

        Arc(dc, x0, x1, y, under, depth: 1.0 * S);
    }

    private PlacedEvent? LastOn(SystemLayout sys)
    {
        PlacedEvent? last = null;
        foreach (var pe in layout.Order)
            if (ReferenceEquals(pe.System, sys))
                last = pe;
        return last;
    }

    /// <summary>A tie/slur arc: a filled crescent, thick at the middle and tapering to its ends.</summary>
    private void Arc(DrawingContext dc, double x0, double x1, double y, bool under, double depth)
    {
        double dir = under ? 1 : -1;
        double mx = (x0 + x1) / 2;
        double cy = y + dir * depth * 2;
        double thick = 0.22 * S;

        var fig = new PathFigure { StartPoint = new Point(x0, y), IsClosed = true };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(mx, cy), new Point(x1, y), true));
        fig.Segments.Add(new QuadraticBezierSegment(new Point(mx, cy - dir * thick), new Point(x0, y), true));
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        dc.DrawGeometry(ink, null, geo);
    }

    // ── Tuplets ─────────────────────────────────────────────────────────────

    private void PaintTuplets(DrawingContext dc)
    {
        var groups = new Dictionary<int, List<PlacedEvent>>();
        foreach (var pe in layout.Order)
        {
            if (pe.Ev.TupletId == 0) continue;
            if (!groups.TryGetValue(pe.Ev.TupletId, out var g)) groups[pe.Ev.TupletId] = g = [];
            g.Add(pe);
        }

        foreach (var g in groups.Values)
        {
            if (g.Count == 0) continue;
            var sys = g[0].System;
            double x0 = g[0].HeadX;
            double x1 = g[^1].HeadX + _noteW;

            double top = double.MaxValue;
            foreach (var pe in g)
            {
                var (_, hi) = Span(pe.Ev, sys.Geom);
                top = Math.Min(top, Y(sys, hi));
            }
            string label = g[0].Ev.TupletNumber.ToString(CultureInfo.InvariantCulture);
            var ft = ScoreText.Build(label, VoltaSize + 1, ppd, FontWeights.SemiBold, FontStyles.Italic, ink);
            double mx = (x0 + x1) / 2;

            // Clear the beam as well as the heads — a beamed tuplet's number sits fully above its beam, so the
            // text's own height has to come off the top, not just a gap.
            double baseY = Math.Min(top - StemLen, sys.TopLineY) - 0.5 * S;
            double textTop = baseY - ft.Height;

            // Only an unbeamed tuplet needs a bracket; a beamed one is already visually grouped.
            bool beamed = g[0].Ev.BeamId != 0;
            foreach (var pe in g) beamed &= pe.Ev.BeamId == g[0].Ev.BeamId;

            if (!beamed)
            {
                var pen = Pen(StaffLineThick);
                double my = textTop + ft.Height / 2;
                double gap = ft.Width / 2 + 0.4 * S;
                dc.DrawLine(pen, new Point(x0, my), new Point(mx - gap, my));
                dc.DrawLine(pen, new Point(mx + gap, my), new Point(x1, my));
                dc.DrawLine(pen, new Point(x0, my), new Point(x0, my + 0.6 * S));
                dc.DrawLine(pen, new Point(x1, my), new Point(x1, my + 0.6 * S));
            }
            dc.DrawText(ft, new Point(mx - ft.Width / 2, textTop));
        }
    }

    // ── Primitives ──────────────────────────────────────────────────────────

    private double DrawGlyph(DrawingContext dc, int codepoint, double x, double baselineY) =>
        Smufl.Draw(dc, codepoint, new Point(x, baselineY), S, ink);
}
