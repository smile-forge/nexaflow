using Nexaflow.Visuals.Text.Editing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

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
    private static readonly FontFamily LabelFont = new("Consolas, Menlo, monospace");

    private BarcodeBlock _block;
    private MarkdownPalette _palette;

    private BarcodePattern? _pattern;
    private string? _encodeError;

    /// <summary>The x of every place the caret can stand, from before the first character to after the last.</summary>
    private double[] _caretEdges = [0];
    private FormattedText? _label;
    private Rect _labelBounds;

    private readonly List<(int Start, int Length)> _selection = [];
    private int? _caret;
    private int? _dragAnchor;

    /// <summary>Where a shift-arrow selection started, so extending it walks from there and not from the caret.</summary>
    private int? _keyAnchor;

    public BarcodeElement(BarcodeBlock block, MarkdownPalette palette)
    {
        _block   = block;
        _palette = palette;

        // The host owns the keyboard and forwards to whichever block holds the caret, exactly as it does
        // for a formula — an embedded element that took focus for itself would fight the document for it.
        Focusable = false;

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
        InvalidateVisual();
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
        InvalidateVisual();
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
        InvalidateVisual();
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
        InvalidateVisual();
    }

    // ── Pointer ───────────────────────────────────────────────────────────

    public void BeginPointerSelect(Point pointInElement)
    {
        InteractiveSelection.Own(this);

        _dragAnchor = OffsetAt(pointInElement);
        _caret      = _dragAnchor.Value;
        _selection.Clear();
        InvalidateVisual();
    }

    public void ExtendPointerSelect(Point pointInElement)
    {
        if (_dragAnchor is not { } anchor) return;

        int here = OffsetAt(pointInElement);
        _selection.Clear();
        if (here != anchor) _selection.Add((Math.Min(anchor, here), Math.Abs(here - anchor)));

        _caret = here;
        InvalidateVisual();
    }

    public void EndPointerSelect() => _dragAnchor = null;

    public bool PointerDoubleClick(Point pointInElement)
    {
        // A double click takes the whole value, which is the only word there is.
        InteractiveSelection.Own(this);
        _selection.Clear();
        if (_block.Value.Length > 0) _selection.Add((0, _block.Value.Length));
        _caret = _block.Value.Length;
        InvalidateVisual();
        return true;
    }

    /// <summary>The caret offset nearest a point — clamped to the label, so a click on the bars still lands.</summary>
    private int OffsetAt(Point point)
    {
        if (_caretEdges.Length <= 1) return 0;

        double x = point.X - _labelBounds.X;

        int nearest = 0;
        for (int i = 1; i < _caretEdges.Length; i++)
            if (Math.Abs(_caretEdges[i] - x) < Math.Abs(_caretEdges[nearest] - x)) nearest = i;

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
        InvalidateVisual();
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
        InvalidateVisual();
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
    /// Lays the label out: the text, and the x of every place the caret can stand.
    ///
    /// <para>
    /// No layout tree. A tree earns itself where the content has structure a caret has to be told about —
    /// which part of a fraction it is in, whether it is inside a script or past it — and a barcode's value
    /// has none of that. It is one run of characters on one line, so the whole of its geometry is the
    /// offsets between them, and building nodes to hold that would be inventing structure to describe a
    /// flat string.
    /// </para>
    /// <para>
    /// The edges come from the width of each prefix, which is exact at precisely the boundaries a caret can
    /// occupy: any kerning between two characters is already inside the wider of the two prefixes.
    /// </para>
    /// </summary>
    private void BuildLabel()
    {
        // What goes under a real barcode is what was encoded — several of these formats add a check digit.
        // While the value will not encode there is nothing to show but what was typed.
        string text = _pattern?.Text ?? _block.Value;

        _label = Text(text);

        _caretEdges = new double[_block.Value.Length + 1];
        for (int i = 1; i < _caretEdges.Length; i++)
            _caretEdges[i] = i <= text.Length ? Text(text[..i]).Width : _caretEdges[i - 1];
    }

    private FormattedText Text(string text) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(LabelFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        _block.FontSize,
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

    private double LabelHeight => _block.DisplayValue ? _block.FontSize * 1.4 : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        double bars = PatternWidth * _block.BarWidth;
        double width = Math.Max(bars, _label?.Width ?? 0) + _block.Margin * 2;
        double height = _block.BarHeight + LabelHeight + _block.Margin * 2;

        return new Size(width, height);
    }

    // ── Drawing ───────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var pattern = _pattern ?? Placeholder;
        double barsWidth = PatternWidth * _block.BarWidth;
        double contentWidth = Math.Max(barsWidth, _label?.Width ?? 0);

        var size = new Size(contentWidth + _block.Margin * 2, _block.BarHeight + LabelHeight + _block.Margin * 2);
        dc.DrawRectangle(Brush(_block.Background, _palette.BarcodeLight), null, new Rect(size));

        double left = _block.Margin + (contentWidth - barsWidth) / 2;
        double top  = _block.Margin;

        // The bars. Faint when they are a stand-in, so the error reads as the subject and they read as
        // the shape it would have taken.
        if (pattern is not null)
        {
            var ink = Brush(_block.LineColor, _palette.BarcodeDark);
            if (_pattern is null) ink = Faded(ink);

            foreach (var (start, length) in pattern.InkRuns())
            {
                dc.DrawRectangle(ink, null, new Rect(
                    left + start * _block.BarWidth, top, length * _block.BarWidth, _block.BarHeight));
            }
        }

        if (_block.DisplayValue && _label is not null)
        {
            double labelLeft = _block.TextAlign switch
            {
                BarcodeTextAlign.Left  => _block.Margin,
                BarcodeTextAlign.Right => size.Width - _block.Margin - _label.Width,
                _                      => (size.Width - _label.Width) / 2,
            };
            double labelTop = top + _block.BarHeight + LabelHeight * 0.1;
            _labelBounds = new Rect(labelLeft, labelTop, _label.Width, _label.Height);

            DrawSelection(dc, labelLeft, labelTop);

            _label.SetForegroundBrush(Brush(_block.LineColor, _palette.BarcodeDark));
            dc.DrawText(_label, new Point(labelLeft, labelTop));

            DrawDiagnostics(dc, labelLeft, labelTop);
            DrawCaret(dc, labelLeft, labelTop);
        }

        // The strike across the symbol. Last, so it sits over the bars it is about.
        if (_encodeError is not null && pattern is not null)
        {
            double y = top + _block.BarHeight / 2;
            var bar = new Rect(left, y - Math.Max(_block.BarHeight * 0.04, 1.5),
                               barsWidth, Math.Max(_block.BarHeight * 0.08, 3));
            dc.DrawRectangle(_palette.Danger, null, bar);
        }
    }

    private void DrawSelection(DrawingContext dc, double left, double top)
    {
        foreach (var (start, length) in _selection)
        {
            double from = EdgeAt(start);
            double to = EdgeAt(start + length);
            if (to <= from) continue;

            dc.DrawRectangle(Faded(_palette.Accent), null,
                new Rect(left + from, top, to - from, _label?.Height ?? _block.FontSize));
        }
    }

    private void DrawDiagnostics(DrawingContext dc, double left, double top)
    {
        if (_encodeError is null) return;

        // The wave every editor has drawn under a mistake for thirty years — it needs no explaining, and
        // the reason is a hover away. One run under the whole value: it is all of it that is wrong, since
        // a format rejects a value entire rather than at a character.
        double width = Math.Max(_label?.Width ?? 0, _block.FontSize);
        var run = new Rect(left, top, width, _label?.Height ?? _block.FontSize);

        dc.DrawGeometry(null, new Pen(_palette.Danger, 1.2), Squiggle.Under([run]));
    }

    private void DrawCaret(DrawingContext dc, double left, double top)
    {
        if (_caret is not { } at) return;

        dc.DrawRectangle(_palette.Text, null,
            new Rect(left + EdgeAt(at), top, 1, _label?.Height ?? _block.FontSize));
    }

    /// <summary>Where the caret stands at an offset, clamped to what the label actually has.</summary>
    private double EdgeAt(int offset) =>
        _caretEdges[Math.Clamp(offset, 0, _caretEdges.Length - 1)];

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
