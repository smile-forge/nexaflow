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

/// <summary>
/// Draws an <c>architecture-beta</c> block. The sides its edges leave by say where everything sits
/// (<see cref="Diagram.Places"/>), so the diagram is a grid: a column for each place across, a row for each
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
internal sealed class ArchitectureBuilder : MermaidBuilder
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

    internal ArchitectureBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>The side of something an edge leaves by, or arrives at.</summary>
    private enum Side
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    /// <summary>Which cell of the grid something sits in, counting from the top left.</summary>
    private readonly record struct Spot(int Column, int Row);

    /// <summary>Which way a set of services sharing a cell is spread.</summary>
    private enum Axis
    {
        /// <summary>Across, which is how anything sharing a cell is spread unless an <c>align column</c> says otherwise.</summary>
        Row,
        Column,
    }

    /// <summary>
    /// A group, read: a box holding whatever is put in it, and put in a box of its own where it says so.
    /// </summary>
    /// <param name="Part">The whole line as it was written, which is what a press on the box means.</param>
    /// <param name="Said">The words drawn on it: what is written on it, or what it is called where nothing is.</param>
    /// <param name="Icon">The icon it is drawn with, as written.</param>
    /// <param name="In">The group it is put in, or null at the outside.</param>
    /// <param name="Order">Where it comes among the groups, which is the colour it takes.</param>
    private sealed record Group(ContentPart Part, string Id, ContentPart? Said, ContentPart? Icon, string? In, int Order)
    {
        /// <summary>The hole standing where what is written on it goes, where holes were asked for and nothing is written yet.</summary>
        public ContentPart? SaidHole { get; init; }
    }

    /// <summary>
    /// A service, read — or a junction, which is a service drawn as a dot for edges to meet at rather than as a box.
    /// </summary>
    /// <param name="Part">The whole line as it was written, which is what a press on it means.</param>
    /// <param name="Said">The words drawn under it: what is written on it, or what it is called where nothing is.</param>
    /// <param name="Icon">The icon drawn over it, as written.</param>
    /// <param name="In">The group it is put in, or null at the outside.</param>
    /// <param name="Order">Where it comes among the services, which is the order two sharing a cell are spread in.</param>
    private sealed record Service(
        ContentPart Part,
        string Id,
        ContentPart? Said,
        ContentPart? Icon,
        string? In,
        bool Junction,
        int Order)
    {
        /// <summary>The hole standing where what is written under it goes, where holes were asked for and nothing is written yet.</summary>
        public ContentPart? SaidHole { get; init; }
    }

    /// <summary>
    /// An edge, read: the two ends it joins, the side of each it leaves by, the heads it draws and what is written on it.
    /// </summary>
    /// <param name="FromGroup">Whether it leaves the group its service is in rather than the service — the <c>{group}</c> after it.</param>
    private sealed record Edge(
        ContentPart Part,
        string From,
        Side FromSide,
        bool FromGroup,
        string To,
        Side ToSide,
        bool ToGroup,
        bool StartHead,
        bool EndHead,
        ContentPart? Said)
    {
        /// <summary>The hole standing where what is written on it goes, where holes were asked for and nothing is written yet.</summary>
        public ContentPart? SaidHole { get; init; }
    }

    /// <summary>The services that share a row or a column, in the order they are to be spread along it.</summary>
    private sealed record Alignment(ContentPart Part, Axis Axis, IReadOnlyList<string> Members);

    /// <summary>
    /// An <c>architecture-beta</c> block, read: the groups, the services and junctions in them, the edges between them, and
    /// where all of it sits. Its title is the block's (<see cref="MermaidBlock.Title"/>).
    ///
    /// <para>
    /// <strong>An edge's sides are what say where things sit.</strong> <c>db:R -- L:server</c> means the server is to the right
    /// of the database, so walking the edges out from each service in turn lays the whole diagram on a grid — which is how
    /// Mermaid starts, before handing the result to a force-directed solver to settle. Here the walk is the answer, so the same
    /// diagram is drawn the same way every time.
    /// </para>
    /// </summary>
    private sealed class Diagram
    {
        private Diagram(ArchitectureConfig config, IReadOnlyList<Group> groups,
                                    IReadOnlyList<Service> services, IReadOnlyList<Edge> edges,
                                    IReadOnlyList<Alignment> alignments)
        {
            Config = config;
            Groups = groups;
            Services = services;
            Edges = edges;
            Alignments = alignments;
            Places = Placed(services, edges, alignments);
        }

        /// <summary>What the front matter asks for.</summary>
        public ArchitectureConfig Config { get; }

        /// <summary>The groups, in the order they are declared.</summary>
        public IReadOnlyList<Group> Groups { get; }

        /// <summary>The services and junctions, in the order they are declared.</summary>
        public IReadOnlyList<Service> Services { get; }

        /// <summary>The edges, in the order they are written.</summary>
        public IReadOnlyList<Edge> Edges { get; }

        /// <summary>The <c>align</c> lines, in the order they are written.</summary>
        public IReadOnlyList<Alignment> Alignments { get; }

        /// <summary>Which cell of the grid each service sits in, worked out from the sides its edges leave by.</summary>
        public IReadOnlyDictionary<string, Spot> Places { get; }

        /// <summary>The service or junction an id names, or null where nothing is called that.</summary>
        public Service? ServiceOf(string id) =>
            Services.FirstOrDefault(service => string.Equals(service.Id, id, StringComparison.Ordinal));

        /// <summary>The group an id names, or null where nothing is called that.</summary>
        public Group? GroupOf(string id) =>
            Groups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal));

        /// <summary>Which way the services sharing a cell are spread: across, unless an <c>align column</c> names them.</summary>
        public Axis Spread(string id) =>
            Alignments.LastOrDefault(alignment => alignment.Members.Contains(id, StringComparer.Ordinal))?.Axis ?? Axis.Row;

        /// <summary>The diagram as written, every line read in the order it is written.</summary>
        public static Diagram Of(ContentPart root, ArchitectureConfig config)
        {
            var groups = new List<Group>();
            var services = new List<Service>();
            var edges = new List<Edge>();
            var alignments = new List<Alignment>();

            foreach (var part in root.SelfAndDescendants())
            {
                switch (part.Kind)
                {
                    case ArchitectureKinds.Group:
                        groups.Add(new Group(part, Named(part, ArchitectureRoles.Id), Words(part, ArchitectureRoles.Title) ?? Words(part, ArchitectureRoles.Id),
                                                         Words(part, ArchitectureRoles.Icon), In(part), groups.Count)
                        {
                            SaidHole = Hole(part, ArchitectureRoles.Title),
                        });
                        break;

                    case ArchitectureKinds.Service or ArchitectureKinds.Junction:
                        services.Add(new Service(part, Named(part, ArchitectureRoles.Id), Words(part, ArchitectureRoles.Title) ?? Words(part, ArchitectureRoles.Id),
                                                             Words(part, ArchitectureRoles.Icon), In(part),
                                                             part.Kind == ArchitectureKinds.Junction, services.Count)
                        {
                            SaidHole = Hole(part, ArchitectureRoles.Title),
                        });
                        break;

                    case ArchitectureKinds.Edge when Edged(part) is { } edge:
                        edges.Add(edge);
                        break;

                    case ArchitectureKinds.Align when Aligned(part) is { } alignment:
                        alignments.Add(alignment);
                        break;
                }
            }

            return new Diagram(config, groups, services, edges, alignments);
        }

        // ── Reading the lines ───────────────────────────────────────────────────

        private static Edge? Edged(ContentPart part)
        {
            var names = part.Children.Where(child => child.Kind == MermaidKinds.Name).ToList();
            var sides = part.Children.Where(child => child.Kind == MermaidKinds.Key && child.Role == ArchitectureRoles.Side).ToList();
            if (names.Count < 2 || sides.Count < 2) return null;

            var marks = part.Children.Where(child => child.Role == ArchitectureRoles.Group).Select(child => child.Start).ToList();
            var heads = part.Children.Where(child => child.Role == ArchitectureRoles.Head).Select(child => child.Text).ToList();
            var said = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);

            return new Edge(part,
                                        names[0].Words()?.Text ?? string.Empty, Sided(sides[0].Text), marks.Any(at => at < sides[0].Start),
                                        names[1].Words()?.Text ?? string.Empty, Sided(sides[1].Text), marks.Any(at => at > sides[1].Start),
                                        heads.Contains("<"), heads.Contains(">"),
                                        said.Words())
            {
                SaidHole = said?.Hole(),
            };
        }

        private static Alignment? Aligned(ContentPart part)
        {
            var axis = part.Children.FirstOrDefault(child => child.Role == ArchitectureRoles.Axis)?.Text;
            var members = part.Inner(MermaidKinds.Names).Named()
                .Select(name => name.Words()?.Text ?? string.Empty)
                .Where(member => member.Length > 0)
                .ToList();

            return members.Count == 0
                ? null
                : new Alignment(part,
                                            string.Equals(axis, ArchitectureGrammar.ColumnWord, StringComparison.OrdinalIgnoreCase)
                                                ? Axis.Column
                                                : Axis.Row,
                                            members);
        }

        private static Side Sided(string said) => said.ToUpperInvariant() switch
        {
            "L" => Side.Left,
            "T" => Side.Top,
            "B" => Side.Bottom,
            _ => Side.Right,
        };

        private static string Named(ContentPart part, string role) => Words(part, role)?.Text ?? string.Empty;

        private static ContentPart? Words(ContentPart part, string role) =>
            part.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == MermaidKinds.Words && inner.Role == role);

        /// <summary>The hole standing in the brackets something is written in, where nothing is written there yet.</summary>
        private static ContentPart? Hole(ContentPart part, string role) =>
            part.Children
                .FirstOrDefault(child => child.Kind == MermaidKinds.Label
                                         && child.SelfAndDescendants().Any(inner => inner.Kind == MermaidKinds.Words && inner.Role == role))
                .Hole();

        private static string? In(ContentPart part) => Words(part, ArchitectureRoles.In) is { Length: > 0 } inside ? inside.Text : null;

        // ── Where everything sits ───────────────────────────────────────────────

        /// <summary>
        /// Which cell each service sits in. An edge's two sides say where its ends sit relative to one another — the right of one
        /// against the left of another puts the second to its right — so a walk out from each service in turn lays out everything
        /// it reaches. A diagram written in several unconnected pieces is walked once for each, and the pieces are laid side by
        /// side. An <c>align</c> line then pulls its members onto the row or the column of the first of them.
        /// </summary>
        private static IReadOnlyDictionary<string, Spot> Placed(
            IReadOnlyList<Service> services, IReadOnlyList<Edge> edges, IReadOnlyList<Alignment> alignments)
        {
            var known = services.Select(service => service.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
            var beside = services.Where(service => service.Id.Length > 0)
                .ToDictionary(service => service.Id, _ => new List<(Side From, Side To, string Id)>(), StringComparer.Ordinal);

            foreach (var edge in edges)
            {
                if (!known.Contains(edge.From) || !known.Contains(edge.To)) continue;

                beside[edge.From].Add((edge.FromSide, edge.ToSide, edge.To));
                beside[edge.To].Add((edge.ToSide, edge.FromSide, edge.From));
            }

            var at = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal);
            var walked = new HashSet<string>(StringComparer.Ordinal);

            foreach (var service in services.Where(service => service.Id.Length > 0))
            {
                if (walked.Contains(service.Id)) continue;

                // A piece of its own: laid out from nought, then moved clear of everything laid out before it.
                var piece = Walk(service.Id, beside, walked);
                var clear = at.Count == 0 ? 0 : at.Values.Max(place => place.X) + 2;
                var from = piece.Values.Min(place => place.X);

                foreach (var (id, place) in piece) at[id] = (place.X - from + clear, place.Y);
            }

            foreach (var alignment in alignments)
            {
                var members = alignment.Members.Where(at.ContainsKey).ToList();
                if (members.Count < 2) continue;

                var first = at[members[0]];
                foreach (var member in members.Skip(1))
                    at[member] = alignment.Axis == Axis.Row ? (at[member].X, first.Y) : (first.X, at[member].Y);
            }

            if (at.Count == 0) return new Dictionary<string, Spot>(StringComparer.Ordinal);

            // The walk counts upwards, as Mermaid's does, so the rows are read back the other way up.
            var left = at.Values.Min(place => place.X);
            var top = at.Values.Max(place => place.Y);

            return at.ToDictionary(place => place.Key, place => new Spot(place.Value.X - left, top - place.Value.Y), StringComparer.Ordinal);
        }

        /// <summary>Everything reached from one service, laid out around it.</summary>
        private static Dictionary<string, (int X, int Y)> Walk(
            string start, IReadOnlyDictionary<string, List<(Side From, Side To, string Id)>> beside, HashSet<string> walked)
        {
            var piece = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal) { [start] = (0, 0) };
            var queue = new Queue<string>([start]);

            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                walked.Add(id);

                foreach (var (from, to, next) in beside[id])
                {
                    if (piece.ContainsKey(next) || Shift(piece[id], from, to) is not { } place) continue;

                    piece[next] = place;
                    queue.Enqueue(next);
                }
            }

            return piece;
        }

        /// <summary>
        /// Where the far end of an edge sits, given the cell its near end is in and the sides the edge leaves and arrives by. The
        /// side it leaves by says where the far end is: leaving by the right puts it to the right, leaving by the top puts it
        /// above. The side it arrives at says the same thing the other way round: arriving at the far end's left means the edge
        /// comes from its left, so that end is to the right, and arriving at its top means the edge comes from above it, so that
        /// end is below. An edge leaving and arriving on the same side says nothing at all, and moves nothing.
        ///
        /// <para>
        /// Mermaid reads three of those four the same way and the fourth the other way round — an edge arriving at the top of a
        /// far end it also leaves sideways puts that end above rather than below — which is why a bend can come out the wrong way
        /// there. Reading all four the same way is what makes a bend a bend.
        /// </para>
        /// </summary>
        private static (int X, int Y)? Shift((int X, int Y) at, Side from, Side to)
        {
            if (from == to) return null;

            var (across, down) = (Across(from), Across(to));

            return (across, down) switch
            {
                (true, false) => (at.X + (from == Side.Left ? -1 : 1), at.Y + (to == Side.Top ? -1 : 1)),
                (true, true) => (at.X + (from == Side.Left ? -1 : 1), at.Y),
                (false, true) => (at.X + (to == Side.Left ? 1 : -1), at.Y + (from == Side.Top ? 1 : -1)),
                _ => (at.X, at.Y + (from == Side.Top ? 1 : -1)),
            };
        }

        private static bool Across(Side side) => side is Side.Left or Side.Right;
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = Diagram.Of(Reading.Root, Configured(ArchitectureConfig.Default));

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
    private Sized Sizing(Diagram diagram, Service service)
    {
        var icon = service.Junction ? Dot * 2 : diagram.Config.IconSize;
        var said = service.Junction
            ? []
            : Wrapped(service.Said, service.SaidHole, diagram.Config.FontSize ?? TextSize, Palette.Text, Widest);

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
    private Dictionary<string, Rect> Place(Diagram diagram, IReadOnlyList<Sized> sized)
    {
        var config = diagram.Config;
        var gap = Math.Max(Least, config.NodeSeparation - config.IconSize);
        var apart = Math.Max(Least, (config.EdgeLength * config.IconSize) - config.IconSize);
        var pad = config.Padding ?? Air;

        // Everything sharing a cell is spread along the cell's own way round, so nothing is drawn on top of anything.
        var cells = new Dictionary<Spot, List<Sized>>();
        var loose = 0;

        foreach (var service in sized)
        {
            // Something no edge reaches and nothing named is still written, so it is given a column after everything else.
            var place = diagram.Places.TryGetValue(service.Service.Id, out var known)
                ? known
                : new Spot(diagram.Places.Values.DefaultIfEmpty().Max(each => each.Column) + ++loose, 0);

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
            var along = diagram.Spread(sharing[0].Service.Id) == Axis.Column;

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
    private static Size Taken(Diagram diagram, IReadOnlyList<Sized> sharing, double apart)
    {
        var along = diagram.Spread(sharing[0].Service.Id) == Axis.Column;
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
        Diagram diagram, IReadOnlyList<Sized> sized, Func<Spot, int> which, double before, double after)
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
    private static bool Inside(Diagram diagram, string? inside, string group)
    {
        for (var at = inside; at is not null; at = diagram.GroupOf(at)?.In)
            if (string.Equals(at, group, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>The box round each group: everything put in it, and the clear air and the header it is written on.</summary>
    private Dictionary<string, Rect> Boxes(Diagram diagram, IReadOnlyList<Sized> sized)
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

    private static int Depth(Diagram diagram, string? group)
    {
        var deep = 0;
        for (var at = diagram.GroupOf(group ?? string.Empty)?.In; at is not null; at = diagram.GroupOf(at)?.In) deep++;

        return deep;
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>A group: its box, what is written at the top of it, and everything put in it drawn inside its piece.</summary>
    private void Holds(LayoutBuilder build, Diagram diagram, IReadOnlyList<Sized> sized,
                       IReadOnlyDictionary<string, Rect> groups, Group group, IReadOnlyList<Geometry> over)
    {
        if (!groups.TryGetValue(group.Id, out var box)) return;

        var inner = diagram.Groups.Where(child => child.In == group.Id).ToList();
        var held = sized.Where(service => service.Service.In == group.Id).ToList();

    var covered = DiagramShapes.United([
            .. over,
            .. inner.Where(child => groups.ContainsKey(child.Id)).Select(child => (Geometry)new RectangleGeometry(groups[child.Id])),
            .. held.Select(service => (Geometry)new RectangleGeometry(service.Bounds)),
        ]);

        var said = Wrapped(group.Said, group.SaidHole, diagram.Config.FontSize ?? TextSize, Palette.Text, box.Width);
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

    /// <summary>The icon drawn over a service — or what its icon is called, where it is one the app does not draw.</summary>
    private void Pictured(LayoutBuilder build, Service service, Rect room)
    {
        // One colour for every service, because nothing written says one differs from another; a group takes a colour of its own
        // because groups nest, and one opening inside another has to be told from it.
        var ink = Palette.Accent;

        build.Open(ArchitecturePiece.Icon, service.Part, stops: Stops.None);
        build.Draw(new GeometryMark(DiagramShapes.Outline(DiagramShape.Rounded, room), DiagramInk.Faded(ink, Wash * 2), ink, 1.3));

        if (Iconed(service.Icon, room.Height * 0.6, ink) is { } icon)
            icon.Set(build, new Point(room.X + ((room.Width - icon.Width) / 2), room.Y + ((room.Height - icon.Height) / 2)), MermaidPiece.Glyph);
        else if (service.Icon is { Length: > 0 } named)
        {
            var said = Written(named, hole: null, TextSize - 2, ink);
            said.Set(build, new Point(room.X + ((room.Width - said.Width) / 2), room.Y + ((room.Height - said.Height) / 2)), MermaidPiece.Words);
        }

        build.Occupies(DiagramShapes.Outline(DiagramShape.Rectangle, room));
        build.Close();
    }

    // ── The edges ───────────────────────────────────────────────────────────

    /// <summary>
    /// Where every edge runs: from the side of one end it names to the side of the other, straight where those two face one
    /// another and round a single corner where they do not, with what is written on it over the middle of the line.
    /// </summary>
    private List<Route> Routes(Diagram diagram, IReadOnlyDictionary<string, Rect> boxes, IReadOnlyDictionary<string, Rect> groups)
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
                : Wrapped(edge.Said, edge.SaidHole, (diagram.Config.FontSize ?? TextSize) - 1, Palette.Text, Widest);

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
    private static Rect? End(Diagram diagram, IReadOnlyDictionary<string, Rect> boxes,
                             IReadOnlyDictionary<string, Rect> groups, string id, bool outer)
    {
        if (outer && diagram.ServiceOf(id)?.In is { } inside && groups.TryGetValue(inside, out var group)) return group;

        return boxes.TryGetValue(id, out var box) ? box : null;
    }

    /// <summary>Where on a box an edge leaving by a side starts.</summary>
    private static Point Anchor(Rect box, Side side) => side switch
    {
        Side.Left => new Point(box.Left, box.Top + (box.Height / 2)),
        Side.Right => new Point(box.Right, box.Top + (box.Height / 2)),
        Side.Top => new Point(box.Left + (box.Width / 2), box.Top),
        _ => new Point(box.Left + (box.Width / 2), box.Bottom),
    };

    /// <summary>
    /// The corner an edge turns at, where the side it leaves by and the side it arrives at are not the same way round —
    /// out the way it leaves, then in the way it arrives. Null where the two face one another and the line is straight.
    /// </summary>
    private static Point? Corner(Point start, Side from, Point stop, Side to)
    {
        var (leaving, arriving) = (Sideways(from), Sideways(to));
        if (leaving == arriving) return null;

        return leaving ? new Point(stop.X, start.Y) : new Point(start.X, stop.Y);
    }

    private static bool Sideways(Side side) => side is Side.Left or Side.Right;

    /// <summary>An edge worked out: where it runs, what is written on it, and the room those words take over the middle of it.</summary>
    private sealed record Route(Edge Edge, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>A service measured: the words under it, the room it needs, and where it ended up.</summary>
    private sealed class Sized(Service service, IReadOnlyList<DiagramWords> words)
    {
        public Service Service { get; } = service;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public Size Natural { get; init; }

        public Rect Bounds { get; set; }
    }
}
