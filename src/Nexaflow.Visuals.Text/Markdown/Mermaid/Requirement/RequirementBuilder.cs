using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Requirement;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Requirement;

/// <summary>The pieces a requirement diagram's layout is made of — its layers, and what is in them.</summary>
public static class RequirementPiece
{
    /// <summary>The diagram itself: the requirements, and the elements that meet them.</summary>
    public const string Boxes = "Requirements";

    /// <summary>One requirement or element, standing for everything written for it.</summary>
    public const string Box = "Requirement";

    /// <summary>One field of one: what is drawn in front of it, and what it is set to.</summary>
    public const string Fact = "Fact";

    /// <summary>The relations, drawn over the diagram.</summary>
    public const string Relations = "Relations";

    /// <inheritdoc cref="Relations"/>
    public const string Relation = "Relation";

    /// <summary>What holds between two of them, written over the middle of the line joining them.</summary>
    public const string Label = "Label";
}

/// <summary>
/// Draws a <c>requirementDiagram</c>. The requirements are laid out in ranks by how far along the relations reach them
/// (<see cref="DiagramLayers"/>), each rank ordered so as few lines cross as can be managed, and the whole thing runs the way a
/// <c>direction</c> line asks.
///
/// <strong>A requirement is two compartments in one box.</strong> What kind of thing it is over its name, then a row for each
/// field — <c>Id</c>, <c>Text</c>, <c>Risk</c>, <c>Verification</c> — with a rule the width of the box between the two. A box
/// nothing writes fields for is the one compartment, which is what a relation naming something no block writes draws.
///
/// <strong>What holds between two of them is written on the line</strong>, in guillemets as SysML writes it; <c>contains</c> is
/// the whole and its parts, drawn as a crosshair at the end that holds rather than as an arrow at the end held.
/// </summary>
internal sealed class RequirementBuilder : MermaidBuilder<RequirementDiagram>
{
    /// <summary>How big what kind of thing it is is drawn, against the name under it.</summary>
    private const double KindSize = 10.5;

    /// <summary>How big what is written on a relation is drawn.</summary>
    private const double LabelSize = 11;

    /// <summary>The air above and below the rows of a band.</summary>
    private const double Air = 5;

    /// <summary>The room a box keeps for its outline, so its rows are not drawn against it.</summary>
    private const double Chrome = 4;

    /// <summary>How thick a box's outline and a relation's line are drawn.</summary>
    private const double Thick = 1.5;

    /// <summary>How wide what is written on a relation runs before it wraps.</summary>
    private const double Widest = 160;

    /// <summary>What kind of thing a box is is drawn between these, which is what Mermaid draws.</summary>
    private const string Opens = "«";
    private const string Shuts = "»";

    private RequirementBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a requirement diagram's source out. Never null, and never throws.</summary>
    /// <param name="writing">Whether somebody is writing in it, which draws what is still to be written.</param>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new RequirementBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <inheritdoc/>
    protected override RequirementDiagram Of(MermaidBlock block) => RequirementDiagram.Of(block);

    protected override Size Draw(RequirementDiagram diagram, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Nodes.Count == 0) return AsWritten(build);

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The relations are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var over = Covered(routes);

        build.Open(RequirementPiece.Boxes, part: null, stops: Stops.None);
        foreach (var sized in plan.Nodes) Drawn(build, diagram.Config, room, sized, over);
        build.Close();

        Relations(build, routes);

