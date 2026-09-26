using System;
using System.Collections.Generic;
using System.Globalization;
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
internal sealed class ErBuilder : MermaidBuilder
{
    /// <summary>The air above and below the rows of a band.</summary>
    private const double Air = 5;

    /// <summary>The room a box keeps for its outline, so its rows are not drawn against it.</summary>
    private const double Chrome = 4;

    /// <summary>The air a subgraph keeps round what it holds.</summary>
    private const double Boxed = 14;

    /// <summary>How thick a box's outline and a relationship's line are drawn.</summary>
    private const double Thick = 1.5;

    /// <summary>How big what is written on a relationship is drawn.</summary>
    private const double LabelSize = 11;

    /// <summary>How wide what is written on a relationship runs before it wraps.</summary>
    private const double Widest = 160;

    internal ErBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>One attribute of an entity: what it holds, what it is called, the keys it is, and what it is for.</summary>
    /// <param name="Part">The attribute as it was written, which is what a press on its row means.</param>
    private sealed record Attribute(ContentPart Part)
    {
        private const string Primary = "PK";

        /// <summary>What it holds: <c>string</c>, <c>int</c>, <c>string[]</c>.</summary>
        public ContentPart? Type { get; init; }

        public ContentPart? Field { get; init; }

        /// <summary>The keys it is, in the order written: <c>PK</c>, <c>FK</c>, <c>UK</c>.</summary>
        public IReadOnlyList<ContentPart> Keys { get; init; } = [];

        /// <summary>What it says it is for, written in quotes at the end of its line.</summary>
        public ContentPart? Comment { get; init; }

        /// <summary>
        /// What the keys say, as they are drawn: <c>PK, FK</c>. A name written with a star — <c>string *id</c> — is Mermaid's
        /// other way of saying it is a primary key: the name is drawn as written, star and all, and the keys say <c>PK</c> first.
        /// </summary>
        public string Keyed => string.Join(", ", Starred && !Keys.Any(key => key.Text.Equals(Primary, StringComparison.OrdinalIgnoreCase))
                                                     ? Keys.Select(key => key.Text).Prepend(Primary)
                                                     : Keys.Select(key => key.Text));

        private bool Starred => Field is { Length: > 0 } named && named.Text.StartsWith('*');
    }

    /// <summary>One entity: where it was first written, what it is called, and the attributes written for it anywhere.</summary>
    /// <param name="Part">The name first writing it, which is what a press on it means and where its style is said.</param>
    /// <param name="Group">The key of the subgraph it was first written in, or null for one written outside them all.</param>
    private sealed class Entity(ContentPart part, string id, string? group)
    {
        public ContentPart Part { get; } = part;

        public string Id { get; } = id;

        public string? Group { get; } = group;

        public MermaidStyle Style { get; } = StyleOf(part);

        /// <summary>The words drawn for it: the label written in brackets, and otherwise its name.</summary>
        public ContentPart? Said { get; set; }

        public ContentPart? SaidHole { get; set; }

        public List<Attribute> Attributes { get; } = [];

        /// <summary>The whole of it as it was written, from the line naming it through the <c>}</c> closing its attributes.</summary>
        public ISourcePart Whole { get; set; } = default(SourceSpan);
    }

    /// <summary>One relationship: the ends it joins, what each end draws, and what it is called.</summary>
    /// <param name="From">What it leaves: an entity's id, or a subgraph's key where <see cref="FromBox"/> says so.</param>
    private sealed record Relation(ContentPart Part, string From, string To, DiagramHead Near, DiagramHead Far, bool Dotted)
    {
        public ContentPart? Said { get; init; }

        public ContentPart? SaidHole { get; init; }

        public bool FromBox { get; init; }

        public bool ToBox { get; init; }
    }

    /// <summary>One subgraph: the box drawn round every entity written inside it.</summary>
    /// <param name="Key">Where it stands among the subgraphs written, which is what an entity says it is inside.</param>
    private sealed record Group(ContentPart Part, string Key, ContentPart? Said, string? Parent)
    {
        /// <summary>The whole of it as it was written, from the line that opened it through the <c>end</c> that closed it.</summary>
        public ISourcePart Whole { get; init; } = default(SourceSpan);
    }

