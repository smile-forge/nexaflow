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
/// A laid-out barcode: where every piece of it landed, what it drew, and — for the pieces that are text —
/// which characters of the value each stands for.
///
/// <para>
/// The symbol places itself. A barcode's geometry is a module width and a few multiples of it, with
/// nothing to typeset, so this both computes the geometry and records it, where a formula's layout has to
/// watch a typesetter to find out. What comes out is an ordinary <see cref="LayoutTree"/>, which is
/// what lets the shared queries answer where a press landed, what a drag selected and where the caret can
/// stand, without any of them knowing what a barcode is.
/// </para>
/// <para>
/// <b>This tree is not the shape of <see cref="BarcodePart"/>, and is not meant to be.</b> The bars and
/// their guards are here and have no part at all — they are how a value is drawn rather than anything it
/// says. Going the other way, only what the shared queries can safely work in offsets carries one here:
/// a run of generated text knows in the parse tree that it was worked out from the whole value, and is
/// given no span here, because a caret would otherwise take its position and height from a piece of
/// printing nobody can type into.
/// </para>
/// <para>
/// <b>The text is drawn a group at a time and measured a character at a time.</b> One
/// <see cref="TextMark"/> per printed group is exactly what was drawn before, so the picture is
/// unchanged; the pieces under it carry bounds and no marks of their own, which is what a caret and a
/// selection need. Splitting the drawing as finely as the querying would re-space the digits, because a
/// run of text is not the sum of its characters measured separately.
/// </para>
/// </summary>
internal sealed class BarcodeBuilder : ContentBuilder
{
    /// <summary>
    /// The face the human-readable line is set in. OCR-B is what the retail standards actually specify —
    /// it is drawn to be unambiguous to a machine as well as to a person — but it ships with no operating
    /// system, so it is named first and the monospace stack catches the commoner case where it is absent.
    /// </summary>
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
    private readonly MarkdownPalette _palette;
    private readonly double _dpi;

    /// <summary>Why the value would not encode, or null — what the reader gets a wave and a hover for.</summary>
    private readonly string? _trouble;

    private double _labelSize;
    private double _barsLeft, _barsTop, _guardDrop;

    private BarcodeBuilder(BarcodeBlock block, MarkdownPalette palette, double pixelsPerDip)
        : base(block.Value)
    {
        _block = block;
        _palette = palette;
        _dpi = pixelsPerDip;

        // Reading the value is the builder's own business, exactly as parsing a formula is. It used to be the
        // element's: the element encoded, kept the pattern and the reason beside each other, and handed both
        // back in. That is a second copy of what a builder is for, and it is why "does it encode" had to be
        // asked twice on every keystroke.
        if (block.Value.Length == 0) _trouble = "A barcode needs a value.";
        else if (BarcodeEncoder.TryEncode(block.Format, block.Value, out var encoded, out string? error))
            _pattern = encoded;
        else _trouble = error;

        // A valid symbol in the asked-for format, drawn faint behind the error when the real value will not
        // encode. A barcode-shaped absence reads as "this is a barcode, and it is wrong"; an empty gap reads
        // as a rendering fault.
        _drawn = _pattern ?? (BarcodeEncoder.TryEncode(
            block.Format, BarcodeEncoder.SampleValue(block.Format), out var sample, out _) ? sample : null);
    }

