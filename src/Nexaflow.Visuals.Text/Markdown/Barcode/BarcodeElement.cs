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
/// A rendered barcode: drawn, selectable, and editable where — and only where — what is drawn is what
/// was typed.
///
/// <para>
/// Most of these formats do not print their input. An EAN-13 works out a thirteenth digit, an ISBN takes
/// the hyphens out and prints a caption over the top, a UPC-E fills in both ends. What is on the page is
/// then a rendering of the value rather than the value, and a caret placed in it would be editing one
/// string while pointing at another — so it takes no caret, and the reader edits it in the source behind
/// it. Which parts are which is worked out in <see cref="BarcodePart"/> and asked here as
/// <see cref="BarcodeLayout.AcceptsCaret"/>.
/// </para>
/// <para>
/// Where the two are the same string — a Code 128, and a value that will not encode at all, which is
/// shown exactly as typed — the value is edited in place and re-encodes on every keystroke. Which means
/// it is invalid much of the time, so "does not encode" cannot mean "show nothing": it means draw a
/// barcode of the right shape for the format, strike it through, wave under the text, and say why on
/// hover. That is the same bargain the formulas make.
/// </para>
/// <para>
/// Everything the document needs from it — selection, the caret arriving and leaving, what is wrong —
/// comes from <see cref="IEditableBlock"/>, so a host that can drive a formula drives this unchanged.
/// </para>
/// </summary>
public sealed class BarcodeElement : FrameworkElement, IEditableBlock
{
    private BarcodeBlock _block;
    private MarkdownPalette _palette;

    private BarcodePattern? _pattern;
    private string? _encodeError;

    /// <summary>
    /// Where every piece of the symbol landed, what it drew, and which characters of the value each
    /// piece stands for. Null only until the first encode, which the constructor does.
    /// </summary>
    private BarcodeLayout? _layout;

    private readonly List<(int Start, int Length)> _selection = [];
    private int? _caret;

    /// <summary>
    /// Which of the bars at the caret's offset it is drawn as. There is more than one wherever a piece of
    /// the number ends and another begins somewhere else on the page — the two halves of a retail symbol,
    /// with a guard between them — and those are different places to stand for one offset.
    /// </summary>
    private int _level;

    /// <summary>Where a pointer drag began, as a piece of the layout rather than as an offset.</summary>
    private ILayoutNode? _dragAnchor;

    /// <summary>Where a shift-arrow selection started, so extending it walks from there and not from the caret.</summary>
    private int? _keyAnchor;

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

    /// <summary>
    /// Whether the caret belongs in this symbol at all — whether any of what it prints really is the
    /// value. False for every format that transforms its input, which is most of them.
    /// </summary>
    public bool AcceptsCaret => _layout?.AcceptsCaret ?? false;

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
    /// Every piece of the symbol and where it landed, which is what the shared queries read to answer
    /// where a press went, what a drag took and where the caret can stand.
    /// </summary>
    ILayoutNode? IEditableBlock.Root => _layout?.Root;

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
        // Nothing printed here is the value, so there is nowhere in it to stand. The caret is handed
        // straight on the way it was already going, and the reader arrows over the symbol as they would
        // over a word — rather than into it, to find that no key does anything.
        if (!AcceptsCaret)
        {
            Exited?.Invoke(this, arrival.Edge == BlockExit.Before ? BlockExit.After : BlockExit.Before);
            return;
        }

        InteractiveSelection.Own(this);

        // Stepping along the text lands on the character stepped onto; stepping onto a line lands where
        // that line starts, whichever side the reader came from.
        _caret = arrival.Step == CaretStep.Character && arrival.Edge == BlockExit.After
            ? _block.Value.Length
            : 0;

        _level     = 0;
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
        if (_layout?.Root is not { } root) return;