        return room.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    private Plan Laid(RequirementDiagram diagram)
    {
        var plan = new Plan();
        var cells = new List<DiagramCell>();

        foreach (var node in diagram.Nodes)
        {
            var sized = Measure(node, diagram.Config);
            sized.Cell = new DiagramCell(sized.Size);

            plan.Nodes.Add(sized);
            plan.Named.TryAdd(node.Id, sized);
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

    private DiagramRoom Reached(RequirementDiagram diagram, Plan plan)
    {
        var room = new DiagramRoom(diagram.Config.Air);

        room.Reach(new Rect(default, plan.Size));
        foreach (var node in plan.Nodes) room.Reach(node.Cell.Bounds);

        foreach (var (relation, join) in plan.Joins)
        {
            foreach (var at in join.Route) room.Reach(new Rect(at, at));

            if (Says(relation) is { Count: > 0 } said) room.Reach(DiagramConnector.Room(join.Route, said));
        }

        return room;
    }

    /// <summary>One box measured: what kind of thing it is over its name, then a row for each field.</summary>
    private Sized Measure(RequirementNode node, RequirementConfig config)
    {
        var ink = Ink.Written(node.Style.Colour) ?? Palette.Text;

        var title = new List<DiagramWords>();
        if (node.Says is { Length: > 0 } says)
            title.Add(Worked(Opens + says + Shuts, node.Kind, KindSize, ink, slant: FontStyles.Italic));

        title.AddRange(Wrapped(node.Said, node.SaidHole, config.TextSize, ink, config.Wrapping, FontWeights.SemiBold));

        var rows = node.Facts
            .Select(fact => (Fact: fact,
                             Label: Worked($"{fact.Label}:", fact.Part, config.TextSize, ink),
                             Said: Written(fact.Said, fact.Hole, config.TextSize, ink)))
            .ToList();

        var head = (Math.Max(1, title.Count) * config.RowHeight) + (Air * 2);
        var body = rows.Count > 0 ? (rows.Count * config.RowHeight) + (Air * 2) : 0;

        var widest = Math.Max(title.Count == 0 ? 0 : title.Max(words => words.Width),
                              rows.Count == 0 ? 0 : rows.Max(row => row.Label.Width + Gap(config) + row.Said.Width));

        return new Sized(node, title, rows)
        {
            Head = head,
            Body = body,
            Size = new Size(Math.Max(config.MinWidth, widest + (config.Padding * 2)),
                            Math.Max(config.MinHeight, head + body) + Chrome),
        };
    }

    // ── The relations ───────────────────────────────────────────────────────

    /// <summary>Where every relation runs once everything is placed, its ends brought in to the boxes it joins.</summary>
    private List<Route> Routes(RequirementDiagram diagram, Plan plan, DiagramRoom room)
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

    /// <summary>What holds between two of them, in guillemets as SysML writes it.</summary>
    private IReadOnlyList<DiagramWords> Says(RequirementRelation relation) =>
        relation.Said is { Length: > 0 } said
            ? [Worked(Opens + said.Text + Shuts, said, LabelSize, Palette.TextMuted)]
            : [];

    /// <summary>A route's ends brought in from the middles of the boxes it joins to their edges.</summary>
    private static IReadOnlyList<Point> Trimmed(DiagramJoin join)
    {
        var points = join.Route.ToList();
        if (ReferenceEquals(join.From, join.To)) return points;

        points[0] = DiagramShapes.Edge(DiagramShape.Rectangle, join.From.Bounds, points[1]);
        points[^1] = DiagramShapes.Edge(DiagramShape.Rectangle, join.To.Bounds, points[^2]);

        return points;
    }

    /// <summary>What the relations cover, which whatever is drawn under them does not stand in.</summary>
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

    /// <summary>The relations, drawn over the diagram.</summary>
    private void Relations(LayoutBuilder build, IReadOnlyList<Route> routes)
    {
        if (routes.Count == 0) return;

        build.Open(RequirementPiece.Relations, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            // What holds the other is a solid line with the crosshair at the end holding it; everything else is a dashed arrow.
            var holds = route.Relation.Holds;
            var stroke = new DiagramStroke(Palette.TextMuted, Thick, holds ? null : DiagramStroke.Dashed);

            DiagramConnector.Draw(build, RequirementPiece.Relation, route.Relation.Part, route.Along, stroke,
                                  holds ? DiagramHead.CrossCircle : DiagramHead.None,
                                  holds ? DiagramHead.None : DiagramHead.Open,
                                  curved: true);

            DiagramConnector.Says(build, RequirementPiece.Label, route.Relation.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    private void Drawn(LayoutBuilder build, RequirementConfig config, DiagramRoom room, Sized sized, IReadOnlyList<Geometry> over)
    {
        var box = room.At(sized.Cell.Bounds);
        var outline = DiagramShapes.Outline(DiagramShape.Rounded, box);

        var title = Heading(sized, config, box).ToList();
        var rows = Rows(sized, config, box).ToList();

        var covered = new GeometryGroup();
        foreach (var shape in over) covered.Children.Add(shape);
        foreach (var (words, at) in title) covered.Children.Add(Taken(words, at));
        foreach (var row in rows)
        {
            covered.Children.Add(Taken(row.Label, row.LabelAt));
            covered.Children.Add(Taken(row.Said, row.SaidAt));
        }

        build.Open(RequirementPiece.Box, sized.Node.Whole, stops: Stops.None);
        build.Open(MermaidPiece.Shape, sized.Node.Part, stops: Stops.None);

        var stroke = Stroke(sized.Node.Style);
        build.Draw(new GeometryMark(outline, Fill(sized.Node), stroke.Ink, stroke.Thickness));

        // The rule under the name is what says a requirement has fields at all.
        if (sized.Body > 0)
            build.Draw(new LineMark(new Point(box.Left, box.Y + sized.Head), new Point(box.Right, box.Y + sized.Head),
                                    Palette.CodeBorder));

        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, covered);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        foreach (var (words, at) in title) words.Set(build, at, MermaidPiece.Words);

        foreach (var row in rows)
        {
            build.Open(RequirementPiece.Fact, row.Fact.Part, stops: Stops.None);
            row.Label.Set(build, row.LabelAt, MermaidPiece.Words);
            row.Said.Set(build, row.SaidAt, MermaidPiece.Words);
            build.Close();
        }

        build.Close();
    }

    /// <summary>Where each line of the name is set: one to a row, across the middle of the band above the fields.</summary>
    private static IEnumerable<(DiagramWords Words, Point At)> Heading(Sized sized, RequirementConfig config, Rect box)
    {
        var top = box.Y + Air;

        foreach (var words in sized.Title)
        {
            yield return (words, new Point(box.X + ((box.Width - words.Width) / 2), top + ((config.RowHeight - words.Height) / 2)));
            top += config.RowHeight;
        }
    }

    /// <summary>Where each field is set: what is drawn in front of it, then what it is set to, one to a row under the rule.</summary>
    private static IEnumerable<Row> Rows(Sized sized, RequirementConfig config, Rect box)
    {
        var top = box.Y + sized.Head + Air;

        foreach (var (fact, label, said) in sized.Facts)
        {
            var at = new Point(box.X + config.Padding, top + ((config.RowHeight - label.Height) / 2));

            yield return new Row(fact, label, at, said,
                                 new Point(at.X + label.Width + Gap(config), top + ((config.RowHeight - said.Height) / 2)));
            top += config.RowHeight;
        }
    }

    private static RectangleGeometry Taken(DiagramWords words, Point at) =>
        new(new Rect(at, new Size(words.Width, words.Height)));

    /// <summary>The space between what is drawn in front of a field and what it is set to, which a measured width leaves out.</summary>
    private static double Gap(RequirementConfig config) => config.TextSize / 3;

    // ── Colour ──────────────────────────────────────────────────────────────

    private Brush Fill(RequirementNode node)
    {
        var fill = Ink.Written(node.Style.Fill) ?? Palette.CodeBg;

        return node.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    private DiagramStroke Stroke(MermaidStyle style) =>
        new(Ink.Written(style.Stroke) ?? Palette.CodeBorder, style.StrokeWidth ?? Thick, DiagramInk.Dashes(style.Dashes));

    private static DiagramWay Towards(RequirementWay way) => way switch
    {
        RequirementWay.Up => DiagramWay.Up,
        RequirementWay.Right => DiagramWay.Right,
        RequirementWay.Left => DiagramWay.Left,
        _ => DiagramWay.Down,
    };

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>One field placed: what is drawn in front of it and where, and what it is set to and where.</summary>
    private sealed record Row(RequirementFact Fact, DiagramWords Label, Point LabelAt, DiagramWords Said, Point SaidAt);

    /// <summary>One box measured: the words of each band, how deep each is, and the cell the layout placed it in.</summary>
    private sealed class Sized(
        RequirementNode node,
        IReadOnlyList<DiagramWords> title,
        IReadOnlyList<(RequirementFact Fact, DiagramWords Label, DiagramWords Said)> facts)
    {
        public RequirementNode Node { get; } = node;

        public IReadOnlyList<DiagramWords> Title { get; } = title;

        public IReadOnlyList<(RequirementFact Fact, DiagramWords Label, DiagramWords Said)> Facts { get; } = facts;

        /// <summary>How deep the band its name is drawn in is.</summary>
        public double Head { get; init; }

        /// <summary>How deep the band its fields are drawn in is, or nought where nothing writes it any.</summary>
        public double Body { get; init; }

        public Size Size { get; init; }

        public DiagramCell Cell { get; set; } = new(default);
    }

    /// <summary>A relation worked out: where it runs, and what is written on it.</summary>
    private sealed record Route(RequirementRelation Relation, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>Everything the diagram was measured and laid out into.</summary>
    private sealed class Plan
    {
        public List<Sized> Nodes { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<RequirementRelation, DiagramJoin> Joins { get; } = [];

        public Size Size { get; set; }
    }
}
