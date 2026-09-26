using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Block;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Block;

/// <summary>The pieces a block diagram's layout is made of — its layers, and what is in them.</summary>
public static class BlockPiece
{
    /// <summary>The grid, which is the diagram itself.</summary>
    public const string Blocks = "Blocks";

    /// <summary>One block, standing for what was written for it.</summary>
    public const string Block = "Block";

    /// <summary>A composite: a block holding the grid its own blocks are laid out in, which are the pieces inside it.</summary>
    public const string Composite = "Composite";

    /// <summary>A composite's own box and what is written on it, behind the blocks it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>A block arrow, standing for what was written for it.</summary>
    public const string Arrow = "Arrow";

    /// <summary>The links, drawn over the grid.</summary>
    public const string Links = "Links";

    /// <inheritdoc cref="Links"/>
    public const string Link = "Link";

    /// <summary>What is written on a link, over the middle of it.</summary>
    public const string Label = "Label";
}

/// <summary>
/// Draws a <c>block-beta</c> block where its author put it. Every cell of a grid is the size of the largest block in it, a
/// block spanning columns being that much wider; a composite is a cell holding a grid of its own, and the blocks in it grow to
/// fill whatever room the cell it is drawn in turned out to have. Nothing is moved to suit the drawing, which is the whole
/// point of the type.
///
/// <para>
/// <strong>The layout is the grid, nested.</strong> A composite's blocks are drawn inside its piece rather than beside it, so
/// pressing a block means that block and pressing the room round it means the composite holding it — and a composite stands
/// only where its own blocks do not cover it. The links are a layer of their own over all of it, because a link joins two
/// blocks that may sit anywhere, in any composite.
/// </para>
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A block stands for the block written for it, and what is
/// drawn on it is the characters written — its label, or its id where nothing else says anything.
/// </para>
/// </summary>
internal sealed class BlockBuilder : MermaidBuilder
{
    /// <summary>How big what is written on a block is, and how wide it runs before it wraps.</summary>
    private const double TextSize = 13;
    private const double LabelSize = 11.5;
    private const double Widest = 200;

    /// <summary>The least room a cell takes, so a block with little to say is still a block to look at.</summary>
    private const double Least = 44;
    private const double Short = 32;

    /// <summary>How thick a link written with equals signs is drawn.</summary>
    private const double Thick = 2.5;

    /// <summary>How far a block arrow's head reaches in from its edge, how thick its shaft is, and how wide its head spreads.</summary>
    private const double Reach = 0.34;
    private const double Shaft = 0.16;
    private const double Wing = 0.3;

    internal BlockBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>What a block in the grid is: a block of its own, cells left empty, an arrow, or a grid holding other blocks.</summary>
    private enum Kind
    {
        Block,
        Space,
        Arrow,
        Composite,
    }

    /// <summary>Where a block arrow points: <c>x</c> is left and right together, <c>y</c> up and down.</summary>
    [Flags]
    private enum Towards
    {
        None = 0,
        Right = 1,
        Left = 2,
        Up = 4,
        Down = 8,
    }

    /// <summary>
    /// One block: where it was first written, what it is called, what is written on it, the shape its brackets say, how many
    /// columns it takes, and — where it is a composite — the grid of blocks inside it.
    /// </summary>
    /// <param name="part">The whole block as it was first written, which is what a press on it means.</param>
    /// <param name="id">What it is called, which is what a link, a <c>class</c> and a <c>style</c> name it by.</param>
    private sealed class Item(ContentPart? part, string id, Kind kind)
    {
        public ContentPart? Part { get; } = part;

        public string Id { get; } = id;

        public Kind Kind { get; } = kind;

        /// <summary>What the styling lines ask for it, said on the name first writing it.</summary>
        public MermaidStyle Style { get; } = StyleOf(part?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name));

        /// <summary>The words drawn on it: what is written on it, or what it is called where nothing is.</summary>
        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        /// <summary>Whether a label was written on it, rather than its id standing in for one.</summary>
        public bool Labelled { get; set; }

