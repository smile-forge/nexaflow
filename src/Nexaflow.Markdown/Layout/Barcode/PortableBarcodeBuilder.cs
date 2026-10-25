using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Layout.Barcode;

/// <summary>
/// Lays a barcode out onto the renderer-free layout tree from what its stage said is drawn (<see cref="BarcodeBlockNode"/>): the bars,
/// and every run printed with them, sized and placed — module arithmetic, not typesetting — so the shared hit-testing and selection
/// work without knowing what a barcode is.
///
/// <para>
/// Bars and guards carry no part — they draw the value, they are not part of what was typed — and nor does anything the format worked
/// out. Only a printed character that is a character of the value carries that character, so the caret stops only where what is printed
/// really is what was written. Text is set and measured a printed run at a time, not a character at a time, so spacing matches what was
/// actually drawn.
/// </para>
/// </summary>
public sealed class PortableBarcodeBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                           PortableNesting nesting)
    : PortableContentBuilder(reading, state, style, isReadOnly, medium, nesting)
{
    /// <summary>OCR-B is the retail-standard face; falls back to monospace since it ships with no OS.</summary>
    private const string LabelFont = "OCR-B, OCRB, OCR B, Consolas, Menlo, monospace";

    private const string SourceFont = "Cascadia Code, Consolas, monospace";

    /// <summary>How big the characters of a block shown as written are set.</summary>
    private const double SourceSize = 13;

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

    /// <summary>How strongly a stand-in symbol's bars are drawn, so the reason it stands in reads as the subject.</summary>
    private const double StandInStrength = 0.25;

    private BarcodeBlockNode _block = null!;
    private BarcodeBarsNode? _bars;

    /// <summary>The value as written — what the wave goes under when it will not encode — or null where nothing follows its colon.</summary>
    private ContentPart? _written;

    /// <summary>Each character of the value as written, by the node it is.</summary>
    private Dictionary<ContentNode, ContentPart> _characters = null!;

    private LayoutFace? _face;
    private LayoutInk? _black;

    private double _labelSize;
    private double _barsLeft, _barsTop, _guardDrop;

    protected override PortableLaid Build()
    {
        // A block that does not read, or a value that will not encode where it cannot be put right in place, is put right in its
        // source: shown as written, with each part at fault marked and why.
        if (Reading.Root.Node is not BarcodeBlockNode block)
            return AsSource([.. Reading.Root.SelfAndDescendants()
                                    .Where(part => part.Trouble is not null && !part.Derived)
                                    .Select(part => (part, part.Trouble!))]);

        _block = block;
        _bars = Reading.Root.Children.Select(part => part.Node).OfType<BarcodeBarsNode>().FirstOrDefault();
        _written = Reading.Root.SelfAndDescendants().FirstOrDefault(part => part.Role == BarcodeRoles.Encoded)?.Part(MatrixRoles.Value);
        _characters = new Dictionary<ContentNode, ContentPart>(ReferenceEqualityComparer.Instance);
        foreach (var character in _written?.Children.Where(part => part.Kind == BarcodeKinds.Character) ?? [])
            _characters[character.Node] = character;

        return Drawn();
    }

    private BarcodeBlock Settings => _block.Settings;

    // ── Laying it out ─────────────────────────────────────────────────────

    private int PatternWidth => _bars?.Modules ?? 0;

    private double LabelSize => _labelSize > 0 ? _labelSize : Settings.FontSize;

    private double LabelHeight => Settings.DisplayValue ? LabelSize * 1.4 : 0;

    private IShapedText Text(string text, double? size = null) =>
        Medium.Shape(text, _face ??= Style.Face(LabelFont), size ?? LabelSize, _black ??= Medium.Ink(LayoutColor.Of(0, 0, 0)));

    /// <summary>Lays out the symbol a value that reads is drawn as — or, while it will not encode, a faint one of its kind.</summary>
    private PortableLaid Drawn()
    {
        var caption = Reading.Root.Children.FirstOrDefault(part => part.Kind == BarcodeKinds.Caption);
        var runs = Reading.Root.Children.Where(part => part.Kind is BarcodeKinds.Group or BarcodeKinds.Worked).ToList();
        var groups = runs.Select(Run).ToList();

        var barsWidth = PatternWidth * Settings.BarWidth;

        _labelSize = FittedLabelSize(groups, barsWidth);
        var gap = _labelSize * 0.35;   // between the bars and a digit set outside them

        // The outside digits widen the symbol; everything else sits within the bars.
        var leftPad = Widest(groups, BarcodeTextPlacement.LeftOfBars, gap);
        var rightPad = Widest(groups, BarcodeTextPlacement.RightOfBars, gap);

        var mainWidth = (_bars?.Main ?? 0) * Settings.BarWidth;

        IShapedText? captioned = null;
        var captionSize = 0d;
        if (caption is not null)
        {
            captionSize = FittedCaptionSize(Run(caption).Text, mainWidth);
            captioned = Text(Run(caption).Text, captionSize);
        }

        var content = Math.Max(leftPad + barsWidth + rightPad, captioned?.Width ?? 0);

        var captionHeight = captioned is null
            ? 0
            : captionSize * 1.35 + CaptionSeparationModules * Settings.BarWidth;

        _barsLeft = Settings.Margin + (content - (leftPad + barsWidth + rightPad)) / 2 + leftPad;
        _barsTop = Settings.Margin + captionHeight;

        // In modules, not font size: tying guard length to the label made guards grow with the text.
        _guardDrop = Settings.BarWidth * GuardExtensionModules;

        var (wide, tall) = (content + Settings.Margin * 2, captionHeight + Settings.BarHeight + LabelHeight + Settings.Margin * 2);

        var build = new PortableBuilder(Medium);
        build.Open(BarcodePiece.Symbol, part: null);

        // A barcode paints its own light ground whatever the theme — a scanner needs dark bars on light.
        build.Draw(new PortableRuleMark(new LayoutRect(0, 0, wide, tall), Ink(Settings.Background, Style.BarcodeLight)));

        LayBars(build);

        if (captioned is not null)
            // Centred on the main symbol, not the whole picture — an add-on sits apart and shouldn't pull the caption toward it.
            LayCaption(build, caption!, captioned, new LayoutPoint(_barsLeft + (mainWidth - captioned.Width) / 2, Settings.Margin), captionSize);

        LayLabel(build, runs, groups, barsWidth, gap);

        build.Close();

        // The wave is under the whole value — that is what the reader would need to change.
        return new PortableLaid(build.Seal(), wide, tall, _block.Refusal is { } refusal ? [PortableDiagnostic.Of(_written!, refusal)] : []);
    }

    /// <summary>What a run prints, and where it goes against the bars.</summary>
    private static BarcodeTextRun Run(ContentPart run) => ((BarcodeRunNode)run.Node).Run;

    private double Widest(IReadOnlyList<BarcodeTextRun> groups, BarcodeTextPlacement where, double gap)
    {
        double widest = 0;
        foreach (var group in groups)
            if (group.Placement == where) widest = Math.Max(widest, Text(group.Text).Width + gap);
        return widest;
    }

    /// <summary>The label's size, shrunk from the block's own until every group fits the well its bars leave for it.</summary>
    private double FittedLabelSize(IReadOnlyList<BarcodeTextRun> groups, double barsWidth)
    {
        _labelSize = 0;                       // measure at the asked-for size, then scale
        double scale = 1;

        foreach (var group in groups)
        {
            // A group set outside the bars has the margin to itself and constrains nothing.
            if (group.Modules <= 0 || group.Placement is BarcodeTextPlacement.LeftOfBars or BarcodeTextPlacement.RightOfBars) continue;

            double natural = Text(group.Text).Width;
            if (natural <= 0) continue;

            double room = group.Modules * Settings.BarWidth * WellFill;
            scale = Math.Min(scale, room / natural);
        }

        return Math.Max(Settings.FontSize * scale, MinimumLabelSize);
    }

    /// <summary>The caption's size — smaller than the label, scaled to the main symbol's width.</summary>
    private double FittedCaptionSize(string caption, double mainWidth)
    {
        double size = LabelSize * CaptionScale;

        double natural = Text(caption, size).Width;
        if (natural > mainWidth && natural > 0) size *= mainWidth / natural;

        return Math.Max(size, MinimumLabelSize);
    }

    // ── The pieces ────────────────────────────────────────────────────────

    /// <summary>Draws the bars — no part, since nothing typed is a bar.</summary>
    private void LayBars(PortableBuilder into)
    {
        if (_bars is null) return;

        var width = PatternWidth * Settings.BarWidth;

        into.Open(BarcodePiece.Bars, part: null, new LayoutPoint(_barsLeft, _barsTop));

        // As tall as the well the guards drop into, whether or not a run of ink reaches the bottom of it.
        into.Covers(new LayoutRect(0, 0, width, Settings.BarHeight + _guardDrop));

        // Faint when the bars are a stand-in, so the error reads as the subject.
        var dark = Ink(Settings.LineColor, Style.BarcodeDark);
        var ink = _bars.StandIn ? Medium.Faded(dark, StandInStrength) : dark;

        // Guards drop past the digits; an add-on lifts clear of the main bars so it reads as a second symbol.
        double addOnFrom = _bars.AddOn;
        double lift = addOnFrom < int.MaxValue ? LabelSize * 1.35 : 0;

        foreach (var (start, length) in _bars.Ink)
        {
            bool addOn = start >= addOnFrom;

            double top = addOn ? lift : 0;
            double height = Settings.BarHeight - (addOn ? lift : 0) + (IsGuard(start) ? _guardDrop : 0);

            into.Draw(new PortableRuleMark(new LayoutRect(start * Settings.BarWidth, top, length * Settings.BarWidth, height), ink));
        }

        // Strike drawn last so it sits over the bars — a barcode with a line through it reads as "wrong".
        if (_block.Refusal is not null)
            into.Draw(new PortableRuleMark(new LayoutRect(0, Settings.BarHeight / 2 - Math.Max(Settings.BarHeight * 0.04, 1.5), width,
                                                          Math.Max(Settings.BarHeight * 0.08, 3)),
                                           Style.Danger));

        into.Close();
    }

    /// <summary>Draws the caption line and lays its editable pieces (the number, not the scheme name).</summary>
    private void LayCaption(PortableBuilder into, ContentPart caption, IShapedText glyphs, LayoutPoint at, double size)
    {
        into.Open(BarcodePiece.Caption, part: null, at);
        into.Draw(new PortableTextMark(glyphs, default, Ink(Settings.LineColor, Style.BarcodeDark)));
        LayPieces(into, caption, glyphs.Height, size);
        into.Close();
    }

    private void LayLabel(PortableBuilder into, IReadOnlyList<ContentPart> runs, IReadOnlyList<BarcodeTextRun> groups, double barsWidth, double gap)
    {
        if (!Settings.DisplayValue) return;

        // The hole where the value goes, under the middle of the bars, the size a character of it would be.
        if (_written?.Children.FirstOrDefault(part => part.Kind == Kinds.Hole) is { } hole)
        {
            var letter = Text("0");
            PortableText.Hole(into, hole, new LayoutPoint(_barsLeft + ((barsWidth - PortableText.HoleWidth(letter)) / 2), _barsTop + Settings.BarHeight),
                              letter, Ink(Settings.LineColor, Style.BarcodeDark));
            return;
        }

        var belowTop = _barsTop + Settings.BarHeight;
        var aboveTop = _barsTop;

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var glyphs = Text(group.Text);

            var at = group.Placement switch
            {
                BarcodeTextPlacement.LeftOfBars => new LayoutPoint(_barsLeft - gap - glyphs.Width, belowTop),
                BarcodeTextPlacement.RightOfBars => new LayoutPoint(_barsLeft + barsWidth + gap, belowTop),
                BarcodeTextPlacement.Above => new LayoutPoint(Centred(group, glyphs, barsWidth), aboveTop),
                _ => new LayoutPoint(Centred(group, glyphs, barsWidth), belowTop),
            };

            // A run the format worked out whole is still drawn, just with nothing inside it — keeps it out of the caret's stops.
            var worked = runs[i].Kind == BarcodeKinds.Worked;

            into.Open(worked ? BarcodePiece.EncodedText : BarcodePiece.Group, part: null, at);
            into.Draw(new PortableTextMark(glyphs, default, Ink(Settings.LineColor, Style.BarcodeDark)));

            if (!worked) LayPieces(into, runs[i], glyphs.Height, null);
            into.Close();
        }

        double Centred(BarcodeTextRun group, IShapedText glyphs, double bars) =>
            group.Modules > 0
                ? _barsLeft + (group.StartModule + group.Modules / 2.0) * Settings.BarWidth - glyphs.Width / 2
                : _barsLeft + (bars - glyphs.Width) / 2;
    }

    /// <summary>Measures each piece as a prefix of the whole run — measuring pieces separately would drift from the glyphs actually drawn.</summary>
    private void LayPieces(PortableBuilder into, ContentPart run, double height, double? size)
    {
        var printed = Run(run).Text;
        var consumed = 0;

        foreach (var piece in run.Children)
        {
            var from = Text(printed[..consumed], size).Width;
            consumed += piece.Node.Text.Length;
            var to = Text(printed[..consumed], size).Width;

            // Only a character of the value carries a part — a part on worked-out text would give the caret a stop nobody could type into.
            var character = piece.Node is BarcodePrintedNode stands ? _characters.GetValueOrDefault(stands.Character) : null;

            into.Open(piece.Node is BarcodePrintedNode ? BarcodePiece.Character : BarcodePiece.EncodedText, character, new LayoutPoint(from, 0));
            into.Covers(new LayoutRect(0, 0, Math.Max(to - from, 0), height));
            into.Close();
        }
    }

    /// <summary>Whether a run of ink begins inside one of the symbol's guard patterns.</summary>
    private bool IsGuard(int start)
    {
        foreach (var (from, length) in _bars!.Guards)
            if (start >= from && start < from + length) return true;
        return false;
    }

    private LayoutInk Ink(HexColor? written, LayoutInk otherwise) =>
        written is { } colour ? Medium.Ink(new LayoutColor(colour.A, colour.R, colour.G, colour.B)) : otherwise;

    /// <summary>How a barcode sets the source it could not lay out: as the fields it was written as.</summary>
    protected override PortableCharacters Characters => new(Style.Face(SourceFont), SourceSize, Style.Text);

    /// <summary>A barcode is drawn at the size its bars say, whatever room it lands in — and so is its source, where it will not draw.</summary>
    protected override double Within(double room) => double.PositiveInfinity;
}
