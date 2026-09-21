using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// A laid-out barcode: geometry computed directly (module math, not typeset) into an ordinary
/// <see cref="LayoutTree"/>, so the shared hit-testing/selection queries work without knowing what a
/// barcode is.
///
/// <para>
/// Bars and guards carry no <see cref="BarcodePart"/> — they draw the value, they are not part of what
/// was typed, so a generated run gets no caret span. Text is drawn and measured a printed group at a
/// time (not per character) so spacing matches what was actually drawn.
/// </para>
/// </summary>
internal sealed class BarcodeBuilder : ContentBuilder
{
    /// <summary>OCR-B is the retail-standard face; falls back to monospace since it ships with no OS.</summary>
    private static readonly FontFamily LabelFont = new("OCR-B, OCRB, OCR B, Consolas, Menlo, monospace");

    /// <summary>How far a guard bar runs past the others, in modules — the standard's figure.</summary>
    private const double GuardExtensionModules = 5;

    /// <summary>How much of a well its digits may fill, leaving the guards and the neighbours clear.</summary>
    private const double WellFill = 0.92;

    /// <summary>Below this the line is unreadable, and a symbol with no legible number is worse than a wide one.</summary>
    private const double MinimumLabelSize = 4;

    /// <summary>How much smaller the caption is set than the number under the bars.</summary>
    private const double CaptionScale = 0.62;

    /// <summary>Clear air between the caption and the bars, in modules, on top of the line's own leading.</summary>
    private const double CaptionSeparationModules = 1.5;

    private readonly BarcodeBlock _block;
    private readonly BarcodePattern? _pattern;
    private readonly BarcodePattern? _drawn;
    private readonly StyleFormat _palette;
    /// <summary>Why the value would not encode, or null — what the reader gets a wave and a hover for.</summary>
    private readonly string? _trouble;

    private double _labelSize;
    private double _barsLeft, _barsTop, _guardDrop;

    // A barcode's value is one run of characters and has no grammar of its own, so what it is read as is that
    // run: enough for the base to report the source and to show it when nothing can be drawn.
    private BarcodeBuilder(BarcodeBlock block, StyleFormat palette)
        : base(ContentReading.Of(ContentNode.Leaf(Kinds.Verbatim, block.Value)),
               EditState.For(block.Value), palette, isReadOnly: true)
    {
        _block = block;
    _palette = palette;

        // Encoding happens here (not in the element) so "does it encode" isn't computed twice per keystroke.
        if (block.Value.Length == 0) _trouble = "A barcode needs a value.";
        else if (BarcodeEncoder.TryEncode(block.Format, block.Value, out var encoded, out string? error))
            _pattern = encoded;
        else _trouble = error;

        // Faint stand-in symbol when the value won't encode, so the error reads as "wrong" rather than "broken".
        _drawn = _pattern ?? (BarcodeEncoder.TryEncode(
            block.Format, BarcodeEncoder.SampleValue(block.Format), out var sample, out _) ? sample : null);
    }

    /// <summary>Lays a barcode out, and gives back the tree and nothing barcode-shaped at all.</summary>
    public static Laid Build(BarcodeBlock block, StyleFormat palette) =>
        new BarcodeBuilder(block, palette).Lay();

    /// <summary>The symbol the value encodes to, or null while it will not encode.</summary>
    public BarcodePattern? Encoded => _pattern;

    // ── Laying it out ─────────────────────────────────────────────────────

    private int PatternWidth => _drawn?.Width ?? 0;

    private double LabelSize => _labelSize > 0 ? _labelSize : _block.FontSize;

    private double LabelHeight => _block.DisplayValue ? LabelSize * 1.4 : 0;

    private FormattedText Text(string text, double? size = null) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(LabelFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        size ?? LabelSize,
        Brushes.Black,
        Editing.LayoutText.Density);

