using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;

namespace Nexaflow.Markdown.Layout.Packet;

/// <summary>
/// Draws a <c>packet</c> block: a row of boxes for every <see cref="PacketConfig.BitsPerRow"/> bits, a box for each field's run
/// of bits on a row with its label in it, and the number of the first and last bit of each run over its ends.
///
/// <para>
/// <strong>Rows of fields, as it is drawn.</strong> The layout is the rows top to bottom, and in each row the runs of the fields
/// on it in the order they are drawn across it — so a row turned round by <c>bitOrder</c> holds its highest bits first — and over
/// them the band of their bit numbers. A run is its box and its label, and stands for the whole field as it was written, so
/// pressing it means that line and choosing it washes the box and nothing over it; a field running across rows is a run in each,
/// every one standing for the same field. The label is the characters written, typed into in the run on the row the field starts
/// on and said again on every row after it; where it is cut short to fit its box, resting on the run says it whole.
/// </para>
/// <para>
/// <strong>The numbers are worked out, never written in.</strong> Which bits a field takes follows from the fields before it and
/// how long each is, so a number over a run is the bit it starts or ends at, standing for nothing — no caret stops in it and no
/// press means it. A field is made longer, shorter or moved by what its right-click and a drag offer (<c>PacketEdits</c>), which
/// number every field again.
/// </para>
/// <para>
/// <strong>A field breaking a rule is drawn where it was written</strong> — the gap or the overlap in plain sight, and one whose
/// width cannot be known a bit wide. A field drawn over another stands in what it covers, so a press there means the one seen.
/// </para>
/// <para>
/// <strong>It fits the room by narrowing its bits</strong>, down to as narrow as a bit can be with every run still holding the
/// numbers over its ends side by side and a letter of its label, setting labels on more lines as their boxes narrow and making a
/// row as tall as its labels need; past that it is drawn smaller.
/// </para>
/// </summary>
internal sealed class PacketBuilder : ContentBuilder
{
    private const double LabelSize = 12;
    private const double BitSize = 10;

    /// <summary>What numbering the bits adds to the air over every row, where the numbers are set — Mermaid's own.</summary>
    private const double Numbered = 10;

    /// <summary>How far over its box a number stands.</summary>
    private const double Over = 2;

    /// <summary>The air round a label inside its box.</summary>
    private const double Inset = 3;

    /// <summary>The air between the numbers over a run's two ends, where the run is as narrow as it can be.</summary>
    private const double Apart = 4;

    internal PacketBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                           LayoutHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    /// <summary>What the packet is drawn in: the colours its front matter writes, and the theme's where it writes none.</summary>
    private DiagramInk Ink { get; }

    /// <summary>What every word the packet draws is made from.</summary>
    private DiagramText Words { get; }

    /// <summary>The sheet the packet is drawn on: its title, what could not be read, and its card.</summary>
    private DiagramSheet Sheet { get; }

