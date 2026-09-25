using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

/// <summary>
/// Draws a <c>flowchart</c> — or a <c>graph</c>, which Mermaid reads the same way. The nodes are laid out in ranks by how far
/// along the links reach them (<see cref="DiagramLayers"/>), each rank ordered so as few lines cross as can be managed, and the
/// whole thing runs the way the header asks: down, up, left or right.
///
/// <para>
/// <strong>A subgraph holds its nodes in the layout.</strong> What is inside one is laid out in its own space — which is what
/// lets a <c>direction</c> line run it its own way — and drawn inside the subgraph's piece, so pressing a node means that node
/// and pressing the room round it means the subgraph holding it. A subgraph stands only where its own nodes, and the links drawn
/// over it, leave it uncovered.
/// </para>
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A node stands for the node written for it, and what is drawn on
/// it is the characters written — its label, or its id where nothing else says anything.
/// </para>
/// <para>
/// <strong>A swimlane is a flowchart laid out in lanes.</strong> Where <see cref="Laning"/> says so, a subgraph written outside them
/// all is a lane rather than a box: a band running the whole length of the chart, its name in a strip at the near end of it, its
/// cells one to a rank, and a link handed to another lane going across rather than on. Everything else about it — the shapes, the
/// links, the styling — is a flowchart's, which is how Mermaid draws it too.
/// </para>
/// <para>
/// <strong>A lane and a box are not the same kind of grouping</strong>, so they do not stand for the same thing. A box is drawn
/// round the nodes written inside it, and stands for the line that opened it; a lane is the stretch of work it holds, and stands
/// for the whole <c>subgraph … end</c> it was written as, so that selecting it selects the lane.
/// </para>
/// </summary>
internal class FlowchartBuilder : MermaidBuilder<FlowchartDiagram>
{
    /// <summary>How big what is written on a node is, and on a link.</summary>
    private const double TextSize = 13;
    private const double LabelSize = 11.5;

    /// <summary>The least room a node takes, so a node with little to say is still a node to look at.</summary>
    private const double Least = 60;
    private const double Short = 34;

    /// <summary>The clear air inside a node's shape, and inside a subgraph's box.</summary>
    private const double Pad = 10;
    private const double Boxed = 14;

    /// <summary>How thick a link written with equals signs is drawn.</summary>
    private const double Thick = 2.5;

    internal FlowchartBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    /// <summary>
    /// How the chart's lanes are laid out, where a subgraph written outside them all is one: whether a link handed from one lane to
    /// another goes across rather than on, and whether the lanes are set across in an order worked out rather than the order they were
    /// written in. Null for a flowchart, whose outermost subgraphs are boxes round what they hold — see <c>SwimlaneBuilder</c>.
    /// </summary>
    protected virtual (bool Sideways, bool Ordered)? Laning(FlowchartDiagram diagram) => null;

    /// <inheritdoc/>
    protected override FlowchartDiagram Of(MermaidBlock block) => FlowchartDiagram.Of(block);

    /// <inheritdoc/>
    protected override DiagramChart? Chart(FlowchartDiagram diagram) =>
        new([.. diagram.Nodes.Select(node => node.Id)], [.. diagram.Links.Select(link => (link.From, link.To))]);

