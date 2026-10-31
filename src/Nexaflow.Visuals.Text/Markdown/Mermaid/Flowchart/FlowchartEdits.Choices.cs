using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Markdown.Layout.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a chart offers over one of its nodes, and what choosing it writes.
///
/// <para>
/// A node's shape and its colour are drawn as no words anywhere, so no caret lands in either and nothing can be typed into them:
/// being offered them is the only way a reader reaches them at all. Every shape Mermaid draws is offered, each drawn as the lines
/// it is drawn with, and the colours are the shared swatch bank as the theme tunes it — either is matched by eye, so neither is
/// named until a reader asks what one is.
/// </para>
/// <para>
/// Each is written where the chart already says it. A node's shape is the last one any line gave it, so a new one is given to
/// whatever said it last: the brackets round its label, where the shape chosen has brackets to be written in, else the
/// <c>shape:</c> of an <c>id@{ … }</c> line. Where nothing said it at all, a line of its own goes in after the last line naming
/// the node, which is after everything that could have said it. A fill is a <c>style</c> line's, written into the one styling
/// that node and no other — never into a line naming other nodes too, which would colour them with it.
/// </para>
/// </summary>
internal sealed partial class FlowchartEdits : IContentLanguage
{
    /// <summary>What a chart's own offers open with, so the one chosen is told from every other language's.</summary>
    private const string Chose = "flowchart.";



    /// <summary>Every shape Mermaid draws, which is every one but the one that is no shape.</summary>
    private static readonly MermaidShape[] Drawable =
        [.. Enum.GetValues<MermaidShape>().Where(shape => shape != MermaidShape.None)];

