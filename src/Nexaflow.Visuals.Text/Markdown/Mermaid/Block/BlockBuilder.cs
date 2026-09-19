using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Block;
using Nexaflow.Visuals.Text.Editing;

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
internal sealed class BlockBuilder : MermaidBuilder<BlockDiagram>
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

    /// <summary>How solid a composite's background is, over the colour it takes from the series.</summary>
    private const double Wash = 0.18;

    /// <summary>How far a block arrow's head reaches in from its edge, how thick its shaft is, and how wide its head spreads.</summary>
    private const double Reach = 0.34;
    private const double Shaft = 0.16;
    private const double Wing = 0.3;

    private BlockBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    /// <param name="writing">Whether somebody is writing in it, which draws what is still to be written.</param>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new BlockBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <inheritdoc/>
    protected override BlockDiagram Of(MermaidBlock block) => BlockDiagram.Of(block);

    protected override Size Draw(BlockDiagram diagram, LayoutBuilder build)
    {
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
    private Sized Sizing(BlockItem item, double pad)
    {
        var said = item.Said is null && item.SaidHole is null
            ? []
            : Says(item.Said, item.SaidHole, TextSize, Ink.Written(item.Style.Colour) ?? Palette.Text, Widest);

        var sized = new Sized(item, said) { Items = [.. item.Items.Select(child => Sizing(child, pad))] };

        sized.Natural = item.Kind switch
        {
            BlockKind.Space => default,
            BlockKind.Composite when sized.Items.Count > 0 => Holding(sized, pad),
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

        foreach (var item in items.Where(item => item.Item.Kind != BlockKind.Space))
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

            if (items[at].Item.Kind == BlockKind.Composite) Place(items[at].Items, items[at].Item.Columns, Inside(items[at], pad), pad);
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
        if (item.Kind == BlockKind.Space) return;

        switch (item.Kind)
        {
            case BlockKind.Composite:
                Holds(build, sized, pad, over);
                break;

            case BlockKind.Arrow:
                Pointing(build, sized);
                break;

            default:
                DiagramShapes.Draw(build, BlockPiece.Block, item.Part, Shaped(item), sized.Bounds, Fill(item), Stroke(item),
                                   DiagramWords.Placed(sized.Words, DiagramShapes.Inside(Shaped(item), sized.Bounds), MermaidPiece.Words));
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
            .. sized.Items.Where(child => child.Item.Kind != BlockKind.Space)
                  .Select(child => DiagramShapes.Outline(Shaped(child.Item), child.Bounds)),
        ]);

        build.Open(BlockPiece.Composite, item.Part, stops: Stops.None);
        DiagramShapes.Draw(build, BlockPiece.Holding, item.Part, Shaped(item), sized.Bounds, Fill(item), Stroke(item),
                           DiagramWords.Placed(sized.Words, heading, MermaidPiece.Words), covered);

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
    private static Geometry Arrow(Rect bounds, BlockTowards towards)
    {
        var least = Math.Min(bounds.Width, bounds.Height);
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var parts = new List<Geometry>();

        foreach (var (direction, tip, back) in new (BlockTowards Direction, Point Tip, Point Back)[]
                 {
                     (BlockTowards.Right, new Point(bounds.Right, middle.Y), new Point(bounds.Left, middle.Y)),
                     (BlockTowards.Left, new Point(bounds.Left, middle.Y), new Point(bounds.Right, middle.Y)),
                     (BlockTowards.Down, new Point(middle.X, bounds.Bottom), new Point(middle.X, bounds.Top)),
                     (BlockTowards.Up, new Point(middle.X, bounds.Top), new Point(middle.X, bounds.Bottom)),
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

    private static BlockTowards Opposite(BlockTowards direction) => direction switch
    {
        BlockTowards.Right => BlockTowards.Left,
        BlockTowards.Left => BlockTowards.Right,
        BlockTowards.Down => BlockTowards.Up,
        _ => BlockTowards.Down,
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
    private List<Route> Routes(BlockDiagram diagram, IReadOnlyDictionary<string, Sized> placed)
    {
        var routes = new List<Route>();

        foreach (var link in diagram.Links)
        {
            if (!placed.TryGetValue(link.From, out var from) || !placed.TryGetValue(link.To, out var to)) continue;

            var along = new[]
            {
                DiagramShapes.Edge(Shaped(from.Item), from.Bounds, Middle(to.Bounds)),
                DiagramShapes.Edge(Shaped(to.Item), to.Bounds, Middle(from.Bounds)),
            };

            var said = link.Said is null && link.SaidHole is null
                ? []
                : Says(link.Said, link.SaidHole, LabelSize, Palette.Text, Widest);

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
            var stroke = DiagramConnector.Stroked(Palette.TextMuted, route.Link.Style, thick: Thick);

            DiagramConnector.Draw(build, BlockPiece.Link, route.Link.Part, route.Along, stroke,
                                  DiagramConnector.Headed(route.Link.Start), DiagramConnector.Headed(route.Link.End));

            DiagramConnector.Says(build, BlockPiece.Label, route.Link.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    /// <summary>A link worked out: where it runs, what is written on it, and the room those words take over the middle of it.</summary>
    private sealed record Route(BlockLink Link, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

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
    private static DiagramShape Shaped(BlockItem item) =>
        item.Shape != MermaidShape.None ? DiagramShapes.For(item.Shape)
        : item.Kind == BlockKind.Composite ? DiagramShape.Rounded
        : DiagramShape.Rectangle;

    /// <summary>
    /// What a block is filled with: what the styling writes for it, and otherwise the card's own colour — or, for a composite,
    /// a wash of the colour its place among the composites gives it, so one opening inside another is told apart from it.
    /// </summary>
    private Brush Fill(BlockItem item)
    {
        var fill = Ink.Written(item.Style.Fill)
                   ?? (item.Kind == BlockKind.Composite ? DiagramInk.Faded(Ink.Series(item.Order), Wash) : Palette.CodeBg);

        return item.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    private DiagramStroke Stroke(BlockItem item) =>
        new(Ink.Written(item.Style.Stroke) ?? Palette.CodeBorder, item.Style.StrokeWidth ?? 1, DiagramInk.Dashes(item.Style.Dashes));

    /// <summary>A block measured: what is written on it, the room it needs, and — once its grid is laid out — where it sits.</summary>
    private sealed class Sized(BlockItem item, IReadOnlyList<DiagramWords> words)
    {
        public BlockItem Item { get; } = item;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public IReadOnlyList<Sized> Items { get; init; } = [];

        public Size Natural { get; set; }

        public Rect Bounds { get; set; }
    }
}