    protected override Size Draw(FlowchartDiagram diagram, LayoutBuilder build)
    {
        // A flowchart with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Nodes.Count == 0 && diagram.Groups.Count == 0) return AsWritten(build);

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The links are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var over = DiagramConnector.Covered(routes.Where(route => route.Link.Drawn).Select(route => (route.Along, route.Room)), Thick);

        build.Open(FlowchartPiece.Nodes, part: null, stops: Stops.None);
        foreach (var group in diagram.Within(null)) Held(build, diagram, plan, room, group, over);
        foreach (var node in diagram.Inside(null)) Drawn(build, plan, room, node, over);
        plan.Spill.Draw(build, room, Ink.Surface, new DiagramStroke(Palette.CodeBorder, 1, DiagramStroke.Dotted));
        build.Close();

        Links(build, routes, diagram.Config);

        return room.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    /// <summary>
    /// Everything measured and placed: a band for each lane, a cell for each node and each subgraph that is a box, a join for each link,
    /// and the layered layout run over the lot of them.
    /// </summary>
    private Plan Laid(FlowchartDiagram diagram)
    {
        var plan = new Plan();
        var cells = new List<DiagramCell>();
        var laning = Laning(diagram);
        var towards = Towards(diagram.Way);

        // The lanes first, in the order their bands are set across the chart, so everything in one knows the band it keeps to.
        if (laning is { } how)
            foreach (var group in Laned(diagram, how.Ordered))
            {
                var said = DiagramWords.Taken(Naming(diagram, group));

                plan.Lanes.Add(new Lane(group, Naming(diagram, group), plan.Lanes.Count + 1,
                                        new DiagramLane(said.Width + (Boxed * 2), Boxed,
                                                        said.Height + diagram.Config.TitleMargin + (Boxed / 2))));
            }

        // Then the subgraphs that are boxes, outermost in, so a nested one knows the cell it sits inside.
        foreach (var group in Nested(diagram, null))
        {
            if (plan.Laned(group.Key) is not null) continue;

            var words = Naming(diagram, group);
            var said = DiagramWords.Taken(words);

            var box = new Box(group, words)
            {
                Cell = new DiagramCell(new Size(said.Width + (Boxed * 2), 0))
                {
                    Inside = Holding(plan, group.Parent),
                    Lane = Banded(plan, diagram, group.Parent),
                    Way = group.Way is { } way ? Towards(way) : null,
                    Pad = Boxed,
                    Heading = said.Height > 0 ? said.Height + diagram.Config.TitleMargin + (Boxed / 2) : 0,
                },
            };

            plan.Groups[group.Key] = box;
            cells.Add(box.Cell);
        }

        foreach (var node in diagram.Nodes)
        {
            // A node folded away is never given a cell, so nothing is laid out round it, the links to it have no end to
            // meet, and a box holding nothing else closes up rather than standing empty.
            if (!Draws(node.Id)) continue;

            var shape = Shaped(node);
            var words = Said(node, diagram.Config.Wrapping);
            var around = node.Picture is { } pictured ? Pictured(pictured, DiagramWords.Taken(words)) : DiagramShapes.Around(shape, DiagramWords.Taken(words), Pad);

            // A marker drawn without words is the size it always is rather than the least a node takes, and a fork lies across the way
            // the chart runs, standing on end where it runs across the page.
            if (shape == DiagramShape.Fork && diagram.Way is FlowchartWay.Right or FlowchartWay.Left) around = new Size(around.Height, around.Width);
            var least = node.Picture is null && DiagramShapes.Worded(shape) ? new Size(Math.Max(around.Width, Least), Math.Max(around.Height, Short)) : around;

            var sized = new Sized(node, words, shape)
            {
                Cell = new DiagramCell(least)
                {
                    Shape = shape,
                    Inside = Holding(plan, node.Group),
                    Lane = Banded(plan, diagram, node.Group),
                },
            };

            plan.Nodes.Add(sized);
            if (node.Id.Length > 0) plan.Named.TryAdd(node.Id, sized);
            cells.Add(sized.Cell);
        }

        foreach (var link in diagram.Links)
        {
            if (Ended(plan, diagram, link.From) is not { } from || Ended(plan, diagram, link.To) is not { } to) continue;

            plan.Joins[link] = new DiagramJoin(from, to, Aside(plan, diagram, link) ? 0 : link.Span);
        }

        // Every line meets a shape at its own place along the edge. A decision has a line in and a line out for each way
        // it can go, and on a diamond the straight run out of the middle leaves by the one point at the bottom — so
        // without this they all set off from that point, on top of one another and on top of what arrives there.
        // A node with too many children draws the first of them and one more node offering the rest, which is laid out
        // with everything else so nothing is placed where it would sit over something.
        plan.Spill = Spilled(id => plan.Named.TryGetValue(id, out var sized) ? sized.Cell : null);
        cells.AddRange(plan.Spill.Cells);

        plan.Size = DiagramLayers.Lay(cells, [.. plan.Joins.Values, .. plan.Spill.Joins], towards, diagram.Config.NodeSpacing,
                                      diagram.Config.RankSpacing,
                                      laning is { } lanes
                                          ? new DiagramLanes([.. plan.Lanes.Select(lane => lane.Band)], across: !lanes.Sideways)
                                          : null,
                                      ports: true);

        return plan;
    }

    /// <summary>What is written at the top of a subgraph, or up the near side of a lane's band.</summary>
    private IReadOnlyList<DiagramWords> Naming(FlowchartDiagram diagram, FlowchartGroup group) =>
        Wrapped(group.Said, group.SaidHole, TextSize, Ink.Written(group.Style.Colour) ?? Palette.Text, diagram.Config.Wrapping);

    /// <summary>The cell something is laid out inside: the box holding it, and none where a lane holds it or nothing does.</summary>
    private static DiagramCell? Holding(Plan plan, string? group) =>
        group is not null && plan.Groups.TryGetValue(group, out var box) ? box.Cell : null;

    /// <summary>The lane something written in a subgraph keeps to, where the outermost subgraph holding it is one.</summary>
    private static int Banded(Plan plan, FlowchartDiagram diagram, string? group) =>
        diagram.Lane(group) is { } lane && plan.Laned(lane.Key) is { } held ? held.Number : 0;

    /// <summary>
    /// The lanes, in the order their bands are set across the chart: the order they are written, or an order worked out to keep the
    /// handoffs between them short where the front matter asks for one.
    /// </summary>
    private static IReadOnlyList<FlowchartGroup> Laned(FlowchartDiagram diagram, bool ordered)
    {
        var lanes = diagram.Lanes.ToList();
        if (!ordered || lanes.Count < 3) return lanes;

        var handoffs = Handoffs(diagram);
        var order = lanes.Select(lane => lane.Key).ToList();
        var reach = Reaching(order, handoffs);

        // Each neighbouring pair swapped in turn, every swap that shortens the handoffs kept, until none of them does.
        for (var pass = 0; pass < order.Count; pass++)
        {
            var settled = true;

            for (var at = 0; at + 1 < order.Count; at++)
            {
                (order[at], order[at + 1]) = (order[at + 1], order[at]);
                var swapped = Reaching(order, handoffs);

                if (swapped < reach) (reach, settled) = (swapped, false);
                else (order[at], order[at + 1]) = (order[at + 1], order[at]);
            }

            if (settled) break;
        }

        return [.. order.Select(key => lanes.First(lane => string.Equals(lane.Key, key, StringComparison.Ordinal)))];
    }

    /// <summary>How many links are handed from each lane to each other one, whichever way round they were written.</summary>
    private static Dictionary<(string From, string To), int> Handoffs(FlowchartDiagram diagram)
    {
        var handoffs = new Dictionary<(string From, string To), int>();

        foreach (var link in diagram.Links)
        {
            if (diagram.Lane(diagram.Find(link.From)?.Group) is not { } from) continue;
            if (diagram.Lane(diagram.Find(link.To)?.Group) is not { } to) continue;
            if (string.Equals(from.Key, to.Key, StringComparison.Ordinal)) continue;

            var pair = string.CompareOrdinal(from.Key, to.Key) <= 0 ? (from.Key, to.Key) : (to.Key, from.Key);
            handoffs[pair] = handoffs.TryGetValue(pair, out var many) ? many + 1 : 1;
        }

        return handoffs;
    }

    /// <summary>How far the handoffs reach with the lanes in this order, all told — what one order is judged against another by.</summary>
    private static int Reaching(List<string> order, IReadOnlyDictionary<(string From, string To), int> handoffs)
    {
        var reaching = 0;

        foreach (var (pair, many) in handoffs)
            reaching += many * Math.Abs(order.IndexOf(pair.From) - order.IndexOf(pair.To));

        return reaching;
    }

    /// <summary>The cell a link's end names: a node, or a subgraph where the id names one of those instead.</summary>
    private static DiagramCell? Ended(Plan plan, FlowchartDiagram diagram, string id)
    {
        if (plan.Named.TryGetValue(id, out var node)) return node.Cell;

        var group = diagram.Groups.FirstOrDefault(held => string.Equals(held.Id, id, StringComparison.Ordinal));
        return group is not null && plan.Groups.TryGetValue(group.Key, out var box) ? box.Cell : null;
    }

    /// <summary>
    /// Whether a way out of a decision is set beside it rather than under it — and so is the way back, where the two of
    /// them are a loop.
    ///
    /// <para>
    /// A loop that comes straight back is a pair of links, and the way back closes a cycle: ranking it as a step down puts
    /// what it leaves a rank below the decision, which drags the aside down with it and undoes the whole point of setting
    /// it aside. Both halves are flat or neither is.
    /// </para>
    /// </summary>
    private static bool Aside(Plan plan, FlowchartDiagram diagram, FlowchartLink link) =>
        Detour(plan, diagram, link)
        || diagram.Links.Any(other => string.Equals(other.From, link.To, StringComparison.Ordinal)
                                      && string.Equals(other.To, link.From, StringComparison.Ordinal)
                                      && Detour(plan, diagram, other));

    /// <summary>
    /// Whether a way out of a decision is the one set beside it rather than under it.
    ///
    /// <para>
    /// What people draw round a diamond is the four ways off it: what asks the question arrives above, the answer that
    /// carries the flow on goes below, and the rest go out to the sides. So a decision's ways out want the places round it,
    /// and only one of them can have the one underneath.
    /// </para>
    ///
    /// <para>
    /// The one that carries on is the one that does not come back: a way out leading round to the decision again is a
    /// detour off the flow, whatever it is called, and a way out that does not is the flow itself. Where every way out
    /// comes back, or only one leaves at all, there is nothing to choose between them and they all go below as before.
    /// </para>
    /// </summary>
    private static bool Detour(Plan plan, FlowchartDiagram diagram, FlowchartLink link)
    {
        if (plan.Named.TryGetValue(link.From, out var node) && node.Shape is not DiagramShape.Diamond) return false;

        var ways = diagram.Links.Where(other => string.Equals(other.From, link.From, StringComparison.Ordinal)).ToList();
        if (ways.Count < 2) return false;

        var back = ways.Where(one => Returns(diagram, one)).ToList();

        return back.Count < ways.Count && back.Contains(link);
    }

    /// <summary>Whether what a way out leads to leads round to where it came from.</summary>
    private static bool Returns(FlowchartDiagram diagram, FlowchartLink link)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { link.From };
        var going = new Queue<string>([link.To]);

        while (going.Count > 0)
        {
            var at = going.Dequeue();
            if (string.Equals(at, link.From, StringComparison.Ordinal)) return true;
            if (!seen.Add(at)) continue;

            foreach (var on in diagram.Links.Where(one => string.Equals(one.From, at, StringComparison.Ordinal)))
                going.Enqueue(on.To);
        }

        return false;
    }