        protected override Laid Build()
    {
        // Several formats add a check digit or move a group outside the bars, so the printed text comes
        // from the encoded pattern, not the raw value — except when it won't encode, where there's nothing else.
        var text = _pattern?.Text ?? _block.Value;

        var groups = _pattern?.TextRuns is { Count: > 0 } runs
            ? runs
            : [new BarcodeTextRun(text, 0, PatternWidth, BarcodeTextPlacement.Below)];

        // No encoded symbol to read when the value is broken, so read the raw value instead — keeps a
        // publication's caption line even when the number itself won't encode.
        var symbol = _pattern?.Symbol ?? BarcodeTextLayout.Read(_block.Value, text, [], CaptionWhenBroken());

        var barsWidth = PatternWidth * _block.BarWidth;

        _labelSize = FittedLabelSize(groups, barsWidth);
        var gap = _labelSize * 0.35;   // between the bars and a digit set outside them

        // The outside digits widen the symbol; everything else sits within the bars.
        var leftPad = Widest(groups, BarcodeTextPlacement.LeftOfBars, gap);
        var rightPad = Widest(groups, BarcodeTextPlacement.RightOfBars, gap);

        var mainWidth = MainSymbolModules() * _block.BarWidth;

        FormattedText? caption = null;
        var captionSize = 0d;
        if (symbol.Children.FirstOrDefault(c => c.Kind == BarcodeKind.Caption) is { } captionPart)
        {
            captionSize = FittedCaptionSize(captionPart.Printed, mainWidth);
            caption = Text(captionPart.Printed, captionSize);
        }

        var content = Math.Max(leftPad + barsWidth + rightPad, caption?.Width ?? 0);

        var captionHeight = caption is null
            ? 0
            : captionSize * 1.35 + CaptionSeparationModules * _block.BarWidth;

        _barsLeft = _block.Margin + (content - (leftPad + barsWidth + rightPad)) / 2 + leftPad;
        _barsTop = _block.Margin + captionHeight;

        // In modules, not font size: tying guard length to the label made guards grow with the text.
        _guardDrop = _block.BarWidth * GuardExtensionModules;

        var size = new Size(
            content + _block.Margin * 2,
            captionHeight + _block.BarHeight + LabelHeight + _block.Margin * 2);

        // ── the tree ──
        var build = new LayoutBuilder();
        build.Open(nameof(BarcodeKind.Symbol), part: null);

        // Barcode paints its own light background regardless of theme — a scanner needs dark bars on light.
        build.Draw(new RuleMark(new Rect(size), Brush(_block.Background, _palette.BarcodeLight)));

        LayBars(build);

        if (caption is not null
            && symbol.Children.FirstOrDefault(c => c.Kind == BarcodeKind.Caption) is { } part)
            // Centred on the main symbol, not the whole picture — an add-on sits apart and shouldn't pull the caption toward it.
            LayCaption(build, part, caption,
                       new Point(_barsLeft + (mainWidth - caption.Width) / 2, _block.Margin), captionSize);

        LayLabel(build, symbol, groups, barsWidth, gap);

        build.Close();

        // Diagnostic spans the whole value — that's what the reader would need to change.
        return new Laid(build.Seal(), size, _trouble is null
            ? []
            : [new Diagnostic(0, Math.Max(_block.Value.Length, 1), DiagnosticSeverity.Error, _trouble)]);
    }

    /// <summary>The caption a publication keeps even when its value won't encode (ISBN/ISSN/ISMN only).</summary>
    private string? CaptionWhenBroken() =>
        _pattern is null && _block.Format is BarcodeSymbology.Isbn or BarcodeSymbology.Issn
                                          or BarcodeSymbology.Ismn
            ? BarcodeTextLayout.CaptionFor(_block.Format, _block.Value)
            : null;

    private double Widest(IReadOnlyList<BarcodeTextRun> groups, BarcodeTextPlacement where, double gap)
    {
        double widest = 0;
        foreach (var group in groups)
            if (group.Placement == where) widest = Math.Max(widest, Text(group.Text).Width + gap);
        return widest;
    }

    /// <summary>The label's font size, shrunk from the block's default until every group fits the well its bars leave for it.</summary>
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

    /// <summary>The caption's font size — smaller than the label, scaled to the main symbol's width.</summary>
    private double FittedCaptionSize(string caption, double mainWidth)
    {
        double size = LabelSize * CaptionScale;

        double natural = Text(caption, size).Width;
        if (natural > mainWidth && natural > 0) size *= mainWidth / natural;

        return Math.Max(size, MinimumLabelSize);
    }

