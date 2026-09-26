using System;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

/// <summary>
/// What each kind of participant is drawn as. An actor is a figure with its name written under it; every other kind is a box
/// with its name written in it, which is what a plain participant has always been — and a kind that is something in
/// particular, a boundary or a database, sets a mark of what it is before its name.
/// </summary>
internal static class SequenceGlyphs
{
    /// <summary>How big a figure is drawn, above the name.</summary>
    public const double Size = 32;

    /// <summary>Whether this kind is a figure with its name under it, rather than a box with its name in it — an actor alone.</summary>
    public static bool Figured(SequenceBuilder.Kind kind) => kind is SequenceBuilder.Kind.Actor;

    /// <summary>
    /// Whether this kind is a box with a mark of what it is set before its name — UML's boundary, control and entity, and the
    /// database, collections and queue. Mermaid draws these as the mark alone, or as a shape the name is squeezed into; a box
    /// holding both says what it is as plainly, lines up with every other participant, and always has room for its name.
    /// </summary>
    public static bool Iconed(SequenceBuilder.Kind kind) =>
        kind is SequenceBuilder.Kind.Boundary or SequenceBuilder.Kind.Control or SequenceBuilder.Kind.Entity
            or SequenceBuilder.Kind.Database or SequenceBuilder.Kind.Collections or SequenceBuilder.Kind.Queue;

    /// <summary>How big the mark set before a name is drawn.</summary>
    public const double Icon = 16;

    /// <summary>The mark saying what kind of participant this is, drawn in <paramref name="bounds"/> before its name.</summary>
    public static Geometry Mark(SequenceBuilder.Kind kind, Rect bounds) => kind switch
    {
        SequenceBuilder.Kind.Database => DiagramShapes.Outline(DiagramShape.Cylinder,
                                                       new Rect(bounds.X + (bounds.Width * 0.1), bounds.Y, bounds.Width * 0.8, bounds.Height)),
        SequenceBuilder.Kind.Collections => Collected(bounds),
        SequenceBuilder.Kind.Queue => DiagramCard.Queued(new Rect(bounds.X, bounds.Y + (bounds.Height * 0.2), bounds.Width, bounds.Height * 0.6)),
        _ => Figure(kind, bounds),
    };

    /// <summary>The box a participant that is not a figure is drawn as.</summary>
    public static Geometry Shape(Rect bounds) => Frozen(new RectangleGeometry(bounds, Corner, Corner));

    /// <summary>The figure a kind is drawn as: an actor's above its name, and the others' as the mark before it.</summary>
    public static Geometry Figure(SequenceBuilder.Kind kind, Rect bounds) => kind switch
    {
        SequenceBuilder.Kind.Boundary => Bounded(bounds),
        SequenceBuilder.Kind.Control => Controlled(bounds),
        SequenceBuilder.Kind.Entity => Held(bounds),
        _ => Standing(bounds),
    };



    /// <summary>How round the corner of a plain participant is.</summary>
    private const double Corner = 4;

    /// <summary>How far the box behind a collection is set back from the one in front of it.</summary>
    private const double Offset = 6;

    /// <summary>A figure: a head, a body, two arms and two legs.</summary>
    private static Geometry Standing(Rect bounds)
    {
        var head = bounds.Height / 5;
        var middle = bounds.X + (bounds.Width / 2);
        var neck = bounds.Y + (head * 2);
        var hips = bounds.Y + (bounds.Height * 0.62);

        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(new Point(middle, bounds.Y + head), head, head));
        group.Children.Add(new LineGeometry(new Point(middle, neck), new Point(middle, hips)));
        group.Children.Add(new LineGeometry(new Point(middle - (head * 1.4), neck + (head / 2)),
                                            new Point(middle + (head * 1.4), neck + (head / 2))));
        group.Children.Add(new LineGeometry(new Point(middle, hips), new Point(middle - (head * 1.2), bounds.Bottom)));
        group.Children.Add(new LineGeometry(new Point(middle, hips), new Point(middle + (head * 1.2), bounds.Bottom)));

        return Frozen(group);
    }

    /// <summary>UML's boundary: a bar, and a circle held off it.</summary>
    private static Geometry Bounded(Rect bounds)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) / 3;
        var middle = bounds.Y + (bounds.Height / 2);
        var bar = bounds.X + (bounds.Width / 6);

        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(new Point(bar, middle - radius), new Point(bar, middle + radius)));
        group.Children.Add(new LineGeometry(new Point(bar, middle), new Point(bounds.Right - (radius * 2), middle)));
        group.Children.Add(new EllipseGeometry(new Point(bounds.Right - radius, middle), radius, radius));

        return Frozen(group);
    }

    /// <summary>UML's control: a circle with the tick of an arrow at the top of it.</summary>
    private static Geometry Controlled(Rect bounds)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) / 2.6;
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(middle, radius, radius));
        group.Children.Add(new LineGeometry(new Point(middle.X - (radius / 2), middle.Y - radius + (radius / 4)),
                                            new Point(middle.X, middle.Y - radius)));
        group.Children.Add(new LineGeometry(new Point(middle.X, middle.Y - radius),
                                            new Point(middle.X - (radius / 2), middle.Y - radius - (radius / 4))));

        return Frozen(group);
    }

    /// <summary>UML's entity: a circle standing on a line.</summary>
    private static Geometry Held(Rect bounds)
    {
        var radius = Math.Min(bounds.Width, bounds.Height) / 2.6;
        var middle = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2) - (radius / 4));

        var group = new GeometryGroup();
        group.Children.Add(new EllipseGeometry(middle, radius, radius));
        group.Children.Add(new LineGeometry(new Point(middle.X - radius, middle.Y + radius + 2),
                                            new Point(middle.X + radius, middle.Y + radius + 2)));

        return Frozen(group);
    }

    /// <summary>Several of a thing: one box behind another.</summary>
    private static Geometry Collected(Rect bounds)
    {
        var offset = Math.Min(Offset, bounds.Width / 4);
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(bounds.X + offset, bounds.Y,
                                                          Math.Max(0, bounds.Width - offset),
                                                          Math.Max(0, bounds.Height - offset)), 1, 1));
        group.Children.Add(new RectangleGeometry(new Rect(bounds.X, bounds.Y + offset,
                                                          Math.Max(0, bounds.Width - offset),
                                                          Math.Max(0, bounds.Height - offset)), 1, 1));

        return Frozen(group);
    }



    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
