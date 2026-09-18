using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Architecture;

/// <summary>The side of something an edge leaves by, or arrives at.</summary>
public enum ArchitectureSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>Which cell of the grid something sits in, counting from the top left.</summary>
public readonly record struct ArchitecturePlace(int Column, int Row);

/// <summary>Which way a set of services sharing a cell is spread.</summary>
public enum ArchitectureAxis
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
public sealed record ArchitectureGroup(ContentPart Part, string Id, ContentPart? Said, ContentPart? Icon, string? In, int Order)
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
public sealed record ArchitectureService(
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
public sealed record ArchitectureEdge(
    ContentPart Part,
    string From,
    ArchitectureSide FromSide,
    bool FromGroup,
    string To,
    ArchitectureSide ToSide,
    bool ToGroup,
    bool StartHead,
    bool EndHead,
    ContentPart? Said)
{
    /// <summary>The hole standing where what is written on it goes, where holes were asked for and nothing is written yet.</summary>
    public ContentPart? SaidHole { get; init; }
}

/// <summary>The services that share a row or a column, in the order they are to be spread along it.</summary>
public sealed record ArchitectureAlignment(ContentPart Part, ArchitectureAxis Axis, IReadOnlyList<string> Members);

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
public sealed class ArchitectureDiagram
{
    private ArchitectureDiagram(MermaidBlock block, ArchitectureConfig config, IReadOnlyList<ArchitectureGroup> groups,
                                IReadOnlyList<ArchitectureService> services, IReadOnlyList<ArchitectureEdge> edges,
                                IReadOnlyList<ArchitectureAlignment> alignments)
    {
        Block = block;
        Config = config;
        Groups = groups;
        Services = services;
        Edges = edges;
        Alignments = alignments;
        Places = Placed(services, edges, alignments);
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    public static ArchitectureDiagram Read(string? block) => Of(MermaidParser.Read(block));

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static ArchitectureDiagram Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public ArchitectureConfig Config { get; }

    /// <summary>The groups, in the order they are declared.</summary>
    public IReadOnlyList<ArchitectureGroup> Groups { get; }

    /// <summary>The services and junctions, in the order they are declared.</summary>
    public IReadOnlyList<ArchitectureService> Services { get; }

    /// <summary>The edges, in the order they are written.</summary>
    public IReadOnlyList<ArchitectureEdge> Edges { get; }

    /// <summary>The <c>align</c> lines, in the order they are written.</summary>
    public IReadOnlyList<ArchitectureAlignment> Alignments { get; }

    /// <summary>Which cell of the grid each service sits in, worked out from the sides its edges leave by.</summary>
    public IReadOnlyDictionary<string, ArchitecturePlace> Places { get; }

    /// <summary>The service or junction an id names, or null where nothing is called that.</summary>
    public ArchitectureService? Service(string id) =>
        Services.FirstOrDefault(service => string.Equals(service.Id, id, StringComparison.Ordinal));

    /// <summary>The group an id names, or null where nothing is called that.</summary>
    public ArchitectureGroup? Group(string id) =>
        Groups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal));

    /// <summary>Which way the services sharing a cell are spread: across, unless an <c>align column</c> names them.</summary>
    public ArchitectureAxis Spread(string id) =>
        Alignments.LastOrDefault(alignment => alignment.Members.Contains(id, StringComparer.Ordinal))?.Axis ?? ArchitectureAxis.Row;

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the architecture's own stages.</summary>
    public static ArchitectureDiagram Of(MermaidBlock block)
    {
        var groups = new List<ArchitectureGroup>();
        var services = new List<ArchitectureService>();
        var edges = new List<ArchitectureEdge>();
        var alignments = new List<ArchitectureAlignment>();

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case ArchitectureKinds.Group:
                    groups.Add(new ArchitectureGroup(part, Named(part, ArchitectureRoles.Id), Words(part, ArchitectureRoles.Title) ?? Words(part, ArchitectureRoles.Id),
                                                     Words(part, ArchitectureRoles.Icon), In(part), groups.Count)
                    {
                        SaidHole = Hole(part, ArchitectureRoles.Title),
                    });
                    break;

                case ArchitectureKinds.Service or ArchitectureKinds.Junction:
                    services.Add(new ArchitectureService(part, Named(part, ArchitectureRoles.Id), Words(part, ArchitectureRoles.Title) ?? Words(part, ArchitectureRoles.Id),
                                                         Words(part, ArchitectureRoles.Icon), In(part),
                                                         part.Kind == ArchitectureKinds.Junction, services.Count)
                    {
                        SaidHole = Hole(part, ArchitectureRoles.Title),
                    });
                    break;

                case ArchitectureKinds.Edge when Edge(part) is { } edge:
                    edges.Add(edge);
                    break;

                case ArchitectureKinds.Align when Align(part) is { } alignment:
                    alignments.Add(alignment);
                    break;
            }
        }

        return new ArchitectureDiagram(block, ArchitectureConfig.Read(block.Config), groups, services, edges, alignments);
    }

    // ── Reading the lines ───────────────────────────────────────────────────

    private static ArchitectureEdge? Edge(ContentPart part)
    {
        var names = part.Children.Where(child => child.Kind == MermaidKinds.Name).ToList();
        var sides = part.Children.Where(child => child.Kind == MermaidKinds.Key && child.Role == ArchitectureRoles.Side).ToList();
        if (names.Count < 2 || sides.Count < 2) return null;

        var marks = part.Children.Where(child => child.Role == ArchitectureRoles.Group).Select(child => child.Start).ToList();
        var heads = part.Children.Where(child => child.Role == ArchitectureRoles.Head).Select(child => child.Text).ToList();
        var said = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);

        return new ArchitectureEdge(part,
                                    names[0].Words()?.Text ?? string.Empty, Side(sides[0].Text), marks.Any(at => at < sides[0].Start),
                                    names[1].Words()?.Text ?? string.Empty, Side(sides[1].Text), marks.Any(at => at > sides[1].Start),
                                    heads.Contains("<"), heads.Contains(">"),
                                    said.Words())
        {
            SaidHole = said?.Hole(),
        };
    }

    private static ArchitectureAlignment? Align(ContentPart part)
    {
        var axis = part.Children.FirstOrDefault(child => child.Role == ArchitectureRoles.Axis)?.Text;
        var members = part.Inner(MermaidKinds.Names).Named()
            .Select(name => name.Words()?.Text ?? string.Empty)
            .Where(member => member.Length > 0)
            .ToList();

        return members.Count == 0
            ? null
            : new ArchitectureAlignment(part,
                                        string.Equals(axis, ArchitectureGrammar.ColumnWord, StringComparison.OrdinalIgnoreCase)
                                            ? ArchitectureAxis.Column
                                            : ArchitectureAxis.Row,
                                        members);
    }

    private static ArchitectureSide Side(string said) => said.ToUpperInvariant() switch
    {
        "L" => ArchitectureSide.Left,
        "T" => ArchitectureSide.Top,
        "B" => ArchitectureSide.Bottom,
        _ => ArchitectureSide.Right,
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
    private static IReadOnlyDictionary<string, ArchitecturePlace> Placed(
        IReadOnlyList<ArchitectureService> services, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<ArchitectureAlignment> alignments)
    {
        var known = services.Select(service => service.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        var beside = services.Where(service => service.Id.Length > 0)
            .ToDictionary(service => service.Id, _ => new List<(ArchitectureSide From, ArchitectureSide To, string Id)>(), StringComparer.Ordinal);

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
                at[member] = alignment.Axis == ArchitectureAxis.Row ? (at[member].X, first.Y) : (first.X, at[member].Y);
        }

        if (at.Count == 0) return new Dictionary<string, ArchitecturePlace>(StringComparer.Ordinal);

        // The walk counts upwards, as Mermaid's does, so the rows are read back the other way up.
        var left = at.Values.Min(place => place.X);
        var top = at.Values.Max(place => place.Y);

        return at.ToDictionary(place => place.Key, place => new ArchitecturePlace(place.Value.X - left, top - place.Value.Y), StringComparer.Ordinal);
    }

    /// <summary>Everything reached from one service, laid out around it.</summary>
    private static Dictionary<string, (int X, int Y)> Walk(
        string start, IReadOnlyDictionary<string, List<(ArchitectureSide From, ArchitectureSide To, string Id)>> beside, HashSet<string> walked)
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
    private static (int X, int Y)? Shift((int X, int Y) at, ArchitectureSide from, ArchitectureSide to)
    {
        if (from == to) return null;

        var (across, down) = (Across(from), Across(to));

        return (across, down) switch
        {
            (true, false) => (at.X + (from == ArchitectureSide.Left ? -1 : 1), at.Y + (to == ArchitectureSide.Top ? -1 : 1)),
            (true, true) => (at.X + (from == ArchitectureSide.Left ? -1 : 1), at.Y),
            (false, true) => (at.X + (to == ArchitectureSide.Left ? 1 : -1), at.Y + (from == ArchitectureSide.Top ? 1 : -1)),
            _ => (at.X, at.Y + (from == ArchitectureSide.Top ? 1 : -1)),
        };
    }

    private static bool Across(ArchitectureSide side) => side is ArchitectureSide.Left or ArchitectureSide.Right;
}