    protected override Laid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new LayoutBuilder(Medium);
        var body = Draw(drawing);
        var config = Config;

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten), Ink.Written(config.TitleColour), config.TitleFontSize);
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);

    /// <summary>What the front matter asks of the packet, as its stages left it on the block.</summary>
    private PacketConfig Config => DiagramConfig.Of(Reading, PacketConfig.Default);

    /// <summary>A field as its stages left it, the part of the reading it is, and the numbers and label written in it.</summary>
    private sealed record Field(ContentPart Part, PacketFieldNode Said)
    {
        public ContentPart? Label => this.Part.Inner(PacketKinds.Label);
    }

    /// <summary>
    /// A run of a field's bits as it is laid: where its box stands, its label set in it, its numbers set over it, and the label
    /// whole where it is cut short to fit — what resting on the run says.
    /// </summary>
    private sealed record Box(Field Field, PacketSpan Span, LayoutRect Bounds,
                              IReadOnlyList<(DiagramWords Words, LayoutPoint At)> Label,
                              IReadOnlyList<(DiagramWords Words, LayoutPoint At)> Numbers,
                              string? Tip);

    /// <summary>A run of a field's bits on one row, which of its runs it is, and the numbers set over its ends — the same at any width.</summary>
    private sealed record Run(Field Field, PacketSpan Span, int At, IReadOnlyList<Numbering> Numbers);

    /// <summary>Every box laid at one bit width, and the room they take.</summary>
    private sealed record Plan(IReadOnlyList<Box> Boxes, LayoutSize Size);

    /// <summary>A number over a box, and which end of it the number stands at.</summary>
    private readonly record struct Numbering(DiagramWords Words, LayoutAlign Edge);

    private LayoutSize Draw(LayoutBuilder build)
    {
        var fields = new List<Field>();
        foreach (var part in Reading.Root.SelfAndDescendants())
            if (part.Node is PacketFieldNode said) fields.Add(new Field(part, said));

        // A packet with no field to draw is the source: there is nothing to look at but the lines written, and what is wrong with them.
        if (fields.All(field => field.Said.Spans.Count == 0)) return Sheet.AsWritten();

        var config = Config;

        // The numbers over a run say which bits it takes, not how wide they are drawn, so they are set once whatever width it comes to.
        var runs = fields.SelectMany(field => field.Said.Spans.Select((span, at) => new Run(field, span, at, Numbers(span, config)))).ToList();
        var least = Narrowest(runs, config);

        var plan = Sheet.LaidNarrower(Math.Max(config.BitWidth, least), least, bit => Planned(runs, config, bit), laid => laid.Size.Width);

        Lay(build, plan, config);
        return Sheet.Reduced(plan.Size);
    }

    /// <summary>
    /// How narrow a bit can be drawn with every run still holding what is set in it and over it: the numbers over its two ends side
    /// by side with air between them, or the one over a run one bit wide, and a letter of its label.
    /// </summary>
    private double Narrowest(IReadOnlyList<Run> runs, PacketConfig config)
    {
        var letter = Words.Text("x", config.LabelFontSize ?? LabelSize, Style.Text).Width + (Inset * 2);
        var narrowest = 1.0;

        foreach (var run in runs)
        {
            var numbered = run.Numbers.Count == 0 ? 0 : run.Numbers.Sum(number => number.Words.Width) + (Apart * (run.Numbers.Count - 1));
            narrowest = Math.Max(narrowest, (Math.Max(numbered, letter) + config.PaddingX) / run.Span.Bits);
        }

        return narrowest;
    }

    // ── Where it all goes ───────────────────────────────────────────────────

    /// <summary>Every run laid as a box with a bit <paramref name="bit"/> wide, a row as tall as its labels need.</summary>
    private Plan Planned(IReadOnlyList<Run> runs, PacketConfig config, double bit)
    {
        var across = (long)config.BitsPerRow;

        // A label is set over a box washed in the theme's accent, as every diagram's node is, so it is the theme's text — and,
        // where the front matter fills the box itself, whatever reads on that fill.
        var labelInk = Ink.Written(config.LabelColour) ?? (Ink.Written(config.BlockFillColour) is { } filled ? Ink.Over(filled) : Style.Text);
        var labelSize = config.LabelFontSize ?? LabelSize;

        var drafts = new List<(Run Run, double X, double Width, IReadOnlyList<DiagramWords> Label)>();
        var rows = 0;

        foreach (var run in runs)
        {
            // A row turned round is numbered from its far end, so a run stands as far from the right as it would from the left.
            var column = run.Span.From % across;
            if (config.BitOrder == PacketBitOrder.Descending) column = across - column - run.Span.Bits;

            // Mermaid's own: each box a bit's width for each of its bits, less the air before the next.
            var width = Math.Max(1, (run.Span.Bits * bit) - config.PaddingX);
            drafts.Add((run, (column * bit) + 1, width, Labelled(run.Field, run.At == 0, width - (Inset * 2), labelSize, labelInk)));
            rows = Math.Max(rows, run.Span.Row + 1);
        }

        var heights = Enumerable.Repeat(config.RowHeight, rows).ToArray();
        foreach (var draft in drafts)
            heights[draft.Run.Span.Row] = Math.Max(heights[draft.Run.Span.Row], draft.Label.Sum(line => line.Height) + (Inset * 2));

        // The numbers are set in the air over each row, which numbering them makes room for.
        var air = config.PaddingY + (runs.Any(run => run.Numbers.Count > 0) ? Numbered : 0);
        var tops = new double[rows];
        var down = 0.0;

        for (var row = 0; row < rows; row++)
        {
            down += air;
            tops[row] = down;
            down += heights[row];
        }

        var boxes = drafts.Select(draft =>
        {
            var bounds = new LayoutRect(draft.X, tops[draft.Run.Span.Row], draft.Width, heights[draft.Run.Span.Row]);
            var numbers = draft.Run.Numbers.Select(number => (number.Words, Above(number, bounds))).ToList();
            var tip = draft.Label.Any(line => line.CutShort) && draft.Run.Field.Label.Words() is { } words ? Words.Shown(words) : null;

            return new Box(draft.Run.Field, draft.Run.Span, bounds, [.. DiagramWords.Stack(draft.Label, bounds)], numbers, tip);
        }).ToList();

        return new Plan(boxes, new LayoutSize((across * bit) + 2, down));
    }

    /// <summary>Where a number stands over the end of <paramref name="bounds"/> it numbers — over the middle, for a box one bit wide.</summary>
    private static LayoutPoint Above(Numbering number, LayoutRect bounds)
    {
        var top = bounds.Top - Over - number.Words.Height;

        return number.Edge switch
        {
            LayoutAlign.Left => new LayoutPoint(bounds.Left, top),
            LayoutAlign.Right => new LayoutPoint(bounds.Right - number.Words.Width, top),
            _ => new LayoutPoint(bounds.Left + ((bounds.Width - number.Words.Width) / 2), top),
        };
    }

    // ── What is set in and over a box ───────────────────────────────────────

    /// <summary>
    /// A field's label: the words written, typed into — on as many lines as its box needs — in the box on the row the field
    /// starts on, and what they say on every row after it.
    /// </summary>
    private IReadOnlyList<DiagramWords> Labelled(Field field, bool first, double width, double size, LayoutInk ink)
    {
        var label = field.Label;
        var words = label.Words();

        return first
            ? Words.Wrapped(words, label.Hole(), size, ink, Math.Max(1, width))
            : [Words.Worked(words is null ? string.Empty : Words.Shown(words), field.Part, size, ink)];
    }

    /// <summary>
    /// The numbers over a run's ends: its first and last bit as the row reads, or the one bit of a run one bit wide — worked out from
    /// the bits, standing for nothing: nobody writes in them and no press means them. None where the bits are not numbered.
    /// </summary>
    private IReadOnlyList<Numbering> Numbers(PacketSpan span, PacketConfig config)
    {
        if (!config.ShowBits) return [];

        var size = config.ByteFontSize ?? BitSize;
        var leading = Ink.Written(config.StartByteColour) ?? Style.TextMuted;
        var trailing = Ink.Written(config.EndByteColour) ?? Style.TextMuted;

        if (span.Bits == 1) return [new Numbering(Number(span.From, size, leading), LayoutAlign.Centre)];

        var descending = config.BitOrder == PacketBitOrder.Descending;

        return
        [
            new Numbering(Number(descending ? span.To : span.From, size, leading), LayoutAlign.Left),
            new Numbering(Number(descending ? span.From : span.To, size, trailing), LayoutAlign.Right),
        ];
    }

    /// <summary>The number of <paramref name="bit"/>, standing for nothing.</summary>
    private DiagramWords Number(long bit, double size, LayoutInk ink) => Words.Worked(bit.ToString(CultureInfo.InvariantCulture), null, size, ink);

    // ── Drawing ─────────────────────────────────────────────────────────────

    private void Lay(LayoutBuilder build, Plan plan, PacketConfig config)
    {
        var fill = Fill(config);
        var edge = Ink.Written(config.BlockStrokeColour) ?? Ink.NodeEdge;
        var thickness = config.BlockStrokeWidth ?? 1;

        build.Open(PacketPiece.Rows, part: null, stops: LayoutStops.None);

        foreach (var row in plan.Boxes.GroupBy(box => box.Span.Row).OrderBy(row => row.Key))
        {
            // Across the row as it is drawn, which is the order it reads in — its highest bits first, where it is turned round.
            var boxes = row.OrderBy(box => box.Bounds.Left).ToList();

            build.Open(PacketPiece.Row, part: null, stops: LayoutStops.None);

            for (var at = 0; at < boxes.Count; at++)
            {
                var box = boxes[at];
                var outline = build.Medium.Rectangle(box.Bounds);

                // A box stands in what its words and the boxes drawn after it on its row leave uncovered: fields overlap only on a
                // row they share, and a press there means the one seen.
                var over = new List<LayoutShape>();
                foreach (var (words, place) in box.Label) over.Add(build.Medium.Rectangle(new LayoutRect(place, new LayoutSize(words.Width, words.Height))));
                for (var later = at + 1; later < boxes.Count; later++)
                    if (boxes[later].Bounds.IntersectsWith(box.Bounds)) over.Add(build.Medium.Rectangle(boxes[later].Bounds));

                build.Open(PacketPiece.Field, box.Field.Part, stops: LayoutStops.None);
                if (box.Tip is { } tip) build.Acts(new LayoutActions { Tip = tip });

                build.Open(MermaidPiece.Shape, box.Field.Part, stops: LayoutStops.None);
                build.Draw(DiagramMarks.Outlined(build.Medium, outline, fill, edge, thickness));
                build.Occupies(over.Count == 0 ? outline : build.Medium.Combine(outline, build.Medium.Group(over), ShapeCombine.Exclude));
                build.Close();

                foreach (var (words, place) in box.Label) words.Set(build, place, PacketPiece.Label);

                build.Close();
            }

            // Over the row's fields and apart from them, so choosing a field washes its box and not the numbers over it.
            build.Open(PacketPiece.Bits, part: null, stops: LayoutStops.None);
            foreach (var box in boxes)
                foreach (var (words, place) in box.Numbers) words.Set(build, place, PacketPiece.Bit);
            build.Close();

            build.Close();
        }

        build.Close();
    }

    /// <summary>What a field is filled with: the front matter's <c>blockFillColor</c>, or what the theme fills a box with.</summary>
    private LayoutInk Fill(PacketConfig config) => Ink.Written(config.BlockFillColour) ?? Ink.Node;
}
