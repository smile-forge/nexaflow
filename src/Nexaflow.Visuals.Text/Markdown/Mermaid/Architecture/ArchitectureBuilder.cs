using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;

/// <summary>The pieces an architecture diagram's layout is made of — its layers, and what is in them.</summary>
public static class ArchitecturePiece
{
    /// <summary>Everything the architecture is made of, which is the diagram itself.</summary>
    public const string Parts = "Parts";

    /// <summary>A group: a box holding whatever is put in it, which are the pieces inside it.</summary>
    public const string Group = "Group";

    /// <summary>A group's own box and what is written on it, behind the services it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>A service: its icon, and what is written under it.</summary>
    public const string Service = "Service";

    /// <summary>A junction: a place edges meet, drawn as a dot.</summary>
    public const string Junction = "Junction";

    /// <summary>The picture drawn over a service, or the words standing in for one nobody has.</summary>
    public const string Icon = "Icon";

    /// <summary>The edges, drawn over everything they join.</summary>
    public const string Edges = "Edges";

    /// <inheritdoc cref="Edges"/>
    public const string Edge = "Edge";

    /// <summary>What is written on an edge, over the middle of it.</summary>
    public const string Label = "Label";
}

/// <summary>
/// Draws an <c>architecture-beta</c> block. The sides its edges leave by say where everything sits
/// (<see cref="ArchitectureDiagram.Places"/>), so the diagram is a grid: a column for each place across, a row for each
/// place down, every cell as big as what is in it, and a group a box round whatever was put in it.
///
/// <para>
/// <strong>A group holds its services in the layout.</strong> They are drawn inside its piece rather than beside it, so
/// pressing a service means that service and pressing the room round it means the group holding it — and a group stands
/// only where its own services do not cover it. The edges are a layer of their own over all of it, since an edge may join
/// two services in different groups, or the groups themselves.
/// </para>
/// <para>
/// Mermaid keeps one neighbour per side of a service, so three services reaching a fourth the same way land two deep and
/// <c>align</c> exists to separate them. Here they all land in one cell and the cell spreads them — across, or down where
/// an <c>align column</c> says so — which is what <c>align</c> was asking for.
/// </para>
/// </summary>
internal sealed class ArchitectureBuilder : MermaidBuilder<ArchitectureDiagram>
{
    /// <summary>How big what is written under a service is, and how wide it runs before it wraps.</summary>
    private const double TextSize = 12;
    private const double Widest = 130;

    /// <summary>The clear air between an icon and the words under it.</summary>
    private const double Snug = 4;

    /// <summary>How big a junction's dot is.</summary>
    private const double Dot = 5;

    /// <summary>The room at the top of a group for what is written on it, and its own clear air where the front matter asks for none.</summary>
    private const double Header = 19;
    private const double Air = 12;

    /// <summary>The least clear air between two services, and how thick an edge is drawn.</summary>
    private const double Least = 8;
    private const double Thick = 1.4;

    /// <summary>How solid a group's background is, over the colour it takes from the series.</summary>
    private const double Wash = 0.14;

    internal ArchitectureBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override ArchitectureDiagram Of(MermaidBlock block) => ArchitectureDiagram.Of(block);

    protected override Size Draw(ArchitectureDiagram diagram, LayoutBuilder build)
    {
        // Nothing to draw yet is the source: what the reader wants back is their own lines.
        if (diagram.Services.Count == 0) return AsWritten(build);

        var sized = diagram.Services.Select(service => Sizing(diagram, service)).ToList();
        var boxes = Place(diagram, sized);
        var groups = Boxes(diagram, sized);

        // The edges are worked out before anything is drawn, because a group under one of them does not stand where it runs.
        var routes = Routes(diagram, boxes, groups);
        var over = DiagramConnector.Covered(routes.Select(route => (route.Along, route.Room)), Thick);

        build.Open(ArchitecturePiece.Parts, part: null, stops: Stops.None);

        foreach (var group in diagram.Groups.Where(group => group.In is null)) Holds(build, diagram, sized, groups, group, over);
        foreach (var service in sized.Where(service => service.Service.In is null)) Drawn(build, service);

        build.Close();
        Edges(build, routes);

        var reach = boxes.Values.Concat(groups.Values).Aggregate(Rect.Empty, Rect.Union);
        return new Size(reach.Right, reach.Bottom);
    }