    /// <summary>The subgraphs, each before the ones nested in it, so a nested one is measured after the box it sits in.</summary>
    private static IEnumerable<FlowchartGroup> Nested(FlowchartDiagram diagram, string? inside)
    {
        foreach (var group in diagram.Within(inside))
        {
            yield return group;
            foreach (var held in Nested(diagram, group.Key)) yield return held;
        }
    }

    /// <summary>
    /// Everything the chart means to draw, gathered so the whole of it — a link looping out beside a node included — is brought inside
    /// the box the block takes.
    /// </summary>
    private DiagramRoom Reached(FlowchartDiagram diagram, Plan plan)
    {
        var room = DiagramRoom.Round(diagram.Config.Padding, plan.Size,
                                     [.. plan.Nodes.Select(node => node.Cell), .. plan.Groups.Values.Select(box => box.Cell)],
                                     plan.Joins.Select(join => (join.Value, Says(join.Key))));

        // A lane is not a cell — it is the band its cells are laid out in, and its name is drawn at the near end of it.
        foreach (var lane in plan.Lanes) room.Reach(lane.Band.Bounds);

        return room;
    }

    /// <summary>What is written on a node: its label, or the words an <c>id@{ label: … }</c> line worked out for it.</summary>
    private IReadOnlyList<DiagramWords> Said(FlowchartNode node, double widest)
    {
        var ink = Ink.Written(node.Style.Colour) ?? Palette.Text;

        if (node.Worked is { Length: > 0 } worked) return [Worked(worked, node.WorkedPart, TextSize, ink)];

        return node.Said is null && node.SaidHole is null ? [] : Wrapped(node.Said, node.SaidHole, TextSize, ink, widest);
    }

