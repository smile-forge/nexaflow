using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.State;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.State;

/// <summary>The pieces a state diagram's layout is made of — its layers, and what is in them.</summary>
public static class StatePiece
{
    /// <summary>The diagram itself: the states, and the composite states they are gathered into.</summary>
    public const string States = "States";

    /// <summary>One state, standing for what was written for it.</summary>
    public const string State = "State";

    /// <summary>A composite state: the box, and the states it holds drawn inside its piece.</summary>
    public const string Group = "Group";

    /// <summary>A composite state's own box and what is written at the top of it, behind the states it holds.</summary>
    public const string Holding = "Holding";

    /// <summary>The line dividing two regions of a composite state, which run at the same time.</summary>
    public const string Divider = "Divider";

    /// <summary>A note written beside a state.</summary>
    public const string Note = "Note";

    /// <summary>The transitions, drawn over the diagram.</summary>
    public const string Steps = "Steps";

    /// <inheritdoc cref="Steps"/>
    public const string Step = "Step";

    /// <summary>What is written on a transition, over the middle of its line.</summary>
    public const string Label = "Label";
}

/// <summary>
/// Draws a <c>stateDiagram</c> — or a <c>stateDiagram-v2</c>, which Mermaid reads the same way. The states are laid out in ranks by
/// how far along the transitions reach them (<see cref="DiagramLayers"/>), each rank ordered so as few lines cross as can be
/// managed, and the whole thing runs the way a <c>direction</c> line asks.
///
/// <para>
/// <strong>A composite state holds its states in the layout.</strong> What is inside one is laid out in its own space — which is
/// what lets a <c>direction</c> line run it its own way — and drawn inside the composite state's piece, so pressing a state means
/// that state and pressing the room round it means the composite state holding it.
/// </para>
/// <para>
/// <strong>What a state is drawn as is what it is.</strong> A plain state is a box with its words in it; the dots a scope starts
/// and stops at are a filled circle and a ringed one; a fork and a join are bars; a choice is a diamond; and the <c>--</c> dividing
/// two regions is a line drawn the width of the composite state holding it.
/// </para>
/// </summary>
internal sealed class StateBuilder : MermaidBuilder<StateDiagram>
{
    /// <summary>How big what is written on a state is, and on a transition.</summary>
    private const double TextSize = 13;
    private const double LabelSize = 11.5;

    /// <summary>The least room a state takes, so a state with little to say is still a state to look at.</summary>
    private const double Short = 34;

    /// <summary>How wide across the dots a scope starts and stops at are drawn.</summary>
    private const double Dot = 18;

    /// <summary>How much of the ring the dot a diagram stops at fills.</summary>
    private const double Bullseye = 0.55;

    /// <summary>The least room a choice takes across, so a diamond with nothing in it is still a diamond.</summary>
    private const double Decides = 44;

    /// <summary>The clear air inside a state's shape, and inside a composite state's box.</summary>
    private const double Pad = 10;
    private const double Boxed = 14;

    /// <summary>How thick a fork's bar and a region divider are drawn.</summary>
    private const double Thick = 2;

    /// <summary>How wide what is written on a transition runs before it wraps.</summary>
    private const double Widest = 160;

    internal StateBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override StateDiagram Of(MermaidBlock block) => StateDiagram.Of(block);

    /// <inheritdoc/>
    protected override DiagramChart? Chart(StateDiagram diagram) =>
        new([.. diagram.Nodes.Select(node => node.Id)], [.. diagram.Steps.Select(step => (step.From, step.To))]);

    protected override Size Draw(StateDiagram diagram, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Nodes.Count == 0 && diagram.Groups.Count == 0) return AsWritten(build);

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The transitions are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var over = DiagramConnector.Covered(routes.Select(route => (route.Along, route.Room)), Thick);

        build.Open(StatePiece.States, part: null, stops: Stops.None);
        foreach (var group in diagram.Within(null)) Held(build, diagram, plan, room, group, over);
        foreach (var node in diagram.Inside(null)) Drawn(build, diagram, plan, room, node, over);
        foreach (var note in plan.Notes.Where(note => note.Group is null)) Noted(build, room, note, over);
        plan.Spill.Draw(build, room, Ink.Surface, new DiagramStroke(Palette.CodeBorder, 1, DiagramStroke.Dotted));
        build.Close();