    /// <summary>Width of the main symbol in modules — everything before an add-on, or all of it if none.</summary>
    private double MainSymbolModules()
    {
        int addOn = AddOnStartModule();
        if (_pattern is null || addOn == int.MaxValue) return PatternWidth;

        int end = 0;
        foreach (var (start, length) in _pattern.InkRuns())
            if (start < addOn) end = Math.Max(end, start + length);

        return end > 0 ? end : PatternWidth;
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

    // ── The pieces ────────────────────────────────────────────────────────

    private static bool Generated(BarcodePart part) => part.Kind == BarcodeKind.EncodedText;

    /// <summary>Draws the bars — no <see cref="BarcodePart"/>, since nothing typed is a bar.</summary>
    private void LayBars(LayoutBuilder into)
    {
        if (_drawn is null) return;

        var width = PatternWidth * _block.BarWidth;

        into.Open("Bars", part: null, new Point(_barsLeft, _barsTop));

        // As tall as the well the guards drop into, whether or not a run of ink reaches the bottom of it.
        into.Covers(new Rect(0, 0, width, _block.BarHeight + _guardDrop));

        

        // Faint when the pattern is a stand-in, so the error reads as the subject.
        var dark = Brush(_block.LineColor, _palette.BarcodeDark);
        var ink = _pattern is null ? Faded(dark) : dark;

        // Guards drop past the digits; an add-on lifts clear of the main bars so it reads as a second symbol.
        double addOnFrom = AddOnStartModule();
        double lift = addOnFrom < int.MaxValue ? LabelSize * 1.35 : 0;

        foreach (var (start, length) in _drawn.InkRuns())
        {
            bool addOn = start >= addOnFrom;

            double top = addOn ? lift : 0;
            double height = _block.BarHeight - (addOn ? lift : 0) + (IsGuard(_drawn, start) ? _guardDrop : 0);

                into.Draw(new RuleMark(
                    new Rect(start * _block.BarWidth, top, length * _block.BarWidth, height), ink));
            }

            // Strike drawn last so it sits over the bars — a barcode with a line through it reads as "wrong".
            if (_trouble is not null)
                into.Draw(new RuleMark(
                    new Rect(0,
                             _block.BarHeight / 2 - Math.Max(_block.BarHeight * 0.04, 1.5),
                             width,
                             Math.Max(_block.BarHeight * 0.08, 3)),
                    _palette.Danger));

            into.Close();
    }

    /// <summary>Draws the caption line and lays its editable pieces (the number, not the scheme name).</summary>
    private void LayCaption(LayoutBuilder into, BarcodePart part, FormattedText glyphs, Point at, double size)
    {
        into.Open(part.Kind.ToString(), part: null, at);
        into.Draw(new TextMark(glyphs, default, Brush(_block.LineColor, _palette.BarcodeDark)));
        LayPieces(into, part, 0, glyphs.Height, size);
        into.Close();
    }

    private void LayLabel(LayoutBuilder into, BarcodePart symbol, IReadOnlyList<BarcodeTextRun> groups,
                          double barsWidth, double gap)
    {
        if (!_block.DisplayValue) return;

        var parts = symbol.Children
            .Where(c => c.Role is BarcodeRole.Label or BarcodeRole.AddOn)
            .ToList();

        var belowTop = _barsTop + _block.BarHeight;
        var aboveTop = _barsTop;

        var placed = new List<Rect>();
        var underlined = new List<Rect>();

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var glyphs = Text(group.Text);

            var at = group.Placement switch
            {
                BarcodeTextPlacement.LeftOfBars => new Point(_barsLeft - gap - glyphs.Width, belowTop),
                BarcodeTextPlacement.RightOfBars => new Point(_barsLeft + barsWidth + gap, belowTop),
                BarcodeTextPlacement.Above => new Point(Centred(group, glyphs, barsWidth), aboveTop),
                _ => new Point(Centred(group, glyphs, barsWidth), belowTop),
            };

            placed.Add(new Rect(at.X, at.Y, glyphs.Width, glyphs.Height));

            // No underline for an add-on — a format rejects the value as a whole, not one character of it.
            if (group.Placement != BarcodeTextPlacement.Above)
                underlined.Add(new Rect(at.X, at.Y, Math.Max(glyphs.Width, _block.FontSize), glyphs.Height));

            // The parse tree's runs were read from these same groups, in this order, so they line up.
            if (i >= parts.Count) continue;
            var part = parts[i];

            // Generated digits are still drawn, just with no part — keeps them out of the caret's stops.
            into.Open(part.Kind.ToString(), part: null, at);
            into.Draw(new TextMark(glyphs, default, Brush(_block.LineColor, _palette.BarcodeDark)));

            if (!Generated(part)) LayPieces(into, part, 0, glyphs.Height, null);
            into.Close();
        }

        // underlined/placed are unused — positions and diagnostics come from the pieces themselves.
        _ = underlined;
        _ = placed;

        double Centred(BarcodeTextRun group, FormattedText glyphs, double bars) =>
            group.Modules > 0
                ? _barsLeft + (group.StartModule + group.Modules / 2.0) * _block.BarWidth - glyphs.Width / 2
                : _barsLeft + (bars - glyphs.Width) / 2;
    }

    /// <summary>Measures each piece as a prefix of the whole run — measuring pieces separately would drift from the glyphs actually drawn.</summary>
    private void LayPieces(LayoutBuilder into, BarcodePart part, double y, double height, double? size)
    {
        var run = part.Printed;
        var consumed = 0;

        foreach (var piece in part.Children)
        {
            var from = Text(run[..consumed], size).Width;
            consumed += piece.Printed.Length;
            var to = Text(run[..consumed], size).Width;

            // Only a value character carries a part — a part on generated text would give the caret a stop nobody could type into.
            into.Open(piece.Kind.ToString(), piece.IsSource ? piece : null, new Point(from, y));
            into.Covers(new Rect(0, 0, Math.Max(to - from, 0), height));
            into.Close();
        }
    }

    /// <summary>Whether a run of ink begins inside one of the symbol's guard patterns.</summary>
    private static bool IsGuard(BarcodePattern pattern, int start)
    {
        foreach (var (from, length) in pattern.Guards)
            if (start >= from && start < from + length) return true;
        return false;
    }

    // ── Painting ──────────────────────────────────────────────────────────

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

    /// <summary>Fallback glyph rendering when layout fails entirely — same face as the label line.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(LabelFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            MinimumLabelSize,
            Brushes.Black,
            Editing.LayoutText.Density);
}