    /// <summary>
    /// What the block writes, read down the tree in the order it is written. An entity written twice is one entity: the second
    /// writing says more about the one the first made — its attributes, its label — which is what lets a relationship name the
    /// entities a line above wrote.
    /// </summary>
    private sealed class Diagram
    {
        /// <summary>What a name with nothing written in it yet is known by, which is where it was written.</summary>
        private const string Unwritten = " ";

        private readonly Dictionary<string, Entity> known = new(StringComparer.Ordinal);

        private Diagram(ErConfig config)
        {
            Config = config;
            Way = Wayward(config.Way) ?? DiagramWay.Down;
        }

        /// <summary>What the front matter asks for.</summary>
        public ErConfig Config { get; }

        /// <summary>Which way it is laid out: the last <c>direction</c> line, and otherwise the front matter's.</summary>
        public DiagramWay Way { get; private set; }

        public List<Entity> Entities { get; } = [];

        public List<Relation> Relations { get; } = [];

        public List<Group> Groups { get; } = [];

        public static Diagram Of(ContentPart root, ErConfig config)
        {
            var diagram = new Diagram(config);
            diagram.Read(root, null);
            return diagram;
        }

        /// <summary>The entities written inside one subgraph, or outside them all.</summary>
        public IEnumerable<Entity> Inside(string? group) =>
            Entities.Where(entity => string.Equals(entity.Group, group, StringComparison.Ordinal));

        /// <summary>The subgraphs written inside one, or outside them all.</summary>
        public IEnumerable<Group> Within(string? group) =>
            Groups.Where(box => string.Equals(box.Parent, group, StringComparison.Ordinal));

        /// <summary>Everything written in one part of the block — the whole of it, or one subgraph — inside the subgraph given.</summary>
        private void Read(ContentPart holder, string? inside)
        {
            foreach (var part in holder.Children)
            {
                if (part.Kind == MermaidKinds.Group)
                {
                    if (Opened(part, inside) is { } key) Read(part, key);
                    continue;
                }

                if (part.Stated() is not { } stated) continue;

                switch (stated.Kind)
                {
                    case ErKinds.Block:
                        Bodied(stated, inside);
                        break;

                    case ErKinds.Entity:
                        Gathered(stated.Inner(ErKinds.Named), inside);
                        break;

                    case ErKinds.Relation:
                        Related(stated, inside);
                        break;

                    case ErKinds.Direction:
                        Way = Wayward(Piece(stated, MermaidKinds.Setting, ErRoles.Towards)?.Text) ?? Way;
                        break;
                }
            }
        }

        /// <summary>A subgraph, and the box it makes: known by where it stands among the subgraphs, as a relationship names it.</summary>
        private string? Opened(ContentPart group, string? inside)
        {
            if (group.Children.FirstOrDefault()?.Stated() is not { } opening) return null;

            var said = opening.Inner(MermaidKinds.Label)?.Words() ?? opening.Inner(MermaidKinds.Name)?.Words();
            var key = Groups.Count.ToString(CultureInfo.InvariantCulture);

            // The end closing a subgraph is where the whole of it stops, which is what a press on the room round it means.
            var closing = group.Children[^1].Stated() is { Kind: ErKinds.Ends } ends ? ends : opening;

            Groups.Add(new Group(opening, key, said is { Length: > 0 } ? said : null, inside)
            {
                Whole = new SourceSpan(opening.Start, closing.End - opening.Start),
            });

            return key;
        }

        /// <summary>An entity and every attribute written between its braces.</summary>
        private void Bodied(ContentPart stated, string? inside)
        {
            if (stated.Inner(ErKinds.Opens) is not { } opens) return;
            if (Gathered(opens.Inner(ErKinds.Named), inside) is not { } entity) return;

            entity.Whole = new SourceSpan(stated.Start, stated.End - stated.Start);

            foreach (var part in stated.SelfAndDescendants())
            {
                if (part.Kind != ErKinds.Attribute) continue;

                var type = Piece(part, MermaidKinds.Words, ErRoles.Type);
                if (type is not { Length: > 0 } && part.Hole() is null) continue;

                entity.Attributes.Add(new Attribute(part)
                {
                    Type = type is { Length: > 0 } ? type : null,
                    Field = Piece(part, MermaidKinds.Words, ErRoles.Field) is { Length: > 0 } field ? field : null,
                    Keys = [.. part.SelfAndDescendants()
                                .Where(inner => inner.Kind == MermaidKinds.Words && inner.Role == ErRoles.Key && inner.Length > 0)],
                    Comment = Piece(part, MermaidKinds.Words, ErRoles.Comment) is { Length: > 0 } says ? says : null,
                });
            }
        }