        Steps(build, routes);

        return room.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    /// <summary>
    /// Everything measured and placed: a cell for each state, each composite state and each note, a join for each transition, and
    /// the layered layout run over the lot of them.
    /// </summary>
    private Plan Laid(StateDiagram diagram)
    {
        var plan = new Plan();
        var cells = new List<DiagramCell>();
        var towards = Towards(diagram.Way);

        foreach (var group in Nested(diagram, null))
        {
            var words = Naming(diagram, group);
            var said = DiagramWords.Taken(words);

            var box = new Box(group, words)
            {
                Cell = new DiagramCell(new Size(said.Width + (Boxed * 2), 0))
                {
                    Inside = Holder(plan, group.Parent, group.Region),
                    Way = group.Way is { } way ? Towards(way) : null,
                    Pad = Boxed,
                    Heading = said.Height > 0 ? said.Height + diagram.Config.TitleMargin + (Boxed / 2) : 0,
                },
            };

            plan.Groups[group.Key] = box;
            cells.Add(box.Cell);

            // A composite a -- divides holds its regions, each a box of its own with no outline, laid out the composite's way.
            var dividers = diagram.Inside(group.Key).Count(node => node.Shape == StateShape.Divider);
            if (dividers == 0) continue;

            plan.Regions[group.Key] =
            [
                .. Enumerable.Range(0, dividers + 1).Select(_ => new DiagramCell(new Size(1, 0))
                {
                    Inside = box.Cell,
                    Way = box.Cell.Way,
                    Pad = Boxed / 2,
                }),
            ];

            cells.AddRange(plan.Regions[group.Key]);
        }

        foreach (var node in diagram.Nodes)
        {
            // A state folded away is never given a cell, so the steps to it have no end to meet and a box holding
            // nothing else closes up rather than standing empty.
            if (!Draws(node.Id)) continue;

            // A divider is where one region ends and the next begins, which the regions' own boxes say — it takes no place of its own.
            if (node.Shape == StateShape.Divider) continue;

            var shape = Shaped(node);
            var words = Said(node, diagram.Config);
            var sized = new Sized(node, words, shape)
            {
                Cell = new DiagramCell(Around(node, shape, words, diagram.Config, towards))
                {
                    Inside = Holder(plan, node.Group, node.Region),
                },
            };

            plan.Nodes.Add(sized);
            plan.Named.TryAdd(node.Id, sized);
            cells.Add(sized.Cell);
        }

        foreach (var step in diagram.Steps)
        {
            if (Ended(plan, diagram, step.From) is not { } from || Ended(plan, diagram, step.To) is not { } to) continue;

            plan.Joins[step] = new DiagramJoin(from, to);
        }

        // A note is written beside the state it is about, so it is held in the same rank rather than after it.
        foreach (var note in diagram.Notes)
        {
            var words = Says(note, diagram.Config);
            var taken = DiagramWords.Taken(words);
            var beside = new Pinned(note, words, diagram.Find(note.Of)?.Group)
            {
                Cell = new DiagramCell(new Size(taken.Width + (Pad * 2), Math.Max(taken.Height + (Pad * 2), Short)))
                {
                    Inside = plan.Named.TryGetValue(note.Of, out var of) ? of.Cell.Inside : null,
                },
            };

            plan.Notes.Add(beside);
            cells.Add(beside.Cell);

            if (plan.Named.TryGetValue(note.Of, out var about))
                plan.Beside.Add(new DiagramJoin(about.Cell, beside.Cell, span: 0));
        }

        plan.Spill = Spilled(id => plan.Named.TryGetValue(id, out var sized) ? sized.Cell : null);
        cells.AddRange(plan.Spill.Cells);

        // With ports, as a flowchart is: each line meets a state at a place of its own, and two transitions each way between the same
        // two states open into a lens rather than lying one along the other.
        plan.Size = DiagramLayers.Lay(cells, [.. plan.Joins.Values, .. plan.Beside, .. plan.Spill.Joins], towards,
                                      diagram.Config.NodeSpacing, diagram.Config.RankSpacing, ports: true);

        return plan;
    }