    // ── How much room it all takes ──────────────────────────────────────────

    /// <summary>What a service needs: its icon, and the words under it wrapped to the width they run to.</summary>
    private Sized Sizing(ArchitectureDiagram diagram, ArchitectureService service)
    {
        var icon = service.Junction ? Dot * 2 : diagram.Config.IconSize;
        var said = service.Junction
            ? []
            : Says(service.Said, service.SaidHole, diagram.Config.FontSize ?? TextSize, Palette.Text, Widest);

        var words = DiagramWords.Taken(said);

        return new Sized(service, said)
        {
            Natural = new Size(Math.Max(icon, words.Width), icon + (words.Height > 0 ? Snug + words.Height : 0)),
        };
    }

    /// <summary>
    /// Where everything goes: a column for each place across and a row for each place down, every one of them as big as the
    /// largest cell in it, with room left round the edges of a grid for the groups that start and end there.
    /// </summary>
    private Dictionary<string, Rect> Place(ArchitectureDiagram diagram, IReadOnlyList<Sized> sized)
    {
        var config = diagram.Config;
        var gap = Math.Max(Least, config.NodeSeparation - config.IconSize);
        var apart = Math.Max(Least, (config.EdgeLength * config.IconSize) - config.IconSize);
        var pad = config.Padding ?? Air;

        // Everything sharing a cell is spread along the cell's own way round, so nothing is drawn on top of anything.
        var cells = new Dictionary<ArchitecturePlace, List<Sized>>();
        var loose = 0;

        foreach (var service in sized)
        {
            // Something no edge reaches and nothing named is still written, so it is given a column after everything else.
            var place = diagram.Places.TryGetValue(service.Service.Id, out var known)
                ? known
                : new ArchitecturePlace(diagram.Places.Values.DefaultIfEmpty().Max(each => each.Column) + ++loose, 0);

            if (!cells.TryGetValue(place, out var sharing)) cells[place] = sharing = [];
            sharing.Add(service);
        }

        var columns = cells.Keys.Select(place => place.Column).Distinct().Order().ToList();
        var rows = cells.Keys.Select(place => place.Row).Distinct().Order().ToList();

        var wide = columns.ToDictionary(column => column,
                                        column => cells.Where(cell => cell.Key.Column == column).Max(cell => Taken(diagram, cell.Value, apart).Width));
        var tall = rows.ToDictionary(row => row,
                                     row => cells.Where(cell => cell.Key.Row == row).Max(cell => Taken(diagram, cell.Value, apart).Height));

        // A group needs room of its own round what is in it: its padding on every side, and the header it is written on.
        var (left, right) = Room(diagram, sized, place => place.Column, pad, pad);
        var (top, foot) = Room(diagram, sized, place => place.Row, pad + Header, pad);

        var at = Across(columns, wide, left, right, gap);
        var down = Across(rows, tall, top, foot, gap);
        var boxes = new Dictionary<string, Rect>(StringComparer.Ordinal);

        foreach (var (place, sharing) in cells)
        {
            var room = new Rect(at[place.Column], down[place.Row], wide[place.Column], tall[place.Row]);
            var taken = Taken(diagram, sharing, apart);
            var along = diagram.Spread(sharing[0].Service.Id) == ArchitectureAxis.Column;

            var next = along ? room.Y + ((room.Height - taken.Height) / 2) : room.X + ((room.Width - taken.Width) / 2);

            foreach (var service in sharing.OrderBy(service => service.Service.Order))
            {
                var size = service.Natural;

                service.Bounds = along
                    ? new Rect(room.X + ((room.Width - size.Width) / 2), next, size.Width, size.Height)
                    : new Rect(next, room.Y + ((room.Height - size.Height) / 2), size.Width, size.Height);

                next += (along ? size.Height : size.Width) + apart;
                boxes.TryAdd(service.Service.Id, service.Bounds);
            }
        }

        return boxes;
    }