        /// <summary>A relationship: what each end names — a subgraph where a stage said so, and otherwise an entity.</summary>
        private void Related(ContentPart stated, string? inside)
        {
            var named = stated.SelfAndDescendants().Where(part => part.Kind == ErKinds.Named).ToList();
            if (named.Count < 2) return;

            if (Ended(named[0], inside) is not { } from) return;
            if (Ended(named[1], inside) is not { } to) return;

            var cards = stated.SelfAndDescendants()
                              .Where(part => part.Role == ErRoles.Card && part.Length > 0)
                              .Select(part => part.Text)
                              .ToList();

            var arrow = stated.SelfAndDescendants().FirstOrDefault(part => part.Role == ErRoles.Arrow)?.Text ?? string.Empty;
            var said = stated.Inner(ErKinds.Said);

            Relations.Add(new Relation(stated, from.Id, to.Id,
                                       Headed(cards.Count > 0 ? cards[0] : null),
                                       Headed(cards.Count > 1 ? cards[1] : null),
                                       ErGrammar.Dotted.Contains(arrow, StringComparer.Ordinal)
                                       || string.Equals(arrow, ErGrammar.OptionallyWord, StringComparison.OrdinalIgnoreCase))
            {
                Said = said.Words() is { Length: > 0 } words ? words : null,
                SaidHole = said.Hole(),
                FromBox = from.Box,
                ToBox = to.Box,
            });
        }

        /// <summary>What one end of a relationship names: the subgraph the stages said it names, and otherwise the entity.</summary>
        private (string Id, bool Box)? Ended(ContentPart named, string? inside) =>
            named.Node is GroupReferenceNode reference ? (reference.Group.ToString(CultureInfo.InvariantCulture), true)
            : Gathered(named, inside) is { } entity ? (entity.Id, false)
            : null;

        /// <summary>
        /// The entity a name names: the one already made where it names it again, and otherwise a new one. A name with nothing
        /// written in it yet is an entity of its own, known by where it is written, so writing it is watched as it is typed.
        /// </summary>
        private Entity? Gathered(ContentPart? named, string? inside)
        {
            if (named is not { } holder) return null;
            if (holder.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name) is not { } name) return null;

            var words = name.Words();
            var hole = name.Hole();
            if (words is not { Length: > 0 } && hole is null) return null;

            var id = words is { Length: > 0 } said ? said.Text : Unwritten + name.Start;

            if (!known.TryGetValue(id, out var entity))
            {
                entity = new Entity(name, id, inside)
                {
                    Said = words,
                    SaidHole = hole,
                    Whole = new SourceSpan(name.Start, name.End - name.Start),
                };

                Entities.Add(entity);
                known[id] = entity;
            }

            // A label written in brackets is drawn instead of the name, wherever it is written.
            if (holder.Inner(MermaidKinds.Label)?.Words() is { } label) entity.Said = label;

            return entity;
        }

