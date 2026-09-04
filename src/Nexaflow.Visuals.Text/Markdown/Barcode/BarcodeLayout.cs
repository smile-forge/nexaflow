using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Barcode;

/// <summary>
/// A laid-out barcode: where every piece of it landed, what it drew, and — for the pieces that are text —
/// which characters of the value each stands for.
///
/// <para>
/// The symbol places itself. A barcode's geometry is a module width and a few multiples of it, with
/// nothing to typeset, so this both computes the geometry and records it, where a formula's layout has to
/// watch a typesetter to find out. What comes out is an ordinary <see cref="LayoutNode"/> tree, which is
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
internal sealed class BarcodeLayout
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

    private double _labelSize;
    private double _barsLeft, _barsTop, _guardDrop;

    private BarcodeLayout(BarcodeBlock block, BarcodePattern? pattern, BarcodePattern? placeholder,
                          MarkdownPalette palette, double pixelsPerDip)
    {
        _block = block;
        _pattern = pattern;
        _drawn = pattern ?? placeholder;
        _palette = palette;
        _dpi = pixelsPerDip;
    }

    /// <summary>Every piece of the symbol, where it landed and what it drew.</summary>
    public LayoutNode Root { get; private set; } = null!;

    /// <summary>What the element wants to be.</summary>
    public Size Size { get; private set; }

    /// <summary>
    /// Whether the caret belongs in this symbol at all: whether any of what it prints is the value.
    /// <para>
    /// True for the formats that print what they are given, and for the caption of a publication, which
    /// carries the number as it was written. False for a printed number that was worked out — where the
    /// reader edits the block source instead, and is not offered a caret that would be editing one string
    /// while pointing at another.
    /// </para>
    /// </summary>
    public bool AcceptsCaret { get; private set; }

    /// <summary>Where the human-readable rows sit, for measuring a press that landed near them.</summary>
    public Rect LabelBounds { get; private set; }

    /// <summary>Each printed run of the value, for waving under what would not encode.</summary>
    public IReadOnlyList<Rect> LabelRuns { get; private set; } = [];

    /// <summary>Where the bars themselves sit, for a strike drawn across them.</summary>
    public Rect? Bars { get; private set; }

    public static BarcodeLayout Build(BarcodeBlock block, BarcodePattern? pattern, BarcodePattern? placeholder,
                                      MarkdownPalette palette, double pixelsPerDip)
    {
        var layout = new BarcodeLayout(block, pattern, placeholder, palette, pixelsPerDip);
        layout.Lay();
        return layout;
    }

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

    private void Lay()
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

        Size = new Size(
            content + _block.Margin * 2,
            captionHeight + _block.BarHeight + LabelHeight + _block.Margin * 2);

        // ── the tree ──
        Root = new LayoutNode(new Rect(Size), 0, 0, nameof(BarcodeKind.Symbol), isInk: false);

        // The ground the symbol is printed on. A barcode paints its own light field whatever the theme,
        // because a scanner needs dark bars on a light one.
        Root.Drew(new RuleMark(new Rect(Size), Brush(_block.Background, _palette.BarcodeLight)));

        LayBars();

        if (caption is not null
            && symbol.Children.FirstOrDefault(c => c.Kind == BarcodeKind.Caption) is { } part)
            // Over the main symbol's middle, not the whole picture's: with an add-on beside it those are
            // several modules apart, and a title that drifts towards the price reads as belonging to it.
            LayCaption(part, caption, new Point(_barsLeft + (mainWidth - caption.Width) / 2, _block.Margin),
                       captionSize);

        LayLabel(symbol, groups, barsWidth, gap);

        AcceptsCaret = Root.SelfAndDescendants().Any(n => n.Kind == nameof(BarcodeKind.Character));
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

    /// <summary>
    /// The bars, which are layout and nothing else: no piece of what the author typed is a bar, so there
    /// is no part of the parse tree here to project. They stand for nothing in the source, which is what
    /// keeps the caret out among the digits where a reader can see it.
    /// </summary>
    private void LayBars()
    {
        if (_drawn is null) return;

        var bounds = new Rect(_barsLeft, _barsTop, PatternWidth * _block.BarWidth, _block.BarHeight + _guardDrop);
        var node = Root.Add(new LayoutNode(bounds, 0, 0, "Bars", isInk: false));

        Bars = new Rect(_barsLeft, _barsTop, PatternWidth * _block.BarWidth, _block.BarHeight);

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

            double top = _barsTop + (addOn ? lift : 0);
            double height = _block.BarHeight - (addOn ? lift : 0) + (IsGuard(_drawn, start) ? _guardDrop : 0);

            node.Drew(new RuleMark(
                new Rect(_barsLeft + start * _block.BarWidth, top, length * _block.BarWidth, height), ink));
        }
    }

    /// <summary>
    /// The caption, drawn as one line and measured in its pieces — the scheme's name, which nobody typed,
    /// and the number, which is the value and so is the one half a caret can be in.
    /// </summary>
    private void LayCaption(BarcodePart part, FormattedText glyphs, Point at, double size)
    {
        var node = Root.Add(new LayoutNode(
            new Rect(at.X, at.Y, glyphs.Width, glyphs.Height), 0, 0, part.Kind.ToString(), isInk: false));

        node.Drew(new TextMark(glyphs, at, null));
        LayPieces(node, part, at.X, at.Y, glyphs.Height, size);
    }

    private void LayLabel(BarcodePart symbol, IReadOnlyList<BarcodeTextRun> groups, double barsWidth, double gap)
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

            var bounds = new Rect(at.X, at.Y, glyphs.Width, glyphs.Height);
            placed.Add(bounds);

            // One wave under each group of the value, and none under an add-on: it is all of the value
            // that is wrong, since a format rejects a value entire rather than at a character.
            if (group.Placement != BarcodeTextPlacement.Above)
                underlined.Add(new Rect(at.X, at.Y, Math.Max(glyphs.Width, _block.FontSize), glyphs.Height));

            // The parse tree's runs were read from these same groups, in this order, so they line up.
            if (i >= parts.Count) continue;
            var part = parts[i];

            var node = Root.Add(new LayoutNode(bounds, 0, 0, part.Kind.ToString(), isInk: Generated(part)));
            node.Drew(new TextMark(glyphs, at, null));

            if (!Generated(part)) LayPieces(node, part, at.X, at.Y, glyphs.Height, null);
        }

        LabelRuns = underlined;
        LabelBounds = placed.Count == 0
            ? new Rect(_barsLeft, belowTop, barsWidth, LabelHeight)
            : placed.Aggregate(placed[0], Rect.Union);

        double Centred(BarcodeTextRun group, FormattedText glyphs, double bars) =>
            group.Modules > 0
                ? _barsLeft + (group.StartModule + group.Modules / 2.0) * _block.BarWidth - glyphs.Width / 2
                : _barsLeft + (bars - glyphs.Width) / 2;
    }

    private static bool Generated(BarcodePart part) => part.Kind == BarcodeKind.EncodedText;

    /// <summary>
    /// Where each piece of a run begins, measured as a prefix of the whole run rather than on its own: a
    /// run of text is not the sum of its pieces measured separately, and placing them one at a time would
    /// drift away from the glyphs actually drawn.
    /// </summary>
    private void LayPieces(LayoutNode into, BarcodePart part, double x, double y, double height, double? size)
    {
        var run = part.Printed;
        var consumed = 0;

        foreach (var piece in part.Children)
        {
            var from = x + Text(run[..consumed], size).Width;
            consumed += piece.Printed.Length;
            var to = x + Text(run[..consumed], size).Width;

            into.Add(new LayoutNode(
                new Rect(from, y, Math.Max(to - from, 0), height),
                // Only a character of the value carries one. Generated printing knows in the parse tree
                // that it stands for the whole value, and says so when it is pressed — but a span here
                // would put it among the caret's stops, and the caret would take its height and position
                // from a piece of the symbol nobody can type into.
                piece.IsSource ? piece.Start : 0,
                piece.IsSource ? piece.Length : 0,
                piece.Kind.ToString(),
                isInk: true));
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

    /// <summary>
    /// Paints the symbol by walking its own tree. Everything drawn is a mark recorded while it was laid
    /// out, so what is on the page and what the queries answer about cannot come apart.
    /// <para>
    /// In two layers, so that a caller with something of its own to put between them — a selection wash —
    /// lands over the bars and under the digits, which is where it was always drawn.
    /// </para>
    /// </summary>
    public void Paint(DrawingContext dc, Brush foreground, Action<DrawingContext>? underTheText = null)
    {
        foreach (var mark in Marks().Where(m => m is not TextMark)) mark.PaintOn(dc, foreground);

        underTheText?.Invoke(dc);

        foreach (var mark in Marks().Where(m => m is TextMark)) mark.PaintOn(dc, foreground);
    }

    private IEnumerable<LayoutMark> Marks() =>
        Root.SelfAndDescendants().OfType<LayoutNode>().SelectMany(n => n.Marks);

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
}