    /// <summary>How much room a cell's services take, spread the way the cell spreads them.</summary>
    private static Size Taken(ArchitectureDiagram diagram, IReadOnlyList<Sized> sharing, double apart)
    {
        var along = diagram.Spread(sharing[0].Service.Id) == ArchitectureAxis.Column;
        var gaps = apart * (sharing.Count - 1);

        return along
            ? new Size(sharing.Max(service => service.Natural.Width), sharing.Sum(service => service.Natural.Height) + gaps)
            : new Size(sharing.Sum(service => service.Natural.Width) + gaps, sharing.Max(service => service.Natural.Height));
    }

    /// <summary>
    /// The extra room a row or a column needs on each side of it: one group's worth for every group starting or ending
    /// there, since a group starting where another does is a group inside that one.
    /// </summary>
    private static (Dictionary<int, double> Before, Dictionary<int, double> After) Room(
        ArchitectureDiagram diagram, IReadOnlyList<Sized> sized, Func<ArchitecturePlace, int> which, double before, double after)
    {
        var starts = new Dictionary<int, double>();
        var ends = new Dictionary<int, double>();

        foreach (var group in diagram.Groups)
        {
            var places = sized.Where(service => Inside(diagram, service.Service.In, group.Id))
                .Select(service => diagram.Places.TryGetValue(service.Service.Id, out var at) ? which(at) : (int?)null)
                .OfType<int>()
                .ToList();

            if (places.Count == 0) continue;

            starts[places.Min()] = starts.GetValueOrDefault(places.Min()) + before;
            ends[places.Max()] = ends.GetValueOrDefault(places.Max()) + after;
        }

        return (starts, ends);
    }

    /// <summary>Where each row or column starts, once everything before it and the room round it is counted.</summary>
    private static Dictionary<int, double> Across(IReadOnlyList<int> lines, IReadOnlyDictionary<int, double> sizes,
                                                  IReadOnlyDictionary<int, double> before, IReadOnlyDictionary<int, double> after, double gap)
    {
        var at = new Dictionary<int, double>();
        var next = 0.0;

        foreach (var line in lines)
        {
            next += before.GetValueOrDefault(line);
            at[line] = next;
            next += sizes[line] + after.GetValueOrDefault(line) + gap;
        }

        return at;
    }

