using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
internal sealed class RequirementBuilder : MermaidBuilder
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

    internal RequirementBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>Which way a requirement diagram is laid out: where a relation written left to right points.</summary>
    private enum Way
    {
        /// <summary><c>TB</c> and <c>TD</c>.</summary>
        Down,

        /// <summary><c>BT</c>.</summary>
        Up,

        /// <summary><c>LR</c>.</summary>
        Right,

        /// <summary><c>RL</c>.</summary>
        Left,
    }

    /// <summary>One field written inside a requirement or an element, read.</summary>
    /// <param name="Part">The field as it was written, which is what a press on the row means.</param>
    /// <param name="Key">Which field it is, as Mermaid names it.</param>
    /// <param name="Says">What it is set to.</param>
    private sealed record Fact(ContentPart Part, string Key, string Says, int Order)
    {
        /// <summary>What it is set to as it was written, which is what a caret goes into.</summary>
        public ContentPart? Said { get; init; }

        /// <summary>The hole standing where that goes while nothing is written there.</summary>
        public ContentPart? Hole { get; init; }

        /// <summary>What is drawn in front of it, which is Mermaid's own word for the field rather than the key written.</summary>
        public string Label => Key.ToLowerInvariant() switch
        {
            RequirementGrammar.IdKey => "Id",
            RequirementGrammar.TextKey => "Text",
            RequirementGrammar.RiskKey => "Risk",
            RequirementGrammar.MethodKey => "Verification",
            RequirementGrammar.TypeKey => "Type",
            RequirementGrammar.RefKey => "Doc Ref",
            _ => Key,
        };
    }

    /// <summary>One requirement or element, read: where it was written, what it is called, and the fields inside it.</summary>
    /// <param name="Part">The name as it was written, which is what a press on it means.</param>
    /// <param name="Id">What it is called, which is what a relation and a styling line name it by.</param>
    /// <param name="Said">The words drawn for its name, which are the characters written.</param>
    private sealed record Node(ContentPart Part, string Id, ContentPart? Said, int Order)
    {
        /// <summary>
        /// The word its block opened with, drawn in guillemets over its name — none at all for one only a relation names, which is
        /// drawn as the box it stands for with nothing said about it.
        /// </summary>
        public ContentPart? Kind { get; init; }

        /// <summary>Its fields, in the order they are written.</summary>
        public IReadOnlyList<Fact> Facts { get; init; } = [];

        public MermaidStyle Style { get; init; } = MermaidStyle.None;

        /// <summary>The hole standing where its name goes.</summary>
        public ContentPart? SaidHole { get; init; }

        /// <summary>The whole of it as it was written, from the line opening it through the <c>}</c> closing its fields.</summary>
        public ISourcePart Whole { get; init; } = default(SourceSpan);

        /// <summary>What kind of thing it is, as it is drawn: <c>functionalRequirement</c> reads «Functional Requirement».</summary>
        public string? Says => Kind is { Length: > 0 } kind ? Spaced(kind.Text) : null;

        /// <summary>A word written as Mermaid draws it: capitalised, and broken where it runs two words together.</summary>
        public static string Spaced(string word)
        {
            if (word.Length == 0) return word;

            var built = new StringBuilder(word.Length + 2).Append(char.ToUpperInvariant(word[0]));

            foreach (var character in word[1..])
            {
                if (char.IsUpper(character)) built.Append(' ');
                built.Append(character);
            }

            return built.ToString();
        }
    }

    /// <summary>One relation, read: what it joins, and what holds between them.</summary>
    /// <param name="Part">The relation as it was written, which is what a press on its line means.</param>
    /// <param name="From">The one it leaves, which is the one written first however it was written round.</param>
    /// <param name="Says">What holds between them: <c>satisfies</c>, <c>derives</c>.</param>
    private sealed record Relation(ContentPart Part, string From, string To, string Says, int Order)
    {
        /// <summary>What holds between them as it was written, which is what a press on the words on the line means.</summary>
        public ContentPart? Said { get; init; }

        /// <summary>
        /// Whether the one it leaves holds the other, which SysML draws as a crosshair at that end of a solid line rather than as an
        /// arrow at the other.
        /// </summary>
        public bool Holds => string.Equals(Says, RequirementGrammar.Holding, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A <c>requirementDiagram</c> block, read: the requirements written in it, the elements that meet them, and what holds between
    /// them. Its title is the block's (<see cref="MermaidBlock.Title"/>).
    ///
    /// A name written twice is one thing: a relation names what a block above it wrote, and a relation naming what no block writes
    /// makes the box for it — which is what lets a diagram be nothing but relations.
    /// </summary>
    private sealed class Diagram
    {
        /// <summary>What a name with nothing written in it yet is known by, which is where it was written.</summary>
        private const string Unwritten = "\0";

        private Diagram(RequirementConfig config, Way way, IReadOnlyList<Node> nodes, IReadOnlyList<Relation> relations)
        {
            Config = config;
            Way = way;
            Nodes = nodes;
            Relations = relations;
        }

        /// <summary>What the front matter asks for.</summary>
        public RequirementConfig Config { get; }

        /// <summary>Which way it is laid out.</summary>
        public Way Way { get; }

        /// <summary>The requirements and the elements, in the order they are first written.</summary>
        public IReadOnlyList<Node> Nodes { get; }

        /// <summary>What holds between them, in the order written.</summary>
        public IReadOnlyList<Relation> Relations { get; }

        public Node? Find(string id) => Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

        /// <summary>Which way a <c>direction</c> line lays it out, or null for a way nobody writes.</summary>
        public static Way? Wayward(string? said) => said?.ToUpperInvariant() switch
        {
            "TB" or "TD" => Way.Down,
            "BT" => Way.Up,
            "LR" => Way.Right,
            "RL" => Way.Left,
            _ => null,
        };

        /// <summary>
        /// The diagram as written: every line read in the order it is written, what its stages said of it — what styles each
        /// box, on the name first writing it — taken as it comes.
        /// </summary>
        public static Diagram Of(ContentPart root, RequirementConfig config)
        {
            var nodes = new List<Made>();
            var known = new Dictionary<string, Made>(StringComparer.Ordinal);
            var relations = new List<Relation>();
            var way = Way.Down;

            foreach (var line in root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line))
            {
                if (line.Stated() is not { } stated) continue;

                switch (stated.Kind)
                {
                    case RequirementKinds.Block:
                        Bodied(stated, nodes, known);
                        break;

                    case RequirementKinds.Relation:
                        Related(stated, nodes, known, relations);
                        break;

                    case RequirementKinds.Naming:
                        Gathered(stated.Inner(RequirementKinds.Named), nodes, known);
                        break;

                    case RequirementKinds.Direction:
                        way = Wayward(Setting(stated, RequirementRoles.Towards)) ?? way;
                        break;
                }
            }

            return new Diagram(config, way, [.. nodes.Select(Frozen)], relations);
        }

        // ── What each line says ─────────────────────────────────────────────────

        /// <summary>A block: what it opens, and every field written between its braces.</summary>
        private static void Bodied(ContentPart stated, List<Made> nodes, Dictionary<string, Made> known)
        {
            if (stated.Inner(RequirementKinds.Opens) is not { } opens) return;
            if (Gathered(opens.Inner(RequirementKinds.Named), nodes, known) is not { } made) return;

            made.Kind ??= Piece(opens, MermaidKinds.Key, RequirementRoles.Kind);
            made.Whole = new SourceSpan(stated.Start, stated.End - stated.Start);

            foreach (var part in stated.SelfAndDescendants())
            {
                if (part.Kind != RequirementKinds.Field) continue;
                if (Piece(part, MermaidKinds.Key, RequirementRoles.Key) is not { Length: > 0 } key) continue;

                var value = part.Inner(RequirementKinds.Value);
                var said = Valued(value);

                made.Facts.Add(new Fact(part, key.Text, said?.Text ?? string.Empty, made.Facts.Count)
                {
                    Said = said is { Length: > 0 } ? said : null,
                    Hole = value.Hole(),
                });
            }
        }

        /// <summary>A relation: the two it joins — made where no block writes them — and what holds between them.</summary>
        private static void Related(ContentPart stated, List<Made> nodes, Dictionary<string, Made> known,
                                    List<Relation> relations)
        {
            var named = stated.SelfAndDescendants().Where(part => part.Kind == RequirementKinds.Named).ToList();
            if (named.Count < 2) return;

            if (Gathered(named[0], nodes, known) is not { } one || Gathered(named[1], nodes, known) is not { } two) return;

            var said = Piece(stated, MermaidKinds.Setting, RequirementRoles.Says);
            var back = stated.SelfAndDescendants()
                             .Any(part => part.Role == RequirementRoles.Arrow && part.Text == RequirementGrammar.Backward);

            relations.Add(new Relation(stated, back ? two.Id : one.Id, back ? one.Id : two.Id,
                                                  said?.Text ?? string.Empty, relations.Count)
            {
                Said = said is { Length: > 0 } ? said : null,
            });
        }

        /// <summary>
        /// The one a name names: the one already made where it names it again, and otherwise a new one. A name with nothing written
        /// in it yet is a box of its own, known by where it is written, so writing it is watched as it is typed.
        /// </summary>
        private static Made? Gathered(ContentPart? named, List<Made> nodes, Dictionary<string, Made> known)
        {
            if (named is not { } holder) return null;

            if (holder.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name) is not { } name) return null;

            var words = name.Words();
            var hole = name.Hole();

            if (words is not { Length: > 0 } && hole is null) return null;

            var id = words is { Length: > 0 } written ? written.Text : Unwritten + name.Start;

            if (!known.TryGetValue(id, out var made))
            {
                made = new Made(name, id, nodes.Count) { Said = words, SaidHole = hole };

                nodes.Add(made);
                known[id] = made;
            }

            return made;
        }

        private static Node Frozen(Made made) =>
            new(made.Part, made.Id, made.Said, made.Order)
            {
                Kind = made.Kind,
                Facts = made.Facts,
                Style = StyleOf(made.Part),
                SaidHole = made.SaidHole,
                Whole = made.Whole ?? new SourceSpan(made.Part.Start, made.Part.End - made.Part.Start),
            };

        // ── What the pieces say ─────────────────────────────────────────────────

        /// <summary>What a field is set to: its words, or the one of a few words a risk and a verification method may be.</summary>
        private static ContentPart? Valued(ContentPart? value) =>
            value?.SelfAndDescendants().FirstOrDefault(inner => inner.Role == RequirementRoles.Value
                                                                && inner.Kind is MermaidKinds.Words or MermaidKinds.Setting);

        private static ContentPart? Piece(ContentPart part, string kind, string role) =>
            part.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == kind && inner.Role == role);

        private static string? Setting(ContentPart stated, string role) =>
            Piece(stated, MermaidKinds.Setting, role)?.Text;

        /// <summary>One of them while the block is being read, before what every line says about it is known.</summary>
        private sealed class Made(ContentPart part, string id, int order)
        {
            public ContentPart Part { get; } = part;

            public string Id { get; } = id;

            public int Order { get; } = order;

            public ContentPart? Said { get; set; }

            public ContentPart? SaidHole { get; set; }

            public ContentPart? Kind { get; set; }

            public List<Fact> Facts { get; } = [];

            public ISourcePart? Whole { get; set; }
        }
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = Diagram.Of(Reading.Root, Configured(RequirementConfig.Default));

        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Nodes.Count == 0) return AsWritten(build);

        // How much of it is drawn, worked out before anything is placed.
        Fold(new DiagramChart([.. diagram.Nodes.Select(node => node.Id)], [.. diagram.Relations.Select(relation => (relation.From, relation.To))]));

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The relations are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var over = DiagramConnector.Covered(routes.Select(route => (route.Along, route.Room)), Thick);

        build.Open(RequirementPiece.Boxes, part: null, stops: Stops.None);
        foreach (var sized in plan.Nodes) Drawn(build, room, sized, over);
        plan.Spill.Draw(build, room, Ink.Surface, new DiagramStroke(Palette.CodeBorder, 1, DiagramStroke.Dotted));
        build.Close();

        Relations(build, routes);

        return room.Size;
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    private Plan Laid(Diagram diagram)
    {
        var plan = new Plan();
        var cells = new List<DiagramCell>();

        foreach (var node in diagram.Nodes)
        {
            // A requirement folded away is never given a cell, so the relations to it have no end to meet.
            if (!Draws(node.Id)) continue;

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

        plan.Spill = Spilled(id => plan.Named.TryGetValue(id, out var sized) ? sized.Cell : null);
        cells.AddRange(plan.Spill.Cells);

        plan.Size = DiagramLayers.Lay(cells, [.. plan.Joins.Values, .. plan.Spill.Joins], Towards(diagram.Way),
                                      diagram.Config.NodeSpacing, diagram.Config.RankSpacing);

        return plan;
    }

    private DiagramRoom Reached(Diagram diagram, Plan plan) =>
        DiagramRoom.Round(diagram.Config.Air, plan.Size, plan.Nodes.Select(node => node.Cell),
                          plan.Joins.Select(join => (join.Value, Says(join.Key))));

    /// <summary>One box measured: what kind of thing it is over its name, then a row for each field.</summary>
    private Sized Measure(Node node, RequirementConfig config)
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

        var bands = new List<DiagramCompartment>
        {
            new([.. title.Select(words => DiagramRow.Of(words))]) { Centred = true },
        };

        // What is drawn in front of a field and what it is set to follow one another along the row, as Mermaid writes them.
        if (rows.Count > 0) bands.Add(new([.. rows.Select(row => DiagramRow.Of(row.Label, row.Said))]));

        return new Sized(node, rows,
                         DiagramBox.Measure(bands, config.RowHeight, config.Padding, Air, config.TextSize / 3,
                                            new Size(config.MinWidth, config.MinHeight), Chrome));
    }

    // ── The relations ───────────────────────────────────────────────────────

    /// <summary>Where every relation runs once everything is placed, its ends brought in to the boxes it joins.</summary>
    private List<Route> Routes(Diagram diagram, Plan plan, DiagramRoom room)
    {
        var routes = new List<Route>();

        foreach (var relation in diagram.Relations)
        {
            if (!plan.Joins.TryGetValue(relation, out var join) || join.Route.Count < 2) continue;

            var placed = DiagramConnector.Trimmed(join).Select(room.At).ToList();
            var said = Says(relation);

            routes.Add(new Route(relation, placed, said, DiagramConnector.Room(placed, said)));
        }

        return routes;
    }

    /// <summary>What holds between two of them, in guillemets as SysML writes it.</summary>
    private IReadOnlyList<DiagramWords> Says(Relation relation) =>
        relation.Said is { Length: > 0 } said
            ? [Worked(Opens + said.Text + Shuts, said, LabelSize, Palette.TextMuted)]
            : [];

    /// <summary>The relations, drawn over the diagram.</summary>
    private void Relations(LayoutBuilder build, IReadOnlyList<Route> routes)
    {
        if (routes.Count == 0) return;

        build.Open(RequirementPiece.Relations, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            // What holds the other is a solid line with the crosshair at the end holding it; everything else is a dashed arrow.
            var holds = route.Relation.Holds;
            var stroke = new DiagramStroke(Ink.Link, Thick, holds ? null : DiagramStroke.Dashed);

            DiagramConnector.Draw(build, RequirementPiece.Relation, route.Relation.Part, route.Along, stroke,
                                  holds ? DiagramHead.CrossCircle : DiagramHead.None,
                                  holds ? DiagramHead.None : DiagramHead.Open,
                                  curved: true);

            DiagramConnector.Says(build, RequirementPiece.Label, route.Relation.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    private void Drawn(LayoutBuilder build, DiagramRoom room, Sized sized, IReadOnlyList<Geometry> over)
    {
        var box = room.At(sized.Cell.Bounds);
        var outline = DiagramShapes.Outline(DiagramShape.Rounded, box);
        var placed = sized.Laid.Placed(box).ToList();

        var covered = new GeometryGroup();
        foreach (var shape in over) covered.Children.Add(shape);
        foreach (var (_, _, set) in placed)
            foreach (var (words, at) in set) covered.Children.Add(Taken(words, at));

        build.Open(RequirementPiece.Box, sized.Node.Whole, stops: Stops.None);
        build.Open(MermaidPiece.Shape, sized.Node.Part, stops: Stops.None);

        var stroke = Stroke(sized.Node.Style);
        build.Draw(new GeometryMark(outline, Fill(sized.Node), stroke.Ink, stroke.Thickness));

        // The rule under the name is what says a requirement has fields at all.
        foreach (var at in sized.Laid.Rules(box))
            build.Draw(new LineMark(new Point(box.Left, at), new Point(box.Right, at), DiagramInk.Ruled(stroke.Ink)));

        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, covered);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        foreach (var (band, at, set) in placed)
        {
            if (band > 0) build.Open(RequirementPiece.Fact, sized.Facts[at].Fact.Part, stops: Stops.None);

            foreach (var (words, where) in set) words.Set(build, where, MermaidPiece.Words);

            if (band > 0) build.Close();
        }

        build.Close();

        Chipped(build, sized.Node.Id, box, sized.Node.Whole);
    }

    private static RectangleGeometry Taken(DiagramWords words, Point at) =>
        new(new Rect(at, new Size(words.Width, words.Height)));

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>What a box is filled with: what its styling writes, and otherwise what every box is.</summary>
    private Brush Fill(Node node)
    {
        var fill = Ink.Written(node.Style.Fill) ?? Ink.Node;

        return node.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What a box is outlined in: what its styling writes, and otherwise what every box is.</summary>
    private DiagramStroke Stroke(MermaidStyle style) =>
        new(Ink.Written(style.Stroke) ?? Ink.NodeEdge, style.StrokeWidth ?? Thick, DiagramInk.Dashes(style.Dashes));

    private static DiagramWay Towards(Way way) => way switch
    {
        Way.Up => DiagramWay.Up,
        Way.Right => DiagramWay.Right,
        Way.Left => DiagramWay.Left,
        _ => DiagramWay.Down,
    };

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>One box measured: the words of each of its fields, the box they are set in, and the cell the layout placed it in.</summary>
    private sealed class Sized(
        Node node,
        IReadOnlyList<(Fact Fact, DiagramWords Label, DiagramWords Said)> facts,
        DiagramBox laid)
    {
        public Node Node { get; } = node;

        public IReadOnlyList<(Fact Fact, DiagramWords Label, DiagramWords Said)> Facts { get; } = facts;

        /// <summary>Its name and its fields measured into a box of compartments (<see cref="DiagramBox"/>).</summary>
        public DiagramBox Laid { get; } = laid;

        public Size Size => Laid.Size;

        public DiagramCell Cell { get; set; } = new(default);
    }

    /// <summary>A relation worked out: where it runs, and what is written on it.</summary>
    private sealed record Route(Relation Relation, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>Everything the diagram was measured and laid out into.</summary>
    private sealed class Plan
    {
        public List<Sized> Nodes { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<Relation, DiagramJoin> Joins { get; } = [];

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public DiagramSpill Spill { get; set; } = DiagramSpill.None;

        public Size Size { get; set; }
    }
}
