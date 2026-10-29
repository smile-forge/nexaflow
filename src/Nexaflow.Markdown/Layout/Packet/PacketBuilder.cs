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
/// <strong>Two layers.</strong> The fields — each box and its label — and the numbers over them, so the numbers sit over every
/// box whatever is drawn next to it. A box stands for the whole field as it was written, so pressing it means that line; its
/// label is the characters written, typed into in the box on the row the field starts on, and said again in the box on every
/// row it runs on into. A number over an end of a field is the number written there where it is — typed into — and otherwise
/// worked out from the bits, and pressed as the field.
/// </para>
/// <para>
/// <strong>What is wrong is drawn where it can be put right.</strong> A field that starts where it should not is drawn where it
/// was written to start, the gap or the overlap in plain sight; one whose width cannot be known is drawn a bit wide, with the
/// number in the way over it — a hole, where it is still to write. A field drawn over another stands in what it covers, so a
/// press there means the one seen.
/// </para>
/// <para>
/// <strong>It fits the room by narrowing its bits</strong>, down to as narrow as a bit can be and still be numbered, setting
/// labels on more lines as their boxes narrow and making a row as tall as its labels need; past that it is drawn smaller.
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

    /// <summary>How narrow a bit may be drawn before the packet is drawn smaller instead: as narrow as still takes its number.</summary>
    private const double Narrowest = 14;

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
        /// <summary>The number written as <paramref name="role"/> — its start, end or count — or null where none is written as that.</summary>
        public ContentPart? Number(string role) =>
            this.Part.SelfAndDescendants().FirstOrDefault(part => part.Kind == Kinds.Number && part.Role == role);

        public ContentPart? Label => this.Part.Inner(PacketKinds.Label);
    }

    /// <summary>A run of a field's bits as it is laid: where its box stands, its label set in it, and its numbers set over it.</summary>
    private sealed record Box(Field Field, PacketSpan Span, LayoutRect Bounds,
                              IReadOnlyList<(DiagramWords Words, LayoutPoint At)> Label,
                              IReadOnlyList<(DiagramWords Words, LayoutPoint At)> Numbers);

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
        var plan = Sheet.LaidNarrower(config.BitWidth, Math.Min(config.BitWidth, Narrowest),
                                      bit => Planned(fields, config, bit), laid => laid.Size.Width);

        Lay(build, plan, config);
        return Sheet.Reduced(plan.Size);
    }

    // ── Where it all goes ───────────────────────────────────────────────────

    /// <summary>Every field's runs laid as boxes with a bit <paramref name="bit"/> wide, a row as tall as its labels need.</summary>
    private Plan Planned(IReadOnlyList<Field> fields, PacketConfig config, double bit)
    {
        var across = (long)config.BitsPerRow;
        var labelInk = Ink.Written(config.LabelColour) ?? Ink.Over(Fill(config));
        var labelSize = config.LabelFontSize ?? LabelSize;

        var drafts = new List<(Field Field, PacketSpan Span, double X, double Width, IReadOnlyList<DiagramWords> Label, IReadOnlyList<Numbering> Numbers)>();
        var rows = 0;

        foreach (var field in fields)
        {
            for (var run = 0; run < field.Said.Spans.Count; run++)
            {
                var span = field.Said.Spans[run];

                // A row turned round is numbered from its far end, so a run stands as far from the right as it would from the left.
                var column = span.From % across;
                if (config.BitOrder == PacketBitOrder.Descending) column = across - column - span.Bits;

                // Mermaid's own: each box a bit's width for each of its bits, less the air before the next.
                var width = Math.Max(1, (span.Bits * bit) - config.PaddingX);
                var label = Labelled(field, run == 0, width - (Inset * 2), labelSize, labelInk);

                drafts.Add((field, span, (column * bit) + 1, width, label, Numbers(field, span, config)));
                rows = Math.Max(rows, span.Row + 1);
            }
        }

        var heights = Enumerable.Repeat(config.RowHeight, rows).ToArray();
        foreach (var draft in drafts)
            heights[draft.Span.Row] = Math.Max(heights[draft.Span.Row], draft.Label.Sum(line => line.Height) + (Inset * 2));

        // The numbers are set in the air over each row, which numbering them makes room for.
        var air = config.PaddingY + (drafts.Any(draft => draft.Numbers.Count > 0) ? Numbered : 0);
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
            var bounds = new LayoutRect(draft.X, tops[draft.Span.Row], draft.Width, heights[draft.Span.Row]);
            var numbers = draft.Numbers.Select(number => (number.Words, Above(number, bounds))).ToList();

            return new Box(draft.Field, draft.Span, bounds, [.. DiagramWords.Stack(draft.Label, bounds)], numbers);
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
    /// The numbers over a run's ends: its first and last bit as the row reads, or the one bit of a run one bit wide. Over a field
    /// whose width cannot be known, the number in the way of knowing it — the end or the count — is the one set, so it can be put
    /// right where it stands.
    /// </summary>
    private IReadOnlyList<Numbering> Numbers(Field field, PacketSpan span, PacketConfig config)
    {
        var size = config.ByteFontSize ?? BitSize;
        var leading = Ink.Written(config.StartByteColour) ?? Style.TextMuted;
        var trailing = Ink.Written(config.EndByteColour) ?? Style.TextMuted;
        var said = field.Said;

        if (said.Unsized is { } unsized)
            return Only(Number(field, field.Number(unsized), said.First, config, size, leading), LayoutAlign.Centre);

        var start = span.From == said.First ? field.Number(PacketRoles.Start) : null;
        var end = span.To == said.Last ? field.Number(PacketRoles.End) : null;

        if (span.Bits == 1) return Only(Number(field, start ?? end, span.From, config, size, leading), LayoutAlign.Centre);

        var descending = config.BitOrder == PacketBitOrder.Descending;
        var low = Number(field, start, span.From, config, size, descending ? trailing : leading);
        var high = Number(field, end, span.To, config, size, descending ? leading : trailing);

        var numbers = new List<Numbering>(2);
        if (low is not null) numbers.Add(new Numbering(low, descending ? LayoutAlign.Right : LayoutAlign.Left));
        if (high is not null) numbers.Add(new Numbering(high, descending ? LayoutAlign.Left : LayoutAlign.Right));

        return numbers;

        static IReadOnlyList<Numbering> Only(DiagramWords? words, LayoutAlign edge) => words is null ? [] : [new Numbering(words, edge)];
    }

    /// <summary>
    /// The number of <paramref name="bit"/>: the number written, where <paramref name="written"/> is the one written there, and
    /// otherwise worked out and pressed as the field — or null where the bits are not numbered. A number written wrong, or still
    /// to write, is set whether or not they are: it is in the way, and set is where it is put right.
    /// </summary>
    private DiagramWords? Number(Field field, ContentPart? written, long bit, PacketConfig config, double size, LayoutInk ink)
    {
        if (written is not null)
            return config.ShowBits || written.Trouble is not null || written.Length == 0
                ? Words.Written(written, written.Parent.Hole(), size, ink)
                : null;

        return config.ShowBits ? Words.Worked(bit.ToString(CultureInfo.InvariantCulture), field.Part, size, ink) : null;
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    private void Lay(LayoutBuilder build, Plan plan, PacketConfig config)
    {
        var fill = Fill(config);
        var edge = Ink.Written(config.BlockStrokeColour) ?? Ink.NodeEdge;
        var thickness = config.BlockStrokeWidth ?? 1;

        // Fields overlap only on a row they share, so a box asks only what is drawn after it on its own row what covers it.
        var onRow = plan.Boxes.Select((box, at) => (box.Span.Row, at)).GroupBy(each => each.Row)
                              .ToDictionary(row => row.Key, row => row.Select(each => each.at).ToList());

        build.Open(PacketPiece.Fields, part: null, stops: LayoutStops.None);

        for (var at = 0; at < plan.Boxes.Count; at++)
        {
            var box = plan.Boxes[at];
            var outline = build.Medium.Rectangle(box.Bounds);

            var over = new List<LayoutShape>();
            foreach (var (words, place) in box.Label) over.Add(build.Medium.Rectangle(new LayoutRect(place, new LayoutSize(words.Width, words.Height))));
            foreach (var later in onRow[box.Span.Row])
                if (later > at && plan.Boxes[later].Bounds.IntersectsWith(box.Bounds)) over.Add(build.Medium.Rectangle(plan.Boxes[later].Bounds));

            build.Open(PacketPiece.Field, box.Field.Part, stops: LayoutStops.None);

            build.Open(MermaidPiece.Shape, box.Field.Part, stops: LayoutStops.None);
            build.Draw(DiagramMarks.Outlined(build.Medium, outline, fill, edge, thickness));
            build.Occupies(over.Count == 0 ? outline : build.Medium.Combine(outline, build.Medium.Group(over), ShapeCombine.Exclude));
            build.Close();

            foreach (var (words, place) in box.Label) words.Set(build, place, PacketPiece.Label);

            build.Close();
        }

        build.Close();

        build.Open(PacketPiece.Bits, part: null, stops: LayoutStops.None);
        foreach (var box in plan.Boxes)
            foreach (var (words, place) in box.Numbers) words.Set(build, place, PacketPiece.Bit);
        build.Close();
    }

    /// <summary>What a field is filled with: the front matter's <c>blockFillColor</c>, or what the theme fills a box with.</summary>
    private LayoutInk Fill(PacketConfig config) => Ink.Written(config.BlockFillColour) ?? Ink.Node;
}
