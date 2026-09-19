using System;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid.Sequence;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

/// <summary>
/// What each kind of participant is drawn as. UML's robustness marks — the figure, the boundary, the control and the entity —
/// are a figure with the name written under them; the rest are a shape with the name written in them, which is what a plain
/// participant has always been.
/// </summary>
internal static class SequenceGlyphs
{
    /// <summary>How big a figure is drawn, above the name.</summary>
    public const double Size = 32;

    /// <summary>Whether this kind is a figure with its name under it, rather than a shape with its name in it.</summary>
    public static bool Figured(SequenceKind kind) =>
        kind is SequenceKind.Actor or SequenceKind.Boundary or SequenceKind.Control or SequenceKind.Entity;

    /// <summary>The shape a participant of this kind is drawn as, for the kinds that hold their name.</summary>
    public static Geometry Shape(SequenceKind kind, Rect bounds) => kind switch
    {
        SequenceKind.Database => DiagramShapes.Outline(DiagramShape.Cylinder, bounds),
        SequenceKind.Collections => Collected(bounds),
        SequenceKind.Queue => DiagramCard.Queued(bounds),
        _ => Frozen(new RectangleGeometry(bounds, Corner, Corner)),
    };

    /// <summary>The room a shape of this kind leaves for the name inside it.</summary>
    public static Rect Inside(SequenceKind kind, Rect bounds) => kind switch
    {
        SequenceKind.Database => DiagramShapes.Inside(DiagramShape.Cylinder, bounds),
        SequenceKind.Collections => new Rect(bounds.X, bounds.Y + Offset,
                                             Math.Max(0, bounds.Width - Offset), Math.Max(0, bounds.Height - Offset)),
        SequenceKind.Queue => new Rect(bounds.X, bounds.Y, Math.Max(0, bounds.Width - (bounds.Height / 2)), bounds.Height),
        _ => bounds,
    };

    /// <summary>The figure drawn above the name, for the kinds that are one.</summary>
    public static Geometry Figure(SequenceKind kind, Rect bounds) => kind switch
    {
        SequenceKind.Boundary => Bounded(bounds),
        SequenceKind.Control => Controlled(bounds),
        SequenceKind.Entity => Held(bounds),
        _ => Standing(bounds),
    };

    /// <summary>
    /// The card a participant is drawn as, where a diagram draws one rather than a plain box — which is <see cref="DiagramCard"/>'s,
    /// since a structural C4 diagram draws the very same card as a box of its graph.
    /// </summary>
    public static DiagramCardShape Carded(SequenceCardShape shape) => shape switch
    {
        SequenceCardShape.Database => DiagramCardShape.Database,
        SequenceCardShape.Queue => DiagramCardShape.Queue,
        SequenceCardShape.Person => DiagramCardShape.Person,
        _ => DiagramCardShape.Box,
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
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(bounds.X + Offset, bounds.Y,
                                                          Math.Max(0, bounds.Width - Offset),
                                                          Math.Max(0, bounds.Height - Offset)), Corner, Corner));
        group.Children.Add(new RectangleGeometry(new Rect(bounds.X, bounds.Y + Offset,
                                                          Math.Max(0, bounds.Width - Offset),
                                                          Math.Max(0, bounds.Height - Offset)), Corner, Corner));

        return Frozen(group);
    }



    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
