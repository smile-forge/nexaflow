using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.State;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.State;

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
internal sealed class StateBuilder : MermaidBuilder
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

    internal StateBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>What a state is drawn as.</summary>
    private enum Form
    {
        /// <summary>A box, with what is written on it inside.</summary>
        Plain,

        /// <summary>The filled dot its scope starts at.</summary>
        Start,

        /// <summary>The ringed dot its scope stops at.</summary>
        Stop,

        /// <summary>The bar work forks from.</summary>
        Fork,

        /// <summary>The bar work joins at.</summary>
        Join,

        /// <summary>The diamond a choice between paths is drawn as.</summary>
        Choice,

        /// <summary>The line dividing two regions of a composite state, which run at the same time.</summary>
        Divider,
    }

    /// <summary>One state: where it was first written, what it is called, what is drawn on it, and what it is drawn as.</summary>
    /// <param name="part">The state as it was first named, which is what a press on it means.</param>
    /// <param name="group">The key of the composite state it was first written in, or null for one written outside them all.</param>
    /// <param name="region">
    /// Which region of that composite it is in, counted from one: a <c>--</c> line divides a composite state into regions that run
    /// at the same time, and what is written after one is in the next. A divider is in the region it closes.
    /// </param>
    private sealed class Node(ContentPart part, string id, string? group, int region)
    {
        public ContentPart Part { get; } = part;

        /// <summary>What it is called, which is what a transition, a <c>class</c>, a <c>style</c> and a note name it by.</summary>
        public string Id { get; } = id;

        public string? Group { get; } = group;

        public int Region { get; } = region;

        public Form Form { get; set; }

        /// <summary>The words drawn on it: what is written on it, or what it is called where nothing else says anything.</summary>
        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        public MermaidStyle Style { get; set; } = MermaidStyle.None;

        /// <summary>Whether it is one of the dots a scope starts and stops at, which hold no words and are drawn small.</summary>
        public bool Marker => Form is Form.Start or Form.Stop;
    }

    /// <summary>One transition: the states it joins and what is written on it.</summary>
    private sealed record Step(ContentPart Part, string From, string To, ContentPart? Said, ContentPart? SaidHole);

    /// <summary>
    /// One composite state: the box drawn round every state first written inside it, what is written at the top of it, the
    /// composite state it is itself inside, and the way its own states are laid out where a <c>direction</c> line says one.
    /// </summary>
    /// <param name="key">Where it stands among the composite states written, which is what a state says it is inside.</param>
    /// <param name="id">What it is called, which a transition and a <c>style</c> line name it by.</param>
    private sealed class Group(ContentPart part, string key, string id, string? parent, int region)
    {
        public ContentPart Part { get; } = part;

        public string Key { get; } = key;

        public string Id { get; } = id;

        public string? Parent { get; } = parent;

        public int Region { get; } = region;

        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        /// <summary>
        /// The whole of it as it was written, from the line that opened it through the <c>}</c> that closed it — the opening line
        /// alone, where nothing closed it.
        /// </summary>
        public ISourcePart Whole { get; init; } = default(SourceSpan);

        public DiagramWay? Way { get; set; }

        public MermaidStyle Style { get; set; } = MermaidStyle.None;
    }

    /// <summary>One note: the state it is written beside, and what it says, a part for each line it is written across.</summary>
    private sealed record Note(ContentPart Part, string Of, IReadOnlyList<ContentPart> Said, ContentPart? SaidHole);

    /// <summary>
    /// What the block writes, read down the tree in the order it is written. A state written twice is one state: the second writing
    /// says more about the one the first made — what is drawn on it, what it is drawn as — rather than making another, which is what
    /// lets a transition name the states a line above wrote. A state belongs to the composite state it was first written in, and a
    /// name the stages say is a composite's is drawn as that composite's box rather than as a state of its own.
    /// </summary>
    private sealed class Diagram
    {
        private readonly Dictionary<string, Node> known = new(StringComparer.Ordinal);

        /// <summary>What each name is styled with, said on the one first writing it — a <c>[*]</c>'s by the dot it is.</summary>
        private readonly Dictionary<string, MermaidStyle> styles = new(StringComparer.Ordinal);

        private Diagram(StateConfig config) => Config = config;

        public StateConfig Config { get; }

        /// <summary>The way the whole diagram is laid out.</summary>
        public DiagramWay Way { get; private set; } = DiagramWay.Down;

        /// <summary>The states, in the order they are first written.</summary>
        public List<Node> Nodes { get; } = [];

        public List<Step> Steps { get; } = [];

        /// <summary>The composite states, each before the ones nested in it.</summary>
        public List<Group> Groups { get; } = [];

        public List<Note> Notes { get; } = [];

        public static Diagram Of(ContentPart root, StateConfig config)
        {
            var diagram = new Diagram(config);
            diagram.Read(root, null);

            foreach (var node in diagram.Nodes) node.Style = diagram.styles.GetValueOrDefault(node.Id, MermaidStyle.None);
            foreach (var group in diagram.Groups) group.Style = diagram.styles.GetValueOrDefault(group.Id, MermaidStyle.None);

            return diagram;
        }

        public Node? Find(string id) => id.Length == 0 ? null : known.GetValueOrDefault(id);

        /// <summary>The states written inside a composite state — those written outside them all, for null.</summary>
        public IEnumerable<Node> Inside(string? group) =>
            Nodes.Where(node => string.Equals(node.Group, group, StringComparison.Ordinal));

        /// <summary>The composite states opened inside one — the outermost ones, for null.</summary>
        public IEnumerable<Group> Within(string? group) =>
            Groups.Where(nested => string.Equals(nested.Parent, group, StringComparison.Ordinal));

        /// <summary>Everything written in one scope — the block, or one composite state — counting the regions its dividers make.</summary>
        private void Read(ContentPart holder, Group? inside)
        {
            var scope = inside?.Key;
            var region = 1;

            foreach (var part in holder.Children)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    if (part.Children.FirstOrDefault()?.Stated() is { Kind: StateKinds.Opens } opens)
                        Read(part, Opened(part, opens, scope, region));

                    continue;
                }

                if (part.Stated() is not { } stated) continue;
                Styled(stated);

                switch (stated.Kind)
                {
                    case StateKinds.State:
                        Said(stated, scope, region);
                        break;

                    case StateKinds.Transition:
                        Stepped(stated, scope, region);
                        break;

                    case StateKinds.Concurrent:
                        Divided(stated, scope, region++);
                        break;

                    case StateKinds.Note:
                        Notes.Add(Noted(stated));
                        break;

                    case StateKinds.Direction when Wayward(Setting(stated, StateRoles.Towards)) is { } way:
                        if (inside is null) Way = way;
                        else inside.Way = way;
                        break;
                }
            }
        }

        /// <summary>What each name a line writes is styled with, where it is the first writing of that name.</summary>
        private void Styled(ContentPart stated)
        {
            foreach (var named in stated.SelfAndDescendants().Where(part => part.Kind == StateKinds.Named))
                if (Name(named) is { } name && ((named.Node as MarkerNode)?.Id ?? name.Words()?.Text) is { Length: > 0 } id)
                    styles.TryAdd(id, StyleOf(name));
        }

        /// <summary>A state written on its own: what is drawn on it, and what it is drawn as.</summary>
        private void Said(ContentPart stated, string? scope, int region)
        {
            if (stated.Inner(StateKinds.Named) is not { } named || Gathered(named, scope, region) is not { } node) return;

            node.Said = Words(stated, StateKinds.Said) ?? Words(stated, MermaidKinds.Quoted) ?? node.Said;
            node.SaidHole = Hole(stated, StateKinds.Said) ?? Hole(stated, MermaidKinds.Quoted) ?? node.SaidHole;

            if (Setting(stated, StateRoles.Kind) is { Length: > 0 } drawn) node.Form = Formed(drawn);
        }

        /// <summary>A transition: the states either side of it, and what is written on it.</summary>
        private void Stepped(ContentPart stated, string? scope, int region)
        {
            var named = stated.Children.Where(child => child.Kind == StateKinds.Named).ToList();
            if (named.Count < 2) return;

            var from = Ended(named[0], scope, region);
            var to = Ended(named[1], scope, region);
            if (from is null || to is null) return;

            Steps.Add(new Step(stated, from, to, Words(stated, StateKinds.Said), Hole(stated, StateKinds.Said)));
        }

        /// <summary>What one end of a transition names: a composite state by its id, and otherwise a state, made where it is new.</summary>
        private string? Ended(ContentPart named, string? scope, int region) =>
            named.Node is GroupReferenceNode ? Name(named)?.Words()?.Text : Gathered(named, scope, region)?.Id;

        /// <summary>
        /// The state a name says, made where it has not been written before: a <c>[*]</c> is the dot the stages said it is, and a name
        /// a composite state is called by is no state at all.
        /// </summary>
        private Node? Gathered(ContentPart named, string? scope, int region)
        {
            if (named.Node is GroupReferenceNode || Name(named)?.Words() is not { } words) return null;

            var marker = named.Node as MarkerNode;
            var id = marker?.Id ?? words.Text;

            if (!known.TryGetValue(id, out var node))
            {
                node = new Node(named, id, scope, region)
                {
                    Form = marker is null ? Form.Plain : marker.Stop ? Form.Stop : Form.Start,
                    Said = marker is null ? words : null,
                };

                Nodes.Add(node);
                known[id] = node;
            }

            return node;
        }

        /// <summary>The line dividing two regions of a composite state, which is a state of the layout and nothing else.</summary>
        private void Divided(ContentPart stated, string? scope, int region)
        {
            var id = $"--@{Nodes.Count}";
            var node = new Node(stated, id, scope, region) { Form = Form.Divider };

            Nodes.Add(node);
            known[id] = node;
        }

        /// <summary>A composite state opening: known by where it stands among them, as a name the stages point at it says.</summary>
        private Group Opened(ContentPart group, ContentPart opens, string? parent, int region)
        {
            Styled(opens);

            var name = Name(opens.Inner(StateKinds.Named));
            var closing = group.Children[^1].Stated() is { Kind: StateKinds.Ends } ends ? ends : opens;

            var held = new Group(opens, Groups.Count.ToString(CultureInfo.InvariantCulture), name.Words()?.Text ?? string.Empty, parent, region)
            {
                // A composite state written with what is on it first is called by its id and drawn with those words.
                Said = Words(opens, MermaidKinds.Quoted) ?? name.Words(),
                SaidHole = Hole(opens, MermaidKinds.Quoted),
                Whole = new SourceSpan(opens.Start, closing.End - opens.Start),
            };

            Groups.Add(held);
            return held;
        }

        /// <summary>A note: the state it is beside, and what it says.</summary>
        private static Note Noted(ContentPart stated)
        {
            var opens = stated.Inner(StateKinds.NoteOpens) ?? stated;
            var name = Name(opens.Inner(StateKinds.Named) ?? stated.Inner(StateKinds.Named));

            var said = new List<ContentPart>();
            if (Words(opens, StateKinds.Said) is { } one) said.Add(one);
            if (Words(stated, MermaidKinds.Quoted) is { } floating) said.Add(floating);

            foreach (var text in stated.SelfAndDescendants().Where(part => part.Kind == StateKinds.NoteText))
                if (text.Words() is { } words) said.Add(words);

            return new Note(stated, name.Words()?.Text ?? string.Empty, said, Hole(opens, StateKinds.Said) ?? Hole(stated, MermaidKinds.Quoted));
        }

        private static ContentPart? Name(ContentPart? named) => named?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

        private static ContentPart? Words(ContentPart stated, string kind) => stated.Inner(kind)?.Words();

        private static ContentPart? Hole(ContentPart stated, string kind) => stated.Inner(kind)?.Hole();

        private static string? Setting(ContentPart stated, string role) =>
            stated.SelfAndDescendants().FirstOrDefault(part => part.Kind == MermaidKinds.Setting && part.Role == role)?.Text;

        private static Form Formed(string drawn) => drawn.ToLowerInvariant() switch
        {
            "fork" => Form.Fork,
            "join" => Form.Join,
            "choice" => Form.Choice,
            _ => Form.Plain,
        };

        /// <summary>Which way a word lays a diagram out, or null where it lays it out no way at all.</summary>
        private static DiagramWay? Wayward(string? said) => said?.ToUpperInvariant() switch
        {
            "TB" or "TD" => DiagramWay.Down,
            "BT" => DiagramWay.Up,
            "LR" => DiagramWay.Right,
            "RL" => DiagramWay.Left,
            _ => null,
        };
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = Diagram.Of(Reading.Root, Configured(StateConfig.Default));

        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Nodes.Count == 0 && diagram.Groups.Count == 0) return AsWritten(build);

        // How much of it is drawn, worked out before anything is placed.
        Fold(new DiagramChart([.. diagram.Nodes.Select(node => node.Id)], [.. diagram.Steps.Select(step => (step.From, step.To))]));

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
    private Plan Laid(Diagram diagram)
    {
        var plan = new Plan();
        var cells = new List<DiagramCell>();
        var towards = diagram.Way;

        foreach (var group in Nested(diagram, null))
        {
            var words = Naming(diagram, group);
            var said = DiagramWords.Taken(words);

            var box = new Box(group, words)
            {
                Cell = new DiagramCell(new Size(said.Width + (Boxed * 2), 0))
                {
                    Inside = Holder(plan, group.Parent, group.Region),
                    Way = group.Way,
                    Pad = Boxed,
                    Heading = said.Height > 0 ? said.Height + diagram.Config.TitleMargin + (Boxed / 2) : 0,
                },
            };

            plan.Groups[group.Key] = box;
            cells.Add(box.Cell);

            // A composite a -- divides holds its regions, each a box of its own with no outline, laid out the composite's way.
            var dividers = diagram.Inside(group.Key).Count(node => node.Form == Form.Divider);
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
            if (node.Form == Form.Divider) continue;

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
    private static DiagramCell? Ended(Plan plan, Diagram diagram, string id)
    {
        if (plan.Named.TryGetValue(id, out var node)) return node.Cell;

        var group = diagram.Groups.FirstOrDefault(held => string.Equals(held.Id, id, StringComparison.Ordinal));
        return group is not null && plan.Groups.TryGetValue(group.Key, out var box) ? box.Cell : null;
    }

    /// <summary>How much room a state takes, which is what it is drawn as rather than what is written on it alone.</summary>
    private static Size Around(Node node, DiagramShape shape, IReadOnlyList<DiagramWords> words, StateConfig config,
                               DiagramWay way)
    {
        var down = way is DiagramWay.Down or DiagramWay.Up;

        return node.Form switch
        {
            Form.Start or Form.Stop => new Size(Dot, Dot),
            Form.Fork or Form.Join => down
                ? new Size(config.ForkWidth, config.ForkHeight)
                : new Size(config.ForkHeight, config.ForkWidth),

            // A line dividing two regions takes a rank of its own and is drawn the width of the box holding it.
            Form.Divider => down ? new Size(1, config.DividerMargin * 2) : new Size(config.DividerMargin * 2, 1),
            _ => Taken(shape, words, node.Form == Form.Choice ? Decides : config.LeastWidth),
        };
    }

    private static Size Taken(DiagramShape shape, IReadOnlyList<DiagramWords> words, double least)
    {
        var around = DiagramShapes.Around(shape, DiagramWords.Taken(words), Pad);

        return new Size(Math.Max(around.Width, least), Math.Max(around.Height, Short));
    }

    /// <summary>The composite states, each before the ones nested in it, so a nested one is measured after the box it sits in.</summary>
    private static IEnumerable<Group> Nested(Diagram diagram, string? inside)
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
    private DiagramRoom Reached(Diagram diagram, Plan plan) =>
        DiagramRoom.Round(diagram.Config.Padding, plan.Size,
                          [.. plan.Nodes.Select(node => node.Cell), .. plan.Notes.Select(note => note.Cell),
                           .. plan.Groups.Values.Select(box => box.Cell)],
                          plan.Joins.Select(join => (join.Value, Says(join.Key, diagram.Config))));

    /// <summary>
    /// What is written on a state: what it says, or what it is called where nothing else says anything. A fork or a join is a bar, and
    /// what it is called is only for the transitions to name it by — Mermaid writes nothing on one, and there is no room on it to.
    /// </summary>
    private IReadOnlyList<DiagramWords> Said(Node node, StateConfig config) =>
        node.Marker || node.Form is Form.Divider or Form.Fork or Form.Join || (node.Said is null && node.SaidHole is null)
            ? []
            : Wrapped(node.Said, node.SaidHole, TextSize, Ink.Written(node.Style.Colour) ?? Palette.Text, config.Wrapping);

    /// <summary>What is written on a transition, where anything is.</summary>
    private IReadOnlyList<DiagramWords> Says(Step step, StateConfig config) =>
        step.Said is null && step.SaidHole is null
            ? []
            : Wrapped(step.Said, step.SaidHole, LabelSize, Palette.Text, Widest);

    /// <summary>What a note says, a line for each line it is written across.</summary>
    private IReadOnlyList<DiagramWords> Says(Note note, StateConfig config) =>
        note.Said.Count == 0 && note.SaidHole is null
            ? Wrapped(null, note.SaidHole, LabelSize, Palette.Text, config.Wrapping)
            : [.. note.Said.SelectMany(said => Wrapped(said, null, LabelSize, Palette.Text, config.Wrapping))];

    /// <summary>What is written at the top of a composite state.</summary>
    private IReadOnlyList<DiagramWords> Naming(Diagram diagram, Group group) =>
        Wrapped(group.Said, group.SaidHole, TextSize, Ink.Written(group.Style.Colour) ?? Palette.Text, diagram.Config.Wrapping);

    // ── The transitions ─────────────────────────────────────────────────────

    /// <summary>Where every transition runs once everything is placed, its ends brought in to the shapes it joins.</summary>
    private List<Route> Routes(Diagram diagram, Plan plan, DiagramRoom room)
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
    private void Held(LayoutBuilder build, Diagram diagram, Plan plan, DiagramRoom room, Group group,
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
    private void Drawn(LayoutBuilder build, Diagram diagram, Plan plan, DiagramRoom room, Node node,
                       IReadOnlyList<Geometry> over)
    {
        if (plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)) is not { } sized) return;

        var bounds = room.At(sized.Cell.Bounds);



        if (node.Form == Form.Stop)
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
    private void Stopped(LayoutBuilder build, Node node, Rect bounds)
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
    private void Divided(LayoutBuilder build, Diagram diagram, Plan plan, DiagramRoom room, Group group, Rect bounds, double top)
    {
        if (!plan.Regions.TryGetValue(group.Key, out var regions)) return;

        var dividers = diagram.Inside(group.Key).Where(node => node.Form == Form.Divider).ToList();
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

    private static IEnumerable<Sized> Inside(Diagram diagram, Plan plan, string? group) =>
        diagram.Inside(group)
            .Select(node => plan.Nodes.FirstOrDefault(sized => ReferenceEquals(sized.Node, node)))
            .OfType<Sized>();

    // ── Colour and shape ────────────────────────────────────────────────────

    /// <summary>What a state is drawn as: the shape what it is says.</summary>
    private static DiagramShape Shaped(Node node) => node.Form switch
    {
        Form.Start => DiagramShape.Circle,
        Form.Stop => DiagramShape.DoubleCircle,
        Form.Choice => DiagramShape.Diamond,
        Form.Fork or Form.Join or Form.Divider => DiagramShape.Rectangle,
        _ => DiagramShape.Rounded,
    };

    /// <summary>What a state is filled with: what its styling writes, and otherwise what every state is — a dot the ink's.</summary>
    private Brush Fill(Node node)
    {
        var fill = Ink.Written(node.Style.Fill)
                   ?? (node.Marker || node.Form is Form.Fork or Form.Join ? Palette.Text : Ink.Node);

        return node.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What a composite state's box is filled with: what its styling writes, and otherwise what every composite is.</summary>
    private Brush Fill(Group group)
    {
        var fill = Ink.Written(group.Style.Fill) ?? Ink.Group;

        return group.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What something is outlined in: what its styling writes, and otherwise <paramref name="usual"/>.</summary>
    private DiagramStroke Stroke(MermaidStyle style, Brush usual) =>
        new(Ink.Written(style.Stroke) ?? usual, style.StrokeWidth ?? 1, DiagramInk.Dashes(style.Dashes));

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>A state measured: what is written on it, what it is drawn as, and the cell the layout placed it in.</summary>
    private sealed class Sized(Node node, IReadOnlyList<DiagramWords> words, DiagramShape shape)
    {
        public Node Node { get; } = node;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public DiagramShape Shape { get; } = shape;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A composite state measured.</summary>
    private sealed class Box(Group group, IReadOnlyList<DiagramWords> words)
    {
        public Group Group { get; } = group;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A note measured, held in the rank of the state it is about and drawn inside whatever holds that state.</summary>
    private sealed class Pinned(Note note, IReadOnlyList<DiagramWords> words, string? group)
    {
        public Note Note { get; } = note;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        /// <summary>The composite state the state it is about is in, or null for one about a state outside them all.</summary>
        public string? Group { get; } = group;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A transition worked out: where it runs, what is written on it, and the room those words take over its middle.</summary>
    private sealed record Route(Step Step, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

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

        public Dictionary<Step, DiagramJoin> Joins { get; } = [];

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public DiagramSpill Spill { get; set; } = DiagramSpill.None;

        /// <summary>The joins that hold a note beside the state it is about, which nothing draws.</summary>
        public List<DiagramJoin> Beside { get; } = [];

        public Size Size { get; set; }
    }
}
