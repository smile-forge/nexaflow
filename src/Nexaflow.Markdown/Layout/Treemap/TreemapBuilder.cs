using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Treemap;

namespace Nexaflow.Markdown.Layout.Treemap;

/// <summary>
/// Draws a <c>treemap</c> block: every item a tile as big as its share of what the map adds up to, a section's tile holding the
/// tiles of the items in it under a band with its label and total, and a leaf's its label and value.
///
/// <para>
/// <strong>Tiles in tiles, as it is drawn.</strong> A treemap is boxes inside boxes, so the layout is too: a section is a piece
/// holding its own tile and band, then the pieces of the items in it — which is what makes choosing a section wash the whole of it,
/// and a press on a tile inside it mean that tile. A section's own tile stands only in its band and the gutters round what it
/// holds. The tiles are laid as near square as they go, largest first (<see cref="TreemapTiles"/>), as Mermaid lays them, and each
/// item is coloured by its label, as Mermaid colours it.
/// </para>
/// <para>
/// <strong>What is written is written in where it is drawn.</strong> A label is the characters written, typed into; a leaf's value is
/// set as the front matter's <c>valueFormat</c> says, and pressed it is shown as written and typed into. A section's total follows
/// from what is in it, so it is worked out and never a place for the caret.
/// </para>
/// <para>
/// <strong>Nothing written goes missing.</strong> An item worth nothing — a section with nothing in it yet, a leaf whose value is
/// nought or still to write — has no room in the map, so it is drawn under it as a chip of its own, where it can still be picked
/// out, written in and given something.
/// </para>
/// <para>
/// It is drawn as wide as the room, to as wide as <c>nodeWidth</c> asks, as tall as <c>nodeHeight</c> makes it at that width.
/// </para>
/// </summary>
internal sealed class TreemapBuilder : ContentBuilder
{
    /// <summary>How deep a section's band is, at the least — Mermaid's own.</summary>
    private const double Band = 25;

    private const double BandSize = 12;
    private const double TotalSize = 10;

    /// <summary>The air round what is set in a tile.</summary>
    private const double Inset = 4;

    /// <summary>The air between the map and the chips under it, and between two chips.</summary>
    private const double Apart = 10;

    /// <summary>How solid a leaf's tile, a section's band and a section's own tile are filled, over the colour its label gives it.</summary>
    private const double LeafFill = 0.6;
    private const double BandFill = 0.45;
    private const double SectionFill = 0.18;