    // A geometry belongs to the thread that made it, and every window's thread shows ribbons of its own.
    private static readonly ThreadLocal<Dictionary<MermaidShape, Geometry>> ShapeIcons = new(() => []);

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask)
    {
        if (ask.IsReadOnly || ask.Root is not { } root) return [];
        if (Named(ask.Part) is not { Length: > 0 } id || Charted(root, id) is not { } node) return [];

        return [Shapes(node.Shape), Colours(Filling(root, id))];
    }

    // ── What is offered ─────────────────────────────────────────────────────

    /// <summary>
    /// Every shape a node may be drawn as, each drawn as itself, the one it is drawn as now picked out — and the one chosen written where
    /// the chart says the shape last.
    /// </summary>
    private static MiniRibbonChoice Shapes(MermaidShape drawn) =>
        new(Chose + "shape", Str.Get("Text.Flowchart.Shape"),
            [.. Drawable.Select(shape => new MiniRibbonOption(MermaidShapes.Wording(shape)!, ReadAs(shape)) { Shape = ShapeIcon(shape), Letters = Lettered(shape) })],
            static (edit, option) => OfferedOver(edit) is { } id ? Shaped(edit, id, option.Id) : null)
        {
            Current = MermaidShapes.Wording(drawn),
        };

    /// <summary>
    /// The colours a node may be filled with — the shared swatch bank, and the clear one that takes a fill back out again — the one it is
    /// filled with now picked out, and the one chosen written into the line styling that node.
    /// </summary>
    private static MiniRibbonChoice Colours(string? filled)
    {
        var said = DiagramSwatches.Shade(filled);
        var clear = new MiniRibbonOption(DiagramSwatches.Clear.ToLowerInvariant(), Str.Get("Text.Flowchart.NoFill")) { Shade = DiagramSwatches.Nothing };
        var options = new List<MiniRibbonOption> { clear };
        var current = filled is null ? clear.Id : null;

        foreach (var (name, label, shade) in DiagramSwatches.Bank())
        {
            options.Add(new MiniRibbonOption(name.ToLowerInvariant(), label) { Shade = shade });
            if (current is null && said == shade) current = name.ToLowerInvariant();
        }

        return new MiniRibbonChoice(Chose + "fill", Str.Get("Text.Flowchart.Fill"), options,
                                    static (edit, option) => OfferedOver(edit) is { } id ? Coloured(edit, id, option.Id) : null)
        {
            Current = current,
        };
    }

    /// <summary>What a shape is called where a reader has to read it, in the window's language.</summary>
    private static string ReadAs(MermaidShape shape) => shape switch
    {
        MermaidShape.Rectangle => Str.Get("Text.Shape.Rectangle"),
        MermaidShape.Rounded => Str.Get("Text.Shape.Rounded"),
        MermaidShape.Stadium => Str.Get("Text.Shape.Stadium"),
        MermaidShape.Subroutine => Str.Get("Text.Shape.Subroutine"),
        MermaidShape.Cylinder => Str.Get("Text.Shape.Cylinder"),
        MermaidShape.Circle => Str.Get("Text.Shape.Circle"),
        MermaidShape.DoubleCircle => Str.Get("Text.Shape.DoubleCircle"),
        MermaidShape.Asymmetric => Str.Get("Text.Shape.Asymmetric"),
        MermaidShape.Diamond => Str.Get("Text.Shape.Diamond"),
        MermaidShape.Hexagon => Str.Get("Text.Shape.Hexagon"),
        MermaidShape.Parallelogram => Str.Get("Text.Shape.Parallelogram"),
        MermaidShape.ParallelogramAlt => Str.Get("Text.Shape.ParallelogramAlt"),
        MermaidShape.Trapezoid => Str.Get("Text.Shape.Trapezoid"),
        MermaidShape.TrapezoidAlt => Str.Get("Text.Shape.TrapezoidAlt"),
        MermaidShape.Document => Str.Get("Text.Shape.Document"),
        MermaidShape.LinedDocument => Str.Get("Text.Shape.LinedDocument"),
        MermaidShape.StackedDocument => Str.Get("Text.Shape.StackedDocument"),
        MermaidShape.TaggedDocument => Str.Get("Text.Shape.TaggedDocument"),
        MermaidShape.Card => Str.Get("Text.Shape.Card"),
        MermaidShape.NotchedPentagon => Str.Get("Text.Shape.NotchedPentagon"),
        MermaidShape.LinedRectangle => Str.Get("Text.Shape.LinedRectangle"),
        MermaidShape.DividedRectangle => Str.Get("Text.Shape.DividedRectangle"),
        MermaidShape.WindowPane => Str.Get("Text.Shape.WindowPane"),
        MermaidShape.TaggedRectangle => Str.Get("Text.Shape.TaggedRectangle"),
        MermaidShape.StackedRectangle => Str.Get("Text.Shape.StackedRectangle"),
        MermaidShape.SlopedRectangle => Str.Get("Text.Shape.SlopedRectangle"),
        MermaidShape.Delay => Str.Get("Text.Shape.Delay"),
        MermaidShape.CurvedTrapezoid => Str.Get("Text.Shape.CurvedTrapezoid"),
        MermaidShape.BowTie => Str.Get("Text.Shape.BowTie"),
        MermaidShape.Flag => Str.Get("Text.Shape.Flag"),
        MermaidShape.Triangle => Str.Get("Text.Shape.Triangle"),
        MermaidShape.FlippedTriangle => Str.Get("Text.Shape.FlippedTriangle"),
        MermaidShape.Hourglass => Str.Get("Text.Shape.Hourglass"),
        MermaidShape.Bolt => Str.Get("Text.Shape.Bolt"),
        MermaidShape.Fork => Str.Get("Text.Shape.Fork"),
        MermaidShape.SmallCircle => Str.Get("Text.Shape.SmallCircle"),
        MermaidShape.FramedCircle => Str.Get("Text.Shape.FramedCircle"),
        MermaidShape.FilledCircle => Str.Get("Text.Shape.FilledCircle"),
        MermaidShape.CrossedCircle => Str.Get("Text.Shape.CrossedCircle"),
        MermaidShape.HorizontalCylinder => Str.Get("Text.Shape.HorizontalCylinder"),
        MermaidShape.LinedCylinder => Str.Get("Text.Shape.LinedCylinder"),
        MermaidShape.DataStore => Str.Get("Text.Shape.DataStore"),
        MermaidShape.Bucket => Str.Get("Text.Shape.Bucket"),
        MermaidShape.Brace => Str.Get("Text.Shape.Brace"),
        MermaidShape.BraceRight => Str.Get("Text.Shape.BraceRight"),
        MermaidShape.Braces => Str.Get("Text.Shape.Braces"),
        MermaidShape.Browser => Str.Get("Text.Shape.Browser"),
        MermaidShape.Console => Str.Get("Text.Shape.Console"),
        MermaidShape.Folder => Str.Get("Text.Shape.Folder"),
        MermaidShape.Person => Str.Get("Text.Shape.Person"),
        MermaidShape.Cloud => Str.Get("Text.Shape.Cloud"),
        MermaidShape.Bang => Str.Get("Text.Shape.Bang"),
        MermaidShape.Text => Str.Get("Text.Shape.Text"),
        _ => shape.ToString(),
    };

    /// <summary>
    /// A shape drawn as itself in the box a ribbon button gives it, or null for the one shape there is no picture of. Kept once
    /// drawn: a right-click asks for every shape there is, and not one of them turns on which node the pointer is over.
    /// </summary>
    private static Geometry? ShapeIcon(MermaidShape shape)
    {
        if (shape == MermaidShape.Text) return null;

        var kept = ShapeIcons.Value!;

        return kept.TryGetValue(shape, out var already)
            ? already
            : kept[shape] = ((WpfShape)DiagramShapes.AsPicture(WpfMedium.OnThisThread, DiagramShapes.For(shape), Side)).Geometry;
    }

    /// <summary>
    /// The letters a shape is drawn as where there is no shape to draw it by: words with nothing round them, whose whole shape is
    /// having none, so a picture of what holds them would be the rectangle standing next to it.
    /// </summary>
    private static string? Lettered(MermaidShape shape) => shape == MermaidShape.Text ? "Txt" : null;

    /// <summary>How wide and high a shape is on a button, which is what a ribbon gives a picture.</summary>
    private const double Side = 16;

    // ── What was chosen ─────────────────────────────────────────────────────

    /// <summary>The node a control of the ribbon was used over — named, and a node of the chart — or null where it is none.</summary>
    private static string? OfferedOver(ContentEdit edit) =>
        Named(edit.Part) is { Length: > 0 } id && Charted(edit.Root, id) is not null ? id : null;

    /// <summary>
    /// The shape a node is drawn as given a new one, where the chart says it last — anywhere earlier being written over by what is
    /// already said after it.
    /// </summary>
    private static ContentChange? Shaped(ContentEdit edit, string id, string word)
    {
        if (MermaidShapes.Named(word) is not { } shape) return null;

        var last = Shaping(edit.Root, id).LastOrDefault();
        var pair = MermaidShapes.Nodes.FirstOrDefault(node => node.Shape == shape);

        // The brackets round a label say the shape, where the one chosen has brackets of its own to be written in.
        if (last is { Brackets: true } && pair.Open is { Length: > 0 }
            && last.Part.Children.FirstOrDefault(child => child.Role == Roles.Open) is { } open
            && last.Part.Children.LastOrDefault(child => child.Role == Roles.Close) is { } close)
            return Changed(edit, [new ContentWrite(open, pair.Open), new ContentWrite(close, pair.Close)]);

        // A shape: already written says it instead, whatever it said before.
        if (last is { Brackets: false }) return Changed(edit, [new ContentWrite(last.Part, word)]);

        return Told(edit, id, "shape", word, last?.Part);
    }

    /// <summary>
    /// A node given a fill, which a <c>style</c> line says: written into the line styling that node alone, into a line of its own
    /// at the foot of the chart where there is none, and out again — the line with it, where the fill was all the line said — for
    /// the clear colour.
    /// </summary>
    private static ContentChange? Coloured(ContentEdit edit, string id, string name)
    {
        var clear = string.Equals(name, DiagramSwatches.Clear, StringComparison.OrdinalIgnoreCase);
        var shade = clear ? null : DiagramSwatches.Hex(name);

        if (!clear && shade is null) return null;

        var styled = Styling(edit.Root, id).LastOrDefault();

        if (styled is null)
            return shade is null
                ? ContentChange.Stay(edit.State)
                : Appended(edit, MermaidStyling.StyleWord + " " + id + " fill:" + shade);

        if (Setting(styled, "fill") is not { } written)
            return shade is null ? ContentChange.Stay(edit.State) : Changed(edit, [Set(styled, "fill", shade, ":", ",")]);

        if (shade is not null) return Changed(edit, [new ContentWrite(written.Part(MermaidRoles.Value) ?? written, shade)]);

        return Properties(styled) is { } properties && properties.Children.Count(child => child.Kind == MermaidKinds.Property) > 1
            ? Changed(edit, [Cleared(properties, written)])
            : Dropped(edit, styled);
    }

    // ── What the chart says now ─────────────────────────────────────────────

    /// <summary>
    /// What the node a gesture landed on is called — the name of the node written there, or of the <c>id@{ … }</c> line whose label
    /// was drawn — or null where the gesture landed on something that is no node.
    /// </summary>
    private static string? Named(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind is FlowchartKinds.Node or FlowchartKinds.Said) return Calls(at);

        return null;
    }

    /// <summary>
    /// The node of the chart a name belongs to — what every line naming it amounts to — or null for a name that is no node of it,
    /// which is how a subgraph's own name is told from a node's.
    /// </summary>
    private static FlowchartGraphNode? Charted(ContentPart root, string id) =>
        root.SelfAndDescendants().Select(part => part.Node).OfType<FlowchartGraphNode>().FirstOrDefault(node => node.Id == id);

    /// <summary>One place the chart says a node's shape: the brackets round a label, or the value a <c>shape:</c> is set to.</summary>
    private sealed record ShapeSaid(ContentPart Part, bool Brackets);

    /// <summary>Every place the chart says a node's shape, in the order they are written.</summary>
    private static IEnumerable<ShapeSaid> Shaping(ContentPart root, string id)
    {
        foreach (var part in root.SelfAndDescendants())
        {
            if (part.Derived) continue;

            if (part.Kind == FlowchartKinds.Node && Calls(part) == id
                && part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label) is { } label
                && label.Children.Any(child => child.Role == Roles.Open))
                yield return new ShapeSaid(label, true);

            if (part.Kind == FlowchartKinds.Said && Calls(part) == id && Setting(part, "shape")?.Part(MermaidRoles.Value) is { } value)
                yield return new ShapeSaid(value, false);
        }
    }

    /// <summary>The fill the line styling a node alone says it has, or null where no such line says one.</summary>
    private static string? Filling(ContentPart root, string id) =>
        Styling(root, id).Select(line => Setting(line, "fill")?.Part(MermaidRoles.Value))
                         .LastOrDefault(value => value is not null) is { } written
            ? Valued(written)
            : null;

    /// <summary>
    /// What a property is set to, as the characters a reader wrote them: the words between its quotes, where it is quoted, else
    /// the whole of it with its entities read.
    /// </summary>
    private static string Valued(ContentPart value) =>
        value.Kind == MermaidKinds.Quoted ? value.Words()?.Text ?? string.Empty : MermaidText.Bare(value.Text);

    /// <summary>Every <c>style</c> line styling a node and no other, in the order they are written.</summary>
    private static IEnumerable<ContentPart> Styling(ContentPart root, string id) =>
        root.SelfAndDescendants()
            .Where(part => part is { Derived: false, Kind: FlowchartKinds.Style }
                           && FlowchartGrammar.Styling.Ids(part) is [var only] && only == id);

    /// <summary>Every <c>id@{ … }</c> line about a node, in the order they are written.</summary>
    private static IEnumerable<ContentPart> Metadata(ContentPart root, string id) =>
        root.SelfAndDescendants().Where(part => part is { Derived: false, Kind: FlowchartKinds.Said } && Calls(part) == id);

    /// <summary>Every node written with a name, in the order they are written.</summary>
    private static IEnumerable<ContentPart> Mentions(ContentPart root, string id) =>
        root.SelfAndDescendants().Where(part => part is { Derived: false, Kind: FlowchartKinds.Node } && Calls(part) == id);

    /// <summary>What a statement names, by the first name written on it.</summary>
    private static string? Calls(ContentPart statement) =>
        statement.SelfAndDescendants().FirstOrDefault(part => part.Kind == Kinds.Words && part.Role == FlowchartRoles.Id)?.Text;

    /// <summary>The property of a statement that sets a name, or null where it sets none.</summary>
    private static ContentPart? Setting(ContentPart statement, string name) =>
        Properties(statement)?.Children
            .FirstOrDefault(property => property.Kind == MermaidKinds.Property
                                        && string.Equals(property.Part(Roles.Name)?.Text, name, StringComparison.OrdinalIgnoreCase)
                                        && property.Part(MermaidRoles.Value) is { Length: > 0 });

    /// <summary>What a statement sets, where it sets anything.</summary>
    private static ContentPart? Properties(ContentPart statement) =>
        statement.Children.FirstOrDefault(part => part.Kind == MermaidKinds.Properties);

    // ── Writing it ──────────────────────────────────────────────────────────

    /// <summary>
    /// A node told something by a line of its own: put into the <c>id@{ … }</c> already written about it after everything that
    /// could have said the same thing, and written as a line of its own after the last line naming the node where there is none.
    /// </summary>
    private static ContentChange? Told(ContentEdit edit, string id, string name, string value, ContentPart? after)
    {
        var from = after?.End ?? edit.Root.Start;

        if (Metadata(edit.Root, id).LastOrDefault(line => line.Start >= from) is { } written)
            return Changed(edit, [Set(written, name, value, ": ", ", ")]);

        if (Lined(after ?? Mentions(edit.Root, id).LastOrDefault()) is not { } line) return null;

        var newline = Newline(edit);
        var said = Indent(line) + id + "@{ " + name + ": " + value + " }";
        var whole = line.Node.Print();

        return Changed(edit, [Ends(line)
            ? new ContentWrite(line, whole + said + newline)
            : new ContentWrite(line, whole + newline + said)]);
    }

    /// <summary>
    /// A property written into what a statement sets: before the ones already there, between the braces of an <c>id@{ }</c> that
    /// sets nothing yet, and after the name a <c>style</c> line is about where it styles it with nothing yet.
    /// </summary>
    private static ContentWrite Set(ContentPart statement, string name, string value, string between, string after)
    {
        if (Properties(statement) is { } properties)
            return new ContentWrite(properties.Start, 0, name + between + value + after);

        if (statement.Children.FirstOrDefault(part => part.Role == Roles.Open && part.Text == "{") is { } open
            && statement.Children.FirstOrDefault(part => part.Role == Roles.Close && part.Text == "}") is { } close)
            return new ContentWrite(open.End, close.Start - open.End, " " + name + between + value + " ");

        return statement.Children.LastOrDefault() is { Kind: Kinds.Space } tail
            ? new ContentWrite(tail.Start, tail.Length, " " + name + between + value)
            : new ContentWrite(statement.End, 0, " " + name + between + value);
    }

    /// <summary>A property taken out, and the comma beside it with it, so what is left still reads.</summary>
    private static ContentWrite Cleared(ContentPart properties, ContentPart property)
    {
        var set = properties.Children.Where(child => child.Kind == MermaidKinds.Property).ToList();
        var at = set.IndexOf(property);

        return at > 0
            ? new ContentWrite(set[at - 1].End, property.End - set[at - 1].End, string.Empty)
            : new ContentWrite(property.Start, (at + 1 < set.Count ? set[at + 1].Start : properties.End) - property.Start, string.Empty);
    }

    /// <summary>
    /// A line taken out, with the line ending closing it — or, where it is the last line and has none of its own, the ending of
    /// the line above it, which would otherwise be left closing nothing.
    /// </summary>
    private static ContentChange? Dropped(ContentEdit edit, ContentPart statement)
    {
        if (Lined(statement) is not { } line) return null;

        var writes = new List<ContentWrite> { new(line, string.Empty) };

        if (!Ends(line) && Above(edit.Root, line) is { } above && Ending(above) is { } ending)
            writes.Add(new ContentWrite(ending, string.Empty));

        return Changed(edit, writes);
    }

    /// <summary>A line of its own at the foot of the chart, indented as its lines are.</summary>
    private static ContentChange Appended(ContentEdit edit, string said)
    {
        var lines = Lines(edit.Root).ToList();
        var indent = lines.Select(Indent).LastOrDefault(given => given.Length > 0) ?? string.Empty;
        var newline = Newline(edit);
        var ends = edit.Root.End;
        var closed = ends > edit.Root.Start && edit.State.Source[ends - 1] == '\n';

        return Changed(edit, [new ContentWrite(ends, 0, (closed ? string.Empty : newline) + indent + said + newline)]);
    }

    /// <summary>What the writes come to, the caret left where it was — a choice is made with the pointer, not the caret.</summary>
    private static ContentChange Changed(ContentEdit edit, IReadOnlyList<ContentWrite> writes) =>
        new(writes, After(writes, edit.State.Caret));

    /// <summary>Where an offset in the chart as it stood is, once the writes are made.</summary>
    private static int After(IEnumerable<ContentWrite> writes, int offset) =>
        offset + writes.Where(write => write.End <= offset).Sum(write => write.Text.Length - write.Length);

    // ── Its lines ───────────────────────────────────────────────────────────

    /// <summary>Every line of the chart, in the order they are written — a subgraph's own among them.</summary>
    private static IEnumerable<ContentPart> Lines(ContentPart root) =>
        root.SelfAndDescendants().Where(part => part.Kind == MermaidKinds.Line);

    /// <summary>The line a part is written on.</summary>
    private static ContentPart? Lined(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == MermaidKinds.Line) return at;

        return null;
    }

    /// <summary>The line written before another, or null where it is the first.</summary>
    private static ContentPart? Above(ContentPart root, ContentPart line) =>
        Lines(root).Where(part => part.Start < line.Start).MaxBy(part => part.Start);

    /// <summary>The space a line starts with.</summary>
    private static string Indent(ContentPart line) =>
        line.Children.FirstOrDefault() is { Kind: Kinds.Space } space && space.Start == line.Start ? space.Text : string.Empty;

    /// <summary>The line ending that closes a line, where it has one.</summary>
    private static ContentPart? Ending(ContentPart line) =>
        line.Children.LastOrDefault(part => part.Kind != MermaidKinds.Line) is { Kind: Kinds.Space } space && space.Text.EndsWith('\n')
            ? space
            : null;

    /// <summary>Whether a line is closed by a line ending of its own.</summary>
    private static bool Ends(ContentPart line) => Ending(line) is not null;

    /// <summary>The line ending the chart is written with.</summary>
    private static string Newline(ContentEdit edit) => edit.Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
