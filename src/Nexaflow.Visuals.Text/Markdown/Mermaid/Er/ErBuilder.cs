using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Er;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Er;

/// <summary>The pieces an ER diagram's layout is made of — its layers, and what is in them.</summary>
public static class ErPiece
{
    /// <summary>The diagram itself: the entities, and the subgraphs they are boxed into.</summary>
    public const string Entities = "Entities";

    /// <summary>One entity, standing for everything written for it.</summary>
    public const string Entity = "Entity";

    /// <summary>One attribute of one: what it holds, what it is called, the keys it is, and what it is for.</summary>
    public const string Attribute = "Attribute";

    /// <summary>A subgraph: the box, and the entities it holds drawn inside its piece.</summary>
    public const string Group = "Group";

    /// <summary>A subgraph's own box and its name, behind the entities it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>The relationships, drawn over the diagram.</summary>
    public const string Relations = "Relations";

    /// <inheritdoc cref="Relations"/>
    public const string Relation = "Relation";

    /// <summary>What a relationship is called, written over the middle of its line.</summary>
    public const string Label = "Label";
}

/// <summary>
/// Draws an <c>erDiagram</c>. The entities are laid out in ranks by how far along the relationships reach them
/// (<see cref="DiagramLayers"/>), each rank ordered so as few lines cross as can be managed, and the whole thing runs the way a
/// <c>direction</c> line asks.
///
/// <strong>An entity is its name over its attributes.</strong> A rule the width of the box divides the two, and the attributes
/// are set in columns — what each holds, what it is called, the keys it is, and what it is for — so they read down as well as
/// across, which is how Mermaid sets them.
///
/// <strong>How many of each entity the other has is drawn at the end it belongs to</strong>, as crow's feet: a bar for one, a
/// circle for none, a fork for many. A relationship that does not identify what it reaches is drawn dotted.
///
/// <strong>A subgraph holds its entities in the layout.</strong> What is inside one is laid out in its own space and drawn
/// inside the subgraph's piece, so pressing an entity means that entity and pressing the room round it means the subgraph.
/// </summary>
internal sealed class ErBuilder : MermaidBuilder<ErDiagram>
{
    /// <summary>The air above and below the rows of a band.</summary>
    private const double Air = 5;

    /// <summary>The room a box keeps for its outline, so its rows are not drawn against it.</summary>
    private const double Chrome = 4;

    /// <summary>The air a subgraph keeps round what it holds.</summary>
    private const double Boxed = 14;

    /// <summary>How much of a subgraph's own colour is washed over it.</summary>
    private const double Wash = 0.12;

    /// <summary>How thick a box's outline and a relationship's line are drawn.</summary>
    private const double Thick = 1.5;

    /// <summary>How big what is written on a relationship is drawn.</summary>
    private const double LabelSize = 11;

    /// <summary>How wide what is written on a relationship runs before it wraps.</summary>
    private const double Widest = 160;

    private ErBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays an ER diagram's source out. Never null, and never throws.</summary>
    /// <param name="writing">Whether somebody is writing in it, which draws what is still to be written.</param>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new ErBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <inheritdoc/>
    protected override ErDiagram Of(MermaidBlock block) => ErDiagram.Of(block);

    protected override Size Draw(ErDiagram diagram, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Entities.Count == 0 && diagram.Groups.Count == 0) return AsWritten(build);

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The relationships are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var over = Covered(routes);

        build.Open(ErPiece.Entities, part: null, stops: Stops.None);
        foreach (var group in diagram.Within(null)) Held(build, diagram, plan, room, group, over);
        foreach (var entity in diagram.Inside(null)) Drawn(build, diagram.Config, plan, room, entity, over);
        build.Close();

        Relations(build, routes);