    internal TreemapBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                            LayoutHost host) : base(reading, state, style, isReadOnly, medium, host)
    {
        Ink = new DiagramInk(style, medium);
        Words = new DiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, LayoutStanding.Inline));
        Sheet = new DiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    private DiagramInk Ink { get; }

    private DiagramText Words { get; }

    private DiagramSheet Sheet { get; }

    protected override Laid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new LayoutBuilder(Medium);
        var body = Draw(drawing);

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten));
    }

    protected override LayoutCharacters Characters => DiagramSheet.Characters(Style);

    /// <summary>What the front matter asks of the treemap, as its stages left it on the block.</summary>
    private TreemapConfig Config => DiagramConfig.Of(Reading, TreemapConfig.Default);

    /// <summary>
    /// An item as its stages left it, the items under it, which of the theme's colours it is handed, and what it is laid out as worth —
    /// it and everything under it (<see cref="Kept"/>).
    /// </summary>
    private sealed record Item(ContentPart Part, TreemapNode Said, IReadOnlyList<Item> Under, int Shade, double Worth)
    {
        public ContentPart? Label => this.Part.Children[0].Stated()?.Inner(TreemapKinds.Label);

        public ContentPart? Value => this.Part.Children[0].Stated()?.Inner(MermaidKinds.Amount);

        /// <summary>Whether it has room in the map: it is worth something.</summary>
        public bool Drawn => this.Worth > 0;
    }

    /// <summary>What an item is drawn with: its fill, its band's, its outline, its words, and how thick its outline is.</summary>
    private readonly record struct Paint(LayoutInk Fill, LayoutInk Band, LayoutInk Edge, LayoutInk Words, double Thickness);

    private LayoutSize Draw(LayoutBuilder build)
    {
        var weighed = Items(Reading.Root);
        var items = Shaded(weighed, Shades(weighed), Whole);

        // A treemap with no item to draw is the source: there is nothing to look at but the lines written.
        if (items.Count == 0) return Sheet.AsWritten();

        var config = Config;
        var width = Math.Max(Band * 4, double.IsFinite(Sheet.Space) ? Math.Min(Sheet.Space, config.NodeWidth * 10) : config.NodeWidth * 10);
        var drawn = items.Where(item => item.Drawn).ToList();
        var bottom = 0.0;

        if (drawn.Count > 0)
        {
            var map = new LayoutRect(0, 0, width, width * config.NodeHeight / config.NodeWidth);

            build.Open(TreemapPiece.Map, part: null, stops: LayoutStops.None);
            foreach (var (item, tile) in Tiled(drawn, map, config)) Lay(build, item, tile, config);
            build.Close();

            bottom = map.Bottom;
        }

        var empty = Preorder(items).Where(item => !item.Drawn).ToList();
        if (empty.Count > 0) bottom = Chips(build, empty, drawn.Count > 0 ? bottom + Apart : 0, width, config);

        return Sheet.Reduced(new LayoutSize(width, bottom));
    }

    /// <summary>The items gathered in <paramref name="holder"/>, each with those under it and what it is laid out as worth, in the order written.</summary>
    private List<Item> Items(ContentPart holder)
    {
        var items = new List<Item>();

        foreach (var part in ItemsIn(holder))
        {
            var said = (TreemapNode)part.Node;
            var under = Items(part);
            items.Add(new Item(part, said, under, Whole, Kept(part, said) + under.Sum(item => item.Worth)));
        }

        return items;
    }

    /// <summary>
    /// What <paramref name="item"/>'s own value is laid out as: what it said before the step being typed into it began, while the caret is
    /// still in it — so the tiles hold still under the caret, and move once, when it leaves — and otherwise what is written.
    /// </summary>
    private double Kept(ContentPart item, TreemapNode said) =>
        State.Raw is { } zone && item.Children[0].Stated()?.Inner(MermaidKinds.Amount) is { } amount
        && zone.Start >= amount.Start && zone.End <= amount.End && ShownBefore is { } before
            ? TreemapValues.Read(before) ?? 0
            : said.Value ?? 0;

    /// <summary>
    /// <paramref name="items"/> with the colours they are handed: a section the colour <paramref name="shades"/> gives its label, and a
    /// leaf the colour of what it is in, <paramref name="within"/> — as Mermaid colours them.
    /// </summary>
    private static List<Item> Shaded(IReadOnlyList<Item> items, IReadOnlyDictionary<string, int> shades, int within) =>
        [.. items.Select(item => item.Under.Count > 0 && shades[LabelOf(item.Part)] is var shade
                ? item with { Shade = shade, Under = Shaded(item.Under, shades, shade) }
                : item with { Shade = within })];

    /// <summary>The colour the treemap itself is handed, before any section: the theme's first. An item in no section is drawn in it.</summary>
    private const int Whole = 0;

    /// <summary>
    /// The colour each section's label gives it, handed out as Mermaid hands them: in the order it draws sections — the treemap itself
    /// first, then a level at a time from the top, the largest first among those side by side. Where an item is written among its
    /// siblings changes no colour, and the same label is the same colour.
    /// </summary>
    private static Dictionary<string, int> Shades(IReadOnlyList<Item> top)
    {
        var shades = new Dictionary<string, int>(StringComparer.Ordinal);
        var waiting = new Queue<IReadOnlyList<Item>>([top]);

        while (waiting.TryDequeue(out var level))
            foreach (var item in level.OrderByDescending(item => item.Worth).Where(item => item.Under.Count > 0))
            {
                shades.TryAdd(LabelOf(item.Part), shades.Count + 1);
                waiting.Enqueue(item.Under);
            }

        return shades;
    }

    /// <summary>The items written directly in <paramref name="holder"/>, in the order written.</summary>
    private static IEnumerable<ContentPart> ItemsIn(ContentPart holder) =>
        holder.Children.Where(part => part.Kind == TreemapKinds.Node && part.Node is TreemapNode);

    /// <summary>What an item's label says — nothing, where it says nothing yet.</summary>
    private static string LabelOf(ContentPart item) => item.Children[0].Stated()?.Inner(TreemapKinds.Label)?.Words()?.Text ?? string.Empty;

    private static IEnumerable<Item> Preorder(IEnumerable<Item> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var under in Preorder(item.Under)) yield return under;
        }
    }

    /// <summary>Where each of <paramref name="items"/> goes in <paramref name="room"/>, largest first, with the air between them.</summary>
    private static IEnumerable<(Item Item, LayoutRect Tile)> Tiled(IReadOnlyList<Item> items, LayoutRect room, TreemapConfig config)
    {
        var ordered = items.OrderByDescending(item => item.Worth).ToList();
        var tiles = TreemapTiles.Squarified([.. ordered.Select(item => item.Worth)], room);
        var air = config.Padding / 2;

        for (var at = 0; at < ordered.Count; at++)
            yield return (ordered[at], Inside(tiles[at], air));
    }

    /// <summary><paramref name="rect"/> less <paramref name="air"/> all round — as little as nothing.</summary>
    private static LayoutRect Inside(LayoutRect rect, double air)
    {
        var across = Math.Min(air, rect.Width / 2);
        var down = Math.Min(air, rect.Height / 2);

        return new LayoutRect(rect.X + across, rect.Y + down, rect.Width - (across * 2), rect.Height - (down * 2));
    }

    // ── A tile ──────────────────────────────────────────────────────────────

    private void Lay(LayoutBuilder build, Item item, LayoutRect tile, TreemapConfig config)
    {
        if (tile.Width < 1 || tile.Height < 1) return;

        var under = item.Under.Where(each => each.Drawn).ToList();
        if (under.Count > 0) Section(build, item, under, tile, config);
        else Leaf(build, item, tile, config);
    }

    /// <summary>
    /// A section: its tile, its band with its label — and its total, where values are shown — and the tiles of what is in it, laid in
    /// the tile under the band.
    /// </summary>
    private void Section(LayoutBuilder build, Item item, IReadOnlyList<Item> under, LayoutRect tile, TreemapConfig config)
    {
        var paint = Painted(item, config);

        var total = config.ShowValues ? Words.Worked(TreemapValues.Format(item.Worth, config.ValueFormat), item.Part, TotalSize, paint.Words) : null;
        var room = Math.Max(1, tile.Width - (Inset * 2) - (total is null ? 0 : total.Width + Inset));
        var label = Labelled(item, BandSize, paint.Words, room).FirstOrDefault();

        var deep = Math.Min(tile.Height, Math.Max(Band, (label?.Height ?? 0) + (Inset * 2)));
        var band = new LayoutRect(tile.X, tile.Y, tile.Width, deep);
        var inside = new LayoutRect(tile.X, band.Bottom, tile.Width, Math.Max(0, tile.Height - deep));

        var placed = new List<(DiagramWords Words, LayoutPoint At, string Kind)>();
        if (label is not null) placed.Add((label, new LayoutPoint(tile.X + Inset, band.Y + ((deep - label.Height) / 2)), TreemapPiece.Label));
        if (total is not null && total.Width + (Inset * 2) <= tile.Width)
            placed.Add((total, new LayoutPoint(tile.Right - Inset - total.Width, band.Y + ((deep - total.Height) / 2)), TreemapPiece.Total));

        var tiles = inside.Height >= 1 ? Tiled(under, inside, config).ToList() : [];

        build.Open(TreemapPiece.Section, item.Part, stops: LayoutStops.None);

        var outline = Medium.Rectangle(tile);
        build.Open(MermaidPiece.Shape, item.Part, stops: LayoutStops.None);

        // Its band tells what it had no room for — the tiles in it tell their own.
        var labelDrawn = label is not null && !label.CutShort;
        var totalDrawn = total is null || placed.Any(words => words.Kind == TreemapPiece.Total);
        if (Untold(item, total, labelDrawn, totalDrawn) is { } untold) build.Tip(untold);

        build.Draw(DiagramMarks.Outlined(Medium, outline, paint.Fill, paint.Edge, paint.Thickness));
        build.Draw(DiagramMarks.Filled(Medium.Rectangle(band), paint.Band));
        build.Occupies(Less(outline, [.. placed.Select(words => Covered(words.Words, words.At)), .. tiles.Select(each => each.Tile)]));
        build.Close();

        foreach (var (words, at, kind) in placed) words.Set(build, at, kind);
        foreach (var (each, inner) in tiles) Lay(build, each, inner, config);

        build.Close();
    }

    /// <summary>A leaf: its tile, and its label and value set in the middle of it — as many of their lines as it has room for.</summary>
    private void Leaf(LayoutBuilder build, Item item, LayoutRect tile, TreemapConfig config)
    {
        var paint = Painted(item, config);
        var label = Labelled(item, config.LabelFontSize, paint.Words, Math.Max(1, tile.Width - (Inset * 2)));
        var value = Valued(item, config, paint.Words);
        var lines = Fitted(label, value, tile.Height - (Inset * 2));

        var stacked = DiagramWords.Stack(lines.Select(line => line.Words).ToList(), Inside(tile, Inset)).ToList();
        var outline = Medium.Rectangle(tile);

        build.Open(TreemapPiece.Leaf, item.Part, stops: LayoutStops.None);

        // What its room left out it tells a reader pointing at it: its label and its value where the label is not all drawn, and its
        // value where only that is left off.
        var labelDrawn = lines.Count(line => line.Kind == TreemapPiece.Label) == label.Count && !lines.Any(line => line.Words.CutShort);
        var valueDrawn = value is not { Says.Length: > 0 } || lines.Any(line => line.Kind == TreemapPiece.Value);
        if (Untold(item, value, labelDrawn, valueDrawn) is { } untold) build.Tip(untold);

        build.Open(MermaidPiece.Shape, item.Part, stops: LayoutStops.None);
        build.Draw(DiagramMarks.Outlined(Medium, outline, paint.Fill, paint.Edge, paint.Thickness));
        build.Occupies(Less(outline, [.. stacked.Select(words => Covered(words.Words, words.At))]));
        build.Close();

        // Its words stood together apart from the tile under them, so up and down step between the label and the value.
        build.Open(TreemapPiece.Words, part: null, stops: LayoutStops.None);
        for (var at = 0; at < stacked.Count; at++) stacked[at].Words.Set(build, stacked[at].At, lines[at].Kind);
        build.Close();

        build.Close();
    }

    /// <summary>
    /// What an item's tile or band would have said that its room left out: its label and its value — or total — on lines of their own
    /// where the label is not all drawn, its value alone where only that is left off, and null where everything is drawn.
    /// </summary>
    private static string? Untold(Item item, DiagramWords? value, bool labelDrawn, bool valueDrawn)
    {
        var says = value is { Says.Length: > 0 } ? value.Says : null;
        var label = item.Label?.Words()?.Text;

        return !labelDrawn && label is { Length: > 0 } ? (says is null ? label : label + "\n" + says)
             : !valueDrawn ? says
             : null;
    }

    /// <summary>
    /// The lines of a label and the value under it that go in <paramref name="room"/> of height: the label's lines from the first for as
    /// long as they fit, and the value after them where there is room left for it.
    /// </summary>
    private static List<(DiagramWords Words, string Kind)> Fitted(IReadOnlyList<DiagramWords> label, DiagramWords? value, double room)
    {
        var kept = new List<(DiagramWords, string)>();
        var taken = 0.0;

        foreach (var line in label)
        {
            if (taken + line.Height > room) break;

            kept.Add((line, TreemapPiece.Label));
            taken += line.Height;
        }

        if (value is not null && taken + value.Height <= room) kept.Add((value, TreemapPiece.Value));
        return kept;
    }

    // ── Under the map ───────────────────────────────────────────────────────

    /// <summary>
    /// The items worth nothing, under the map from <paramref name="top"/> down: a chip each — its label, and a leaf's value or the hole
    /// where it goes — along a row as wide as the map and on into the next. How far down they reach.
    /// </summary>
    private double Chips(LayoutBuilder build, IReadOnlyList<Item> items, double top, double width, TreemapConfig config)
    {
        build.Open(TreemapPiece.Empty, part: null, stops: LayoutStops.None);

        var (across, down, row) = (0.0, top, 0.0);

        foreach (var item in items)
        {
            var paint = Painted(item, config);
            var label = Labelled(item, config.LabelFontSize, paint.Words, double.PositiveInfinity).FirstOrDefault();
            var value = item.Said.Section ? null : Valued(item, config, paint.Words);

            var wide = (Inset * 2) + (label?.Width ?? 0) + (value is null ? 0 : value.Width + Inset);
            var high = (Inset * 2) + Math.Max(label?.Height ?? 0, value?.Height ?? 0);

            if (across > 0 && across + wide > width)
            {
                (across, down) = (0, down + row + Apart);
                row = 0;
            }

            var chip = new LayoutRect(across, down, wide, high);
            var placed = new List<(DiagramWords Words, LayoutPoint At, string Kind)>();
            if (label is not null) placed.Add((label, new LayoutPoint(chip.X + Inset, chip.Y + ((high - label.Height) / 2)), TreemapPiece.Label));
            if (value is not null) placed.Add((value, new LayoutPoint(chip.Right - Inset - value.Width, chip.Y + ((high - value.Height) / 2)), TreemapPiece.Value));

            var outline = Medium.Rectangle(chip, 3, 3);

            build.Open(item.Said.Section ? TreemapPiece.Section : TreemapPiece.Leaf, item.Part, stops: LayoutStops.None);

            build.Open(MermaidPiece.Shape, item.Part, stops: LayoutStops.None);
            build.Draw(DiagramMarks.Outlined(Medium, outline, Ink.Faded(paint.Fill, 0.5), paint.Edge, paint.Thickness, [4, 3]));
            build.Occupies(Less(outline, [.. placed.Select(words => Covered(words.Words, words.At))]));
            build.Close();

            foreach (var (words, at, kind) in placed) words.Set(build, at, kind);

            build.Close();

            across += wide + Apart;
            row = Math.Max(row, high);
        }

        build.Close();
        return down + row;
    }

    // ── What is set ─────────────────────────────────────────────────────────

    /// <summary>An item's label: the words written, typed into — wrapped to <paramref name="width"/> — or the hole where nothing is written yet.</summary>
    private IReadOnlyList<DiagramWords> Labelled(Item item, double size, LayoutInk ink, double width)
    {
        var label = item.Label;
        return Words.Wrapped(label.Words(), label.Hole(), size, ink, width);
    }

    /// <summary>
    /// A leaf's value: the number written, set as the front matter formats it and typed into as written; what is written, where it is no
    /// number; the hole after the colon where nothing is written yet. Nothing for a section, or where values are not shown.
    /// </summary>
    private DiagramWords? Valued(Item item, TreemapConfig config, LayoutInk ink)
    {
        if (!config.ShowValues || item.Value is not { } amount) return null;

        if (amount.Hole() is { } hole) return Words.Written(amount, hole, config.ValueFontSize, ink);
        if (amount.Inner(Kinds.Number) is not { Length: > 0 } number) return null;

        return item.Said.Value is { } value
            ? Words.Formatted(number, TreemapValues.Format(value, config.ValueFormat), config.ValueFontSize, ink)
            : Words.Written(number, null, config.ValueFontSize, ink);
    }

    /// <summary>
    /// What an item is drawn with: the colour it is handed — filled part way, and outlined in it — or what its class writes, and
    /// the theme's text for its words, where its class writes none.
    /// </summary>
    private Paint Painted(Item item, TreemapConfig config)
    {
        var style = item.Said.Style;
        var shade = Ink.Series(item.Shade);
        var written = Ink.Written(style.Fill);
        var section = item.Under.Any(under => under.Drawn);

        return new Paint(
            written ?? Ink.Faded(shade, section ? SectionFill : LeafFill),
            written ?? Ink.Faded(shade, BandFill),
            Ink.Written(style.Stroke) ?? shade,
            Ink.Written(style.Colour) ?? Style.Text,
            style.StrokeWidth ?? config.BorderWidth);
    }

    /// <summary>Where <paramref name="words"/> set at <paramref name="at"/> stand.</summary>
    private static LayoutRect Covered(DiagramWords words, LayoutPoint at) => new(at, new LayoutSize(words.Width, words.Height));

    /// <summary><paramref name="shape"/> less <paramref name="over"/>: what a press on a tile means, where its words and the tiles in it are not.</summary>
    private LayoutShape Less(LayoutShape shape, IReadOnlyList<LayoutRect> over) =>
        over.Count == 0 ? shape : Medium.Combine(shape, Medium.Group([.. over.Select(rect => Medium.Rectangle(rect))], evenOdd: false), ShapeCombine.Exclude);
}
