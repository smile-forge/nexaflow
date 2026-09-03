using Nexaflow.Visuals.Text.Editing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// A rendered barcode whose value can be edited where it stands.
///
/// <para>
/// The bars are a picture of the value and nothing else, so the value is the only thing here worth
/// editing — and editing it in place, rather than in the source behind it, is what makes the block feel
/// like a barcode rather than like a paragraph of settings. Typing re-encodes on every keystroke.
/// </para>
///
/// <para>
/// Which means it is invalid most of the time. Every value is unreadable while it is half-typed, so
/// "does not encode" cannot mean "show nothing": it means draw a barcode of the right shape for the
/// format, strike it through, wave under the text, and say why on hover. That is the same bargain the
/// formulas make, and it is why <see cref="Diagnostics"/> exists on the seam rather than in the formula.
/// </para>
///
/// <para>
/// Everything the document needs from it — selection, the caret arriving and leaving, what is wrong —
/// comes from <see cref="IEditableBlock"/>, so a host that can drive a formula drives this unchanged.
/// </para>
/// </summary>
public sealed class BarcodeElement : FrameworkElement, IEditableBlock
{
    /// <summary>
    /// The face the human-readable line is set in. OCR-B is what the retail standards actually specify —
    /// it is drawn to be unambiguous to a machine as well as to a person — but it ships with no operating
    /// system, so it is named first and the monospace stack catches the commoner case where it is absent.
    /// WPF resolves the list left to right, so this line is the whole of "use it when it is installed".
    /// </summary>
    private static readonly FontFamily LabelFont = new("OCR-B, OCRB, OCR B, Consolas, Menlo, monospace");

    private BarcodeBlock _block;
    private MarkdownPalette _palette;

    /// <summary>How far a guard bar runs past the others, in modules — the standard's figure.</summary>
    private const double GuardExtensionModules = 5;

    private BarcodePattern? _pattern;
    private string? _encodeError;

    /// <summary>The human-readable text as placed: each group, and where on the symbol it goes.</summary>
    private readonly List<(FormattedText Glyphs, Point At, BarcodeTextPlacement Where)> _runs = [];

    /// <summary>
    /// Every place the caret can stand, from before the first character to after the last — the x it is
    /// drawn at, and the row it belongs to.
    /// <para>
    /// The row travels with it because the text is not always one row: a retail number is broken across the
    /// two halves of the symbol and a digit of it sits out beside the bars, so two neighbouring offsets can
    /// be a long way apart and an add-on's digits are above the bars rather than below them.
    /// </para>
    /// </summary>
    private (double X, double Top, double Height)[] _caretEdges = [(0, 0, 0)];

    /// <summary>The caption over the whole symbol, for the numbering schemes that print one.</summary>
    private FormattedText? _caption;
    private Point _captionAt;

    /// <summary>Where the bars themselves start, and how far down a guard runs past the digits.</summary>
    private double _barsLeft, _barsTop, _guardDrop;

    /// <summary>
    /// The size the human-readable line is actually set at: the block's <c>fontSize</c>, reduced when
    /// that will not fit the space the bars leave for it. See <see cref="FittedLabelSize"/>.
    /// </summary>
    private double _labelSize;

    /// <summary>The size the caption is set at — smaller than the number, and its own to fit.</summary>
    private double _captionSize;

    /// <summary>The text row, for hit-testing a click that landed near it.</summary>
    private Rect _labelBounds;

    private readonly List<(int Start, int Length)> _selection = [];
    private int? _caret;
    private int? _dragAnchor;

    /// <summary>Where a shift-arrow selection started, so extending it walks from there and not from the caret.</summary>
    private int? _keyAnchor;

    /// <summary>Which string the text row was last laid out for — see <see cref="ShowsValue"/>.</summary>
    private bool _showingValue;

    /// <summary>
    /// Windows' own caret rate. WPF does not surface <c>GetCaretBlinkTime</c>, and a P/Invoke for one
    /// number is not worth the trouble; this is the default every version has shipped.
    /// </summary>
    private static readonly TimeSpan BlinkRate = TimeSpan.FromMilliseconds(530);

    private DispatcherTimer? _blink;
    private bool _caretVisible = true;