    /// <summary>Lays a barcode out, and gives back the tree and nothing barcode-shaped at all.</summary>
    public static Laid Build(BarcodeBlock block, MarkdownPalette palette, double pixelsPerDip) =>
        new BarcodeBuilder(block, palette, pixelsPerDip).Lay();

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
        _dpi);

        protected override Laid Read()
    {
        // What goes under a real barcode is what was encoded — several of these formats add a check digit,
        // and the retail ones break the number into groups and set one of them outside the bars. While the
        // value will not encode there is nothing to show but what was typed.
        var text = _pattern?.Text ?? _block.Value;

        var groups = _pattern?.TextRuns is { Count: > 0 } runs
            ? runs
            : [new BarcodeTextRun(text, 0, PatternWidth, BarcodeTextPlacement.Below)];

        // A value that will not encode has no symbol to read, so it is read here from what is drawn — which
        // is the value itself, and is what leaves a broken barcode repairable where it stands. A
        // publication keeps its caption through that, so it does not lose the one line carrying its number.
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

        // Five modules is the figure the retail standards give, and it is in modules rather than in font
        // size on purpose: everything else about a symbol's geometry is a multiple of the module, and
        // tying this to the label instead made the guards grow whenever the text did.
        _guardDrop = _block.BarWidth * GuardExtensionModules;

        var size = new Size(
            content + _block.Margin * 2,
            captionHeight + _block.BarHeight + LabelHeight + _block.Margin * 2);

        // ── the tree ──
        var build = new LayoutBuilder();
        build.Open(nameof(BarcodeKind.Symbol), part: null);

        // The ground the symbol is printed on. A barcode paints its own light field whatever the theme,
        // because a scanner needs dark bars on a light one.
        build.Draw(new RuleMark(new Rect(size), Brush(_block.Background, _palette.BarcodeLight)));

        LayBars(build);

        if (caption is not null
            && symbol.Children.FirstOrDefault(c => c.Kind == BarcodeKind.Caption) is { } part)
            // Over the main symbol's middle, not the whole picture's: with an add-on beside it those are
            // several modules apart, and a title that drifts towards the price reads as belonging to it.
            LayCaption(build, part, caption,
                       new Point(_barsLeft + (mainWidth - caption.Width) / 2, _block.Margin), captionSize);

        LayLabel(build, symbol, groups, barsWidth, gap);

        build.Close();

        // Reported over the whole value, because that is the span the reader has to change. The wave and
        // the hover are the host's doing from here — a builder says what is wrong, not how to show it.
        return new Laid(build.Seal(), size, _trouble is null
            ? []
            : [new Diagnostic(0, Math.Max(_block.Value.Length, 1), DiagnosticSeverity.Error, _trouble)]);
    }

    /// <summary>
    /// The caption a publication keeps even when its value will not encode. Only for the schemes that
    /// print one — asked of the format, because there is no encoded symbol left to ask.
    /// </summary>
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

    /// <summary>
    /// The size to set the human-readable line at: what the block asked for, reduced until every run fits
    /// the space its bars leave for it — the wells the guards make. A point size is the wrong thing to
    /// state that in, because whether it fits depends on the module width and on the face.
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
    /// The size to set the caption at. It is a title rather than part of the number, so it is set smaller —
    /// as it is on a book's cover — and it belongs to the main symbol rather than to the pair.
    /// </summary>
    private double FittedCaptionSize(string caption, double mainWidth)
    {
        double size = LabelSize * CaptionScale;

        double natural = Text(caption, size).Width;
        if (natural > mainWidth && natural > 0) size *= mainWidth / natural;

        return Math.Max(size, MinimumLabelSize);
    }

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

    /// <summary>
    /// The bars, which are layout and nothing else: no piece of what the author typed is a bar, so there
    /// is no part of the parse tree here to project. They stand for nothing in the source, which is what
    /// keeps the caret out among the digits where a reader can see it.
    /// </summary>
    private void LayBars(LayoutBuilder into)
    {
        if (_drawn is null) return;

        var width = PatternWidth * _block.BarWidth;

        into.Open("Bars", part: null, new Point(_barsLeft, _barsTop));

        // As tall as the well the guards drop into, whether or not a run of ink reaches the bottom of it.
        into.Covers(new Rect(0, 0, width, _block.BarHeight + _guardDrop));

        

        // Faint when they are a stand-in, so the error reads as the subject and they read as the shape it
        // would have taken.
        var dark = Brush(_block.LineColor, _palette.BarcodeDark);
        var ink = _pattern is null ? Faded(dark) : dark;

        // Dropping the guards past the digits and lifting an add-on clear of its own. Both are what makes
        // a retail symbol recognisable at a glance: the guards frame the two halves of the number, and the
        // add-on stands apart and higher so it reads as a second symbol rather than as more of the first.
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

            // The strike, last so it sits over the bars it is about. A barcode-shaped absence with a line
            // through it reads as "this is a barcode and it is wrong"; the bars alone read as a real symbol.
            // It is drawing, so it is a mark here rather than something the element paints afterwards from
            // its own copy of whether the value encoded.
            if (_trouble is not null)
                into.Draw(new RuleMark(
                    new Rect(0,
                             _block.BarHeight / 2 - Math.Max(_block.BarHeight * 0.04, 1.5),
                             width,
                             Math.Max(_block.BarHeight * 0.08, 3)),
                    _palette.Danger));

            into.Close();
    }

    /// <summary>
    /// The caption, drawn as one line and measured in its pieces — the scheme's name, which nobody typed,
    /// and the number, which is the value and so is the one half a caret can be in.
    /// </summary>
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

            // One wave under each group of the value, and none under an add-on: it is all of the value
            // that is wrong, since a format rejects a value entire rather than at a character.
            if (group.Placement != BarcodeTextPlacement.Above)
                underlined.Add(new Rect(at.X, at.Y, Math.Max(glyphs.Width, _block.FontSize), glyphs.Height));

            // The parse tree's runs were read from these same groups, in this order, so they line up.
            if (i >= parts.Count) continue;
            var part = parts[i];

            // Printing that was worked out rather than typed is still ink: it is a digit on the page and a
            // reader can point at it. It carries no part, which is what keeps it out of the caret's stops.
            into.Open(part.Kind.ToString(), part: null, at);
            into.Draw(new TextMark(glyphs, default, Brush(_block.LineColor, _palette.BarcodeDark)));

            if (!Generated(part)) LayPieces(into, part, 0, glyphs.Height, null);
            into.Close();
        }

        // Neither the runs nor their hull are kept. Where the number sits is what the pieces say, and the
        // wave a broken value wears goes under the characters that carry it — which is the same wave every
        // other kind of content draws, from the same question asked of the same tree.
        _ = underlined;
        _ = placed;

        double Centred(BarcodeTextRun group, FormattedText glyphs, double bars) =>
            group.Modules > 0
                ? _barsLeft + (group.StartModule + group.Modules / 2.0) * _block.BarWidth - glyphs.Width / 2
                : _barsLeft + (bars - glyphs.Width) / 2;
    }

    /// <summary>
    /// Where each piece of a run begins, measured as a prefix of the whole run rather than on its own: a
    /// run of text is not the sum of its pieces measured separately, and placing them one at a time would
    /// drift away from the glyphs actually drawn.
    /// </summary>
    private void LayPieces(LayoutBuilder into, BarcodePart part, double y, double height, double? size)
    {
        var run = part.Printed;
        var consumed = 0;

        foreach (var piece in part.Children)
        {
            var from = Text(run[..consumed], size).Width;
            consumed += piece.Printed.Length;
            var to = Text(run[..consumed], size).Width;

            // Only a character of the value carries a part. Generated printing knows in the parse tree
            // that it stands for the whole value, and says so when it is pressed — but a part here
            // would put it among the caret's stops, and the caret would take its height and position
            // from a piece of the symbol nobody can type into.
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

    /// <summary>
    /// How a symbol sets characters it could not lay out at all — the same face its human-readable line
    /// wears, because a barcode that failed still has to show the value somebody typed.
    /// </summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(LabelFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            MinimumLabelSize,
            Brushes.Black,
            _dpi);
}