        return room.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    private Plan Laid(ErDiagram diagram)
    {
        var plan = new Plan();
        var cells = new List<DiagramCell>();

        foreach (var group in diagram.Groups)
        {
            var words = Naming(group, diagram.Config);
            var said = DiagramWords.Taken(words);

            var box = new Box(group, words)
            {
                Cell = new DiagramCell(new Size(said.Width + (Boxed * 2), 0))
                {
                    Inside = group.Parent is { } parent && plan.Groups.TryGetValue(parent, out var held) ? held.Cell : null,
                    Pad = Boxed,
                    Heading = said.Height > 0 ? said.Height + diagram.Config.TitleMargin + (Boxed / 2) : 0,
                },
            };

            plan.Groups[group.Key] = box;
            cells.Add(box.Cell);
        }

        foreach (var entity in diagram.Entities)
        {
            var sized = Measure(entity, diagram.Config);
            sized.Cell = new DiagramCell(sized.Size)
            {
                Inside = entity.Group is { } group && plan.Groups.TryGetValue(group, out var box) ? box.Cell : null,
            };

            plan.Entities.Add(sized);
            plan.Named.TryAdd(entity.Id, sized);
            cells.Add(sized.Cell);
        }

        foreach (var relation in diagram.Relations)
        {
            if (!plan.Named.TryGetValue(relation.From, out var from) || !plan.Named.TryGetValue(relation.To, out var to)) continue;

            plan.Joins[relation] = new DiagramJoin(from.Cell, to.Cell);
        }

        plan.Size = DiagramLayers.Lay(cells, [.. plan.Joins.Values], Towards(diagram.Way),
                                      diagram.Config.NodeSpacing, diagram.Config.RankSpacing);

        return plan;
    }

    private DiagramRoom Reached(ErDiagram diagram, Plan plan)
    {
        var room = new DiagramRoom(diagram.Config.Air);

        room.Reach(new Rect(default, plan.Size));
        foreach (var entity in plan.Entities) room.Reach(entity.Cell.Bounds);
        foreach (var box in plan.Groups.Values) room.Reach(box.Cell.Bounds);

        foreach (var (relation, join) in plan.Joins)
        {
            foreach (var at in join.Route) room.Reach(new Rect(at, at));

            if (Says(relation) is { Count: > 0 } said) room.Reach(DiagramConnector.Room(join.Route, said));
        }

        return room;
    }

    /// <summary>One entity measured: its name over its attributes, and how wide each column of them runs.</summary>
    private Sized Measure(ErEntity entity, ErConfig config)
    {
        var ink = Ink.Written(entity.Style.Colour) ?? Palette.Text;

        var title = Wrapped(entity.Said, entity.SaidHole, config.TextSize, ink, config.Wrapping, FontWeights.SemiBold);

        var rows = entity.Attributes
            .Select(attribute => new Row(attribute,
                                         [Worded(attribute.Type, ink, config),
                                          Worded(attribute.Field, ink, config),
                                          Keyed(attribute, ink, config),
                                          Worded(attribute.Comment, Palette.TextMuted, config)]))
            .ToList();

        var columns = new double[Columns];
        foreach (var row in rows)
            for (var at = 0; at < Columns; at++)
                columns[at] = Math.Max(columns[at], row.Said[at]?.Width ?? 0);

        var across = columns.Where(column => column > 0).ToList();
        var widest = Math.Max(title.Count == 0 ? 0 : title.Max(words => words.Width),
                              across.Sum() + (Math.Max(0, across.Count - 1) * Gap(config)));

        var head = (Math.Max(1, title.Count) * config.RowHeight) + (Air * 2);
        var body = rows.Count > 0 ? (rows.Count * config.RowHeight) + (Air * 2) : 0;

        return new Sized(entity, title, rows, columns)
        {
            Head = head,
            Body = body,
            Size = new Size(Math.Max(config.MinWidth, widest + (config.Padding * 2)),
                            Math.Max(config.MinHeight, head + body) + Chrome),
        };
    }

    /// <summary>What is written at the top of a subgraph.</summary>
    private IReadOnlyList<DiagramWords> Naming(ErGroup group, ErConfig config) =>
        Wrapped(group.Said, null, config.TextSize, Palette.Text, config.Wrapping);