    /// <summary>What is written on a link, where anything is.</summary>
    private IReadOnlyList<DiagramWords> Says(FlowchartLink link) =>
        link.Said is null && link.SaidHole is null
            ? []
            : Wrapped(link.Said, link.SaidHole, LabelSize, Ink.Written(link.Written.Colour) ?? Palette.Text, Widest);

    /// <summary>How wide what is written on a link runs before it wraps.</summary>
    private const double Widest = 160;

    // ── The links ───────────────────────────────────────────────────────────

    /// <summary>
    /// Where every link runs, once everything is placed: the layout's own route, its ends brought in to the edges of the shapes it
    /// joins, and the room what is written on it takes over the middle of the line.
    /// </summary>
    private List<Route> Routes(FlowchartDiagram diagram, Plan plan, DiagramRoom room)
    {
        var routes = new List<Route>();

        foreach (var link in diagram.Links)
        {
            if (!plan.Joins.TryGetValue(link, out var join) || join.Route.Count < 2) continue;

            var along = DiagramConnector.Trimmed(join, (cell, end, toward) => Edge(plan, cell, end, toward));
            var placed = along.Select(room.At).ToList();
            var said = Says(link);

            routes.Add(new Route(link, placed, said, DiagramConnector.Room(placed, said)));
        }

        return routes;
    }

