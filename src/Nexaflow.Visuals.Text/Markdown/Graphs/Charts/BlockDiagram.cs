namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>The directions a block arrow points; <c>x</c> is <see cref="Left"/>|<see cref="Right"/>, <c>y</c> is <see cref="Up"/>|<see cref="Down"/>.</summary>
[Flags]
public enum BlockArrowDirections { None = 0, Right = 1, Left = 2, Up = 4, Down = 8 }

/// <summary>Explicit styling from a <c>style</c> line or an applied <c>classDef</c>; null ⇒ the theme decides.</summary>
public sealed class BlockStyle
{
    public string? Fill        { get; set; }
    public string? Stroke      { get; set; }
    public string? TextColor   { get; set; }
    public double? StrokeWidth { get; set; }
    public bool    Dashed      { get; set; }

    /// <summary>Layers <paramref name="over"/> on top of this style (set fields win).</summary>
    public BlockStyle Merge(BlockStyle over) => new()
    {
        Fill        = over.Fill        ?? Fill,
        Stroke      = over.Stroke      ?? Stroke,
        TextColor   = over.TextColor   ?? TextColor,
        StrokeWidth = over.StrokeWidth ?? StrokeWidth,
        Dashed      = over.Dashed || Dashed,
    };
}

/// <summary>Anything that occupies cells in a block grid: a node, a space, a block arrow or a nested group.</summary>
public abstract class BlockItem
{
    public string Id { get; init; } = string.Empty;
    /// <summary>Columns spanned (<c>id:3</c>); never below 1.</summary>
    public int Width { get; set; } = 1;
    public BlockStyle? Style { get; set; }
}

/// <summary>A labelled, shaped block — <c>id["label"]</c>, <c>id(("label"))</c>, …</summary>
public sealed class BlockNode : BlockItem
{
    public string Label { get; set; } = string.Empty;
    public NodeShape Shape { get; set; } = NodeShape.Rectangle;
}

/// <summary>An empty cell (<c>space</c> / <c>space:N</c>).</summary>
public sealed class BlockSpace : BlockItem { }

/// <summary>A block arrow — <c>id&lt;["label"]&gt;(right)</c> — a fat arrow glyph filling its cell.</summary>
public sealed class BlockArrow : BlockItem
{
    public string Label { get; set; } = string.Empty;
    public BlockArrowDirections Directions { get; set; } = BlockArrowDirections.Right;
}

/// <summary>
/// A composite block (<c>block:id:N … end</c>, or an anonymous <c>block … end</c>) with its own column
/// count and children.  The diagram root is a group too, with an empty <see cref="BlockItem.Id"/>.
/// </summary>
public sealed class BlockGroup : BlockItem
{
    /// <summary>Column count from <c>columns N</c>; null (auto) lays every child on one row.</summary>
    public int? Columns { get; set; }
    public List<BlockItem> Items { get; } = [];

    public IEnumerable<BlockItem> Descendants()
    {
        foreach (var item in Items)
        {
            yield return item;
            if (item is BlockGroup g)
                foreach (var d in g.Descendants()) yield return d;
        }
    }
}

/// <summary>A link between two items by id — <c>A --&gt; B</c>, <c>A --- B</c>, <c>A -- "label" --&gt; B</c>.</summary>
public sealed class BlockEdge
{
    public required string From { get; init; }
    public required string To   { get; init; }
    public string Label { get; set; } = string.Empty;
    public bool HasArrow { get; set; } = true;
}

/// <summary>
/// Data model for a Mermaid <c>block-beta</c> diagram: a grid of items laid out by columns, nested
/// groups with their own grids, block arrows, spaces, and edges drawn between any two items by id.
/// Independent of the graph/Sugiyama pipeline — placement is the author's, not a layout engine's.
/// </summary>
public sealed class BlockDiagram
{
    public string Title { get; set; } = string.Empty;
    public BlockGroup Root { get; } = new() { Id = string.Empty };
    public List<BlockEdge> Edges { get; } = [];
    public BlockConfig Config { get; set; } = new();

    public IEnumerable<BlockItem> AllItems => Root.Descendants();
    public IEnumerable<BlockNode> Nodes => AllItems.OfType<BlockNode>();
    public int ItemCount => Root.Descendants().Count();

    /// <summary>Finds an item anywhere in the tree by id (ordinal); null when unknown or the id is empty.</summary>
    public BlockItem? Find(string id) =>
        id.Length == 0 ? null : AllItems.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
}