    /// <summary>One part of an attribute, drawn as the characters written — or nothing, where nothing is written.</summary>
    private DiagramWords? Worded(ContentPart? part, Brush ink, ErConfig config) =>
        part is { Length: > 0 } ? Written(part, null, config.TextSize, ink) : null;

    /// <summary>
    /// The keys an attribute is: the one written, where it is the only one, and otherwise what they say together — which nobody
    /// writes as one run, and so is pressed rather than typed into.
    /// </summary>
    private DiagramWords? Keyed(ErAttribute attribute, Brush ink, ErConfig config) => attribute.Keys.Count switch
    {
        0 => null,
        1 => Written(attribute.Keys[0], null, config.TextSize, ink),
        _ => Worked(attribute.Keyed, attribute.Part, config.TextSize, ink),
    };

    // ── The relationships ───────────────────────────────────────────────────

    /// <summary>Where every relationship runs once everything is placed, its ends brought in to the boxes it joins.</summary>
    private List<Route> Routes(ErDiagram diagram, Plan plan, DiagramRoom room)
    {
        var routes = new List<Route>();

        foreach (var relation in diagram.Relations)
        {
            if (!plan.Joins.TryGetValue(relation, out var join) || join.Route.Count < 2) continue;

            var placed = Trimmed(join).Select(room.At).ToList();
            var said = Says(relation);

            routes.Add(new Route(relation, placed, said, DiagramConnector.Room(placed, said)));
        }

        return routes;
    }

    /// <summary>What a relationship is called, where anything is written on it.</summary>
    private IReadOnlyList<DiagramWords> Says(ErRelation relation) =>
        relation.Said is null && relation.SaidHole is null
            ? []
            : Wrapped(relation.Said, relation.SaidHole, LabelSize, Palette.TextMuted, Widest);

    /// <summary>A route's ends brought in from the middles of the boxes it joins to their edges.</summary>
    private static IReadOnlyList<Point> Trimmed(DiagramJoin join)
    {
        var points = join.Route.ToList();
        if (ReferenceEquals(join.From, join.To)) return points;

        points[0] = DiagramShapes.Edge(DiagramShape.Rectangle, join.From.Bounds, points[1]);
        points[^1] = DiagramShapes.Edge(DiagramShape.Rectangle, join.To.Bounds, points[^2]);

        return points;
    }

    /// <summary>What the relationships cover, which whatever is drawn under them does not stand in.</summary>
    private static IReadOnlyList<Geometry> Covered(IReadOnlyList<Route> routes)
    {
        var over = new List<Geometry>();

        foreach (var route in routes)
        {
            over.Add(DiagramConnector.Band(route.Along, Thick));
            if (!route.Room.IsEmpty) over.Add(new RectangleGeometry(route.Room));
        }

        return over;
    }

