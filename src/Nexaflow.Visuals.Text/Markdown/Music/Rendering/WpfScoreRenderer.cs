using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Music.Model;

namespace Nexaflow.Visuals.Text.Markdown.Music.Rendering;

/// <summary>
/// The single native-WPF sheet-music engraver: turns a <see cref="Score"/> into a themed
/// <see cref="FrameworkElement"/> drawn with the Bravura SMuFL font (glyphs) plus WPF geometry (staff
/// lines, stems, beams, bar lines). Both notation parsers target the same <see cref="Score"/> IR, so this
/// is the one place engraving lives. All ink is the palette's text brush — no hard-coded colours.
/// </summary>
public static class WpfScoreRenderer
{
    /// <summary>True when the Bravura SMuFL font loaded; false means the engraver is drawing its geometric
    /// fallback (still functional, but plain note heads and no glyph clefs).</summary>
    public static bool FontAvailable => Smufl.Available;

    public static FrameworkElement Render(Score score, MarkdownPalette palette)
    {
        var element = new ScoreElement(score, palette);
        if (score.Warnings.Count == 0) return element;

        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(element);
        stack.Children.Add(new TextBlock
        {
            Text         = "⚠ " + string.Join("  ·  ", score.Warnings),
            Foreground   = palette.TextMuted,
            FontFamily   = BlockRenderer.BodyFont,
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(2, 4, 0, 0),
        });
        return stack;
    }
}

/// <summary>The drawing surface. Lays every staff out into wrapped systems in <see cref="MeasureOverride"/>,
/// paints them in <see cref="OnRender"/>, and supports mouse selection: click a measure to select it, or
/// drag to select a group of notes. The selection is highlighted with a themed wash and exposed via
/// <see cref="SelectedRange"/> / <see cref="SelectionChanged"/>.</summary>
public sealed class ScoreElement : FrameworkElement, IInteractiveBlock
{
    private readonly Score _score;
    private readonly Brush _ink;
    private readonly Brush _selFill;
    private readonly double _titleSize;

    private const double S = 8.0;               // staff space (px)
    private const double StaffHeight = 4 * S;   // 4 spaces between 5 lines
    private const double StemLen = 3.5 * S;
    private const double StemThick = 0.13 * S;
    private const double BeamThick = 0.5 * S;
    private const double StaffLineThick = 0.10 * S;
    private const double AbovePad = 4.5 * S;
    private const double BelowPad = 4.5 * S;
    private const double SystemGap = 1.5 * S;
    private const double LeftMargin = 2.0;
    private const double RightMargin = 6.0;

    private double _ppd = 1.0;
    private double _noteheadW = 1.18 * S;
    private Layout? _layout;

    // Selection state (global event-index range, inclusive).
    private int? _selA, _selB;
    private int _anchor;
    private bool _dragging;
    private Point _down;

    /// <summary>Raised whenever the selection changes (click, drag, or clear).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The currently selected event range as inclusive global indices, or null when nothing is selected.</summary>
    public (int Start, int End)? SelectedRange =>
        _selA is int a && _selB is int b ? (Math.Min(a, b), Math.Max(a, b)) : null;

    /// <summary>Right-edge x of each engraved system after justification — for layout tests/diagnostics.
    /// Every non-last system shares one value (the chosen line width).</summary>
    public IReadOnlyList<double> SystemRightEdges
    {
        get
        {
            var r = new List<double>();
            if (_layout is not null) foreach (var s in _layout.Systems) r.Add(s.RightX);
            return r;
        }
    }