    /// <summary>What a state written in a composite state is laid out inside: the region it is in, where the composite is divided, and the composite otherwise.</summary>
    private static DiagramCell? Holder(Plan plan, string? group, int region)
    {
        if (group is null || !plan.Groups.TryGetValue(group, out var box)) return null;

        return plan.Regions.TryGetValue(group, out var regions) ? regions[Math.Clamp(region, 1, regions.Count) - 1] : box.Cell;
    }

    /// <summary>The cell a transition's end names: a state, or the box of a composite state where the id names one of those.</summary>
    private static DiagramCell? Ended(Plan plan, StateDiagram diagram, string id)
    {
        if (plan.Named.TryGetValue(id, out var node)) return node.Cell;

        var group = diagram.Groups.FirstOrDefault(held => string.Equals(held.Id, id, StringComparison.Ordinal));
        return group is not null && plan.Groups.TryGetValue(group.Key, out var box) ? box.Cell : null;
    }

    /// <summary>How much room a state takes, which is what it is drawn as rather than what is written on it alone.</summary>
    private static Size Around(StateNode node, DiagramShape shape, IReadOnlyList<DiagramWords> words, StateConfig config,
                               DiagramWay way)
    {
        var down = way is DiagramWay.Down or DiagramWay.Up;

        return node.Shape switch
        {
            StateShape.Start or StateShape.Stop => new Size(Dot, Dot),
            StateShape.Fork or StateShape.Join => down
                ? new Size(config.ForkWidth, config.ForkHeight)
                : new Size(config.ForkHeight, config.ForkWidth),

            // A line dividing two regions takes a rank of its own and is drawn the width of the box holding it.
            StateShape.Divider => down ? new Size(1, config.DividerMargin * 2) : new Size(config.DividerMargin * 2, 1),
            _ => Taken(shape, words, node.Shape == StateShape.Choice ? Decides : config.LeastWidth),
        };
    }

    private static Size Taken(DiagramShape shape, IReadOnlyList<DiagramWords> words, double least)
    {
        var around = DiagramShapes.Around(shape, DiagramWords.Taken(words), Pad);

        return new Size(Math.Max(around.Width, least), Math.Max(around.Height, Short));
    }

    /// <summary>The composite states, each before the ones nested in it, so a nested one is measured after the box it sits in.</summary>
    private static IEnumerable<StateGroup> Nested(StateDiagram diagram, string? inside)
    {
        foreach (var group in diagram.Within(inside))
        {
            yield return group;
            foreach (var held in Nested(diagram, group.Key)) yield return held;
        }
    }

    /// <summary>
    /// Everything the diagram means to draw, gathered so the whole of it is brought inside the box the block takes.
    /// </summary>
    private DiagramRoom Reached(StateDiagram diagram, Plan plan) =>
        DiagramRoom.Round(diagram.Config.Padding, plan.Size,
                          [.. plan.Nodes.Select(node => node.Cell), .. plan.Notes.Select(note => note.Cell),
                           .. plan.Groups.Values.Select(box => box.Cell)],
                          plan.Joins.Select(join => (join.Value, Says(join.Key, diagram.Config))));

    /// <summary>
    /// What is written on a state: what it says, or what it is called where nothing else says anything. A fork or a join is a bar, and
    /// what it is called is only for the transitions to name it by — Mermaid writes nothing on one, and there is no room on it to.
    /// </summary>
    private IReadOnlyList<DiagramWords> Said(StateNode node, StateConfig config) =>
        node.Marker || node.Shape is StateShape.Divider or StateShape.Fork or StateShape.Join || (node.Said is null && node.SaidHole is null)
            ? []
            : Wrapped(node.Said, node.SaidHole, TextSize, Ink.Written(node.Style.Colour) ?? Palette.Text, config.Wrapping);

    /// <summary>What is written on a transition, where anything is.</summary>
    private IReadOnlyList<DiagramWords> Says(StateStep step, StateConfig config) =>
        step.Said is null && step.SaidHole is null
            ? []
            : Wrapped(step.Said, step.SaidHole, LabelSize, Palette.Text, Widest);