    public BarcodeElement(BarcodeBlock block, MarkdownPalette palette)
    {
        _block   = block;
        _palette = palette;

        // The host owns the keyboard and forwards to whichever block holds the caret, exactly as it does
        // for a formula — an embedded element that took focus for itself would fight the document for it.
        Focusable = false;

        // Air between one barcode and the next. The quiet zone inside the symbol is part of the symbol —
        // it is what a scanner needs either side of the bars — and being the same white as the ground it
        // separates nothing to the eye: a page of barcodes ran together into one field with bars in it.
        HorizontalAlignment = HorizontalAlignment.Left;
        Margin = new Thickness(0, 6, 0, 10);

        Unloaded += (_, _) => StopBlinking();

        Encode();
    }

    /// <summary>The value changed under the reader's typing; the host puts it back in the block source.</summary>
    public event EventHandler? ValueChanged;

    /// <inheritdoc/>
    public event EventHandler<BlockExit>? Exited;

    /// <summary>Where the value sits in the block that produced it, for splicing an edit back.</summary>
    public int ValueStart => _block.ValueStart;

    /// <summary>How long the value currently is — it changes as the reader types.</summary>
    public int ValueLength => _block.Value.Length;

    /// <summary>The value as it stands.</summary>
    public string Value => _block.Value;

    /// <summary>The encoded symbol, or null while the value cannot be read.</summary>
    public BarcodePattern? Pattern => _pattern;

    // ── What the document around it needs ──────────────────────────────────

    string IEditableBlock.Source => _block.Value;

    /// <summary>
    /// The seam's name for <see cref="ValueChanged"/> — what a host driving every editable block alike
    /// listens to, while the element's own event stays named after the thing that changed.
    /// </summary>
    event EventHandler? IEditableBlock.SourceChanged
    {
        add    => ValueChanged += value;
        remove => ValueChanged -= value;
    }

    /// <summary>
    /// The value's place inside the fenced block that produced it. Never negative: a barcode is always a
    /// run inside its fence, never the whole of the block the way a <c>$$</c> formula can be.
    /// </summary>
    int IEditableBlock.SourceStart => _block.ValueStart;

    /// <summary>
    /// None. A layout tree earns itself where content has structure a caret must be told about — which
    /// part of a fraction it is in, whether it is inside a script or past it. A barcode's value is one
    /// run of characters on one line, so a tree would be inventing structure to describe a flat string.
    /// </summary>
    ILayoutNode? IEditableBlock.Root => null;

    IReadOnlyList<(int Start, int Length)> IEditableBlock.Selection => _selection;

    /// <summary>
    /// The one thing that can be wrong here: the value is not something this format can carry. Reported
    /// over the whole value, because that is the span the reader has to change.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics =>
        _encodeError is null
            ? []
            : [new Diagnostic(0, Math.Max(_block.Value.Length, 1), DiagnosticSeverity.Error, _encodeError)];

    void IEditableBlock.SelectRange(int start, int length)
    {
        _selection.Clear();
        if (length > 0) _selection.Add((start, length));
        Refresh();
    }

    void IEditableBlock.TakeCaretArriving(CaretArrival arrival)
    {
        InteractiveSelection.Own(this);

        // Stepping along the text lands on the character stepped onto; stepping onto a line lands where
        // that line starts, whichever side the reader came from.
        _caret = arrival.Step == CaretStep.Character && arrival.Edge == BlockExit.After
            ? _block.Value.Length
            : 0;

        _keyAnchor = null;
        _selection.Clear();
        Refresh();
    }

    /// <summary>
    /// Gives the caret back, and with it the selection — something else on the page now has both.
    /// </summary>
    public void ReleaseCaret()
    {
        _caret     = null;
        _keyAnchor = null;
        _selection.Clear();
        InteractiveSelection.Release(this);
        Refresh();
    }

    /// <summary>
    /// Drops what is selected here, because another block or a click in the text took the selection.
    /// <para>
    /// The selection only. Losing the selection and losing the caret are different events with different
    /// causes — <see cref="ReleaseCaret"/> is the one that means "something else is being typed into now" —
    /// and clearing the caret from here would let any block that takes a selection anywhere on the page
    /// silently move the reader's insertion point.
    /// </para>
    /// </summary>
    public void ClearSelection()
    {
        if (_selection.Count == 0) return;

        _keyAnchor = null;
        _selection.Clear();
        InteractiveSelection.Release(this);
        Refresh();
    }

    // ── Pointer ───────────────────────────────────────────────────────────

    public void BeginPointerSelect(Point pointInElement)
    {
        InteractiveSelection.Own(this);

        _dragAnchor = OffsetAt(pointInElement);
        _caret      = _dragAnchor.Value;
        _selection.Clear();
        Refresh();
    }