    public ScoreElement(Score score, MarkdownPalette palette)
    {
        _score = score;
        _ink = palette.Text;
        _selFill = SelectionBrush(palette);
        _titleSize = 15;
        SnapsToDevicePixels = true;
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Center;   // block sits centred in the column
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    /// <summary>A translucent selection wash from the theme accent (falls back to the highlight token).</summary>
    private static Brush SelectionBrush(MarkdownPalette palette)
    {
        if (palette.Accent is SolidColorBrush scb)
        {
            var c = scb.Color;
            var b = new SolidColorBrush(Color.FromArgb(0x3A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
        return palette.Marked;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_ppd <= 0) _ppd = 1.0;
        _noteheadW = Smufl.Available ? Smufl.Advance(Smufl.NoteheadBlack, S, _ppd) : 1.18 * S;

        double avail = availableSize.Width;
        if (double.IsInfinity(avail) || double.IsNaN(avail) || avail <= 0) avail = 900;
        _layout = BuildLayout(avail);
        return new Size(Math.Ceiling(_layout.Width), Math.Ceiling(_layout.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var layout = _layout ??= BuildLayout(680);
        var pen = Pen(StaffLineThick);

        // Transparent fill makes the whole element hit-testable (empty gaps included), not just the glyphs.
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null,
            new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        DrawSelection(dc, layout);   // wash behind the notes

        if (!string.IsNullOrWhiteSpace(_score.Title))
            DrawText(dc, _score.Title!, new Point(layout.Width / 2, 2), _titleSize, FontWeights.SemiBold, TextAlignment.Center);
        if (!string.IsNullOrWhiteSpace(_score.Composer))
            DrawText(dc, _score.Composer!, new Point(layout.Width - RightMargin, layout.TitleHeight - 16), 11, FontWeights.Normal, TextAlignment.Right);

        foreach (var sys in layout.Systems)
            DrawSystem(dc, sys, pen);
    }

    // Half-width a single note's wash reaches when unconstrained by a neighbour (capped at the midpoint
    // between note heads so it never extends more than halfway to the previous/next note).
    private const double SelCellHalf = 1.2 * S;
    private const double SelVPad     = 0.6 * S;

    private void DrawSelection(DrawingContext dc, Layout layout)
    {
        if (SelectedRange is not (int a, int b)) return;
        bool Sel(Placed pe) => pe.Index >= a && pe.Index <= b;

        foreach (var sys in layout.Systems)
            foreach (var ml in sys.Measures)
            {
                var evs = ml.Events;
                if (evs.Count == 0) continue;

                bool anySel = false, allSel = true;
                foreach (var pe in evs) { bool s = Sel(pe); anySel |= s; allSel &= s; }
                if (!anySel) continue;

                if (allSel)
                {
                    // Whole measure → barline-to-barline, vertical spanning the staff and all its notes.
                    var (top, bot) = StaffAndNotes(sys, evs, 0, evs.Count);
                    DrawWash(dc, ml.StartX, ml.EndX, top, bot);
                    continue;
                }

                // Partial → one wash per contiguous run of selected notes, centred on the notes and reaching
                // no more than halfway to the un-selected neighbours; vertical follows each run's notes.
                for (int i = 0; i < evs.Count;)
                {
                    if (!Sel(evs[i])) { i++; continue; }
                    int j = i;
                    while (j < evs.Count && Sel(evs[j])) j++;

                    var (top, bot) = StaffAndNotes(sys, evs, i, j);
                    DrawWash(dc, CellLeft(ml, evs, i), CellRight(ml, evs, j - 1), top, bot);
                    i = j;
                }
            }
    }

    private double NoteCenterX(Placed pe) => pe.HeadX + _noteheadW / 2;

    private double CellLeft(MeasureLayout ml, List<Placed> evs, int i)
    {
        double c = NoteCenterX(evs[i]);
        double left = c - SelCellHalf;
        return i > 0 ? Math.Max(left, (c + NoteCenterX(evs[i - 1])) / 2) : Math.Max(left, ml.StartX);
    }

    private double CellRight(MeasureLayout ml, List<Placed> evs, int i)
    {
        double c = NoteCenterX(evs[i]);
        double right = c + SelCellHalf;
        return i < evs.Count - 1 ? Math.Min(right, (c + NoteCenterX(evs[i + 1])) / 2) : Math.Min(right, ml.EndX);
    }

    /// <summary>Vertical span [top, bot] covering the staff and every note head in <c>evs[from..to)</c>, so
    /// a wash reaches out to notes sitting on ledger lines above or below the staff.</summary>
    private (double top, double bot) StaffAndNotes(SystemLayout sys, List<Placed> evs, int from, int to)
    {
        double top = sys.TopLineY, bot = sys.BottomLineY;
        for (int k = from; k < to; k++)
        {
            double cy = NoteHeadY(evs[k], sys);
            top = Math.Min(top, cy);
            bot = Math.Max(bot, cy);
        }
        return (top, bot);
    }

    private void DrawWash(DrawingContext dc, double left, double right, double top, double bot) =>
        dc.DrawRoundedRectangle(_selFill, null,
            new Rect(left, top - SelVPad, Math.Max(2, right - left), (bot - top) + 2 * SelVPad), 3, 3);

    // ── Mouse selection ──────────────────────────────────────────────────────
    //
    // These are the TUNNELING (preview) handlers, not the bubbling ones: when the element is hosted inside a
    // RichTextBox (the markdown editor / selectable view), handling the preview + capturing the mouse pre-empts
    // the RichTextBox's own caret placement / block selection, which would otherwise "select the whole block".
    // Double-click is left for the host (it enters source-edit mode).

    protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;         // let the host handle double-click (edit source)
        BeginPointerSelect(e.GetPosition(this));
        CaptureMouse();                        // standalone hosting: moves route here for the drag
        e.Handled = true;
    }

    /// <summary>Begins a click/drag selection: a click directly on a note selects that note; a click in a
    /// measure's background selects the measure. Does NOT capture the mouse — a RichTextBox host drives the
    /// whole gesture via <see cref="ExtendPointerSelect"/>/<see cref="EndPointerSelect"/> (mouse events never
    /// reach an embedded element reliably); the element's own preview handler captures for plain hosting.</summary>
    public void BeginPointerSelect(Point pointInElement)
    {
        _down = pointInElement;
        _dragging = true;

        var hit = HitTest(pointInElement);
        if (hit.measure is null || hit.measure.Events.Count == 0) { ClearSelection(); _dragging = false; return; }

        var ev = hit.ev ?? hit.measure.Events[0];
        _anchor = ev.Index;
        if (hit.sys is not null && IsOnNote(pointInElement, ev, hit.sys))
        {
            _selA = _selB = ev.Index;                            // note click → just that note
        }
        else
        {
            _selA = hit.measure.Events[0].Index;                 // background click → whole measure
            _selB = hit.measure.Events[^1].Index;
        }
        InteractiveSelection.Own(this);                          // clear any other block's selection
        InvalidateVisual();
    }

    /// <summary>Continues the drag: selects the note range anchor → the event under the pointer.</summary>
    public void ExtendPointerSelect(Point pointInElement)
    {
        if (!_dragging) return;
        if ((pointInElement - _down).Length < 4) return;         // below the drag threshold — still a click
        ExtendSelectionTo(pointInElement);
    }

    /// <summary>Ends the drag gesture and raises <see cref="SelectionChanged"/>.</summary>
    public void EndPointerSelect()
    {
        if (!_dragging) return;
        _dragging = false;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Y of an event's note head (rests sit around the middle line).</summary>
    private double NoteHeadY(Placed pe, SystemLayout sys) =>
        pe.Ev is Note n ? sys.BottomLineY - sys.Geom.HalfSpacesAbove(n.Pitch) * (S / 2)
                        : sys.BottomLineY - 2 * S;

    /// <summary>True when <paramref name="p"/> lands on the note head itself — near it both horizontally and
    /// vertically. Anywhere else in the measure (blank staff space, stems' far ends, between notes) counts as
    /// measure background, so dense passages still allow a measure click.</summary>
    private bool IsOnNote(Point p, Placed ev, SystemLayout sys)
    {
        double headCx = ev.HeadX + _noteheadW / 2;
        double headCy = NoteHeadY(ev, sys);
        return Math.Abs(p.X - headCx) <= 1.3 * S && Math.Abs(p.Y - headCy) <= 1.6 * S;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e) => _dragging = false;

    protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || !IsMouseCaptured) return;              // captured = standalone hosting drives itself
        ExtendPointerSelect(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging || !IsMouseCaptured) return;
        ReleaseMouseCapture();
        EndPointerSelect();
        e.Handled = true;
    }

    /// <summary>Selects the whole measure under <paramref name="p"/> (or clears when clicking empty space).
    /// Public so hosts and tests can drive selection without synthesising mouse input.</summary>
    public void SelectMeasureAt(Point p)
    {
        var hit = HitTest(p);
        if (hit.measure is null) { ClearSelection(); return; }
        _anchor = hit.ev?.Index ?? hit.measure.Events[0].Index;
        _selA = hit.measure.Events[0].Index;
        _selB = hit.measure.Events[^1].Index;
        InteractiveSelection.Own(this);
        InvalidateVisual();
    }

    /// <summary>Extends the selection from the anchor to the event nearest <paramref name="p"/> (drag).</summary>
    public void ExtendSelectionTo(Point p)
    {
        var hit = HitTest(p);
        if (hit.ev is null) return;
        _selA = _anchor;
        _selB = hit.ev.Index;
        InteractiveSelection.Own(this);
        InvalidateVisual();
    }

    /// <summary>Centre of the note head of the event with global <paramref name="index"/>, in element
    /// coordinates — a diagnostics/testing aid for driving precise note clicks.</summary>
    public Point? HeadCenterOf(int index)
    {
        if (_layout is null) return null;
        foreach (var sys in _layout.Systems)
            foreach (var ml in sys.Measures)
                foreach (var pe in ml.Events)
                    if (pe.Index == index)
                        return new Point(pe.HeadX + _noteheadW / 2, NoteHeadY(pe, sys));
        return null;
    }

    public void ClearSelection()
    {
        InteractiveSelection.Release(this);
        if (_selA is null && _selB is null) return;
        _selA = _selB = null;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private (Placed? ev, MeasureLayout? measure, SystemLayout? sys) HitTest(Point p)
    {
        var layout = _layout;
        if (layout is null || layout.Systems.Count == 0) return (null, null, null);

        // Nearest system by vertical distance to its staff band.
        SystemLayout? best = null; double bestDist = double.MaxValue;
        foreach (var sys in layout.Systems)
        {
            double top = sys.TopLineY - AbovePad, bot = sys.BottomLineY + BelowPad;
            double d = p.Y < top ? top - p.Y : p.Y > bot ? p.Y - bot : 0;
            if (d < bestDist) { bestDist = d; best = sys; }
        }
        if (best is null) return (null, null, null);

        // Measure whose x-range contains p.X (nearest if none).
        MeasureLayout? measure = null;
        foreach (var ml in best.Measures)
            if (p.X >= ml.StartX && p.X <= ml.EndX) { measure = ml; break; }
        if (measure is null && best.Measures.Count > 0)
            measure = p.X < best.Measures[0].StartX ? best.Measures[0] : best.Measures[^1];
        if (measure is null || measure.Events.Count == 0) return (null, measure, best);

        // Event whose slot contains p.X (nearest otherwise).
        Placed ev = measure.Events[0];
        foreach (var pe in measure.Events)
        {
            if (p.X >= pe.SlotLeft && p.X <= pe.SlotRight) { ev = pe; break; }
            if (p.X > pe.SlotRight) ev = pe;   // track the last one to the left
        }
        return (ev, measure, best);
    }

    // ── Layout ──────────────────────────────────────────────────────────────

    private sealed class Layout
    {
        public double Width, Height, TitleHeight;
        public List<SystemLayout> Systems = [];
    }

    private sealed class SystemLayout
    {
        public required StaffGeometry Geom;
        public required KeySignature Key;
        public required TimeSignature Time;
        public bool ShowTime;
        public double TopLineY;                 // y of the top staff line
        public double LeftX, ContentStartX, RightX;
        public double ClefX, KeyStartX, TimeStartX;   // header glyph positions (shared by layout + drawing)
        public List<MeasureLayout> Measures = [];
        public double BottomLineY => TopLineY + StaffHeight;
    }

    private sealed class MeasureLayout
    {
        public double StartX, EndX;
        public BarlineKind StartBarline, EndBarline;
        public List<Placed> Events = [];
    }

    private sealed class Placed
    {
        public required MusicalEvent Ev;
        public double HeadX;        // notehead left
        public double AccX;         // accidental left (if any)
        public bool HasAccidental;
        public int Index;           // global reading-order index (for selection)
        public double SlotLeft;     // hit-test x-range of this event's slot
        public double SlotRight;
    }

    private Layout BuildLayout(double availableWidth)
    {
        var layout = new Layout();
        double titleH = 0;
        if (!string.IsNullOrWhiteSpace(_score.Title)) titleH += _titleSize + 8;
        if (!string.IsNullOrWhiteSpace(_score.Composer)) titleH = Math.Max(titleH, 22);
        layout.TitleHeight = titleH;

        // The score occupies 40–80% of the available width, centred (the element's HorizontalAlignment).
        double maxW = Math.Max(0.8 * availableWidth, 320);
        double minW = Math.Max(0.4 * availableWidth, 220);

        double y = titleH + S;
        double maxRight = 0;

        foreach (var staff in _score.Staves)
        {
            var geom = StaffGeometry.For(staff.Clef);
            int fifths = staff.Key.Fifths;

            var lines = BuildLines(staff, geom, fifths, maxW);
            int fullCount = 0;
            foreach (var l in lines) if (l.Count > fullCount) fullCount = l.Count;

            for (int li = 0; li < lines.Count; li++)
            {
                bool showTime = li == 0;
                var (clefX, keyStartX, timeStartX, contentStartX) = Header(geom, fifths, showTime);

                var sys = new SystemLayout
                {
                    Geom = geom, Key = staff.Key, Time = staff.Time, ShowTime = showTime,
                    TopLineY = y + AbovePad, LeftX = LeftMargin,
                    ClefX = clefX, KeyStartX = keyStartX, TimeStartX = timeStartX,
                    ContentStartX = contentStartX,
                };

                double x = contentStartX;
                foreach (var m in lines[li])
                {
                    var ml = PlaceMeasure(m, x, geom);
                    sys.Measures.Add(ml);
                    x = ml.EndX;
                }
                sys.RightX = x;

                bool isLast = li == lines.Count - 1;
                if (lines.Count == 1)
                {
                    // Single line: sit at natural width, clamped into the [40%, 80%] envelope.
                    double fill = Math.Clamp(sys.RightX, minW, maxW);
                    if (sys.RightX < minW) JustifyFill(sys, fill); else JustifyNatural(sys, fill);
                }
                else if (!isLast || lines[li].Count >= fullCount)
                {
                    // Every non-last line fills EXACTLY the chosen width; a last line that is itself full
                    // (as many bars as a full line) is justified too, so an even tune reads even.
                    JustifyFill(sys, maxW);
                }
                else
                {
                    JustifyNatural(sys, maxW);       // genuinely short final line → natural/ragged
                }
                maxRight = Math.Max(maxRight, sys.RightX);

                layout.Systems.Add(sys);
                y = sys.BottomLineY + BelowPad + SystemGap;
            }

            y += S; // gap between staves
        }

        // Assign global reading-order indices for selection/hit-testing.
        int idx = 0;
        foreach (var sys in layout.Systems)
            foreach (var ml in sys.Measures)
                foreach (var pe in ml.Events)
                    pe.Index = idx++;

        layout.Width = Math.Min(Math.Max(maxRight + RightMargin, minW), availableWidth);
        layout.Height = Math.Max(y, titleH + AbovePad + StaffHeight + BelowPad);
        return layout;
    }

    /// <summary>Header (clef + key + time) glyph x-positions, computed once and shared by layout and
    /// drawing so notes align with the header. The gap after the clef is deliberate — key-signature
    /// accidentals sit clear of the clef, matching engraving convention.</summary>
    private (double clefX, double keyStartX, double timeStartX, double contentStartX) Header(
        StaffGeometry geom, int fifths, bool showTime)
    {
        double clefX   = LeftMargin + 0.5 * S;
        double clefAdv = Smufl.Available ? Smufl.Advance(geom.ClefGlyph, S, _ppd) : 2.6 * S;
        double keyStartX = clefX + clefAdv + 0.7 * S;                 // clear gap clef → key signature
        int n = Math.Min(Math.Abs(fifths), 7);
        double keyEndX = keyStartX + (n > 0 ? n * 1.05 * S + 0.3 * S : 0.0);
        double timeStartX = keyEndX + 0.2 * S;
        double timeAdv = showTime ? 2.7 * S : 0;
        double contentStartX = (showTime ? timeStartX + timeAdv : keyEndX) + 0.6 * S;
        return (clefX, keyStartX, timeStartX, contentStartX);
    }

    /// <summary>Groups a staff's measures into systems (lines). Notation-requested breaks
    /// (<see cref="Measure.SystemBreak"/>) are honoured as hard boundaries; each resulting run is kept on ONE
    /// line when it fits the width budget (allowing mild compression), and only wrapped by width when it
    /// genuinely doesn't. Free-form music (no breaks) is one run, so it simply wraps by width — the
    /// "3–6 measures per line" feel then emerges from the 80% budget, not an arbitrary count cap.</summary>
    private List<List<Measure>> BuildLines(Staff staff, StaffGeometry geom, int fifths, double maxW)
    {
        double budget = maxW - Header(geom, fifths, showTime: true).contentStartX - RightMargin;
        if (budget < 6 * S) budget = 6 * S;

        var lines = new List<List<Measure>>();
        var run = new List<Measure>();
        foreach (var m in staff.Measures)
        {
            run.Add(m);
            if (m.SystemBreak) { AddRun(lines, run, budget); run = []; }
        }
        if (run.Count > 0) AddRun(lines, run, budget);
        return lines;
    }

    /// <summary>Emits a notation run as one line when it fits the budget (up to ~1.35× — mild compression is
    /// fine and keeps the source's grouping); otherwise greedily wraps it by width.</summary>
    private void AddRun(List<List<Measure>> lines, List<Measure> run, double budget)
    {
        double natural = 0;
        foreach (var m in run) natural += MeasureWidth(m);
        if (run.Count <= 1 || natural <= budget * 1.35)
        {
            lines.Add(run);
            return;
        }

        var cur = new List<Measure>();
        double used = 0;
        foreach (var m in run)
        {
            double w = MeasureWidth(m);
            if (cur.Count > 0 && used + w > budget) { lines.Add(cur); cur = []; used = 0; }
            cur.Add(m); used += w;
        }
        if (cur.Count > 0) lines.Add(cur);
    }

    /// <summary>Fills a line to exactly <paramref name="targetRight"/> — every non-last line does this, so
    /// all non-last lines share one width regardless of how sparse the content is. No upper cap: capping the
    /// stretch would leave sparse lines short of the target and thus unequal. A compress floor keeps a very
    /// dense line legible.</summary>
    private static void JustifyFill(SystemLayout sys, double targetRight)
    {
        double span = sys.RightX - sys.ContentStartX;
        double target = targetRight - sys.ContentStartX;
        if (span <= 1 || target <= 0) return;
        Apply(sys, Math.Max(target / span, 0.5));
    }

    /// <summary>Leaves a line at its natural width, compressing only if it would overflow
    /// <paramref name="targetRight"/> (a notation-forced wide line). Never stretches.</summary>
    private static void JustifyNatural(SystemLayout sys, double targetRight)
    {
        double span = sys.RightX - sys.ContentStartX;
        double target = targetRight - sys.ContentStartX;
        if (span <= 1 || target <= 0 || span <= target) return;
        Apply(sys, Math.Max(target / span, 0.5));
    }

    private static void Apply(SystemLayout sys, double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.002) return;
        double c = sys.ContentStartX;
        foreach (var ml in sys.Measures)
        {
            ml.StartX = c + (ml.StartX - c) * scale;
            ml.EndX   = c + (ml.EndX   - c) * scale;
            foreach (var pe in ml.Events)
            {
                pe.HeadX    = c + (pe.HeadX - c) * scale;
                pe.SlotLeft = c + (pe.SlotLeft - c) * scale;
                pe.SlotRight = c + (pe.SlotRight - c) * scale;
                if (pe.HasAccidental) pe.AccX = pe.HeadX - 1.1 * S;   // keep accidental snug to its note
            }
        }
        sys.RightX = c + (sys.RightX - c) * scale;
    }

    private double MeasureWidth(Measure m)
    {
        double w = LeadPad(m) + TrailPad(m);
        foreach (var ev in m.Events) w += EventSlot(ev);
        return w;
    }

    private static double LeadPad(Measure m) => m.StartBarline == BarlineKind.RepeatStart ? 1.9 * S : 0.6 * S;
    private static double TrailPad(Measure m) =>
        m.EndBarline is BarlineKind.Final or BarlineKind.RepeatEnd or BarlineKind.RepeatBoth ? 2.2 * S : 1.0 * S;

    private double EventSlot(MusicalEvent ev)
    {
        double baseW = 0.95 * S + 0.6 * S * Math.Sqrt(Math.Max(0.25, ev.Duration.QuarterLength));
        if (ev is Note { Accidental: not AccidentalKind.None }) baseW += 1.1 * S;
        baseW += ev.Duration.Dots * 0.7 * S;
        return baseW;
    }

    private MeasureLayout PlaceMeasure(Measure m, double startX, StaffGeometry geom)
    {
        var ml = new MeasureLayout
        {
            StartX = startX, StartBarline = m.StartBarline, EndBarline = m.EndBarline,
        };
        double x = startX + LeadPad(m);
        foreach (var ev in m.Events)
        {
            double slot = EventSlot(ev);
            bool acc = ev is Note { Accidental: not AccidentalKind.None };
            double accW = acc ? 1.1 * S : 0;
            ml.Events.Add(new Placed { Ev = ev, AccX = x, HasAccidental = acc, HeadX = x + accW, SlotLeft = x, SlotRight = x + slot });
            x += slot;
        }
        ml.EndX = x + TrailPad(m);
        return ml;
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    private void DrawSystem(DrawingContext dc, SystemLayout sys, Pen linePen)
    {
        double left = sys.LeftX;
        double right = sys.RightX;

        // Staff lines.
        for (int k = 0; k <= 4; k++)
        {
            double ly = sys.TopLineY + k * S;
            dc.DrawLine(linePen, new Point(left, ly), new Point(right, ly));
        }

        // Clef.
        double clefRefY = sys.BottomLineY - sys.Geom.ClefRefHalfSpaces * (S / 2);
        DrawGlyph(dc, sys.Geom.ClefGlyph, sys.ClefX, clefRefY);

        // Key signature (positions precomputed in Header).
        DrawKeySignature(dc, sys, sys.KeyStartX);

        // Time signature.
        if (sys.ShowTime)
            DrawTimeSignature(dc, sys, sys.TimeStartX);

        // Measures.
        for (int j = 0; j < sys.Measures.Count; j++)
        {
            var ml = sys.Measures[j];

            // Left bar line for a repeat at the very start of the system.
            if (j == 0 && ml.StartBarline == BarlineKind.RepeatStart)
                DrawBarline(dc, ml.StartX, sys, BarlineKind.RepeatStart, linePen);

            DrawMeasureContents(dc, ml, sys);

            // End bar line — but a following repeat-start wins at this x.
            var endKind = ml.EndBarline;
            if (j + 1 < sys.Measures.Count && sys.Measures[j + 1].StartBarline == BarlineKind.RepeatStart)
                endKind = BarlineKind.RepeatStart;
            DrawBarline(dc, ml.EndX, sys, endKind, linePen);
        }
    }

    private void DrawKeySignature(DrawingContext dc, SystemLayout sys, double startX)
    {
        int fifths = sys.Key.Fifths;
        if (fifths == 0) return;
        bool sharp = fifths > 0;
        int n = Math.Min(Math.Abs(fifths), 7);
        int glyph = sharp ? Smufl.AccidentalSharp : Smufl.AccidentalFlat;
        double x = startX;
        for (int i = 0; i < n; i++)
        {
            int idx = sys.Geom.KeyAccidentalIndex(i, sharp);
            int hs = idx - sys.Geom.BottomLineIndex;
            DrawGlyph(dc, glyph, x, sys.BottomLineY - hs * (S / 2));
            x += 1.05 * S;
        }
    }

    private void DrawTimeSignature(DrawingContext dc, SystemLayout sys, double startX)
    {
        double numY = sys.BottomLineY - 3 * S;   // centred on the 4th line
        double denY = sys.BottomLineY - 1 * S;   // centred on the 2nd line
        DrawDigits(dc, sys.Time.Numerator, startX, numY);
        DrawDigits(dc, sys.Time.Denominator, startX, denY);
    }

    private void DrawDigits(DrawingContext dc, int value, double startX, double baselineY)
    {
        string s = value.ToString(CultureInfo.InvariantCulture);
        double total = 0;
        foreach (char ch in s) total += Smufl.Advance(Smufl.TimeSig0 + (ch - '0'), S, _ppd);
        double x = startX + 1.1 * S - total / 2;   // centre the digit stack in the time column
        foreach (char ch in s)
            x += DrawGlyph(dc, Smufl.TimeSig0 + (ch - '0'), x, baselineY);
    }

    private void DrawMeasureContents(DrawingContext dc, MeasureLayout ml, SystemLayout sys)
    {
        var beam = new List<Placed>();
        int beamId = 0;

        void FlushBeam()
        {
            if (beam.Count >= 2) DrawBeamGroup(dc, beam, sys);
            beam.Clear();
        }

        foreach (var pe in ml.Events)
        {
            if (pe.Ev is Rest rest) { FlushBeam(); DrawRest(dc, rest, pe.HeadX, sys); continue; }
            if (pe.Ev is not Note note) { FlushBeam(); continue; }

            DrawNotehead(dc, note, pe, sys);

            if (note.BeamId != 0)
            {
                if (beam.Count > 0 && beamId != note.BeamId) FlushBeam();
                beamId = note.BeamId;
                beam.Add(pe);
            }
            else
            {
                FlushBeam();
                if (note.Duration.Base >= 2) DrawStemAndFlag(dc, note, pe, sys);
            }
        }
        FlushBeam();
    }

    private void DrawNotehead(DrawingContext dc, Note note, Placed pe, SystemLayout sys)
    {
        int hs = sys.Geom.HalfSpacesAbove(note.Pitch);
        double cy = sys.BottomLineY - hs * (S / 2);

        // Ledger lines.
        var ledgerPen = Pen(StaffLineThick);
        double lx0 = pe.HeadX - 0.28 * S, lx1 = pe.HeadX + _noteheadW + 0.28 * S;
        if (hs < 0)
            for (int e = -2; e >= hs; e -= 2)
                dc.DrawLine(ledgerPen, new Point(lx0, sys.BottomLineY - e * (S / 2)), new Point(lx1, sys.BottomLineY - e * (S / 2)));
        else if (hs > 8)
            for (int e = 10; e <= hs; e += 2)
                dc.DrawLine(ledgerPen, new Point(lx0, sys.BottomLineY - e * (S / 2)), new Point(lx1, sys.BottomLineY - e * (S / 2)));

        // Accidental.
        if (pe.HasAccidental)
        {
            int g = note.Accidental switch
            {
                AccidentalKind.Sharp        => Smufl.AccidentalSharp,
                AccidentalKind.Flat         => Smufl.AccidentalFlat,
                AccidentalKind.Natural      => Smufl.AccidentalNatural,
                AccidentalKind.DoubleSharp  => Smufl.AccidentalDoubleSharp,
                AccidentalKind.DoubleFlat   => Smufl.AccidentalDoubleFlat,
                _                            => Smufl.AccidentalNatural,
            };
            DrawGlyph(dc, g, pe.AccX, cy);
        }

        int headGlyph = note.Duration.Base switch { 1 => Smufl.NoteheadWhole, 2 => Smufl.NoteheadHalf, _ => Smufl.NoteheadBlack };
        if (Smufl.Available) DrawGlyph(dc, headGlyph, pe.HeadX, cy);
        else DrawFallbackHead(dc, note, pe.HeadX, cy);

        // Augmentation dots (in the space; nudged up when the note is on a line).
        double dotY = (hs % 2 == 0) ? cy - S / 2 : cy;
        double dx = pe.HeadX + _noteheadW + 0.35 * S;
        for (int d = 0; d < note.Duration.Dots; d++)
        {
            if (Smufl.Available) dx += DrawGlyph(dc, Smufl.AugmentationDot, dx, dotY) + 0.15 * S;
            else { dc.DrawEllipse(_ink, null, new Point(dx + 0.15 * S, dotY), 0.12 * S, 0.12 * S); dx += 0.6 * S; }
        }
    }

    private void DrawFallbackHead(DrawingContext dc, Note note, double x, double cy)
    {
        var center = new Point(x + _noteheadW / 2, cy);
        double rx = 0.62 * S, ry = 0.46 * S;
        if (note.Duration.Base <= 2) dc.DrawEllipse(null, Pen(0.14 * S), center, rx, ry);   // open head
        else dc.DrawEllipse(_ink, null, center, rx, ry);                                     // filled head
    }

    private void DrawStemAndFlag(DrawingContext dc, Note note, Placed pe, SystemLayout sys)
    {
        int hs = sys.Geom.HalfSpacesAbove(note.Pitch);
        double cy = sys.BottomLineY - hs * (S / 2);
        bool down = hs >= 4;
        double stemX = down ? pe.HeadX + StemThick / 2 : pe.HeadX + _noteheadW - StemThick / 2;
        double endY = down ? cy + StemLen : cy - StemLen;
        dc.DrawLine(Pen(StemThick), new Point(stemX, cy), new Point(stemX, endY));

        int flags = note.Duration.FlagCount;
        if (flags > 0 && Smufl.Available)
        {
            int g = flags switch
            {
                1 => down ? Smufl.Flag8thDown : Smufl.Flag8thUp,
                2 => down ? Smufl.Flag16thDown : Smufl.Flag16thUp,
                _ => down ? Smufl.Flag32ndDown : Smufl.Flag32ndUp,
            };
            DrawGlyph(dc, g, stemX, endY);
        }
    }

    private void DrawBeamGroup(DrawingContext dc, List<Placed> group, SystemLayout sys)
    {
        double avgHs = 0;
        var stems = new List<(double x, double cy)>(group.Count);
        foreach (var pe in group)
        {
            var note = (Note)pe.Ev;
            int hs = sys.Geom.HalfSpacesAbove(note.Pitch);
            avgHs += hs;
            stems.Add((0, sys.BottomLineY - hs * (S / 2)));   // x filled below (needs direction)
        }
        avgHs /= group.Count;
        bool down = avgHs >= 4;

        for (int i = 0; i < group.Count; i++)
        {
            double headX = group[i].HeadX;
            double stemX = down ? headX + StemThick / 2 : headX + _noteheadW - StemThick / 2;
            stems[i] = (stemX, stems[i].cy);
        }

        double x0 = stems[0].x, x1 = stems[^1].x;

        // Sloped beam: a line through the first & last natural stem ends, slope clamped so it stays gentle.
        double e0 = down ? stems[0].cy + StemLen : stems[0].cy - StemLen;
        double e1 = down ? stems[^1].cy + StemLen : stems[^1].cy - StemLen;
        double dy = e1 - e0;
        double maxDy = 1.6 * S;
        if (Math.Abs(dy) > maxDy) dy = Math.Sign(dy) * maxDy;
        double slope = (x1 - x0) != 0 ? dy / (x1 - x0) : 0;
        double BeamY(double x) => e0 + slope * (x - x0);

        // Push the beam away from the heads until every stem is at least a minimum length.
        const double minStem = 2.5 * S;
        double shift = 0;
        foreach (var (x, cy) in stems)
        {
            double len = down ? BeamY(x) - cy : cy - BeamY(x);
            if (len < minStem) shift = Math.Max(shift, minStem - len);
        }
        e0 += down ? shift : -shift;

        foreach (var (x, cy) in stems)
            dc.DrawLine(Pen(StemThick), new Point(x, cy), new Point(x, BeamY(x)));

        // Beam bars — the b=0 bar sits on the stem ends; extra bars (16ths+) stack toward the heads.
        int bars = MaxFlags(group);
        for (int b = 0; b < bars; b++)
        {
            double off = b * (BeamThick + 0.32 * S);
            double n0 = down ? BeamY(x0) - off - BeamThick : BeamY(x0) + off;
            double n1 = down ? BeamY(x1) - off - BeamThick : BeamY(x1) + off;
            var fig = new PathFigure { StartPoint = new Point(x0, n0), IsClosed = true };
            fig.Segments.Add(new LineSegment(new Point(x1, n1), true));
            fig.Segments.Add(new LineSegment(new Point(x1, n1 + BeamThick), true));
            fig.Segments.Add(new LineSegment(new Point(x0, n0 + BeamThick), true));
            var geo = new PathGeometry([fig]);
            geo.Freeze();
            dc.DrawGeometry(_ink, null, geo);
        }
    }

    private static int MaxFlags(List<Placed> group)
    {
        int max = 1;
        foreach (var pe in group) max = Math.Max(max, ((Note)pe.Ev).Duration.FlagCount);
        return max;
    }

    private void DrawRest(DrawingContext dc, Rest rest, double x, SystemLayout sys)
    {
        int glyph = rest.Duration.Base switch
        {
            1 => Smufl.RestWhole,
            2 => Smufl.RestHalf,
            4 => Smufl.RestQuarter,
            8 => Smufl.Rest8th,
            _ => Smufl.Rest16th,
        };
        double y = rest.Duration.Base switch
        {
            1 => sys.BottomLineY - 3 * S,   // whole rest hangs from the 4th line
            2 => sys.BottomLineY - 2 * S,   // half rest sits on the middle line
            _ => sys.BottomLineY - 2 * S,   // others centred on the middle line
        };
        if (Smufl.Available) DrawGlyph(dc, glyph, x, y);
        else dc.DrawRectangle(_ink, null, new Rect(x, sys.BottomLineY - 2.2 * S, 0.9 * S, 0.5 * S));
    }

    private void DrawBarline(DrawingContext dc, double x, SystemLayout sys, BarlineKind kind, Pen thinPen)
    {
        double top = sys.TopLineY, bot = sys.BottomLineY;
        var thick = Pen(0.4 * S);
        switch (kind)
        {
            case BarlineKind.Double:
                dc.DrawLine(thinPen, new Point(x - 0.7 * S, top), new Point(x - 0.7 * S, bot));
                dc.DrawLine(thinPen, new Point(x, top), new Point(x, bot));
                break;
            case BarlineKind.Final:
                dc.DrawLine(thinPen, new Point(x - 0.9 * S, top), new Point(x - 0.9 * S, bot));
                dc.DrawLine(thick, new Point(x - 0.2 * S, top), new Point(x - 0.2 * S, bot));
                break;
            case BarlineKind.RepeatStart:
                dc.DrawLine(thick, new Point(x + 0.2 * S, top), new Point(x + 0.2 * S, bot));
                dc.DrawLine(thinPen, new Point(x + 0.9 * S, top), new Point(x + 0.9 * S, bot));
                RepeatDots(dc, x + 1.4 * S, sys);
                break;
            case BarlineKind.RepeatEnd:
                RepeatDots(dc, x - 1.4 * S, sys);
                dc.DrawLine(thinPen, new Point(x - 0.9 * S, top), new Point(x - 0.9 * S, bot));
                dc.DrawLine(thick, new Point(x - 0.2 * S, top), new Point(x - 0.2 * S, bot));
                break;
            case BarlineKind.RepeatBoth:
                RepeatDots(dc, x - 1.4 * S, sys);
                dc.DrawLine(thick, new Point(x, top), new Point(x, bot));
                RepeatDots(dc, x + 1.4 * S, sys);
                break;
            default:
                dc.DrawLine(thinPen, new Point(x, top), new Point(x, bot));
                break;
        }
    }

    private void RepeatDots(DrawingContext dc, double x, SystemLayout sys)
    {
        double r = 0.16 * S;
        dc.DrawEllipse(_ink, null, new Point(x, sys.BottomLineY - 1.5 * S), r, r);
        dc.DrawEllipse(_ink, null, new Point(x, sys.BottomLineY - 2.5 * S), r, r);
    }

    // ── Primitives ──────────────────────────────────────────────────────────

    private readonly Dictionary<double, Pen> _pens = [];
    private Pen Pen(double thickness)
    {
        if (_pens.TryGetValue(thickness, out var p)) return p;
        p = new Pen(_ink, thickness);
        p.Freeze();
        _pens[thickness] = p;
        return p;
    }

    private double DrawGlyph(DrawingContext dc, int codepoint, double x, double baselineY) =>
        Smufl.Draw(dc, codepoint, new Point(x, baselineY), S, _ink, _ppd);

    private void DrawText(DrawingContext dc, string text, Point anchor, double size, FontWeight weight, TextAlignment align)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BlockRenderer.BodyFont, FontStyles.Normal, weight, FontStretches.Normal),
            size, _ink, _ppd);
        double x = align switch
        {
            TextAlignment.Center => anchor.X - ft.Width / 2,
            TextAlignment.Right  => anchor.X - ft.Width,
            _                    => anchor.X,
        };
        dc.DrawText(ft, new Point(x, anchor.Y));
    }
}