    /// <summary>The relationships, drawn over the diagram.</summary>
    private void Relations(LayoutBuilder build, IReadOnlyList<Route> routes)
    {
        if (routes.Count == 0) return;

        build.Open(ErPiece.Relations, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            var stroke = new DiagramStroke(Palette.TextMuted, Thick, route.Relation.Dotted ? DiagramStroke.Dashed : null);

            DiagramConnector.Draw(build, ErPiece.Relation, route.Relation.Part, route.Along, stroke,
                                  Headed(route.Relation.Near), Headed(route.Relation.Far), curved: true);

            DiagramConnector.Says(build, ErPiece.Label, route.Relation.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    /// <summary>What one end of a relationship draws: the crow's foot saying how many of that entity the other has.</summary>
    private static DiagramHead Headed(ErEnd end) => end switch
    {
        ErEnd.ZeroOne => DiagramHead.ZeroOrOne,
        ErEnd.ZeroMany => DiagramHead.ZeroOrMore,
        ErEnd.OneMany => DiagramHead.OneOrMore,
        _ => DiagramHead.ExactlyOne,
    };

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>A subgraph: its box, its name at the top of it, and the entities it holds drawn inside its piece.</summary>
    private void Held(LayoutBuilder build, ErDiagram diagram, Plan plan, DiagramRoom room, ErGroup group,
                      IReadOnlyList<Geometry> over)
    {
        var box = plan.Groups[group.Key];
        var bounds = room.At(box.Cell.Bounds);
        var said = DiagramWords.Taken(box.Words);
        var heading = new Rect(bounds.X + Boxed, bounds.Y + (Boxed / 3), Math.Max(0, bounds.Width - (Boxed * 2)), said.Height);

        var covered = DiagramShapes.United(
        [
            .. over,
            .. diagram.Within(group.Key).Select(nested => DiagramShapes.Outline(DiagramShape.Rounded, room.At(plan.Groups[nested.Key].Cell.Bounds))),
            .. Inside(diagram, plan, group.Key).Select(entity => DiagramShapes.Outline(DiagramShape.Rectangle, room.At(entity.Cell.Bounds))),
        ]);

        build.Open(ErPiece.Group, group.Whole, stops: Stops.None);
        DiagramShapes.Draw(build, ErPiece.Holding, group.Part, DiagramShape.Rounded, bounds,
                           DiagramInk.Faded(Ink.Series(group.Order), Wash), new DiagramStroke(Palette.CodeBorder, Thick),
                           DiagramWords.Placed(box.Words, heading, MermaidPiece.Words), covered);

        foreach (var nested in diagram.Within(group.Key)) Held(build, diagram, plan, room, nested, over);
        foreach (var entity in diagram.Inside(group.Key)) Drawn(build, diagram.Config, plan, room, entity, over);
        build.Close();
    }

    private void Drawn(LayoutBuilder build, ErConfig config, Plan plan, DiagramRoom room, ErEntity entity,
                       IReadOnlyList<Geometry> over)
    {
        if (plan.Entities.FirstOrDefault(sized => ReferenceEquals(sized.Entity, entity)) is not { } laid) return;

        var box = room.At(laid.Cell.Bounds);
        var outline = DiagramShapes.Outline(DiagramShape.Rounded, box);

        var title = Heading(laid, config, box).ToList();
        var rows = Rows(laid, config, box).ToList();

        var covered = new GeometryGroup();
        foreach (var shape in over) covered.Children.Add(shape);
        foreach (var (words, at) in title) covered.Children.Add(Taken(words, at));
        foreach (var (_, said) in rows) foreach (var (words, at) in said) covered.Children.Add(Taken(words, at));

        build.Open(ErPiece.Entity, entity.Whole, stops: Stops.None);
        build.Open(MermaidPiece.Shape, entity.Part, stops: Stops.None);

        var stroke = Stroke(entity.Style, config);
        build.Draw(new GeometryMark(outline, Fill(entity, config), stroke.Ink, stroke.Thickness));

        // The rule under the name is what says an entity has attributes at all.
        if (laid.Body > 0)
            build.Draw(new LineMark(new Point(box.Left, box.Y + laid.Head), new Point(box.Right, box.Y + laid.Head),
                                    Palette.CodeBorder));

        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, covered);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        foreach (var (words, at) in title) words.Set(build, at, MermaidPiece.Words);

        foreach (var (attribute, said) in rows)
        {
            build.Open(ErPiece.Attribute, attribute.Part, stops: Stops.None);
            foreach (var (words, at) in said) words.Set(build, at, MermaidPiece.Words);
            build.Close();
        }

        build.Close();
    }

    /// <summary>Where each line of the name is set: one to a row, across the middle of the band above the attributes.</summary>
    private static IEnumerable<(DiagramWords Words, Point At)> Heading(Sized sized, ErConfig config, Rect box)
    {
        var top = box.Y + Air;

        foreach (var words in sized.Title)
        {
            yield return (words, new Point(box.X + ((box.Width - words.Width) / 2), top + ((config.RowHeight - words.Height) / 2)));
            top += config.RowHeight;
        }
    }

    /// <summary>Where each attribute is set: its columns across the row, one attribute to a row under the rule.</summary>
    private static IEnumerable<(ErAttribute Attribute, List<(DiagramWords Words, Point At)> Said)> Rows(
        Sized sized, ErConfig config, Rect box)
    {
        var top = box.Y + sized.Head + Air;

        foreach (var row in sized.Rows)
        {
            var said = new List<(DiagramWords, Point)>();
            var across = box.X + config.Padding;

            for (var at = 0; at < Columns; at++)
            {
                if (sized.Columns[at] <= 0) continue;

                if (row.Said[at] is { } words)
                    said.Add((words, new Point(across, top + ((config.RowHeight - words.Height) / 2))));

                across += sized.Columns[at] + Gap(config);
            }

            yield return (row.Attribute, said);
            top += config.RowHeight;
        }
    }

    private static RectangleGeometry Taken(DiagramWords words, Point at) =>
        new(new Rect(at, new Size(words.Width, words.Height)));

    private static IEnumerable<Sized> Inside(ErDiagram diagram, Plan plan, string? group) =>
        diagram.Inside(group)
            .Select(entity => plan.Entities.FirstOrDefault(sized => ReferenceEquals(sized.Entity, entity)))
            .OfType<Sized>();

    /// <summary>The space between one column of the attributes and the next.</summary>
    private static double Gap(ErConfig config) => config.TextSize;

    // ── Colour ──────────────────────────────────────────────────────────────

    private Brush Fill(ErEntity entity, ErConfig config)
    {
        var fill = Ink.Written(entity.Style.Fill) ?? Ink.Written(config.Fill) ?? Palette.CodeBg;

        return entity.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    private DiagramStroke Stroke(MermaidStyle style, ErConfig config) =>
        new(Ink.Written(style.Stroke) ?? Ink.Written(config.Stroke) ?? Palette.CodeBorder,
            style.StrokeWidth ?? Thick, DiagramInk.Dashes(style.Dashes));

    private static DiagramWay Towards(ErWay way) => way switch
    {
        ErWay.Up => DiagramWay.Up,
        ErWay.Right => DiagramWay.Right,
        ErWay.Left => DiagramWay.Left,
        _ => DiagramWay.Down,
    };

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>How many columns an attribute is set in: what it holds, what it is called, its keys, and what it is for.</summary>
    private const int Columns = 4;

    /// <summary>One attribute measured, a run of words to each column it writes.</summary>
    private sealed record Row(ErAttribute Attribute, IReadOnlyList<DiagramWords?> Said);

    /// <summary>One entity measured: its name, its attributes, how wide each column is, and the cell the layout placed it in.</summary>
    private sealed class Sized(
        ErEntity entity,
        IReadOnlyList<DiagramWords> title,
        IReadOnlyList<Row> rows,
        IReadOnlyList<double> columns)
    {
        public ErEntity Entity { get; } = entity;

        public IReadOnlyList<DiagramWords> Title { get; } = title;

        public IReadOnlyList<Row> Rows { get; } = rows;

        /// <summary>How wide each column of the attributes runs, which is what sets them under one another.</summary>
        public IReadOnlyList<double> Columns { get; } = columns;

        /// <summary>How deep the band its name is drawn in is.</summary>
        public double Head { get; init; }

        /// <summary>How deep the band its attributes are drawn in is, or nought where nothing writes it any.</summary>
        public double Body { get; init; }

        public Size Size { get; init; }

        public DiagramCell Cell { get; set; } = new(default);
    }

    /// <summary>A subgraph measured.</summary>
    private sealed class Box(ErGroup group, IReadOnlyList<DiagramWords> words)
    {
        public ErGroup Group { get; } = group;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A relationship worked out: where it runs, and what is written on it.</summary>
    private sealed record Route(ErRelation Relation, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>Everything the diagram was measured and laid out into.</summary>
    private sealed class Plan
    {
        public List<Sized> Entities { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Box> Groups { get; } = new(StringComparer.Ordinal);

        public Dictionary<ErRelation, DiagramJoin> Joins { get; } = [];

        public Size Size { get; set; }
    }
}