    public void ExtendPointerSelect(Point pointInElement)
    {
        if (_dragAnchor is not { } anchor) return;

        int here = OffsetAt(pointInElement);
        _selection.Clear();
        if (here != anchor) _selection.Add((Math.Min(anchor, here), Math.Abs(here - anchor)));

        _caret = here;
        Refresh();
    }

    public void EndPointerSelect() => _dragAnchor = null;

    public bool PointerDoubleClick(Point pointInElement)
    {
        // A double click takes the whole value, which is the only word there is.
        InteractiveSelection.Own(this);
        _selection.Clear();
        if (_block.Value.Length > 0) _selection.Add((0, _block.Value.Length));
        _caret = _block.Value.Length;
        Refresh();
        return true;
    }

    /// <summary>
    /// The caret offset nearest a point — clamped to the value, so a click on the bars still lands.
    /// <para>
    /// Measured in the element's own coordinates against every place the caret can be, rather than along
    /// one row: the printed number can be in three pieces on two rows, and the nearest character to a
    /// click is not always in the group the click was over.
    /// </para>
    /// </summary>
    private int OffsetAt(Point point)
    {
        if (_caretEdges.Length <= 1) return 0;

        int nearest = 0;
        double best = double.MaxValue;

        for (int i = 0; i < _caretEdges.Length; i++)
        {
            var edge = _caretEdges[i];

            // Vertical distance to the row, horizontal distance along it. Weighted so a click level
            // with a group prefers that group over a nearer x on the row above or below.
            double dy = point.Y < edge.Top ? edge.Top - point.Y
                      : point.Y > edge.Top + edge.Height ? point.Y - edge.Top - edge.Height
                      : 0;

            double distance = Math.Abs(edge.X - point.X) + dy * 4;
            if (distance >= best) continue;

            best = distance;
            nearest = i;
        }

        return nearest;
    }

    // ── Editing ───────────────────────────────────────────────────────────

    /// <summary>Types a character at the caret, replacing whatever is selected.</summary>
    /// <returns>False when this block holds no caret, so the key was never its to take.</returns>
    public bool Type(char character)
    {
        if (!TryPlace(out int start, out int length)) return false;

        Replace(start, length, character.ToString());
        _caret = start + 1;
        return true;
    }

    /// <summary>
    /// Where an edit would land: the selection if there is one, otherwise the caret.
    /// <para>
    /// False when the block holds neither. A block with no caret is not the one being typed into, and one
    /// that acted anyway — by assuming the end of its value, say — would quietly edit something nobody was
    /// looking at.
    /// </para>
    /// </summary>
    private bool TryPlace(out int start, out int length)
    {
        if (_selection.Count > 0)
        {
            (start, length) = _selection[0];
            return true;
        }

        if (_caret is { } at)
        {
            start  = Math.Clamp(at, 0, _block.Value.Length);
            length = 0;
            return true;
        }

        start = length = 0;
        return false;
    }

    /// <summary>Backspace. False when there was nothing left to delete, so the document takes the key.</summary>
    public bool Backspace()
    {
        if (!TryPlace(out int start, out int length)) return false;
        if (length > 0) { Replace(start, length, string.Empty); _caret = start; return true; }
        if (start == 0) return false;

        Replace(start - 1, 1, string.Empty);
        _caret = start - 1;
        return true;
    }

    /// <summary>Forward delete. False when the caret is already at the end.</summary>
    public bool Delete()
    {
        if (!TryPlace(out int start, out int length)) return false;
        if (length > 0) { Replace(start, length, string.Empty); _caret = start; return true; }
        if (start >= _block.Value.Length) return false;

        Replace(start, 1, string.Empty);
        _caret = start;
        return true;
    }

    /// <summary>
    /// Moves the caret one character, extending the selection behind it when asked. False when it ran off
    /// an end, having raised <see cref="Exited"/> so the host can put the caret in the text beside us.
    /// </summary>
    public bool MoveCaret(bool forward, bool extend = false)
    {
        int at = _caret ?? 0;
        int next = at + (forward ? 1 : -1);

        if (next < 0 || next > _block.Value.Length)
        {
            Exited?.Invoke(this, forward ? BlockExit.After : BlockExit.Before);
            return false;
        }

        if (extend)
        {
            // The anchor is where extending started, not where the caret is now — that is what lets a
            // selection be walked back to nothing and out the other side without a second gesture.
            _keyAnchor ??= at;
            SelectBetween(_keyAnchor.Value, next);
        }
        else
        {
            _keyAnchor = null;
            _selection.Clear();
        }

        _caret = next;
        Refresh();
        return true;
    }

