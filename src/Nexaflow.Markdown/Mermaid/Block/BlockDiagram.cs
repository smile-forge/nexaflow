using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block;

/// <summary>What a block in the grid is: a block of its own, cells left empty, an arrow, or a grid holding other blocks.</summary>
public enum BlockKind
{
    Block,
    Space,
    Arrow,
    Composite,
}

/// <summary>Where a block arrow points: <c>x</c> is left and right together, <c>y</c> up and down.</summary>
[Flags]
public enum BlockTowards
{
    None = 0,
    Right = 1,
    Left = 2,
    Up = 4,
    Down = 8,
}

/// <summary>
/// One block, read: where it was written, what it is called, what is written on it, the shape its brackets say, how many
/// columns it takes, and — where it is a composite — the grid of blocks inside it.
/// </summary>
/// <param name="Part">The whole block as it was written, which is what a press on it means.</param>
/// <param name="Id">What it is called, which is what a link, a <c>class</c> and a <c>style</c> name it by.</param>
/// <param name="Said">The words drawn on it: what is written on it, or what it is called where nothing is.</param>
/// <param name="Shape">The shape its brackets say, or <see cref="MermaidShape.None"/> for the plain box of a block with none.</param>
/// <param name="Span">How many columns of its grid it takes.</param>
/// <param name="Towards">Where it points, for a block arrow.</param>
/// <param name="Columns">How many columns a composite's own grid is laid out in — null for as many as it holds.</param>
/// <param name="Items">The blocks inside a composite, in the order they are written.</param>
/// <param name="Style">What the <c>classDef</c>, <c>class</c> and <c>style</c> lines ask for it.</param>
/// <param name="Order">Where a composite comes among the composites, which is the colour it opens in.</param>
public sealed record BlockItem(
    ContentPart Part,
    string Id,
    ContentPart? Said,
    MermaidShape Shape,
    BlockKind Kind,
    int Span,
    BlockTowards Towards,
    int? Columns,
    IReadOnlyList<BlockItem> Items,
    MermaidStyle Style,
    int Order)
{
    /// <summary>The hole standing where what is written on it goes, where holes were asked for and nothing is written there yet.</summary>
    public ContentPart? SaidHole { get; init; }

    /// <summary>Whether it is a cell left empty, which is drawn as nothing.</summary>
    public bool Empty => Kind == BlockKind.Space;
}

/// <summary>
/// One link, read: the blocks it joins, what is written on it, and what it draws — the head at each end, how thick it is, and
/// whether it is dotted.
/// </summary>
/// <param name="Part">The link as it was written, which is what a press on it means.</param>
public sealed record BlockLink(
    ContentPart Part,
    string From,
    string To,
    ContentPart? Said,
    MermaidHead Start,
    MermaidHead End,
    MermaidLineStyle Style)
{
    /// <summary>The hole standing where its label goes, where holes were asked for and nothing is written there yet.</summary>
    public ContentPart? SaidHole { get; init; }
}