        private static ContentPart? Piece(ContentPart part, string kind, string role) =>
            part.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == kind && inner.Role == role);

        /// <summary>Which way a <c>direction</c> line, or the front matter, lays it out — or null for a way nobody writes.</summary>
        private static DiagramWay? Wayward(string? said) => said?.Trim().ToUpperInvariant() switch
        {
            "TB" or "TD" => DiagramWay.Down,
            "BT" => DiagramWay.Up,
            "LR" => DiagramWay.Right,
            "RL" => DiagramWay.Left,
            _ => null,
        };

        /// <summary>
        /// What the symbol or the words at one end of a relationship draw: the crow's foot saying how many of the entity at that
        /// end the other has. Each symbol is written one way round at the near end and the other at the far end.
        /// </summary>
        private static DiagramHead Headed(string? said) => said?.Trim().ToLowerInvariant() switch
        {
            "|o" or "o|" or "zero or one" or "one or zero" => DiagramHead.ZeroOrOne,
            "}o" or "o{" or "zero or more" or "zero or many" or "many(0)" or "0+" or "many" => DiagramHead.ZeroOrMore,
            "}|" or "|{" or "one or more" or "one or many" or "many(1)" or "1+" => DiagramHead.OneOrMore,
            _ => DiagramHead.ExactlyOne,
        };
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var diagram = Diagram.Of(Reading.Root, Configured(ErConfig.Default));

        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Entities.Count == 0 && diagram.Groups.Count == 0) return AsWritten(build);

        // How much of it is drawn, worked out before anything is placed.
        Fold(new DiagramChart([.. diagram.Entities.Select(entity => entity.Id)], [.. diagram.Relations.Select(relation => (relation.From, relation.To))]));

        var plan = Laid(diagram);
        var room = Reached(diagram, plan);

        // The relationships are worked out before anything is drawn, because whatever is under one does not stand where it runs.
        var routes = Routes(diagram, plan, room);
        var over = DiagramConnector.Covered(routes.Select(route => (route.Along, route.Room)), Thick);

        build.Open(ErPiece.Entities, part: null, stops: Stops.None);
        foreach (var group in diagram.Within(null)) Held(build, diagram, plan, room, group, over);
        foreach (var entity in diagram.Inside(null)) Drawn(build, diagram.Config, plan, room, entity, over);
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
            // An entity folded away is never given a cell, so the relationships to it have no end to meet and a
            // subgraph holding nothing else closes up rather than standing empty.
            if (!Draws(entity.Id)) continue;

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
            if (Joined(plan, relation.From, relation.FromBox) is not { } from) continue;
            if (Joined(plan, relation.To, relation.ToBox) is not { } to) continue;

            // A subgraph joined to something inside itself has nowhere to run to, so the line is left undrawn.
            if (ReferenceEquals(from, to) && relation.FromBox != relation.ToBox) continue;

            plan.Joins[relation] = new DiagramJoin(from, to);
        }

        plan.Spill = Spilled(id => plan.Named.TryGetValue(id, out var sized) ? sized.Cell : null);
        cells.AddRange(plan.Spill.Cells);

        plan.Size = DiagramLayers.Lay(cells, [.. plan.Joins.Values, .. plan.Spill.Joins], diagram.Way,
                                      diagram.Config.NodeSpacing, diagram.Config.RankSpacing, square: true);

        return plan;
    }

    /// <summary>The cell one end of a relationship is drawn from: an entity's box, or a subgraph's own.</summary>
    private static DiagramCell? Joined(Plan plan, string id, bool box)
    {
        if (box) return plan.Groups.TryGetValue(id, out var held) ? held.Cell : null;

        return plan.Named.TryGetValue(id, out var sized) ? sized.Cell : null;
    }

    private DiagramRoom Reached(Diagram diagram, Plan plan) =>
        DiagramRoom.Round(diagram.Config.Air, plan.Size,
                          [.. plan.Entities.Select(entity => entity.Cell), .. plan.Groups.Values.Select(box => box.Cell)],
                          plan.Joins.Select(join => (join.Value, Says(join.Key))));

    /// <summary>One entity measured: its name over its attributes, each set in its own column.</summary>
    private Sized Measure(Entity entity, ErConfig config)
    {
        var ink = Ink.Written(entity.Style.Colour) ?? Palette.Text;

        var title = Wrapped(entity.Said, entity.SaidHole, config.TextSize, ink, config.Wrapping, FontWeights.SemiBold);

        var rows = entity.Attributes
            .Select(attribute => (Attribute: attribute,
                                  Said: new DiagramRow([Worded(attribute.Type, ink, config),
                                                        Worded(attribute.Field, ink, config),
                                                        Keyed(attribute, ink, config),
                                                        Worded(attribute.Comment, Palette.TextMuted, config)])))
            .ToList();

        var bands = new List<DiagramCompartment>
        {
            new([.. title.Select(words => DiagramRow.Of(words))]) { Centred = true },
        };

        // The attributes line up down the box, column by column, so they read down as well as across.
        if (rows.Count > 0) bands.Add(new([.. rows.Select(row => row.Said)]) { Aligned = true });

        return new Sized(entity, rows,
                         DiagramBox.Measure(bands, config.RowHeight, config.Padding, Air, config.TextSize,
                                            new Size(config.MinWidth, config.MinHeight), Chrome));
    }

    /// <summary>What is written at the top of a subgraph.</summary>
    private IReadOnlyList<DiagramWords> Naming(Group group, ErConfig config) =>
        Wrapped(group.Said, null, config.TextSize, Palette.Text, config.Wrapping);

    /// <summary>One part of an attribute, drawn as the characters written — or nothing, where nothing is written.</summary>
    private DiagramWords? Worded(ContentPart? part, Brush ink, ErConfig config) =>
        part is { Length: > 0 } ? Written(part, null, config.TextSize, ink) : null;

    /// <summary>
    /// The keys an attribute is: the one written, where it is the only one, and otherwise what they say together — which nobody
    /// writes as one run, and so is pressed rather than typed into.
    /// </summary>
    private DiagramWords? Keyed(Attribute attribute, Brush ink, ErConfig config)
    {
        if (attribute.Keyed is not { Length: > 0 } keyed) return null;

        // The one key written is the characters written; anything else — several of them, or the PK a star says — is worked out.
        return attribute.Keys.Count == 1 && keyed == attribute.Keys[0].Text
            ? Written(attribute.Keys[0], null, config.TextSize, ink)
            : Worked(keyed, attribute.Part, config.TextSize, ink);
    }

    // ── The relationships ───────────────────────────────────────────────────

    /// <summary>Where every relationship runs once everything is placed, its ends brought in to the boxes it joins.</summary>
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

    /// <summary>What a relationship is called, where anything is written on it.</summary>
    private IReadOnlyList<DiagramWords> Says(Relation relation) =>
        relation.Said is null && relation.SaidHole is null
            ? []
            : Wrapped(relation.Said, relation.SaidHole, LabelSize, Palette.TextMuted, Widest);

    /// <summary>The relationships, drawn over the diagram.</summary>
    private void Relations(LayoutBuilder build, IReadOnlyList<Route> routes)
    {
        if (routes.Count == 0) return;

        build.Open(ErPiece.Relations, part: null, stops: Stops.None);

        foreach (var route in routes)
        {
            var stroke = new DiagramStroke(Ink.Link, Thick, route.Relation.Dotted ? DiagramStroke.Dashed : null);

            // Square, corners and all: an entity diagram is read as straight runs meeting at right angles, and a rounded
            // corner is a corner that no longer meets the next one.
            DiagramConnector.Draw(build, ErPiece.Relation, route.Relation.Part, route.Along, stroke,
                                  route.Relation.Near, route.Relation.Far);

            DiagramConnector.Says(build, ErPiece.Label, route.Relation.Part, route.Room, route.Said, Palette.CodeBg);
        }

        build.Close();
    }

    // ── Drawing it ──────────────────────────────────────────────────────────

    /// <summary>A subgraph: its box, its name at the top of it, and the entities it holds drawn inside its piece.</summary>
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
            .. Inside(diagram, plan, group.Key).Select(entity => DiagramShapes.Outline(DiagramShape.Rectangle, room.At(entity.Cell.Bounds))),
        ]);

        build.Open(ErPiece.Group, group.Whole, stops: Stops.None);
        DiagramShapes.Draw(build, ErPiece.Holding, group.Part, DiagramShape.Rounded, bounds, Ink.Group,
                           new DiagramStroke(Ink.GroupEdge, Thick), DiagramWords.Placed(box.Words, heading, MermaidPiece.Words), covered,
                           band: Ink.Band(null));

        foreach (var nested in diagram.Within(group.Key)) Held(build, diagram, plan, room, nested, over);
        foreach (var entity in diagram.Inside(group.Key)) Drawn(build, diagram.Config, plan, room, entity, over);
        build.Close();
    }

    private void Drawn(LayoutBuilder build, ErConfig config, Plan plan, DiagramRoom room, Entity entity,
                       IReadOnlyList<Geometry> over)
    {
        if (plan.Entities.FirstOrDefault(sized => ReferenceEquals(sized.Entity, entity)) is not { } laid) return;

        var box = room.At(laid.Cell.Bounds);
        var outline = DiagramShapes.Outline(DiagramShape.Rounded, box);
        var placed = laid.Laid.Placed(box).ToList();

        var covered = new GeometryGroup();
        foreach (var shape in over) covered.Children.Add(shape);
        foreach (var (_, _, set) in placed)
            foreach (var (words, at) in set) covered.Children.Add(Taken(words, at));

        build.Open(ErPiece.Entity, entity.Whole, stops: Stops.None);
        build.Open(MermaidPiece.Shape, entity.Part, stops: Stops.None);

        var stroke = Stroke(entity.Style, config);
        build.Draw(new GeometryMark(outline, Fill(entity, config), stroke.Ink, stroke.Thickness));

        // The rule under the name is what says an entity has attributes at all.
        foreach (var at in laid.Laid.Rules(box))
            build.Draw(new LineMark(new Point(box.Left, at), new Point(box.Right, at), DiagramInk.Ruled(stroke.Ink)));

        var stands = new CombinedGeometry(GeometryCombineMode.Exclude, outline, covered);
        stands.Freeze();
        build.Occupies(stands);
        build.Close();

        foreach (var (band, at, set) in placed)
        {
            if (band > 0) build.Open(ErPiece.Attribute, laid.Rows[at].Attribute.Part, stops: Stops.None);

            foreach (var (words, where) in set) words.Set(build, where, MermaidPiece.Words);

            if (band > 0) build.Close();
        }

        build.Close();

        Chipped(build, entity.Id, box, entity.Whole);
    }

    private static RectangleGeometry Taken(DiagramWords words, Point at) =>
        new(new Rect(at, new Size(words.Width, words.Height)));

    private static IEnumerable<Sized> Inside(Diagram diagram, Plan plan, string? group) =>
        diagram.Inside(group)
            .Select(entity => plan.Entities.FirstOrDefault(sized => ReferenceEquals(sized.Entity, entity)))
            .OfType<Sized>();

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>What an entity is filled with: what its styling or the front matter writes, and otherwise what every entity is.</summary>
    private Brush Fill(Entity entity, ErConfig config)
    {
        var fill = Ink.Written(entity.Style.Fill) ?? Ink.Written(config.Fill) ?? Ink.Node;

        return entity.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What an entity is outlined in: what its styling or the front matter writes, and otherwise what every entity is.</summary>
    private DiagramStroke Stroke(MermaidStyle style, ErConfig config) =>
        new(Ink.Written(style.Stroke) ?? Ink.Written(config.Stroke) ?? Ink.NodeEdge,
            style.StrokeWidth ?? Thick, DiagramInk.Dashes(style.Dashes));

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>One entity measured: its attributes, the box they are set in, and the cell the layout placed it in.</summary>
    private sealed class Sized(
        Entity entity,
        IReadOnlyList<(Attribute Attribute, DiagramRow Said)> rows,
        DiagramBox laid)
    {
        public Entity Entity { get; } = entity;

        public IReadOnlyList<(Attribute Attribute, DiagramRow Said)> Rows { get; } = rows;

        /// <summary>Its name and its attributes measured into a box of compartments (<see cref="DiagramBox"/>).</summary>
        public DiagramBox Laid { get; } = laid;

        public Size Size => Laid.Size;

        public DiagramCell Cell { get; set; } = new(default);
    }

    /// <summary>A subgraph measured.</summary>
    private sealed class Box(Group group, IReadOnlyList<DiagramWords> words)
    {
        public Group Group { get; } = group;

        public IReadOnlyList<DiagramWords> Words { get; } = words;

        public required DiagramCell Cell { get; init; }
    }

    /// <summary>A relationship worked out: where it runs, and what is written on it.</summary>
    private sealed record Route(Relation Relation, IReadOnlyList<Point> Along, IReadOnlyList<DiagramWords> Said, Rect Room);

    /// <summary>Everything the diagram was measured and laid out into.</summary>
    private sealed class Plan
    {
        public List<Sized> Entities { get; } = [];

        public Dictionary<string, Sized> Named { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Box> Groups { get; } = new(StringComparer.Ordinal);

        public Dictionary<Relation, DiagramJoin> Joins { get; } = [];

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public DiagramSpill Spill { get; set; } = DiagramSpill.None;

        public Size Size { get; set; }
    }
}