    /// <summary>Selects the stretch between two caret offsets, whichever way round they came.</summary>
    private void SelectBetween(int a, int b)
    {
        _selection.Clear();
        if (a == b) return;

        InteractiveSelection.Own(this);
        _selection.Add((Math.Min(a, b), Math.Abs(a - b)));
    }

    /// <summary>
    /// The seam's typing verb. The public <see cref="Type(char)"/> reports whether the key was ours to
    /// take; by the time the host calls this it already knows we hold the caret, so there is nothing to
    /// report back.
    /// </summary>
    void IEditableBlock.Type(char character) => Type(character);

    private void Replace(int start, int length, string with)
    {
        string value = _block.Value;
        start  = Math.Clamp(start, 0, value.Length);
        length = Math.Clamp(length, 0, value.Length - start);

        _block = _block.With(string.Concat(value.AsSpan(0, start), with, value.AsSpan(start + length)));
        _keyAnchor = null;
        _selection.Clear();

        Encode();
        InvalidateMeasure();
        Refresh();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-reads the value into bars, keeping the reason when it cannot be read.</summary>
    private void Encode()
    {
        if (_block.Value.Length == 0)
        {
            _pattern     = null;
            _encodeError = "A barcode needs a value.";
        }
        else if (BarcodeEncoder.TryEncode(_block.Format, _block.Value, out var pattern, out string? error))
        {
            _pattern     = pattern;
            _encodeError = null;
        }
        else
        {
            _pattern     = null;
            _encodeError = error;
        }

        ToolTip = _encodeError;
        BuildLabel();
    }

    /// <summary>
    /// Places the human-readable text against the bars, and works out every position the caret can take.
    ///
    /// <para>
    /// No layout tree. A tree earns itself where the content has structure a caret has to be told about —
    /// which part of a fraction it is in, whether it is inside a script or past it — and a barcode's value
    /// has none of that. It is one run of characters, and the whole of its geometry is where each of them
    /// landed; building nodes to hold that would be inventing structure to describe a flat string.
    /// </para>
    /// <para>
    /// Where they land is not always one row, which is the whole of the complication here. A retail
    /// number is broken at the guard bars into groups that sit in the wells between them, with a digit
    /// out beside the symbol and an add-on's digits above it. So a caret offset carries its row as well
    /// as its x, and the offsets either side of a break are simply far apart.
    /// </para>
    /// </summary>
    private void BuildLabel()
    {
        _runs.Clear();
        _showingValue = ShowsValue;
        _caption = null;

        // What goes under a real barcode is what was encoded — several of these formats add a check
        // digit, and the retail ones break the number into groups and set one of them outside the bars.
        // While the value will not encode there is nothing to show but what was typed.
        //
        // Except while it is being typed into, when what is shown is the value itself. The two are
        // different strings for most of these formats — an ISBN's value carries hyphens the symbol never
        // prints, an EAN-13's gains a check digit it never typed — and a caret placed against one while
        // it edits the other points at the wrong character, or at no character at all. It is the value
        // that is being edited, so it is the value the reader is shown editing.
        string text = ShowsValue ? _block.Value : _pattern?.Text ?? _block.Value;

        double barsWidth = PatternWidth * _block.BarWidth;

        var groups = Groups(text);

        _labelSize = FittedLabelSize(groups, barsWidth);
        double gap = _labelSize * 0.35;   // between the bars and a digit set outside them

        // The outside digits widen the symbol; everything else sits within the bars.
        double leftPad  = Width(groups, BarcodeTextPlacement.LeftOfBars,  gap);
        double rightPad = Width(groups, BarcodeTextPlacement.RightOfBars, gap);

        double mainWidth = MainSymbolModules() * _block.BarWidth;

        if (_pattern?.Caption is { Length: > 0 } caption)
        {
            _captionSize = FittedCaptionSize(caption, mainWidth);
            _caption     = Text(caption, _captionSize);
        }

        double content = Math.Max(leftPad + barsWidth + rightPad, _caption?.Width ?? 0);

        double captionHeight = _caption is null
            ? 0
            : _captionSize * 1.35 + CaptionSeparationModules * _block.BarWidth;
        double aboveHeight   = groups.Any(g => g.Placement == BarcodeTextPlacement.Above)
                             ? _labelSize * 1.35 : 0;

        _barsLeft  = _block.Margin + (content - (leftPad + barsWidth + rightPad)) / 2 + leftPad;
        _barsTop   = _block.Margin + captionHeight;
        // Only under the grouped number, whose wells they make. Over the value they run through the
        // middle of it, because the value is one run and there are no wells for them to be between.
        //
        // Five modules is the figure the retail standards give, and it is in modules rather than in
        // font size on purpose: everything else about a symbol's geometry is a multiple of the module,
        // and tying this to the label instead made the guards grow whenever the text did. The digits
        // are free to reach below the guards, which is what they do on a real pack.
        _guardDrop = ShowsValue ? 0 : _block.BarWidth * GuardExtensionModules;

        // Over the main symbol's middle, not the whole picture's: with an add-on beside it those are
        // several modules apart, and a title that drifts towards the price reads as belonging to it.
        if (_caption is not null)
            _captionAt = new Point(_barsLeft + (mainWidth - _caption.Width) / 2, _block.Margin);

        double belowTop = _barsTop + _block.BarHeight;
        double aboveTop = _barsTop;

        foreach (var group in groups)
        {
            var glyphs = Text(group.Text);

            var at = group.Placement switch
            {
                BarcodeTextPlacement.LeftOfBars =>
                    new Point(_barsLeft - gap - glyphs.Width, belowTop),

                BarcodeTextPlacement.RightOfBars =>
                    new Point(_barsLeft + barsWidth + gap, belowTop),

                BarcodeTextPlacement.Above =>
                    new Point(Centred(group, glyphs, barsWidth), aboveTop),

                _ => new Point(Centred(group, glyphs, barsWidth), belowTop),
            };

            _runs.Add((glyphs, at, group.Placement));
        }

        MeasuredSize = new Size(
            content + _block.Margin * 2,
            captionHeight + _block.BarHeight + LabelHeight + _block.Margin * 2);

        BuildCaretEdges(groups);

        // The rows the caret can be on, taken together — what a click near the text is measured against.
        _labelBounds = _runs.Count == 0
            ? new Rect(_barsLeft, belowTop, barsWidth, LabelHeight)
            : new Rect(
                _runs.Min(r => r.At.X), belowTop,
                Math.Max(1, _runs.Max(r => r.At.X + r.Glyphs.Width) - _runs.Min(r => r.At.X)), LabelHeight);

        double Centred(BarcodeTextRun group, FormattedText glyphs, double bars) =>
            group.Modules > 0
                ? _barsLeft + (group.StartModule + group.Modules / 2.0) * _block.BarWidth - glyphs.Width / 2
                : _barsLeft + (bars - glyphs.Width) / 2;
    }

    /// <summary>
    /// Whether the text row is showing the value rather than the number the symbol prints.
    /// <para>
    /// Only while the reader is in it, and only when the two differ — which is most of the retail
    /// family and none of the rest. A Code 128 prints exactly what was typed, so nothing changes under
    /// the caret and there is no reason to swap anything out.
    /// </para>
    /// </summary>
    private bool ShowsValue =>
        (_caret is not null || _selection.Count > 0) && (_pattern?.Text ?? _block.Value) != _block.Value;

    /// <summary>
    /// The text broken into the groups this symbology prints, or one group holding all of it — which is
    /// what every format outside the retail family wants, what a value that will not encode gets, and
    /// what the value itself gets while it is being edited.
    /// </summary>
    private IReadOnlyList<BarcodeTextRun> Groups(string text) =>
        !ShowsValue && _pattern?.TextRuns is { Count: > 0 } runs
            ? runs
            : [new BarcodeTextRun(text, 0, PatternWidth, BarcodeTextPlacement.Below)];

    /// <summary>
    /// Redraws, laying the text out again first when the caret arriving or leaving has changed which
    /// string it shows.
    /// <para>
    /// A measure pass and not only a paint, because the two strings are different lengths: a value
    /// swapped in for the number the symbol prints changes how wide the element wants to be, and a
    /// repaint alone would draw the new text into the old box.
    /// </para>
    /// </summary>
    private void Refresh()
    {
        if (_showingValue != ShowsValue)
        {
            BuildLabel();
            InvalidateMeasure();
        }

        HoldCaretVisible();
        InvalidateVisual();
    }

    /// <summary>
    /// Blinks the caret, because a still one among a row of digits reads as part of the printing rather
    /// than as an insertion point. It runs only while this barcode holds the caret and is torn down on
    /// unload, so a page of barcodes leaves no timers behind.
    /// </summary>
    private void StartBlinking()
    {
        _caretVisible = true;
        if (_blink is not null) { _blink.Stop(); _blink.Start(); return; }

        _blink = new DispatcherTimer(BlinkRate, DispatcherPriority.Normal, OnBlink, Dispatcher);
        _blink.Start();
    }

    private void StopBlinking()
    {
        _blink?.Stop();
        _blink = null;
        _caretVisible = true;
    }

    private void OnBlink(object? sender, EventArgs e)
    {
        if (_caret is null) { StopBlinking(); return; }

        _caretVisible = !_caretVisible;
        InvalidateVisual();
    }

    /// <summary>Shows the caret and restarts the cycle — it must never be mid-blink while you type.</summary>
    private void HoldCaretVisible()
    {
        if (_caret is null) { StopBlinking(); return; }

        _caretVisible = true;
        StartBlinking();
    }

    private double Width(IReadOnlyList<BarcodeTextRun> groups, BarcodeTextPlacement where, double gap)
    {
        double widest = 0;
        foreach (var group in groups)
            if (group.Placement == where) widest = Math.Max(widest, Text(group.Text).Width + gap);
        return widest;
    }

    /// <summary>
    /// Where the caret can stand, walking the groups in the order they were printed.
    /// <para>
    /// The groups concatenate back to the encoded text, so an offset into the value is an offset into
    /// that walk — up to wherever the two stop agreeing, which is what a computed check digit does to
    /// the end of the string. Past the last character the caret rests at the end of the last group,
    /// which is where typing appends.
    /// </para>
    /// </summary>
    private void BuildCaretEdges(IReadOnlyList<BarcodeTextRun> groups)
    {
        var edges = new List<(double, double, double)>(_block.Value.Length + 1);

        for (int i = 0; i < _runs.Count; i++)
        {
            var (glyphs, at, _) = _runs[i];
            string run = groups[i].Text;
            double height = glyphs.Height;

            for (int c = 0; c < run.Length; c++)
                edges.Add((at.X + Text(run[..c]).Width, at.Y, height));

            // The far end of the last group is a place to stand; the far end of any other is the near
            // end of the next one, and adding both would give the reader two carets for one offset.
            if (i == _runs.Count - 1) edges.Add((at.X + glyphs.Width, at.Y, height));
        }

        if (edges.Count == 0) edges.Add((_barsLeft, _barsTop + _block.BarHeight, _block.FontSize));

        // One per place the caret can be in the *value*, which is what the offsets index. A value longer
        // than what was drawn (it will not encode, so nothing was) rests at the end.
        _caretEdges = new (double, double, double)[_block.Value.Length + 1];
        for (int i = 0; i < _caretEdges.Length; i++)
            _caretEdges[i] = edges[Math.Min(i, edges.Count - 1)];
    }

    /// <summary>
    /// The size to set the human-readable line at: what the block asked for, reduced until every run
    /// fits the space its bars leave for it.
    ///
    /// <para>
    /// A retail number is not one line of text but a run per group, each belonging under a stretch of
    /// bars — the wells the guards make. A point size is the wrong thing to state that in, because
    /// whether it fits depends on the module width and on the face: OCR-B, which is what these symbols
    /// are meant to be set in, is appreciably wider than a programmer's monospace at the same size, so
    /// a number that fitted in one spills across the guards in the other. Fitting to the wells makes
    /// the label a property of the symbol's geometry, which is what it is on a real pack, and leaves
    /// <c>fontSize</c> meaning "no larger than this".
    /// </para>
    /// </summary>
    private double FittedLabelSize(IReadOnlyList<BarcodeTextRun> groups, double barsWidth)
    {
        _labelSize = 0;                       // measure at the asked-for size, then scale
        double scale = 1;

        foreach (var group in groups)
        {
            // A group set outside the bars has the margin to itself and constrains nothing.
            if (group.Modules <= 0 || group.Placement is BarcodeTextPlacement.LeftOfBars
                                                      or BarcodeTextPlacement.RightOfBars) continue;

            double natural = Text(group.Text).Width;
            if (natural <= 0) continue;

            double room = group.Modules * _block.BarWidth * WellFill;
            scale = Math.Min(scale, room / natural);
        }

        return Math.Max(_block.FontSize * scale, MinimumLabelSize);
    }

    /// <summary>
    /// The size to set the caption at. It is a title rather than part of the number, so it is set
    /// smaller — as it is on a book's cover — and it belongs to the main symbol: an ISBN's caption names
    /// the number the main symbol carries, not the price add-on standing beside it, so it is measured
    /// and centred over that symbol alone rather than stretched across the pair.
    /// </summary>
    private double FittedCaptionSize(string caption, double mainWidth)
    {
        _captionSize = 0;
        double size = LabelSize * CaptionScale;

        double natural = Text(caption, size).Width;
        if (natural > mainWidth && natural > 0) size *= mainWidth / natural;

        return Math.Max(size, MinimumLabelSize);
    }

    /// <summary>How much smaller the caption is set than the number under the bars.</summary>
    private const double CaptionScale = 0.62;

    /// <summary>Clear air between the caption and the bars, in modules, on top of the line's own leading.</summary>
    private const double CaptionSeparationModules = 1.5;

    /// <summary>
    /// How wide the main symbol is, in modules — everything before an add-on, or the lot when there is
    /// none. The gap belongs to neither, so it is the last ink before the add-on that ends the symbol.
    /// </summary>
    private double MainSymbolModules()
    {
        int addOn = AddOnStartModule();
        if (_pattern is null || addOn == int.MaxValue) return PatternWidth;

        int end = 0;
        foreach (var (start, length) in _pattern.InkRuns())
            if (start < addOn) end = Math.Max(end, start + length);

        return end > 0 ? end : PatternWidth;
    }

    /// <summary>How much of a well its digits may fill, leaving the guards and the neighbours clear.</summary>
    private const double WellFill = 0.92;

    /// <summary>Below this the line is unreadable, and a symbol with no legible number is worse than a wide one.</summary>
    private const double MinimumLabelSize = 4;

    /// <summary>What <see cref="MeasureOverride"/> reports — worked out once, while the text is placed.</summary>
    private Size MeasuredSize { get; set; }

    private FormattedText Text(string text, double? size = null) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(LabelFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size ?? LabelSize,
        Brushes.Black,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ── Layout ────────────────────────────────────────────────────────────

    /// <summary>The bars as drawn — the shape of the symbol, or of a stand-in when there is none.</summary>
    private int PatternWidth => (_pattern ?? Placeholder)?.Width ?? 0;

    /// <summary>
    /// A valid symbol in the asked-for format, drawn faint behind the error when the real value will not
    /// encode. A barcode-shaped absence reads as "this is a barcode, and it is wrong"; an empty gap reads
    /// as a rendering fault.
    /// </summary>
    private BarcodePattern? Placeholder =>
        BarcodeEncoder.TryEncode(_block.Format, BarcodeEncoder.SampleValue(_block.Format), out var sample, out _)
            ? sample
            : null;

    private double LabelHeight => _block.DisplayValue ? LabelSize * 1.4 : 0;

    /// <summary>The size the label is set at, before it has been fitted.</summary>
    private double LabelSize => _labelSize > 0 ? _labelSize : _block.FontSize;

    protected override Size MeasureOverride(Size availableSize) => MeasuredSize;

    // ── Drawing ───────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var pattern = _pattern ?? Placeholder;

        dc.DrawRectangle(Brush(_block.Background, _palette.BarcodeLight), null, new Rect(MeasuredSize));

        var ink = Brush(_block.LineColor, _palette.BarcodeDark);

        if (_caption is not null)
        {
            _caption.SetForegroundBrush(ink);
            dc.DrawText(_caption, _captionAt);
        }

        // The bars. Faint when they are a stand-in, so the error reads as the subject and they read as
        // the shape it would have taken.
        if (pattern is not null) DrawBars(dc, pattern, _pattern is null ? Faded(ink) : ink);

        if (_block.DisplayValue)
        {
            DrawSelection(dc);

            foreach (var (glyphs, at, _) in _runs)
            {
                glyphs.SetForegroundBrush(ink);
                dc.DrawText(glyphs, at);
            }

            DrawDiagnostics(dc);
            DrawCaret(dc);
        }

        // The strike across the symbol. Last, so it sits over the bars it is about.
        if (_encodeError is not null && pattern is not null)
        {
            double y = _barsTop + _block.BarHeight / 2;
            var bar = new Rect(_barsLeft, y - Math.Max(_block.BarHeight * 0.04, 1.5),
                               PatternWidth * _block.BarWidth, Math.Max(_block.BarHeight * 0.08, 3));
            dc.DrawRectangle(_palette.Danger, null, bar);
        }
    }

    /// <summary>
    /// Draws the bars, dropping the guards past the digits and lifting an add-on clear of its own.
    /// <para>
    /// Both are what makes a retail symbol recognisable at a glance: the guards frame the two halves of
    /// the number, and the add-on stands apart and higher so it reads as a second symbol rather than as
    /// more of the first.
    /// </para>
    /// </summary>
    private void DrawBars(DrawingContext dc, BarcodePattern pattern, Brush ink)
    {
        double addOnFrom = AddOnStartModule();
        double lift      = addOnFrom < int.MaxValue ? LabelSize * 1.35 : 0;

        foreach (var (start, length) in pattern.InkRuns())
        {
            bool addOn = start >= addOnFrom;

            double top    = _barsTop + (addOn ? lift : 0);
            double height = _block.BarHeight - (addOn ? lift : 0) + (IsGuard(pattern, start) ? _guardDrop : 0);

            dc.DrawRectangle(ink, null, new Rect(
                _barsLeft + start * _block.BarWidth, top, length * _block.BarWidth, height));
        }
    }

    /// <summary>Whether a run of ink begins inside one of the symbol's guard patterns.</summary>
    private static bool IsGuard(BarcodePattern pattern, int start)
    {
        foreach (var (from, length) in pattern.Guards)
            if (start >= from && start < from + length) return true;
        return false;
    }

    /// <summary>The first module of the add-on, or <see cref="int.MaxValue"/> when there is none.</summary>
    private int AddOnStartModule()
    {
        if (_pattern is null) return int.MaxValue;

        int first = int.MaxValue;
        foreach (var run in _pattern.TextRuns)
            if (run.Placement == BarcodeTextPlacement.Above && run.Modules > 0 && run.StartModule > 0)
                first = Math.Min(first, run.StartModule);

        return first;
    }

    private void DrawSelection(DrawingContext dc)
    {
        foreach (var (start, length) in _selection)
        {
            // A selection that spans a break in the printed number is washed group by group, because
            // between the groups there is nothing selected — there is a guard bar.
            for (int i = start; i < start + length; i++)
            {
                var from = EdgeAt(i);
                var to   = EdgeAt(i + 1);
                if (to.X <= from.X || to.Top != from.Top) continue;

                dc.DrawRectangle(Faded(_palette.Accent), null,
                    new Rect(from.X, from.Top, to.X - from.X, from.Height));
            }
        }
    }

    private void DrawDiagnostics(DrawingContext dc)
    {
        if (_encodeError is null) return;

        // The wave every editor has drawn under a mistake for thirty years — it needs no explaining, and
        // the reason is a hover away. One run under each group of the value: it is all of it that is
        // wrong, since a format rejects a value entire rather than at a character.
        var runs = _runs
            .Where(r => r.Where is BarcodeTextPlacement.Below or BarcodeTextPlacement.LeftOfBars
                                or BarcodeTextPlacement.RightOfBars)
            .Select(r => new Rect(r.At.X, r.At.Y, Math.Max(r.Glyphs.Width, _block.FontSize), r.Glyphs.Height))
            .ToArray();

        if (runs.Length == 0) return;

        dc.DrawGeometry(null, new Pen(_palette.Danger, 1.2), Squiggle.Under(runs));
    }

    /// <summary>
    /// The caret, in the same ink as the value it stands in.
    /// <para>
    /// Not the theme's text brush, which is what it used to be and what made it invisible: a barcode
    /// paints its own light ground whatever the theme, because a scanner needs dark bars on a light
    /// field — so under a dark theme the caret was drawn very nearly white on white. Whatever colour the
    /// digits are readable in, the caret between them is readable in too.
    /// </para>
    /// </summary>
    private void DrawCaret(DrawingContext dc)
    {
        if (_caret is not { } at || !_caretVisible) return;

        var edge = EdgeAt(at);

        // Wide enough to survive being scaled down to fit the column: at a hairline the caret thins to
        // nothing on the first fractional scale and the reader is left typing blind.
        dc.DrawRectangle(Brush(_block.LineColor, _palette.BarcodeDark), null,
            new Rect(edge.X, edge.Top, Math.Max(1.5, _block.FontSize / 12), edge.Height));
    }

    /// <summary>Where the caret stands at an offset, clamped to what was actually drawn.</summary>
    private (double X, double Top, double Height) EdgeAt(int offset) =>
        _caretEdges[Math.Clamp(offset, 0, _caretEdges.Length - 1)];

    // ── Drawing ───────────────────────────────────────────────────────────

    // ── Brushes ───────────────────────────────────────────────────────────

    private static Brush Brush(HexColor? explicitColor, Brush fallback)
    {
        if (explicitColor is not { } c) return fallback;

        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>The same colour at a quarter strength — for a stand-in symbol and a selection wash.</summary>
    private static Brush Faded(Brush brush)
    {
        var faded = brush.Clone();
        faded.Opacity = 0.25;
        faded.Freeze();
        return faded;
    }

    /// <summary>Re-themes without rebuilding, for a theme change under a rendered document.</summary>
    public void Retheme(MarkdownPalette palette)
    {
        _palette = palette;
        InvalidateVisual();
    }
}
