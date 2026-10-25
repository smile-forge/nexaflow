using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Markdown.Layout.Mermaid.Sequence;

/// <summary>
/// What each kind of participant is drawn as. An actor is a figure with its name written under it; every other kind is a box
/// with its name written in it, which is what a plain participant has always been — and a kind that is something in
/// particular, a boundary or a database, sets a mark of what it is before its name.
/// </summary>
internal static class PortableSequenceGlyphs
{
    /// <summary>How big a figure is drawn, above the name.</summary>
    public const double Size = 32;

    /// <summary>Whether this kind is a figure with its name under it, rather than a box with its name in it — an actor alone.</summary>
    public static bool Figured(PortableSequenceDrawing.Kind kind) => kind is PortableSequenceDrawing.Kind.Actor;

    /// <summary>
    /// Whether this kind is a box with a mark of what it is set before its name — UML's boundary, control and entity, and the
    /// database, collections and queue. Mermaid draws these as the mark alone, or as a shape the name is squeezed into; a box
    /// holding both says what it is as plainly, lines up with every other participant, and always has room for its name.
    /// </summary>
    public static bool Iconed(PortableSequenceDrawing.Kind kind) =>
        kind is PortableSequenceDrawing.Kind.Boundary or PortableSequenceDrawing.Kind.Control or PortableSequenceDrawing.Kind.Entity
            or PortableSequenceDrawing.Kind.Database or PortableSequenceDrawing.Kind.Collections or PortableSequenceDrawing.Kind.Queue;

    /// <summary>How big the mark set before a name is drawn.</summary>
    public const double Icon = 16;

    /// <summary>The mark saying what kind of participant this is, drawn in <paramref name="bounds"/> before its name.</summary>
    public static LayoutShape Mark(ILayoutMedium medium, PortableSequenceDrawing.Kind kind, LayoutRect bounds) => kind switch
    {
        PortableSequenceDrawing.Kind.Database => PortableDiagramShapes.Outline(medium, DiagramShape.Cylinder,
                                                                               new LayoutRect(bounds.X + (bounds.Width * 0.1), bounds.Y, bounds.Width * 0.8, bounds.Height)),
        PortableSequenceDrawing.Kind.Collections => Collected(medium, bounds),
        PortableSequenceDrawing.Kind.Queue => PortableDiagramCard.Queued(medium, new LayoutRect(bounds.X, bounds.Y + (bounds.Height * 0.2), bounds.Width, bounds.Height * 0.6)),
        _ => Figure(medium, kind, bounds),
    };

    /// <summary>The box a participant that is not a figure is drawn as.</summary>
    public static LayoutShape Shape(ILayoutMedium medium, LayoutRect bounds) => medium.Rectangle(bounds, Corner, Corner);

    /// <summary>The figure a kind is drawn as: an actor's above its name, and the others' as the mark before it.</summary>
    public static LayoutShape Figure(ILayoutMedium medium, PortableSequenceDrawing.Kind kind, LayoutRect bounds) => kind switch
    {
        PortableSequenceDrawing.Kind.Boundary => Bounded(medium, bounds),
        PortableSequenceDrawing.Kind.Control => Controlled(medium, bounds),
        PortableSequenceDrawing.Kind.Entity => Held(medium, bounds),
        _ => Standing(medium, bounds),
    };



    /// <summary>How round the corner of a plain participant is.</summary>
    private const double Corner = 4;

    /// <summary>How far the box behind a collection is set back from the one in front of it.</summary>
    private const double Offset = 6;

    /// <summary>A figure: a head, a body, two arms and two legs.</summary>
    private static LayoutShape Standing(ILayoutMedium medium, LayoutRect bounds)
    {
        var head = bounds.Height / 5;
        var middle = bounds.X + (bounds.Width / 2);
        var neck = bounds.Y + (head * 2);
        var hips = bounds.Y + (bounds.Height * 0.62);

        var group = new List<LayoutShape>();
        group.Add(medium.Ellipse(new LayoutPoint(middle, bounds.Y + head), head, head));
        group.Add(medium.Line(new LayoutPoint(middle, neck), new LayoutPoint(middle, hips)));
        group.Add(medium.Line(new LayoutPoint(middle - (head * 1.4), neck + (head / 2)),
                                            new LayoutPoint(middle + (head * 1.4), neck + (head / 2))));
        group.Add(medium.Line(new LayoutPoint(middle, hips), new LayoutPoint(middle - (head * 1.2), bounds.Bottom)));
        group.Add(medium.Line(new LayoutPoint(middle, hips), new LayoutPoint(middle + (head * 1.2), bounds.Bottom)));

        return medium.Group(group);
    }

    /// <summary>UML's boundary: a bar, and a circle held off it.</summary>
    private static LayoutShape Bounded(ILayoutMedium medium, LayoutRect bounds)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) / 3;
        var middle = bounds.Y + (bounds.Height / 2);
        var bar = bounds.X + (bounds.Width / 6);

        var group = new List<LayoutShape>();
        group.Add(medium.Line(new LayoutPoint(bar, middle - radius), new LayoutPoint(bar, middle + radius)));
        group.Add(medium.Line(new LayoutPoint(bar, middle), new LayoutPoint(bounds.Right - (radius * 2), middle)));
        group.Add(medium.Ellipse(new LayoutPoint(bounds.Right - radius, middle), radius, radius));

        return medium.Group(group);
    }

    /// <summary>UML's control: a circle with the tick of an arrow at the top of it.</summary>
    private static LayoutShape Controlled(ILayoutMedium medium, LayoutRect bounds)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) / 2.6;
        var middle = new LayoutPoint(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        var group = new List<LayoutShape>();
        group.Add(medium.Ellipse(middle, radius, radius));
        group.Add(medium.Line(new LayoutPoint(middle.X - (radius / 2), middle.Y - radius + (radius / 4)),
                                            new LayoutPoint(middle.X, middle.Y - radius)));
        group.Add(medium.Line(new LayoutPoint(middle.X, middle.Y - radius),
                                            new LayoutPoint(middle.X - (radius / 2), middle.Y - radius - (radius / 4))));

        return medium.Group(group);
    }

    /// <summary>UML's entity: a circle standing on a line.</summary>
    private static LayoutShape Held(ILayoutMedium medium, LayoutRect bounds)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) / 2.6;
        var middle = new LayoutPoint(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2) - (radius / 4));

        var group = new List<LayoutShape>();
        group.Add(medium.Ellipse(middle, radius, radius));
        group.Add(medium.Line(new LayoutPoint(middle.X - radius, middle.Y + radius + 2),
                                            new LayoutPoint(middle.X + radius, middle.Y + radius + 2)));

        return medium.Group(group);
    }

    /// <summary>Several of a thing: one box behind another.</summary>
    private static LayoutShape Collected(ILayoutMedium medium, LayoutRect bounds)
    {
        var offset = Math.Min(Offset, bounds.Width / 4);
        var group = new List<LayoutShape>();
        group.Add(medium.Rectangle(new LayoutRect(bounds.X + offset, bounds.Y,
                                                          Math.Max(0, bounds.Width - offset),
                                                          Math.Max(0, bounds.Height - offset)), 1, 1));
        group.Add(medium.Rectangle(new LayoutRect(bounds.X, bounds.Y + offset,
                                                          Math.Max(0, bounds.Width - offset),
                                                          Math.Max(0, bounds.Height - offset)), 1, 1));

        return medium.Group(group);
    }



}