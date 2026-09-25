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

    internal RequirementBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    /// <inheritdoc/>
    protected override RequirementDiagram Of(MermaidBlock block) => RequirementDiagram.Of(block);

    /// <inheritdoc/>
    protected override DiagramChart? Chart(RequirementDiagram diagram) =>
        new([.. diagram.Nodes.Select(node => node.Id)], [.. diagram.Relations.Select(relation => (relation.From, relation.To))]);

    protected override Size Draw(RequirementDiagram diagram, LayoutBuilder build)
    {
        // A diagram with nothing written in it is the source: what the reader wants back is their own lines.
        if (diagram.Nodes.Count == 0) return AsWritten(build);

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

    private Plan Laid(RequirementDiagram diagram)
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

    private DiagramRoom Reached(RequirementDiagram diagram, Plan plan) =>
        DiagramRoom.Round(diagram.Config.Air, plan.Size, plan.Nodes.Select(node => node.Cell),
                          plan.Joins.Select(join => (join.Value, Says(join.Key))));

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
    private List<Route> Routes(RequirementDiagram diagram, Plan plan, DiagramRoom room)
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
    private IReadOnlyList<DiagramWords> Says(RequirementRelation relation) =>
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
    private Brush Fill(RequirementNode node)
    {
        var fill = Ink.Written(node.Style.Fill) ?? Ink.Node;

        return node.Style.FillOpacity is { } opacity ? DiagramInk.Faded(fill, opacity) : fill;
    }

    /// <summary>What a box is outlined in: what its styling writes, and otherwise what every box is.</summary>
    private DiagramStroke Stroke(MermaidStyle style) =>
        new(Ink.Written(style.Stroke) ?? Ink.NodeEdge, style.StrokeWidth ?? Thick, DiagramInk.Dashes(style.Dashes));

    private static DiagramWay Towards(RequirementWay way) => way switch
    {
        RequirementWay.Up => DiagramWay.Up,
        RequirementWay.Right => DiagramWay.Right,
        RequirementWay.Left => DiagramWay.Left,
        _ => DiagramWay.Down,
    };

    // ── What it works with ──────────────────────────────────────────────────

    /// <summary>One box measured: the words of each of its fields, the box they are set in, and the cell the layout placed it in.</summary>
    private sealed class Sized(
        RequirementNode node,
        IReadOnlyList<(RequirementFact Fact, DiagramWords Label, DiagramWords Said)> facts,
        DiagramBox laid)
    {
        public RequirementNode Node { get; } = node;

        public IReadOnlyList<(RequirementFact Fact, DiagramWords Label, DiagramWords Said)> Facts { get; } = facts;

        /// <summary>Its name and its fields measured into a box of compartments (<see cref="DiagramBox"/>).</summary>
        public DiagramBox Laid { get; } = laid;

        public Size Size => Laid.Size;

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

        /// <summary>The nodes offering what is left of each over-wide set of children.</summary>
        public DiagramSpill Spill { get; set; } = DiagramSpill.None;

        public Size Size { get; set; }
    }
}