/// <summary>
/// A <c>block-beta</c> block, read: the grid of blocks its author laid out, the composites nested in it, and the links
/// between them. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// A block written twice is one block: the second writing of an id says more about the block the first one made — the shape
/// and the words it is drawn with — rather than making another, which is what lets a link name the blocks laid out above it
/// without laying them out again. Mermaid gathers them the same way.
/// </para>
/// </summary>
public sealed class BlockDiagram
{
    private BlockDiagram(MermaidBlock block, BlockConfig config, int? columns, IReadOnlyList<BlockItem> items,
                         IReadOnlyList<BlockLink> links)
    {
        Block = block;
        Config = config;
        Columns = columns;
        Items = items;
        Links = links;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static BlockDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public BlockConfig Config { get; }

    /// <summary>How many columns the outermost grid is laid out in — null for as many as it holds, on one row.</summary>
    public int? Columns { get; }

    /// <summary>The blocks of the outermost grid, in the order they are written.</summary>
    public IReadOnlyList<BlockItem> Items { get; }

    /// <summary>The links, in the order they are written.</summary>
    public IReadOnlyList<BlockLink> Links { get; }

    /// <summary>Every block, the ones nested in composites included, each before what is inside it.</summary>
    public IEnumerable<BlockItem> All => Nested(Items);

    /// <summary>The block an id names, or null where nothing is called that.</summary>
    public BlockItem? Find(string id) =>
        id.Length == 0 ? null : All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the block diagram's own stages.</summary>
    public static BlockDiagram Of(MermaidBlock block)
    {
        var root = new Made(null, string.Empty, BlockKind.Composite, 0);
        var grids = new Dictionary<string, Made>(StringComparer.Ordinal) { [MermaidNesting.Outermost] = root };
        var known = new Dictionary<string, Made>(StringComparer.Ordinal);
        var links = new List<BlockLink>();

        var classes = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);
        var taken = new List<(IReadOnlyList<string> Ids, string Class)>();
        var written = new List<(IReadOnlyList<string> Ids, MermaidStyle Style)>();
        var composites = 0;

        foreach (var line in block.Reading.Root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;
            if (!grids.TryGetValue(stated.Fact(BlockRoles.Inside) ?? MermaidNesting.Outermost, out var grid)) continue;

            switch (stated.Kind)
            {
                case BlockKinds.Columns:
                    grid.Columns = Counted(stated);
                    break;

                case BlockKinds.Opens:
                    var opened = Read(stated.Inner(BlockKinds.Item), BlockKind.Composite, composites++);
                    Gathered(grid, known, opened);
                    if (stated.Fact(BlockRoles.Opened) is { } key) grids[key] = opened;
                    break;

                case BlockKinds.Items:
                    Laid(stated, grid, known, links);
                    break;

                case BlockKinds.ClassDef:
                    var declared = MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties));
                    foreach (var name in BlockGrammar.Styling.Classes(stated)) classes[name] = declared;
                    break;

                case BlockKinds.Class:
                    foreach (var given in BlockGrammar.Styling.Givens(stated))
                        taken.Add((BlockGrammar.Styling.Ids(stated), given));
                    break;

                case BlockKinds.Style:
                    written.Add((BlockGrammar.Styling.Ids(stated), MermaidStyle.None.With(stated.Inner(MermaidKinds.Properties))));
                    break;
            }
        }

        var styles = MermaidStyling.Styles(known.Keys, classes, taken, written);

        return new BlockDiagram(block, BlockConfig.Read(block.Config), root.Columns,
                               [.. root.Items.Select(item => Frozen(item, styles))], links);
    }

    // ── Reading the lines ───────────────────────────────────────────────────

    /// <summary>The blocks a line lays out, and the links between them: each link joins the blocks written either side of it.</summary>
    private static void Laid(ContentPart stated, Made grid, Dictionary<string, Made> known, List<BlockLink> links)
    {
        var pieces = stated.Children
            .Where(part => part.Kind is BlockKinds.Item or BlockKinds.Arrow or BlockKinds.Space or BlockKinds.Link)
            .ToList();

        foreach (var part in pieces)
            if (Kinded(part.Kind) is { } kind)
                Gathered(grid, known, Read(part, kind, order: 0));

        for (var at = 0; at < pieces.Count; at++)
        {
            if (pieces[at].Kind != BlockKinds.Link) continue;
            if (Joined(pieces, at, back: true) is not { } from || Joined(pieces, at, back: false) is not { } to) continue;

            links.Add(Linked(pieces[at], from, to));
        }
    }

    /// <summary>The block nearest a link on one side of it, or null where nothing is written there for it to join.</summary>
    private static string? Joined(IReadOnlyList<ContentPart> pieces, int at, bool back)
    {
        for (var next = back ? at - 1 : at + 1; next >= 0 && next < pieces.Count; next += back ? -1 : 1)
            if (pieces[next].Kind is BlockKinds.Item or BlockKinds.Arrow)
                return pieces[next].Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text;

        return null;
    }

    /// <summary>One block as it was written: what it is called, what is written on it, its shape, its width and where it points.</summary>
    private static Made Read(ContentPart? part, BlockKind kind, int order)
    {
        if (part is null) return new Made(null, string.Empty, kind, order);

        var name = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
        var label = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
        var said = label ?? name;

        return new Made(part, name.Words()?.Text ?? string.Empty, kind, order)
        {
            Shape = MermaidShapes.Of(part),
            Span = part.Inner(MermaidKinds.Amount).Number() is { } span and >= 1 ? (int)span : 1,
            Towards = kind == BlockKind.Arrow ? Towards(part) : BlockTowards.None,

            // A block says what is written on it, or what it is called where nothing is; a composite and an empty cell say
            // only what is written on them, which is how Mermaid leaves an unlabelled one blank.
            Labelled = label.Words() is not null,
            Said = label.Words() ?? (kind is BlockKind.Block or BlockKind.Arrow ? name.Words() : null),
            SaidHole = said?.Hole(),
        };
    }

    /// <summary>
    /// Takes a block into the grid it is written in — unless its id is already written, in which case this is that same block
    /// said again: what it says now is kept, and no second cell is made for it.
    /// </summary>
    private static void Gathered(Made grid, Dictionary<string, Made> known, Made item)
    {
        if (item.Id.Length > 0 && known.TryGetValue(item.Id, out var already))
        {
            // What it says now is what it says, where this writing of it wrote anything: a block whose id was all it had to say is
            // labelled by a later writing, as Mermaid labels it.
            if (item.Labelled)
            {
                already.Said = item.Said;
                already.SaidHole = item.SaidHole;
                already.Labelled = true;
            }

            if (item.Shape != MermaidShape.None) already.Shape = item.Shape;
            return;
        }

        if (item.Id.Length > 0) known[item.Id] = item;
        grid.Items.Add(item);
    }

    /// <summary>How many columns a grid is laid out in: the number written, or null where <c>auto</c> is written or nothing is.</summary>
    private static int? Counted(ContentPart stated)
    {
        if (stated.Children.Any(child => child.Kind == MermaidKinds.Key && child.Role == BlockRoles.Count)) return null;

        return stated.Inner(MermaidKinds.Amount).Number() is { } count and >= 1 ? (int)count : null;
    }

    /// <summary>Where a block arrow points, which is every direction written after it — <c>right</c> by default.</summary>
    private static BlockTowards Towards(ContentPart part)
    {
        var towards = BlockTowards.None;

        foreach (var said in part.Inner(MermaidKinds.Names).Named())
            towards |= (said.Words()?.Text ?? string.Empty).ToLowerInvariant() switch
            {
                "right" => BlockTowards.Right,
                "left" => BlockTowards.Left,
                "up" => BlockTowards.Up,
                "down" => BlockTowards.Down,
                "x" => BlockTowards.Left | BlockTowards.Right,
                "y" => BlockTowards.Up | BlockTowards.Down,
                _ => BlockTowards.None,
            };

        return towards == BlockTowards.None ? BlockTowards.Right : towards;
    }

    private static BlockKind? Kinded(string kind) => kind switch
    {
        BlockKinds.Item => BlockKind.Block,
        BlockKinds.Arrow => BlockKind.Arrow,
        BlockKinds.Space => BlockKind.Space,
        _ => null,
    };

    // ── The links ───────────────────────────────────────────────────────────

    private static BlockLink Linked(ContentPart part, string from, string to)
    {
        var said = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Quoted);
        var drawn = MermaidLinks.Of(part.Children.FirstOrDefault(child => child.Role == BlockRoles.Arrow)?.Text);

        return new BlockLink(part, from, to, said.Words(), drawn.Start, drawn.End, drawn.Style)
        {
            SaidHole = said?.Hole(),
        };
    }

    // ── The styling ─────────────────────────────────────────────────────────

    // ── What it comes to ────────────────────────────────────────────────────

    private static BlockItem Frozen(Made made, IReadOnlyDictionary<string, MermaidStyle> styles) =>
        new(made.Part!, made.Id, made.Said, made.Shape, made.Kind, made.Span, made.Towards, made.Columns,
            [.. made.Items.Select(item => Frozen(item, styles))],
            styles.GetValueOrDefault(made.Id, MermaidStyle.None), made.Order)
        {
            SaidHole = made.SaidHole,
        };

    private static IEnumerable<BlockItem> Nested(IReadOnlyList<BlockItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var inner in Nested(item.Items)) yield return inner;
        }
    }

    /// <summary>A block while the lines are still being read, before the grid it is in is closed.</summary>
    private sealed class Made(ContentPart? part, string id, BlockKind kind, int order)
    {
        public ContentPart? Part { get; } = part;

        public string Id { get; } = id;

        public BlockKind Kind { get; } = kind;

        public int Order { get; } = order;

        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        /// <summary>Whether a label was written on it, rather than its id standing in for one.</summary>
        public bool Labelled { get; set; }

        public MermaidShape Shape { get; set; }

        public int Span { get; init; } = 1;

        public BlockTowards Towards { get; init; }

        public int? Columns { get; set; }

        public List<Made> Items { get; } = [];
    }
}