    /// <summary>What a note says, a line for each line it is written across.</summary>
    private IReadOnlyList<DiagramWords> Says(StateNote note, StateConfig config) =>
        note.Said.Count == 0 && note.SaidHole is null
            ? Wrapped(null, note.SaidHole, LabelSize, Palette.Text, config.Wrapping)
            : [.. note.Said.SelectMany(said => Wrapped(said, null, LabelSize, Palette.Text, config.Wrapping))];

    /// <summary>What is written at the top of a composite state.</summary>
    private IReadOnlyList<DiagramWords> Naming(StateDiagram diagram, StateGroup group) =>
        Wrapped(group.Said, group.SaidHole, TextSize, Ink.Written(group.Style.Colour) ?? Palette.Text, diagram.Config.Wrapping);

    // ── The transitions ─────────────────────────────────────────────────────

    /// <summary>Where every transition runs once everything is placed, its ends brought in to the shapes it joins.</summary>
    private List<Route> Routes(StateDiagram diagram, Plan plan, DiagramRoom room)
    {
        var routes = new List<Route>();

        foreach (var step in diagram.Steps)
        {
            if (!plan.Joins.TryGetValue(step, out var join) || join.Route.Count < 2) continue;

            var along = DiagramConnector.Trimmed(join, (cell, end, toward) => Edge(plan, cell, end, toward));
            var placed = along.Select(room.At).ToList();
            var said = Says(step, diagram.Config);

            routes.Add(new Route(step, placed, said, DiagramConnector.Room(placed, said)));
        }

        return routes;
    }

    /// <summary>Where a line meets one of the diagram's shapes, cast from the line's own end.</summary>
    private static Point Edge(Plan plan, DiagramCell cell, Point end, Point toward)
    {
        var shape = plan.Nodes.FirstOrDefault(node => ReferenceEquals(node.Cell, cell))?.Shape ?? DiagramShape.Rounded;

        return DiagramShapes.Edge(shape, cell.Bounds, toward, end);
    }

    /// <summary>The transitions, drawn over the diagram.</summary>
    private void Steps(LayoutBuilder build, IReadOnlyList<Route> routes)
    {
        if (routes.Count == 0) return;

        build.Open(StatePiece.Steps, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            DiagramConnector.Draw(build, StatePiece.Step, route.Step.Part, route.Along,
                                  new DiagramStroke(Ink.Link, 1), DiagramHead.None, DiagramHead.Arrow, curved: true);

            DiagramConnector.Says(build, StatePiece.Label, route.Step.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>A composite state: its box, what is written at the top of it, and the states it holds drawn inside its piece.</summary>
    private void Held(LayoutBuilder build, StateDiagram diagram, Plan plan, DiagramRoom room, StateGroup group,
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
            .. Inside(diagram, plan, group.Key).Select(node => DiagramShapes.Outline(node.Shape, room.At(node.Cell.Bounds))),
            .. Noting(plan, group.Key).Select(note => DiagramShapes.Outline(DiagramShape.Card, room.At(note.Cell.Bounds))),
        ]);

        build.Open(StatePiece.Group, group.Whole, stops: Stops.None);
        DiagramShapes.Draw(build, StatePiece.Holding, group.Part, DiagramShape.Rounded, bounds, Fill(group), Stroke(group.Style, Ink.GroupEdge),
                           DiagramWords.Placed(box.Words, heading, MermaidPiece.Words), covered,
                           band: Ink.Band(Ink.Written(group.Style.Stroke)));

        Divided(build, diagram, plan, room, group, bounds, heading.Bottom + (Boxed / 3));

        foreach (var nested in diagram.Within(group.Key)) Held(build, diagram, plan, room, nested, over);
        foreach (var node in diagram.Inside(group.Key)) Drawn(build, diagram, plan, room, node, over);
        foreach (var note in Noting(plan, group.Key)) Noted(build, room, note, over);
        build.Close();
    }

    /// <summary>One state: what it is drawn as, and what is written on it inside that.</summary>
    private void Drawn(LayoutBuilder build, StateDiagram diagram, Plan plan, DiagramRoom room, StateNode node,
                       IReadOnlyList<Geometry> over)
    {
        if (plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)) is not { } sized) return;

