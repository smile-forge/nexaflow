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
/// <see cref="BarcodeBuilder.AcceptsCaret"/>.
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
public sealed class BarcodeElement : FrameworkElement, IInteractiveBlock
{
    private BarcodeBlock _block;
    private MarkdownPalette _palette;

    private BarcodePattern? _pattern;
    private string? _encodeError;

    /// <summary>
    /// Where every piece of the symbol landed, what it drew, and which characters of the value each
    /// piece stands for. Null only until the first encode, which the constructor does.
    /// </summary>
    private Laid? _layout;

    private readonly List<(int Start, int Length)> _selection = [];
    /// <summary>
    /// What the caret is standing against. There is more than one place at an offset wherever a piece of
    /// the number ends and another begins somewhere else on the page — the two halves of a retail symbol,
    /// with a guard between them — and those are different places to stand for one character.
    ///
    /// <para>
    /// Only ever true of the layout it was taken from, so it is worked out again whenever the symbol has
    /// been encoded since — which puts the caret back at the innermost place at its offset.
    /// </para>
    /// </summary>

    /// <summary>Where a pointer drag began, as a piece of the layout rather than as an offset.</summary>
    private Piece _dragAnchor;

    /// <summary>Where a shift-arrow selection started, so extending it walks from there and not from the caret.</summary>
    /// <summary>
    /// Windows' own caret rate. WPF does not surface <c>GetCaretBlinkTime</c>, and a P/Invoke for one
    /// number is not worth the trouble; this is the default every version has shipped.
    /// </summary>
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



        Encode();
    }

    /// <summary>The value changed under the reader's typing; the host puts it back in the block source.</summary>
    /// <inheritdoc/>
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
    ///
    /// <para>
    /// Asked of the tree rather than recorded beside it: a piece that names a stretch of source is a
    /// piece somebody typed, and a symbol with none of those is one nobody can type into. Nothing about
    /// that question is barcode-shaped.
    /// </para>
    /// </summary>
    // ── What the document around it needs ──────────────────────────────────
    //
    // A barcode is drawn, pressed and selected here; it is not edited here. Everything a caret needs — the
    // caret itself, typing, backspacing and the seam that carried them — was a second copy of what
    // ContentElement already does, kept in step by hand and drifting whenever the other copy moved. It is
    // deleted rather than ported, and comes back when this element becomes a ContentElement like the
    // formula did: one element, one caret, one edit model.

    /// <summary>Every piece of the symbol and where it landed — what the shared queries read.</summary>
    public Piece Root => _layout?.Root ?? default;

    /// <summary>What is picked out, for the host to copy.</summary>
    public IReadOnlyList<(int Start, int Length)> Selection => _selection;

    /// <summary>
    /// The one thing that can be wrong here: the value is not something this format can carry. Reported
    /// over the whole value, because that is the span the reader has to change.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics =>
        _encodeError is null
            ? []
            : [new Diagnostic(0, Math.Max(_block.Value.Length, 1), DiagnosticSeverity.Error, _encodeError)];

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


        _selection.Clear();
        InteractiveSelection.Release(this);
        Refresh();
    }

    // ── Pointer ───────────────────────────────────────────────────────────

    public void BeginPointerSelect(Point pointInElement)
    {
        InteractiveSelection.Own(this);
        if (_layout?.Root is not { Exists: true } root) return;

        _dragAnchor = root.PieceAt(pointInElement);
        _selection.Clear();

        Refresh();
    }

    public void ExtendPointerSelect(Point pointInElement)
    {
        if (!_dragAnchor.Exists || _layout?.Root is not { Exists: true } root) return;
        if (root.PieceAt(pointInElement) is not { Exists: true } focus) return;

        // Whole pieces rather than a stretch of characters, so what comes back is something the format
        // really is made of — a group of the number, or the number — and never half of a thing worked out
        // from the value, which would stand for a stretch of source it does not cover.
        _selection.Clear();
        _selection.AddRange(Taken(root, _dragAnchor, focus));

        Refresh();
    }

    public void EndPointerSelect() => _dragAnchor = default;

    public bool PointerDoubleClick(Point pointInElement)
    {
        // A double click takes the whole value, which is the only word there is.
        InteractiveSelection.Own(this);
        _selection.Clear();
        if (_block.Value.Length > 0) _selection.Add((0, _block.Value.Length));
        Refresh();
        return true;
    }

    // ── Editing ───────────────────────────────────────────────────────────



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
        _layout = BarcodeBuilder.Build(
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
        InvalidateVisual();
    }

    // ── The caret's blink ─────────────────────────────────────────────────

    // ── Drawing ───────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize) => _layout?.Size ?? new Size();

    /// <summary>
    /// Paints the symbol out of its own layout, with the reader's own marks over it.
    /// <para>
    /// The wash goes over the drawing, as it does everywhere else on the page: it is translucent, so what
    /// it marks shows through it, and the painter never has to be told what a barcode is made of.
    /// </para>
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        if (_layout is not { } layout) return;

        var ink = Brush(_block.LineColor, _palette.BarcodeDark);

        // The drawing, then the wash over it — the one order everything on the page is painted in. A
        // selection wash is translucent, so laid over the digits it tints them rather than hiding them, and
        // nothing has to be told that a barcode has two kinds of ink. This used to paint the bars, then the
        // wash, then the digits, which is the same picture reached by explaining a barcode to the painter.
        LayoutPainter.Paint(dc, layout.Root, ink);
        DrawSelection(dc);

        DrawDiagnostics(dc, layout);

        // The strike across the symbol. Last, so it sits over the bars it is about.
        if (_encodeError is not null && Of(layout, "Bars") is { Exists: true, Bounds: var bars })
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
        if (_selection.Count == 0 || _layout?.Root is not { Exists: true } root) return;

        var wash = Faded(_palette.Accent);

        foreach (var node in root.Leaves())
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
    private static bool Generated(Piece piece) => piece.Kind == nameof(BarcodeKind.EncodedText);

    /// <summary>
    /// What a drag from one piece to another took.
    /// <para>
    /// Pointing at anything the format worked out means the whole value, because that is what it was
    /// worked out from: there is no smaller answer, and a check digit stands for all the digits rather
    /// than for the one it is printed beside.
    /// </para>
    /// </summary>
    private IReadOnlyList<(int Start, int Length)> Taken(Piece root, Piece anchor, Piece focus) =>
        Generated(anchor) || Generated(focus)
            ? [(0, _block.Value.Length)]
            : ContentSelection.Between(root, anchor, focus).Ranges;

    /// <summary>
    /// The wave every editor has drawn under a mistake for thirty years — it needs no explaining, and the
    /// reason is a hover away.
    ///
    /// <para>
    /// Under the characters that carry the value, which is what the tree already says: a piece naming a
    /// stretch of source is a piece the reader wrote, and those are exactly the ones the error is about.
    /// </para>
    /// </summary>
    private void DrawDiagnostics(DrawingContext dc, Laid layout)
    {
        if (_encodeError is null) return;

        var runs = layout.Root.Leaves()
            .Where(piece => piece.Part is { Length: > 0 })
            .Select(piece => piece.Bounds)
            .ToList();

        if (runs.Count == 0) return;

        dc.DrawGeometry(null, new Pen(_palette.Danger, 1.2), Squiggle.Under(runs));
    }

    /// <summary>The one piece of a kind, or nothing — how the element asks the tree for a landmark.</summary>
    private static Piece Of(Laid layout, string kind)
    {
        foreach (var piece in layout.Root.SelfAndDescendants())
            if (piece.Kind == kind) return piece;

        return default;
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