    /// <summary>Where a line meets one of the diagram's shapes.</summary>
    private static Point Edge(Plan plan, DiagramCell cell, Point end, Point toward)
    {
        var shape = plan.Nodes.FirstOrDefault(node => ReferenceEquals(node.Cell, cell))?.Shape ?? DiagramShape.Rounded;

        // A diamond is a decision, and a decision is drawn met at its points rather than wherever a line crosses its slopes:
        // met on the slope, two lines leaving a decision read as one line forking off the middle of nothing in particular.
        return shape is DiagramShape.Diamond
            ? DiagramShapes.Cornered(shape, cell.Bounds, end, toward)
            : DiagramShapes.Edge(shape, cell.Bounds, toward, end);
    }

    /// <summary>The links, drawn over the chart.</summary>
    private void Links(LayoutBuilder build, IReadOnlyList<Route> routes, FlowchartConfig config)
    {
        if (routes.Count == 0) return;

        build.Open(FlowchartPiece.Links, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            // A link of tildes draws nothing at all: it only holds what it joins apart.
            if (!route.Link.Drawn) continue;

            var written = route.Link.Written;
            var stroke = DiagramConnector.Stroked(Ink.Written(written.Stroke) ?? Ink.Link, route.Link.Style,
                                                 written.StrokeWidth ?? 1, Thick, DiagramInk.Dashes(written.Dashes));

            // A link's own metadata says how it is curved, over whatever the front matter asks for every one of them.
            var curved = route.Link.Curve is { Length: > 0 } curve ? FlowchartConfig.Curving(curve) : config.Curved;

            DiagramConnector.Draw(build, FlowchartPiece.Link, route.Link.Part, route.Along, stroke,
                                  DiagramConnector.Headed(route.Link.Start), DiagramConnector.Headed(route.Link.End), curved);

            DiagramConnector.Says(build, FlowchartPiece.Label, route.Link.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>
    /// A subgraph: its box — or its band, where it is a lane — what is written on it, and the nodes it holds drawn inside its piece.
    /// </summary>
    private void Held(LayoutBuilder build, FlowchartDiagram diagram, Plan plan, DiagramRoom room, FlowchartGroup group,
                      IReadOnlyList<Geometry> over)
    {
        var lane = plan.Laned(group.Key);
        var words = lane?.Words ?? plan.Groups[group.Key].Words;
        var bounds = room.At(lane?.Band.Bounds ?? plan.Groups[group.Key].Cell.Bounds);
        var said = DiagramWords.Taken(words);
        var heading = new Rect(bounds.X + Boxed, bounds.Y + (Boxed / 3), Math.Max(0, bounds.Width - (Boxed * 2)), said.Height);

        var covered = DiagramShapes.United(
        [
            .. over,
            .. diagram.Within(group.Key).Select(nested => DiagramShapes.Outline(DiagramShape.Rounded, room.At(plan.Groups[nested.Key].Cell.Bounds))),
            .. Inside(diagram, plan, group.Key).Select(node => DiagramShapes.Outline(node.Shape, room.At(node.Cell.Bounds))),
        ]);

    // A lane stands for the whole of the subgraph that opened it, so what is drawn inside it sits inside what it stands for too.
        build.Open(FlowchartPiece.Group, lane is not null ? group.Whole : group.Part, stops: Stops.None);

        if (lane is not null) Banded(build, diagram, lane, bounds, room.At(lane.Band.Strip), covered);
        else
            DiagramShapes.Draw(build, FlowchartPiece.Holding, group.Part, DiagramShape.Rounded, bounds, Fill(group),
                               Stroke(Ink, group.Style, Ink.GroupEdge), DiagramWords.Placed(words, heading, MermaidPiece.Words), covered,
                               band: Ink.Band(Ink.Written(group.Style.Stroke)));

        foreach (var nested in diagram.Within(group.Key)) Held(build, diagram, plan, room, nested, over);
        foreach (var node in diagram.Inside(group.Key)) Drawn(build, plan, room, node, over);
        build.Close();
    }

    /// <summary>
    /// A lane: the band the work in it runs through, and the strip at the near end of the band with the lane's own name in it. Only the
    /// strip is filled, so the lanes are told apart without colouring over the work; both stand for the whole of the
    /// <c>subgraph … end</c> the lane was written as, so pressing anywhere in it that nothing else stands means the lane, and
    /// everything drawn in it stands for a stretch of what the lane itself stands for.
    /// </summary>
    private void Banded(LayoutBuilder build, FlowchartDiagram diagram, Lane lane, Rect bounds, Rect strip, Geometry covered)
    {
        var turned = diagram.Way is FlowchartWay.Right or FlowchartWay.Left;
        var stroke = Stroke(Ink, lane.Group.Style, Ink.GroupEdge);

        DiagramShapes.Draw(build, FlowchartPiece.Lane, lane.Group.Whole, DiagramShape.Rectangle, bounds, null, stroke, [],
                           DiagramShapes.United([covered, new RectangleGeometry(strip)]));

        DiagramShapes.Draw(build, FlowchartPiece.Title, lane.Group.Whole, DiagramShape.Rectangle, strip, Fill(lane.Group), stroke,
                           [.. Named(lane.Words, strip, turned)], covered, turned ? -90 : 0);
    }

    /// <summary>
    /// Where the lines of a lane's name go in the strip at the near end of its band: across the strip where the chart runs down the
    /// page, and turned a quarter turn to read up it where the chart runs across — anchored at their foot, reaching up by however wide
    /// they are, the lines side by side across the strip.
    /// </summary>
    private static IEnumerable<(DiagramWords Words, Point At, string Kind)> Named(IReadOnlyList<DiagramWords> words, Rect strip,
                                                                                 bool turned)
    {
        if (!turned)
        {
            foreach (var (line, at) in DiagramWords.Stack(words, strip)) yield return (line, at, MermaidPiece.Words);

            yield break;
        }

        var across = strip.X + ((strip.Width - words.Sum(line => line.Height)) / 2);

        foreach (var line in words)
        {
            yield return (line, new Point(across, strip.Y + ((strip.Height + line.Width) / 2)), MermaidPiece.Words);
            across += line.Height;
        }
    }

    /// <summary>One node: its shape, and what is written on it inside that shape.</summary>
    private void Drawn(LayoutBuilder build, Plan plan, DiagramRoom room, FlowchartNode node, IReadOnlyList<Geometry> over)
    {
        if (plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)) is not { } sized) return;

        var bounds = room.At(sized.Cell.Bounds);

        if (node.Picture is { } pictured)
        {
            Framed(build, sized, bounds, pictured);
            return;
        }

        var words = DiagramWords.Placed(sized.Words, DiagramShapes.Inside(sized.Shape, bounds), MermaidPiece.Words);

        // A node asked to be words alone is drawn as its words: nothing is filled or stroked round them.
        var plain = node.Shape == MermaidShape.Text;

        DiagramShapes.Draw(build, FlowchartPiece.Node, node.Part, sized.Shape, bounds,
                                                      plain ? null : Fill(node), plain ? null : Stroke(Ink, node.Style, Ink.NodeEdge), words, DiagramShapes.United(over),
                                                      acts: Answers(node));

                           Chipped(build, node.Id, bounds, node.Part, Shown(node.Said ?? node.Part));
    }

    /// <summary>How far a picture's label stands from it.</summary>
    private const double Caption = 4;

    /// <summary>How big an icon is drawn where nothing says, as Mermaid draws one.</summary>
    private const double IconSide = 40;

    /// <summary>How wide a picture is drawn at most where nothing says how big: its own size, brought down to this.</summary>
    private const double Broadest = 160;

    /// <summary>How much room a node drawn as a picture takes: the picture, and its label above or below it.</summary>
    private static Size Pictured(FlowchartPicture pictured, Size said)
    {
        var box = Framing(pictured);
        return new Size(Math.Max(box.Width, said.Width), box.Height + (said.Height > 0 ? Caption + said.Height : 0));
    }

    /// <summary>
    /// The size a picture is drawn at: the size it is asked for — kept to its own shape inside that where it is asked to be —
    /// or, asked for one side, the other to match; its own size where nothing is said, no wider than <see cref="Broadest"/>. An
    /// icon is square, as tall as it is asked to be.
    /// </summary>
    private static Size Framing(FlowchartPicture pictured)
    {
        if (pictured.Icon is not null)
        {
            var side = pictured.Height ?? pictured.Width ?? IconSide;
            return new Size(side, side);
        }

        var picture = Stages.WithDiagramPictures.Of(pictured.Written);
        var (wide, tall) = (picture?.Width ?? 0, picture?.Height ?? 0);
        var aspect = wide > 0 && tall > 0 ? wide / tall : 1;

        return (pictured.Width, pictured.Height) switch
        {
            ({ } across, { } down) when pictured.Keeps => across / down > aspect ? new Size(down * aspect, down) : new Size(across, across / aspect),
            ({ } across, { } down) => new Size(across, down),
            (null, { } down) => new Size(down * aspect, down),
            ({ } across, null) => new Size(across, across / aspect),
            _ when wide > 0 => wide > Broadest ? new Size(Broadest, Broadest / aspect) : new Size(wide, tall),
            _ => new Size(IconSide * 1.5, IconSide * 1.5),
        };
    }

    /// <summary>
    /// A node drawn as its picture or its icon, with its label above or below — standing in the picture, and meaning the node
    /// wherever it is pressed.
    /// </summary>
    private void Framed(LayoutBuilder build, Sized sized, Rect bounds, FlowchartPicture pictured)
    {
        var node = sized.Node;
        var box = Framing(pictured);
        var said = DiagramWords.Taken(sized.Words);
        var label = said.Height > 0 ? said.Height + Caption : 0;

        var frame = new Rect(bounds.X + ((bounds.Width - box.Width) / 2), pictured.Above ? bounds.Y + label : bounds.Y, box.Width, box.Height);
        var room = pictured.Above ? new Rect(bounds.X, bounds.Y, bounds.Width, said.Height) : new Rect(bounds.X, frame.Bottom + Caption, bounds.Width, said.Height);

        var stroke = Stroke(Ink, node.Style, Ink.NodeEdge);
        DiagramWords? asked = null;

        build.Open(FlowchartPiece.Node, node.Part, stops: Stops.None);
        if (Answers(node) is { } acts) build.Acts(acts);

        build.Open(MermaidPiece.Shape, node.Part, stops: Stops.None);

        if (pictured.Icon is { } icon)
        {
            // Stood in its form where it is given one — a square, a circle, a rounded square — and on nothing where it is not.
            if (pictured.Form is { } form)
                build.Draw(new GeometryMark(DiagramShapes.Outline(form switch { "circle" => DiagramShape.Circle, "rounded" => DiagramShape.Rounded, _ => DiagramShape.Rectangle }, frame),
                                            Fill(node), stroke?.Ink, stroke?.Thickness ?? 0));

            var inner = new Rect(frame.X + (frame.Width * 0.2), frame.Y + (frame.Height * 0.2), frame.Width * 0.6, frame.Height * 0.6);
            var ink = Ink.Written(node.Style.Colour) ?? Palette.Text;

            // The icons this draws, drawn; any other a question mark, which is what Mermaid draws for an icon it has no pack for.
            if (Architecture.ArchitectureIcons.Picture(icon, inner) is { } drawn) build.Draw(new GeometryMark(drawn, null, ink, 1.5));
            else asked = Worked("?", null, inner.Height * 0.8, ink);
        }
        else if (Stages.WithDiagramPictures.Of(pictured.Written) is { } picture) build.Draw(new PictureMark(picture, frame));
        else build.Draw(new GeometryMark(new RectangleGeometry(frame), null, stroke?.Ink ?? Palette.TextMuted, 1) { Dashes = new DoubleCollection([4, 3]) });

        var stands = new RectangleGeometry(frame);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        asked?.Set(build, new Point(frame.X + ((frame.Width - asked.Width) / 2), frame.Y + ((frame.Height - asked.Height) / 2)), MermaidPiece.Words);

        foreach (var (words, at, kind) in DiagramWords.Placed(sized.Words, room, MermaidPiece.Words)) words.Set(build, at, kind);

        build.Close();
    }

    /// <summary>
    /// What a press on a node means: where a <c>click</c> line said it leads, and which node it is where anything is
    /// listening — an ordinary flowchart has no host following its selection, and an intent nobody reads is a table
    /// entry for nothing.
    /// </summary>
    private LayoutActions? Answers(FlowchartNode node)
    {
        LayoutIntent? click = node.Href is { Length: > 0 } href ? new LayoutIntent(LayoutVerbs.Navigate, href, node.Tip) : null;
        LayoutIntent? select = !Folds.IsEmpty && node.Id.Length > 0 ? new LayoutIntent(LayoutVerbs.Select, node.Id, node.Tip) : null;

        return click is null && select is null ? null : new LayoutActions { Click = click, Select = select };
    }

    private static IEnumerable<Sized> Inside(FlowchartDiagram diagram, Plan plan, string? group) =>
        diagram.Inside(group)
            .Select(node => plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)))
            .OfType<Sized>();

    // ── Colour and shape ────────────────────────────────────────────────────

    /// <summary>What a node is drawn as: the shape its brackets or its metadata say, and a box where nothing says one.</summary>
    private static DiagramShape Shaped(FlowchartNode node) =>
        node.Shape is MermaidShape.None or MermaidShape.Text ? DiagramShape.Rectangle : DiagramShapes.For(node.Shape);

    /// <summary>What a node is filled with: what its styling writes, and otherwise what every node is.</summary>
    private Brush Fill(FlowchartNode node)
    {
        var fill = Ink.Written(node.Style.Fill) ?? Ink.Node;

        return node.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What a subgraph's box is filled with: what its styling writes, and otherwise what every subgraph is.</summary>
    private Brush Fill(FlowchartGroup group)
    {
        var fill = Ink.Written(group.Style.Fill) ?? Ink.Group;

        return group.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What something is outlined in: what its styling writes, and otherwise <paramref name="usual"/>.</summary>
    private static DiagramStroke Stroke(DiagramInk ink, MermaidStyle style, Brush usual) =>
        new(ink.Written(style.Stroke) ?? usual, style.StrokeWidth ?? 1, DiagramInk.Dashes(style.Dashes));

    private static DiagramWay Towards(FlowchartWay way) => way switch
    {
        FlowchartWay.Up => DiagramWay.Up,
        FlowchartWay.Right => DiagramWay.Right,
        FlowchartWay.Left => DiagramWay.Left,
        _ => DiagramWay.Down,
    };

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>A node measured: what is written on it, the shape it is drawn as, and the cell the layout placed it in.</summary>
    private sealed class Sized(FlowchartNode node, IReadOnlyList<DiagramWords> words, DiagramShape shape)
    {
        public FlowchartNode Node { get; } = node;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public DiagramShape Shape { get; } = shape;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A subgraph measured: what is written at the top of it, and the cell the layout placed it in.</summary>
    private sealed class Box(FlowchartGroup group, IReadOnlyList<DiagramWords> words)
    {
        public FlowchartGroup Group { get; } = group;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A lane measured: what is written on it, where it comes among the lanes, and the band the layout gave it.</summary>
    private sealed class Lane(FlowchartGroup group, IReadOnlyList<DiagramWords> words, int number, DiagramLane band)
    {
        public FlowchartGroup Group { get; } = group;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        /// <summary>Where it comes among the lanes, counted from one, which is the band its cells name.</summary>
        public int Number { get; } = number;

        public DiagramLane Band { get; } = band;
    }

    /// <summary>A link worked out: where it runs, what is written on it, and the room those words take over the middle of it.</summary>
    private sealed record Route(FlowchartLink Link, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>Everything the chart was measured and laid out into.</summary>
    private sealed class Plan
    {
        public List<Sized> Nodes { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Box> Groups { get; } = new(StringComparer.Ordinal);

        /// <summary>The lanes, in the order their bands are set across the chart.</summary>
        public List<Lane> Lanes { get; } = [];

        public Dictionary<FlowchartLink, DiagramJoin> Joins { get; } = [];

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public DiagramSpill Spill { get; set; } = DiagramSpill.None;

        public Size Size { get; set; }

        /// <summary>The lane a subgraph is, or null where that subgraph is a box rather than a lane.</summary>
        public Lane? Laned(string? key) =>
            key is null ? null : Lanes.FirstOrDefault(lane => string.Equals(lane.Group.Key, key, StringComparison.Ordinal));
    }
}