        var bounds = room.At(sized.Cell.Bounds);



        if (node.Shape == StateShape.Stop)
        {
            Stopped(build, node, bounds);
            return;
        }

        var words = DiagramWords.Placed(sized.Words, DiagramShapes.Inside(sized.Shape, bounds), MermaidPiece.Words);

        DiagramShapes.Draw(build, StatePiece.State, node.Part, sized.Shape, bounds, Fill(node), Stroke(node.Style, Ink.NodeEdge), words,
                                                      DiagramShapes.United(over));

                           Chipped(build, node.Id, bounds, node.Part);
    }

    /// <summary>The dot a diagram stops at: a ring with a dot filled in the middle of it, UML's bullseye, in the ink the start is filled with.</summary>
    private void Stopped(LayoutBuilder build, StateNode node, Rect bounds)
    {
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
        var radius = Math.Min(bounds.Width, bounds.Height) / 2;

        var ring = new EllipseGeometry(middle, radius - 1, radius - 1);
        var dot = new EllipseGeometry(middle, radius * Bullseye, radius * Bullseye);
        ring.Freeze();
        dot.Freeze();

        var ink = Ink.Written(node.Style.Fill) ?? Palette.Text;

        build.Open(StatePiece.State, node.Part, stops: Stops.None);
        build.Open(MermaidPiece.Shape, node.Part, stops: Stops.None);
        build.Draw(new GeometryMark(ring, Ink.Surface, Ink.Written(node.Style.Stroke) ?? ink, 1.5));
        build.Draw(new GeometryMark(dot, ink, null, 0));
        build.Occupies(ring);
        build.Close();
        build.Close();
    }

    /// <summary>
    /// The lines dividing a composite state's regions, dashed, each standing for the <c>--</c> that wrote it: in the air between one
    /// region and the next, and across the whole of the composite beside them — down it where the regions stand side by side, across
    /// it where they stand one above the other.
    /// </summary>
    private void Divided(LayoutBuilder build, StateDiagram diagram, Plan plan, DiagramRoom room, StateGroup group, Rect bounds, double top)
    {
        if (!plan.Regions.TryGetValue(group.Key, out var regions)) return;

        var dividers = diagram.Inside(group.Key).Where(node => node.Shape == StateShape.Divider).ToList();
        var boxes = regions.Select(region => room.At(region.Bounds)).ToList();
        var inner = new Rect(bounds.X, top, bounds.Width, Math.Max(0, bounds.Bottom - top));

        for (var at = 0; at + 1 < boxes.Count && at < dividers.Count; at++)
        {
            var (one, next) = (boxes[at], boxes[at + 1]);
            var beside = Math.Min(one.Bottom, next.Bottom) > Math.Max(one.Top, next.Top);

            var line = beside
                ? new LineGeometry(new Point((one.Right + next.Left) / 2, inner.Top), new Point((one.Right + next.Left) / 2, inner.Bottom))
                : new LineGeometry(new Point(inner.Left, (one.Bottom + next.Top) / 2), new Point(inner.Right, (one.Bottom + next.Top) / 2));
            line.Freeze();

            build.Open(StatePiece.Divider, dividers[at].Part, stops: Stops.None);
            build.Draw(new GeometryMark(line, null, Ink.GroupEdge, 1) { Dashes = DiagramStroke.Dashed });
            build.Occupies(DiagramConnector.Band([line.StartPoint, line.EndPoint], Thick * 2));
            build.Close();
        }
    }

    /// <summary>A note: what it says in a box of its own, beside the state it is about.</summary>
    private void Noted(LayoutBuilder build, DiagramRoom room, Pinned note, IReadOnlyList<Geometry> over)
    {
        var bounds = room.At(note.Cell.Bounds);
        var words = DiagramWords.Placed(note.Words, DiagramShapes.Inside(DiagramShape.Card, bounds), MermaidPiece.Words);

        DiagramShapes.Draw(build, StatePiece.Note, note.Note.Part, DiagramShape.Card, bounds, Ink.Note,
                           new DiagramStroke(Ink.NoteEdge, 1), words, DiagramShapes.United(over));
    }

    /// <summary>The notes drawn inside a composite state, which are the ones about the states it holds.</summary>
    private static IEnumerable<Pinned> Noting(Plan plan, string? group) =>
        plan.Notes.Where(note => string.Equals(note.Group, group, StringComparison.Ordinal));

    private static IEnumerable<Sized> Inside(StateDiagram diagram, Plan plan, string? group) =>
        diagram.Inside(group)
            .Select(node => plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)))
            .OfType<Sized>();

    // ── Colour and shape ────────────────────────────────────────────────────

    /// <summary>What a state is drawn as: the shape what it is says.</summary>
    private static DiagramShape Shaped(StateNode node) => node.Shape switch
    {
        StateShape.Start => DiagramShape.Circle,
        StateShape.Stop => DiagramShape.DoubleCircle,
        StateShape.Choice => DiagramShape.Diamond,
        StateShape.Fork or StateShape.Join or StateShape.Divider => DiagramShape.Rectangle,
        _ => DiagramShape.Rounded,
    };

    /// <summary>What a state is filled with: what its styling writes, and otherwise what every state is — a dot the ink's.</summary>
    private Brush Fill(StateNode node)
    {
        var fill = Ink.Written(node.Style.Fill)
                   ?? (node.Marker || node.Shape is StateShape.Fork or StateShape.Join ? Palette.Text : Ink.Node);

        return node.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What a composite state's box is filled with: what its styling writes, and otherwise what every composite is.</summary>
    private Brush Fill(StateGroup group)
    {
        var fill = Ink.Written(group.Style.Fill) ?? Ink.Group;

        return group.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What something is outlined in: what its styling writes, and otherwise <paramref name="usual"/>.</summary>
    private DiagramStroke Stroke(MermaidStyle style, Brush usual) =>
        new(Ink.Written(style.Stroke) ?? usual, style.StrokeWidth ?? 1, DiagramInk.Dashes(style.Dashes));

    private static DiagramWay Towards(StateWay way) => way switch
    {
        StateWay.Up => DiagramWay.Up,
        StateWay.Right => DiagramWay.Right,
        StateWay.Left => DiagramWay.Left,
        _ => DiagramWay.Down,
    };

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>A state measured: what is written on it, what it is drawn as, and the cell the layout placed it in.</summary>
    private sealed class Sized(StateNode node, IReadOnlyList<DiagramWords> words, DiagramShape shape)
    {
        public StateNode Node { get; } = node;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public DiagramShape Shape { get; } = shape;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A composite state measured.</summary>
    private sealed class Box(StateGroup group, IReadOnlyList<DiagramWords> words)
    {
        public StateGroup Group { get; } = group;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A note measured, held in the rank of the state it is about and drawn inside whatever holds that state.</summary>
    private sealed class Pinned(StateNote note, IReadOnlyList<DiagramWords> words, string? group)
    {
        public StateNote Note { get; } = note;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        /// <summary>The composite state the state it is about is in, or null for one about a state outside them all.</summary>
        public string? Group { get; } = group;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A transition worked out: where it runs, what is written on it, and the room those words take over its middle.</summary>
    private sealed record Route(StateStep Step, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>Everything the diagram was measured and laid out into.</summary>
    private sealed class Plan
    {
        public List<Sized> Nodes { get; } = [];

        public List<Pinned> Notes { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Box> Groups { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The regions of each composite state a <c>--</c> divides, in the order they are written: each laid out on its own inside the
        /// composite, so its own dots and states keep to it.
        /// </summary>
        public Dictionary<string, List<DiagramCell>> Regions { get; } = new(StringComparer.Ordinal);

        public Dictionary<StateStep, DiagramJoin> Joins { get; } = [];

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public DiagramSpill Spill { get; set; } = DiagramSpill.None;

        /// <summary>The joins that hold a note beside the state it is about, which nothing draws.</summary>
        public List<DiagramJoin> Beside { get; } = [];

        public Size Size { get; set; }
    }
}