    /// <summary>Whether something put in <paramref name="inside"/> is in <paramref name="group"/>, however deep it sits.</summary>
    private static bool Inside(ArchitectureDiagram diagram, string? inside, string group)
    {
        for (var at = inside; at is not null; at = diagram.Group(at)?.In)
            if (string.Equals(at, group, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>The box round each group: everything put in it, and the clear air and the header it is written on.</summary>
    private Dictionary<string, Rect> Boxes(ArchitectureDiagram diagram, IReadOnlyList<Sized> sized)
    {
        var pad = diagram.Config.Padding ?? Air;
        var boxes = new Dictionary<string, Rect>(StringComparer.Ordinal);

        // Innermost first, so a group round a group is drawn round the box that one came to.
        foreach (var group in diagram.Groups.OrderByDescending(group => Depth(diagram, group.Id)))
        {
            var held = sized.Where(service => Inside(diagram, service.Service.In, group.Id)).Select(service => service.Bounds)
                .Concat(diagram.Groups.Where(inner => inner.In == group.Id && boxes.ContainsKey(inner.Id)).Select(inner => boxes[inner.Id]))
                .ToList();

            if (held.Count == 0) continue;

            var round = held.Aggregate(Rect.Empty, Rect.Union);
            boxes[group.Id] = new Rect(round.X - pad, round.Y - pad - Header, round.Width + (pad * 2), round.Height + (pad * 2) + Header);
        }

        return boxes;
    }

    private static int Depth(ArchitectureDiagram diagram, string? group)
    {
        var deep = 0;
        for (var at = diagram.Group(group ?? string.Empty)?.In; at is not null; at = diagram.Group(at)?.In) deep++;

        return deep;
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>A group: its box, what is written at the top of it, and everything put in it drawn inside its piece.</summary>
    private void Holds(LayoutBuilder build, ArchitectureDiagram diagram, IReadOnlyList<Sized> sized,
                       IReadOnlyDictionary<string, Rect> groups, ArchitectureGroup group, IReadOnlyList<Geometry> over)
    {
        if (!groups.TryGetValue(group.Id, out var box)) return;

        var inner = diagram.Groups.Where(child => child.In == group.Id).ToList();
        var held = sized.Where(service => service.Service.In == group.Id).ToList();

    var covered = DiagramShapes.United([
            .. over,
            .. inner.Where(child => groups.ContainsKey(child.Id)).Select(child => (Geometry)new RectangleGeometry(groups[child.Id])),
            .. held.Select(service => (Geometry)new RectangleGeometry(service.Bounds)),
        ]);

        var said = Says(group.Said, group.SaidHole, diagram.Config.FontSize ?? TextSize, Palette.Text, box.Width);
        var heading = new Rect(box.X + 6, box.Y + 2, Math.Max(0, box.Width - 12), DiagramWords.Taken(said).Height);

        build.Open(ArchitecturePiece.Group, group.Part, stops: Stops.None);
        DiagramShapes.Draw(build, ArchitecturePiece.Holding, group.Part, DiagramShape.Rounded, box, Ink.Group,
                           new DiagramStroke(Ink.GroupEdge, 1, DiagramStroke.Dashed),
                           DiagramWords.Placed(said, heading, MermaidPiece.Words, TextAlignment.Left), covered, band: Ink.Band(null));

        foreach (var child in inner) Holds(build, diagram, sized, groups, child, over);
        foreach (var service in held) Drawn(build, service);

        build.Close();
    }

    /// <summary>A service: the picture over it and the words under it — or, for a junction, the dot edges meet at.</summary>
    private void Drawn(LayoutBuilder build, Sized sized)
    {
        var service = sized.Service;
        var bounds = sized.Bounds;

        if (service.Junction)
        {
            var dot = new EllipseGeometry(new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)), Dot, Dot);
            dot.Freeze();

            build.Open(ArchitecturePiece.Junction, service.Part, stops: Stops.None);
            build.Draw(new GeometryMark(dot, Palette.TextMuted, null, 0));
            build.Occupies(dot);
            build.Close();
            return;
        }

        var said = DiagramWords.Taken(sized.Words);
        var room = new Rect(bounds.X + ((bounds.Width - (bounds.Height - (said.Height > 0 ? Snug + said.Height : 0))) / 2), bounds.Y,
                            bounds.Height - (said.Height > 0 ? Snug + said.Height : 0), bounds.Height - (said.Height > 0 ? Snug + said.Height : 0));

        build.Open(ArchitecturePiece.Service, service.Part, stops: Stops.None);
        Pictured(build, service, room);

        foreach (var (words, at, kind) in DiagramWords.Placed(sized.Words, new Rect(bounds.X, room.Bottom + Snug, bounds.Width, said.Height), MermaidPiece.Words))
            words.Set(build, at, kind);

        build.Close();
    }

    /// <summary>The picture drawn over a service — or what its icon is called, where it is one nobody here draws.</summary>
    private void Pictured(LayoutBuilder build, ArchitectureService service, Rect room)
    {
    // One colour for every service, because nothing written says one differs from another; a group takes a colour of its own
        // because groups nest, and one opening inside another has to be told from it.
        var ink = Palette.Accent;

        build.Open(ArchitecturePiece.Icon, service.Part, stops: Stops.None);

        if (ArchitectureIcons.Picture(service.Icon?.Text, room) is { } picture)
        {
            build.Draw(new GeometryMark(picture, DiagramInk.Faded(ink, Wash * 2), ink, 1.3));
        }
        else
        {
            var box = DiagramShapes.Outline(DiagramShape.Rounded, room);
            build.Draw(new GeometryMark(box, DiagramInk.Faded(ink, Wash * 2), ink, 1.3));

            if (service.Icon is { Length: > 0 } icon)
            {
                var said = Written(icon, hole: null, TextSize - 2, ink);
                said.Set(build, new Point(room.X + ((room.Width - said.Width) / 2), room.Y + ((room.Height - said.Height) / 2)), MermaidPiece.Words);
            }
        }

        build.Occupies(DiagramShapes.Outline(DiagramShape.Rectangle, room));
        build.Close();
    }

    // ── The edges ───────────────────────────────────────────────────────────

    /// <summary>
    /// Where every edge runs: from the side of one end it names to the side of the other, straight where those two face one
    /// another and round a single corner where they do not, with what is written on it over the middle of the line.
    /// </summary>
    private List<Route> Routes(ArchitectureDiagram diagram, IReadOnlyDictionary<string, Rect> boxes, IReadOnlyDictionary<string, Rect> groups)
    {
        var routes = new List<Route>();

        foreach (var edge in diagram.Edges)
        {
            if (End(diagram, boxes, groups, edge.From, edge.FromGroup) is not { } from) continue;
            if (End(diagram, boxes, groups, edge.To, edge.ToGroup) is not { } to) continue;

            var start = Anchor(from, edge.FromSide);
            var stop = Anchor(to, edge.ToSide);
            var along = Corner(start, edge.FromSide, stop, edge.ToSide) is { } corner ? new[] { start, corner, stop } : [start, stop];

            var said = edge.Said is null && edge.SaidHole is null
                ? []
                : Says(edge.Said, edge.SaidHole, (diagram.Config.FontSize ?? TextSize) - 1, Palette.Text, Widest);

            routes.Add(new Route(edge, along, said, DiagramConnector.Room(along, said)));
        }

        return routes;
    }

    /// <summary>The edges, drawn over everything they join.</summary>
    private void Edges(LayoutBuilder build, IReadOnlyList<Route> routes)
    {
        if (routes.Count == 0) return;

        build.Open(ArchitecturePiece.Edges, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            DiagramConnector.Draw(build, ArchitecturePiece.Edge, route.Edge.Part, route.Along, new DiagramStroke(Ink.Link, Thick),
                                  route.Edge.StartHead ? DiagramHead.Arrow : DiagramHead.None,
                                  route.Edge.EndHead ? DiagramHead.Arrow : DiagramHead.None);

            DiagramConnector.Says(build, ArchitecturePiece.Label, route.Edge.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    /// <summary>What an edge's end is drawn against: the service it names, or the group that service is in where it says so.</summary>
    private static Rect? End(ArchitectureDiagram diagram, IReadOnlyDictionary<string, Rect> boxes,
                             IReadOnlyDictionary<string, Rect> groups, string id, bool outer)
    {
        if (outer && diagram.Service(id)?.In is { } inside && groups.TryGetValue(inside, out var group)) return group;

        return boxes.TryGetValue(id, out var box) ? box : null;
    }

    /// <summary>Where on a box an edge leaving by a side starts.</summary>
    private static Point Anchor(Rect box, ArchitectureSide side) => side switch
    {
        ArchitectureSide.Left => new Point(box.Left, box.Top + (box.Height / 2)),
        ArchitectureSide.Right => new Point(box.Right, box.Top + (box.Height / 2)),
        ArchitectureSide.Top => new Point(box.Left + (box.Width / 2), box.Top),
        _ => new Point(box.Left + (box.Width / 2), box.Bottom),
    };

    /// <summary>
    /// The corner an edge turns at, where the side it leaves by and the side it arrives at are not the same way round —
    /// out the way it leaves, then in the way it arrives. Null where the two face one another and the line is straight.
    /// </summary>
    private static Point? Corner(Point start, ArchitectureSide from, Point stop, ArchitectureSide to)
    {
        var (leaving, arriving) = (Sideways(from), Sideways(to));
        if (leaving == arriving) return null;

        return leaving ? new Point(stop.X, start.Y) : new Point(start.X, stop.Y);
    }

    private static bool Sideways(ArchitectureSide side) => side is ArchitectureSide.Left or ArchitectureSide.Right;

    /// <summary>An edge worked out: where it runs, what is written on it, and the room those words take over the middle of it.</summary>
    private sealed record Route(ArchitectureEdge Edge, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>A service measured: the words under it, the room it needs, and where it ended up.</summary>
    private sealed class Sized(ArchitectureService service, IReadOnlyList<DiagramWords> words)
    {
        public ArchitectureService Service { get; } = service;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public Size Natural { get; init; }

        public Rect Bounds { get; set; }
    }
}