        /// <summary>The shape its brackets say, or <see cref="MermaidShape.None"/> for the plain box of a block with none.</summary>
        public MermaidShape Shape { get; set; }

        /// <summary>How many columns of its grid it takes.</summary>
        public int Span { get; init; } = 1;

        public Towards Towards { get; init; }

        /// <summary>How many columns a composite's own grid is laid out in — null for as many as it holds.</summary>
        public int? Columns { get; set; }

        /// <summary>The blocks inside a composite, in the order they are written.</summary>
        public List<Item> Items { get; } = [];
    }

    /// <summary>One link: the blocks it joins, what is written on it, and what it draws.</summary>
    /// <param name="Part">The link as it was written, which is what a press on it means.</param>
    private sealed record Link(ContentPart Part, string From, string To, ContentPart? Said, MermaidHead Start, MermaidHead End,
                               MermaidLineStyle Style)
    {
        public ContentPart? SaidHole { get; init; }
    }

    /// <summary>
    /// The grid the block's author laid out, read down the tree in the order it is written: the composites nested in it, and the
    /// links between the blocks. A block written twice is one block: the second writing of an id says more about the block the
    /// first one made — the shape and the words it is drawn with — rather than making another, which is what lets a link name the
    /// blocks laid out above it without laying them out again. Mermaid gathers them the same way.
    /// </summary>
    private sealed class Diagram
    {
        private readonly Item root = new(null, string.Empty, Kind.Composite);

        private readonly Dictionary<string, Item> known = new(StringComparer.Ordinal);

        private Diagram(BlockConfig config) => Config = config;

        public BlockConfig Config { get; }

        /// <summary>How many columns the outermost grid is laid out in — null for as many as it holds, on one row.</summary>
        public int? Columns => root.Columns;

        /// <summary>The blocks of the outermost grid, in the order they are written.</summary>
        public IReadOnlyList<Item> Items => root.Items;

        public List<Link> Links { get; } = [];

        public static Diagram Of(ContentPart root, BlockConfig config)
        {
            var diagram = new Diagram(config);
            diagram.Read(root, diagram.root);
            return diagram;
        }

        /// <summary>Everything written in one part of the block — the whole of it, or one composite — into the grid given.</summary>
        private void Read(ContentPart holder, Item grid)
        {
            foreach (var part in holder.Children)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    if (part.Children.FirstOrDefault()?.Stated() is { Kind: BlockKinds.Opens } opens)
                    {
                        var opened = ItemOf(opens.Inner(BlockKinds.Item), Kind.Composite);
                        Gathered(grid, opened);
                        Read(part, opened);
                    }

                    continue;
                }

                switch (part.Stated())
                {
                    case { Kind: BlockKinds.Columns } stated:
                        grid.Columns = Counted(stated);
                        break;

                    case { Kind: BlockKinds.Items } stated:
                        Laid(stated, grid);
                        break;
                }
            }
        }

        /// <summary>The blocks a line lays out, and the links between them: each link joins the blocks written either side of it.</summary>
        private void Laid(ContentPart stated, Item grid)
        {
            var pieces = stated.Children
                .Where(part => part.Kind is BlockKinds.Item or BlockKinds.Arrow or BlockKinds.Space or BlockKinds.Link)
                .ToList();

            foreach (var part in pieces)
                if (Kinded(part.Kind) is { } kind)
                    Gathered(grid, ItemOf(part, kind));

            for (var at = 0; at < pieces.Count; at++)
            {
                if (pieces[at].Kind != BlockKinds.Link) continue;
                if (Joined(pieces, at, back: true) is not { } from || Joined(pieces, at, back: false) is not { } to) continue;

                Links.Add(Linked(pieces[at], from, to));
            }
        }

        /// <summary>
        /// Takes a block into the grid it is written in — unless its id is already written, in which case this is that same block
        /// said again: what it says now is kept, and no second cell is made for it.
        /// </summary>
        private void Gathered(Item grid, Item item)
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

        /// <summary>The block nearest a link on one side of it, or null where nothing is written there for it to join.</summary>
        private static string? Joined(IReadOnlyList<ContentPart> pieces, int at, bool back)
        {
            for (var next = back ? at - 1 : at + 1; next >= 0 && next < pieces.Count; next += back ? -1 : 1)
                if (pieces[next].Kind is BlockKinds.Item or BlockKinds.Arrow)
                    return pieces[next].Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text;

            return null;
        }

        /// <summary>One block as it was written: what it is called, what is written on it, its shape, its width and where it points.</summary>
        private static Item ItemOf(ContentPart? part, Kind kind)
        {
            if (part is null) return new Item(null, string.Empty, kind);

            var name = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);
            var label = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
            var said = label ?? name;

            return new Item(part, name.Words()?.Text ?? string.Empty, kind)
            {
                Shape = MermaidShapes.Of(part),
                Span = part.Inner(MermaidKinds.Amount).Number() is { } span and >= 1 ? (int)span : 1,
                Towards = kind == Kind.Arrow ? Pointed(part) : Towards.None,

                // A block says what is written on it, or what it is called where nothing is; a composite and an empty cell say
                // only what is written on them, which is how Mermaid leaves an unlabelled one blank.
                Labelled = label.Words() is not null,
                Said = label.Words() ?? (kind is Kind.Block or Kind.Arrow ? name.Words() : null),
                SaidHole = said?.Hole(),
            };
        }

        /// <summary>How many columns a grid is laid out in: the number written, or null where <c>auto</c> is written or nothing is.</summary>
        private static int? Counted(ContentPart stated)
        {
            if (stated.Children.Any(child => child.Kind == MermaidKinds.Key && child.Role == BlockRoles.Count)) return null;

            return stated.Inner(MermaidKinds.Amount).Number() is { } count and >= 1 ? (int)count : null;
        }

        /// <summary>Where a block arrow points, which is every direction written after it — <c>right</c> by default.</summary>
        private static Towards Pointed(ContentPart part)
        {
            var towards = Towards.None;

            foreach (var said in part.Inner(MermaidKinds.Names).Named())
                towards |= (said.Words()?.Text ?? string.Empty).ToLowerInvariant() switch
                {
                    "right" => Towards.Right,
                    "left" => Towards.Left,
                    "up" => Towards.Up,
                    "down" => Towards.Down,
                    "x" => Towards.Left | Towards.Right,
                    "y" => Towards.Up | Towards.Down,
                    _ => Towards.None,
                };

            return towards == Towards.None ? Towards.Right : towards;
        }

        private static Kind? Kinded(string kind) => kind switch
        {
            BlockKinds.Item => Kind.Block,
            BlockKinds.Arrow => Kind.Arrow,
            BlockKinds.Space => Kind.Space,
            _ => null,
        };

        private static Link Linked(ContentPart part, string from, string to)
        {
            var said = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Quoted);
            var drawn = MermaidLinks.Of(part.Children.FirstOrDefault(child => child.Role == BlockRoles.Arrow)?.Text);

            return new Link(part, from, to, said.Words(), drawn.Start, drawn.End, drawn.Style)
            {
                SaidHole = said?.Hole(),
            };
        }
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = Diagram.Of(Reading.Root, Configured(BlockConfig.Default));
        // A block diagram with nothing laid out in it is the source: what the reader wants back is their own lines.
        if (diagram.Items.Count == 0) return AsWritten(build);

        var pad = diagram.Config.Padding;
        var sized = diagram.Items.Select(item => Sizing(item, pad)).ToList();
        var size = Grid(sized, diagram.Columns, pad);

        Place(sized, diagram.Columns, new Rect(default, size), pad);

        // The links are worked out before anything is drawn, because a composite under one of them does not stand where it runs.
        var routes = Routes(diagram, Placed(sized));
        var over = DiagramConnector.Covered(routes.Select(route => (route.Along, route.Room)), Thick);

        build.Open(BlockPiece.Blocks, part: null, stops: Stops.None);
        foreach (var item in sized) Drawn(build, item, pad, over);
        build.Close();

        Links(build, routes);

        return size;
    }

    // ── How much room it all takes ──────────────────────────────────────────

    /// <summary>What a block needs: the room its words take inside its shape, or the grid of a composite's own blocks.</summary>
    private Sized Sizing(Item item, double pad)
    {
        var said = item.Said is null && item.SaidHole is null
            ? []
            : Wrapped(item.Said, item.SaidHole, TextSize, Ink.Written(item.Style.Colour) ?? Palette.Text, Widest);

        var sized = new Sized(item, said) { Items = [.. item.Items.Select(child => Sizing(child, pad))] };

        sized.Natural = item.Kind switch
        {
            Kind.Space => default,
            Kind.Composite when sized.Items.Count > 0 => Holding(sized, pad),
            _ => DiagramShapes.Around(Shaped(item), DiagramWords.Taken(said), pad),
        };

        return sized;
    }

    /// <summary>What a composite needs: the grid inside it, and room over that for whatever is written on it.</summary>
    private static Size Holding(Sized sized, double pad)
    {
        var grid = Grid(sized.Items, sized.Item.Columns, pad);
        var said = DiagramWords.Taken(sized.Words);

        return new Size(Math.Max(grid.Width, said.Width + (pad * 2)), grid.Height + Heading(said, pad));
    }

    /// <summary>How much room what is written on a composite takes at the top of it, above the grid inside it.</summary>
    private static double Heading(Size said, double pad) => said.Height > 0 ? said.Height + (pad / 2) : 0;

    /// <summary>The room a grid of blocks takes: a cell for each column, with clear air round every one of them.</summary>
    private static Size Grid(IReadOnlyList<Sized> items, int? columns, double pad)
    {
        var (_, wide, rows) = Spread(items, columns);
        if (rows == 0) return default;

        var cell = Cell(items, pad);
        return new Size((wide * (cell.Width + pad)) + pad, (rows * (cell.Height + pad)) + pad);
    }

    /// <summary>
    /// The size every cell of a grid takes, which is what the largest block in it needs — a block spanning columns counting
    /// only for its share of one, so one wide block does not widen every column.
    /// </summary>
    private static Size Cell(IReadOnlyList<Sized> items, double pad)
    {
        var wide = 0.0;
        var tall = 0.0;

        foreach (var item in items.Where(item => item.Item.Kind != Kind.Space))
        {
            wide = Math.Max(wide, (item.Natural.Width - (pad * (item.Item.Span - 1))) / item.Item.Span);
            tall = Math.Max(tall, item.Natural.Height);
        }

        return new Size(Math.Max(wide, Least), Math.Max(tall, Short));
    }

    /// <summary>
    /// Where each block sits in its grid, and how many columns and rows that grid comes to. A grid given no column count is one
    /// row of everything in it; one given a count wraps to the next row as the columns fill, a block spanning as many of them as
    /// are left on its row.
    /// </summary>
    private static (IReadOnlyList<(int Column, int Row, int Span)> Cells, int Columns, int Rows) Spread(
        IReadOnlyList<Sized> items, int? columns)
    {
        var wide = columns is { } count and >= 1 ? count : Math.Max(1, items.Sum(item => item.Item.Span));
        var cells = new List<(int Column, int Row, int Span)>(items.Count);
        var at = 0;

        foreach (var item in items)
        {
            var (column, row) = (at % wide, at / wide);
            var span = Math.Max(1, Math.Min(item.Item.Span, wide - column));

            cells.Add((column, row, span));
            at += span;
        }

        return (cells, wide, cells.Count == 0 ? 0 : cells.Max(cell => cell.Row) + 1);
    }

    /// <summary>
    /// Where every block goes in the room its grid has: the cells share it out evenly, so a grid drawn bigger than it asked for
    /// — because something beside it was wider — grows its blocks to fill it rather than leaving them in a corner.
    /// </summary>
    private static void Place(IReadOnlyList<Sized> items, int? columns, Rect room, double pad)
    {
        var (cells, wide, rows) = Spread(items, columns);
        if (rows == 0) return;

        var across = Math.Max(0, (room.Width - (pad * (wide + 1))) / wide);
        var down = Math.Max(0, (room.Height - (pad * (rows + 1))) / rows);

        for (var at = 0; at < items.Count; at++)
        {
            var (column, row, span) = cells[at];

            items[at].Bounds = new Rect(room.X + pad + (column * (across + pad)),
                                        room.Y + pad + (row * (down + pad)),
                                        (across * span) + (pad * (span - 1)),
                                        down);

            if (items[at].Item.Kind == Kind.Composite) Place(items[at].Items, items[at].Item.Columns, Inside(items[at], pad), pad);
        }
    }

    /// <summary>The room a composite leaves for the grid inside it: all of it, less what is written at the top.</summary>
    private static Rect Inside(Sized sized, double pad)
    {
        var top = Heading(DiagramWords.Taken(sized.Words), pad);

        return new Rect(sized.Bounds.X, sized.Bounds.Y + top, sized.Bounds.Width, Math.Max(0, sized.Bounds.Height - top));
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    private void Drawn(LayoutBuilder build, Sized sized, double pad, IReadOnlyList<Geometry> over)
    {
        var item = sized.Item;
        if (item.Kind == Kind.Space) return;

        switch (item.Kind)
        {
            case Kind.Composite:
                Holds(build, sized, pad, over);
                break;

            case Kind.Arrow:
                Pointing(build, sized);
                break;

            default:
                DiagramShapes.Draw(build, BlockPiece.Block, item.Part, Shaped(item), Standing(sized), Fill(item), Stroke(item),
                                   DiagramWords.Placed(sized.Words, DiagramShapes.Inside(Shaped(item), Standing(sized)), MermaidPiece.Words));
                break;
        }
    }

    /// <summary>A composite: its box, what is written at the top of it, and the blocks of its own grid drawn inside its piece.</summary>
    private void Holds(LayoutBuilder build, Sized sized, double pad, IReadOnlyList<Geometry> over)
    {
        var item = sized.Item;
        var said = DiagramWords.Taken(sized.Words);
        var heading = new Rect(sized.Bounds.X + pad, sized.Bounds.Y + (pad / 4), Math.Max(0, sized.Bounds.Width - (pad * 2)), said.Height);

        var covered = DiagramShapes.United(
        [
            .. over,
            .. sized.Items.Where(child => child.Item.Kind != Kind.Space)
                  .Select(child => DiagramShapes.Outline(Shaped(child.Item), Standing(child))),
        ]);

        build.Open(BlockPiece.Composite, item.Part, stops: Stops.None);
        DiagramShapes.Draw(build, BlockPiece.Holding, item.Part, Shaped(item), sized.Bounds, Fill(item), Stroke(item),
                           DiagramWords.Placed(sized.Words, heading, MermaidPiece.Words), covered,
                           band: Ink.Band(Ink.Written(item.Style.Stroke)));

        foreach (var child in sized.Items) Drawn(build, child, pad, over);
        build.Close();
    }

    /// <summary>
    /// A block arrow: a shaft across the room it has for every direction it points, a head at each of those edges, and what it
    /// says over the middle of it.
    /// </summary>
    private void Pointing(LayoutBuilder build, Sized sized)
    {
        var item = sized.Item;
        var arrow = Arrow(sized.Bounds, item.Towards);
        var words = DiagramWords.Placed(sized.Words, sized.Bounds, MermaidPiece.Words);
        var stroke = Stroke(item);

        build.Open(BlockPiece.Arrow, item.Part, stops: Stops.None);

        build.Open(MermaidPiece.Shape, item.Part, stops: Stops.None);
        build.Draw(new GeometryMark(arrow, Fill(item), stroke.Ink, stroke.Thickness) { Dashes = stroke.Dashes });
        build.Occupies(Uncovered(arrow, words));
        build.Close();

        foreach (var (said, at, kind) in words) said.Set(build, at, kind);
        build.Close();
    }

    /// <summary>The arrow filling the room it has: a head where it points, and a shaft reaching back from each head.</summary>
    private static Geometry Arrow(Rect bounds, Towards towards)
    {
        var least = Math.Min(bounds.Width, bounds.Height);
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var parts = new List<Geometry>();

        foreach (var (direction, tip, back) in new (Towards Direction, Point Tip, Point Back)[]
                 {
                     (Towards.Right, new Point(bounds.Right, middle.Y), new Point(bounds.Left, middle.Y)),
                     (Towards.Left, new Point(bounds.Left, middle.Y), new Point(bounds.Right, middle.Y)),
                     (Towards.Down, new Point(middle.X, bounds.Bottom), new Point(middle.X, bounds.Top)),
                     (Towards.Up, new Point(middle.X, bounds.Top), new Point(middle.X, bounds.Bottom)),
                 })
        {
            if (!towards.HasFlag(direction)) continue;

            var along = tip - middle;
            along.Normalize();
            var side = new Vector(along.Y, -along.X);

            // The shaft reaches back to the far edge unless the arrow points that way too, where the two shafts meet in the middle.
            var from = towards.HasFlag(Opposite(direction)) ? middle : back;
            var neck = tip - (along * (least * Reach));

            parts.Add(Polygon(neck + (side * (least * Wing)), tip, neck - (side * (least * Wing))));
            parts.Add(Polygon(from + (side * (least * Shaft)), neck + (side * (least * Shaft)),
                              neck - (side * (least * Shaft)), from - (side * (least * Shaft))));
        }

        if (parts.Count == 0) return DiagramShapes.Outline(DiagramShape.Rectangle, bounds);

        var shape = parts.Aggregate((Geometry)new RectangleGeometry(Rect.Empty),
                                    (all, part) => new CombinedGeometry(GeometryCombineMode.Union, all, part));
        shape.Freeze();
        return shape;
    }

    private static Towards Opposite(Towards direction) => direction switch
    {
        Towards.Right => Towards.Left,
        Towards.Left => Towards.Right,
        Towards.Down => Towards.Up,
        _ => Towards.Down,
    };

    private static Geometry Polygon(params Point[] points)
    {
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(points[0], isFilled: true, isClosed: true);
            pen.PolyLineTo([.. points.Skip(1)], isStroked: true, isSmoothJoin: false);
        }

        return shape;
    }

    /// <summary>Where a shape of a diagram's own stands: all of it, less where the words drawn over it are.</summary>
    private static Geometry Uncovered(Geometry shape, IReadOnlyList<(DiagramWords Words, Point At, string Kind)> words)
    {
        var over = Rect.Empty;
        foreach (var (said, at, _) in words) over.Union(new Rect(at, new Size(said.Width, said.Height)));

        return over.IsEmpty ? shape : DiagramShapes.Clear(shape, over);
    }

    // ── The links ───────────────────────────────────────────────────────────

    /// <summary>
    /// Where every link runs: from the edge of the block it leaves to the edge of the block it reaches, with what is written
    /// on it over the middle of the line.
    /// </summary>
    private List<Route> Routes(Diagram diagram, IReadOnlyDictionary<string, Sized> placed)
    {
        var routes = new List<Route>();

        foreach (var link in diagram.Links)
        {
            if (!placed.TryGetValue(link.From, out var from) || !placed.TryGetValue(link.To, out var to)) continue;

            var along = new[]
            {
                DiagramShapes.Edge(Shaped(from.Item), Standing(from), Middle(to.Bounds)),
                DiagramShapes.Edge(Shaped(to.Item), Standing(to), Middle(from.Bounds)),
            };

            var said = link.Said is null && link.SaidHole is null
                ? []
                : Wrapped(link.Said, link.SaidHole, LabelSize, Palette.Text, Widest);

            routes.Add(new Route(link, along, said, DiagramConnector.Room(along, said)));
        }

        return routes;
    }

    /// <summary>The links, drawn over the grid.</summary>
    private void Links(LayoutBuilder build, IReadOnlyList<Route> routes)
    {
        if (routes.Count == 0) return;

        build.Open(BlockPiece.Links, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            var stroke = DiagramConnector.Stroked(Ink.Link, route.Link.Style, thick: Thick);

            DiagramConnector.Draw(build, BlockPiece.Link, route.Link.Part, route.Along, stroke,
                                  DiagramConnector.Headed(route.Link.Start), DiagramConnector.Headed(route.Link.End));

            DiagramConnector.Says(build, BlockPiece.Label, route.Link.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    /// <summary>A link worked out: where it runs, what is written on it, and the room those words take over the middle of it.</summary>
    private sealed record Route(Link Link, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    private static Point Middle(Rect rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    /// <summary>Every block by the id it is called, the ones nested in composites included.</summary>
    private static Dictionary<string, Sized> Placed(IReadOnlyList<Sized> items)
    {
        var placed = new Dictionary<string, Sized>(StringComparer.Ordinal);
        Gather(items);

        return placed;

        void Gather(IReadOnlyList<Sized> said)
        {
            foreach (var item in said)
            {
                if (item.Item.Id.Length > 0) placed.TryAdd(item.Item.Id, item);
                Gather(item.Items);
            }
        }
    }

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>What a block is drawn as: the shape its brackets say, a rounded box for a composite that says none, a box otherwise.</summary>
    private static DiagramShape Shaped(Item item) =>
        item.Shape != MermaidShape.None ? DiagramShapes.For(item.Shape)
        : item.Kind == Kind.Composite ? DiagramShape.Rounded
        : DiagramShape.Rectangle;

    /// <summary>
    /// Where a block's shape stands in its cell: the whole cell, which grows to fill its column and its row — but a circle is round
    /// whatever room it is given, so it stands in a square in the middle of the cell rather than stretching to an ellipse.
    /// </summary>
    private static Rect Standing(Sized sized)
    {
        if (Shaped(sized.Item) is not (DiagramShape.Circle or DiagramShape.DoubleCircle)) return sized.Bounds;

        var side = Math.Min(sized.Bounds.Width, sized.Bounds.Height);
        return new Rect(sized.Bounds.X + ((sized.Bounds.Width - side) / 2), sized.Bounds.Y + ((sized.Bounds.Height - side) / 2), side, side);
    }

    /// <summary>
    /// What a block is filled with: what the styling writes for it, and otherwise what every one of its kind is — a block, a
    /// composite holding others, or an arrow pointing between them.
    /// </summary>
    private Brush Fill(Item item)
    {
        var fill = Ink.Written(item.Style.Fill) ?? item.Kind switch
        {
            Kind.Composite => Ink.Group,
            Kind.Arrow => Ink.Quiet,
            _ => Ink.Node,
        };

        return item.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What a block is outlined in: what the styling writes for it, and otherwise what every one of its kind is.</summary>
    private DiagramStroke Stroke(Item item) =>
        new(Ink.Written(item.Style.Stroke) ?? item.Kind switch
            {
                Kind.Composite => Ink.GroupEdge,
                Kind.Arrow => Ink.QuietEdge,
                _ => Ink.NodeEdge,
            },
            item.Style.StrokeWidth ?? 1, DiagramInk.Dashes(item.Style.Dashes));

    /// <summary>A block measured: what is written on it, the room it needs, and — once its grid is laid out — where it sits.</summary>
    private sealed class Sized(Item item, IReadOnlyList<DiagramWords> words)
    {
        public Item Item { get; } = item;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public IReadOnlyList<Sized> Items { get; init; } = [];

        public Size Natural { get; set; }

        public Rect Bounds { get; set; }
    }
}