        _dragAnchor = root.NodeAt(pointInElement);
        _selection.Clear();
        _caret = AcceptsCaret ? CaretNear(root, pointInElement) : null;
        _level = 0;
        Refresh();
    }

    public void ExtendPointerSelect(Point pointInElement)
    {
        if (_dragAnchor is null || _layout?.Root is not { } root) return;
        if (root.NodeAt(pointInElement) is not { } focus) return;

        // Whole pieces rather than a stretch of characters, so what comes back is something the format
        // really is made of — a group of the number, or the number — and never half of a thing worked out
        // from the value, which would stand for a stretch of source it does not cover.
        _selection.Clear();
        _selection.AddRange(Taken(root, _dragAnchor, focus));

        if (AcceptsCaret) _caret = CaretNear(root, pointInElement);
        Refresh();
    }

    public void EndPointerSelect() => _dragAnchor = null;

    public bool PointerDoubleClick(Point pointInElement)
    {
        // A double click takes the whole value, which is the only word there is.
        InteractiveSelection.Own(this);
        _selection.Clear();
        if (_block.Value.Length > 0) _selection.Add((0, _block.Value.Length));
        if (AcceptsCaret) _caret = _block.Value.Length;
        Refresh();
        return true;
    }

    /// <summary>
    /// The caret offset nearest a point: the side of the piece under it that the point fell on.
    /// <para>
    /// Node-based rather than measured against a list of positions, so the printed number being in three
    /// pieces on two rows needs no special handling — the piece under the pointer already knows which
    /// characters it is.
    /// </para>
    /// </summary>
    private static int CaretNear(ILayoutNode root, Point point)
    {
        if (root.NodeAt(point) is not { } node) return 0;

        return point.X <= node.Bounds.X + node.Bounds.Width / 2
            ? node.Sits().Start
            : node.Sits().End;
    }

    // ── Editing ───────────────────────────────────────────────────────────

    /// <summary>Types a character at the caret, replacing whatever is selected.</summary>
    /// <returns>False when this block holds no caret, so the key was never its to take.</returns>
    public bool Type(char character)
    {
        if (!AcceptsCaret || !TryPlace(out int start, out int length)) return false;

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
        if (!AcceptsCaret || !TryPlace(out int start, out int length)) return false;
        if (length > 0) { Replace(start, length, string.Empty); _caret = start; return true; }
        if (start == 0) return false;

        Replace(start - 1, 1, string.Empty);
        _caret = start - 1;
        return true;
    }

    /// <summary>Forward delete. False when the caret is already at the end.</summary>
    public bool Delete()
    {
        if (!AcceptsCaret || !TryPlace(out int start, out int length)) return false;
        if (length > 0) { Replace(start, length, string.Empty); _caret = start; return true; }
        if (start >= _block.Value.Length) return false;

        Replace(start, 1, string.Empty);
        _caret = start;
        return true;
    }

    /// <summary>
    /// Moves the caret one stop, extending the selection behind it when asked. False when it ran off an
    /// end, having raised <see cref="Exited"/> so the host can put the caret in the text beside us.
    /// <para>
    /// A stop rather than a character, because the two are not the same thing here: a retail number is
    /// broken at the guard bars, so the end of one group and the start of the next are one offset in two
    /// places, and a reader arrowing along expects to visit both.
    /// </para>
    /// </summary>
    public bool MoveCaret(bool forward, bool extend = false)
    {
        if (!AcceptsCaret || _layout?.Root is not { } root)
        {
            Exited?.Invoke(this, forward ? BlockExit.After : BlockExit.Before);
            return false;
        }

        var from = new CaretPlace(_caret ?? 0, _level);
        if (root.Step(from, forward) is not { } next)
        {
            Exited?.Invoke(this, forward ? BlockExit.After : BlockExit.Before);
            return false;
        }

        if (extend)
        {
            // The anchor is where extending started, not where the caret is now — that is what lets a
            // selection be walked back to nothing and out the other side without a second gesture.
            _keyAnchor ??= from.Offset;
            SelectBetween(_keyAnchor.Value, next.Offset);
        }
        else
        {
            _keyAnchor = null;
            _selection.Clear();
        }

        _caret = next.Offset;
        _level = next.Level;
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
        Rebuild();
    }

    /// <summary>
    /// Lays the symbol out again: where each piece of it goes, what it draws, and which characters of the
    /// value each piece stands for.
    /// </summary>
    private void Rebuild() =>
        _layout = BarcodeLayout.Build(
            _block, _pattern, Placeholder, _palette, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>
    /// A valid symbol in the asked-for format, drawn faint behind the error when the real value will not
    /// encode. A barcode-shaped absence reads as "this is a barcode, and it is wrong"; an empty gap reads
    /// as a rendering fault.
    /// </summary>
    private BarcodePattern? Placeholder =>
        BarcodeEncoder.TryEncode(_block.Format, BarcodeEncoder.SampleValue(_block.Format), out var sample, out _)
            ? sample
            : null;

    /// <summary>Redraws, and keeps the caret visible while it is being moved or typed at.</summary>
    private void Refresh()
    {
        HoldCaretVisible();
        InvalidateVisual();
    }

    // ── The caret's blink ─────────────────────────────────────────────────

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

    // ── Drawing ───────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize) => _layout?.Size ?? new Size();

    /// <summary>
    /// Paints the symbol out of its own layout, with the reader's own marks over it.
    /// <para>
    /// The selection wash goes between the two layers the layout paints in, so it lands over the bars and
    /// under the digits — a wash drawn over the number greys the very thing it is meant to be pointing at.
    /// </para>
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (_layout is not { } layout) return;

        var ink = Brush(_block.LineColor, _palette.BarcodeDark);

        layout.Paint(dc, ink, underTheText: DrawSelection);

        DrawDiagnostics(dc, layout);
        DrawCaret(dc, layout);

        // The strike across the symbol. Last, so it sits over the bars it is about.
        if (_encodeError is not null && layout.Bars is { } bars)
        {
            double y = bars.Y + _block.BarHeight / 2;
            dc.DrawRectangle(_palette.Danger, null, new Rect(
                bars.X,
                y - Math.Max(_block.BarHeight * 0.04, 1.5),
                bars.Width,
                Math.Max(_block.BarHeight * 0.08, 3)));
        }
    }

    /// <summary>
    /// The wash behind what is selected, a piece at a time.
    /// <para>
    /// Only over what is printed as text: a selection reaching the whole value covers the bars too, and
    /// washing those makes the symbol look unscannable when nothing about it has changed.
    /// </para>
    /// </summary>
    private void DrawSelection(DrawingContext dc)
    {
        if (_selection.Count == 0 || _layout?.Root is not { } root) return;

        var wash = Faded(_palette.Accent);

        foreach (var node in root.Ink())
        {
            // Generated printing holds no place in the source, so it has no span here to compare — but it
            // was worked out from the whole value, so it is washed when the whole value is taken.
            var (start, length) = Generated(node)
                ? (0, _block.Value.Length)
                : (node.Sits().Start, node.Sits().Length);

            if (length == 0) continue;

            foreach (var (from, over) in _selection)
                if (start >= from && start + length <= from + over)
                {
                    dc.DrawRectangle(wash, null, node.Bounds);
                    break;
                }
        }
    }

    /// <summary>Whether a piece of the layout is printing that was worked out rather than typed.</summary>
    private static bool Generated(ILayoutNode node) => node.Kind == nameof(BarcodeKind.EncodedText);

    /// <summary>
    /// What a drag from one piece to another took.
    /// <para>
    /// Pointing at anything the format worked out means the whole value, because that is what it was
    /// worked out from: there is no smaller answer, and a check digit stands for all the digits rather
    /// than for the one it is printed beside.
    /// </para>
    /// </summary>
    private IReadOnlyList<(int Start, int Length)> Taken(ILayoutNode root, ILayoutNode anchor, ILayoutNode focus) =>
        Generated(anchor) || Generated(focus)
            ? [(0, _block.Value.Length)]
            : ContentSelection.Between(root, anchor, focus).Ranges;

    private void DrawDiagnostics(DrawingContext dc, BarcodeLayout layout)
    {
        if (_encodeError is null || layout.LabelRuns.Count == 0) return;

        // The wave every editor has drawn under a mistake for thirty years — it needs no explaining, and
        // the reason is a hover away.
        dc.DrawGeometry(null, new Pen(_palette.Danger, 1.2), Squiggle.Under(layout.LabelRuns));
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
    private void DrawCaret(DrawingContext dc, BarcodeLayout layout)
    {
        if (_caret is not { } at || !_caretVisible) return;

        var bar = layout.Root.CaretRect(new CaretPlace(at, _level));

        // Wide enough to survive being scaled down to fit the column: at a hairline the caret thins to
        // nothing on the first fractional scale and the reader is left typing blind.
        dc.DrawRectangle(Brush(_block.LineColor, _palette.BarcodeDark), null,
            new Rect(bar.X, bar.Y, Math.Max(1.5, _block.FontSize / 12), bar.Height));
    }

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

    /// <summary>Re-themes without re-encoding, for a theme change under a rendered document.</summary>
    public void Retheme(MarkdownPalette palette)
    {
        _palette = palette;
        Rebuild();
        InvalidateVisual();
    }
}
