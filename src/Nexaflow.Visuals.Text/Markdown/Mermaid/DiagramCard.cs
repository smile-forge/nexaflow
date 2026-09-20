using System;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The outline a card is drawn with, where a diagram draws something richer than a box of words.</summary>
internal enum DiagramCardShape
{
    /// <summary>A rounded box, which is what a card is unless it says otherwise.</summary>
    Box,

    /// <summary>A box with a head above it — somebody rather than something.</summary>
    Person,

    /// <summary>A cylinder — a store.</summary>
    Database,

    /// <summary>A box with one end rounded away — a queue, which things go into and come out of.</summary>
    Queue,
}

/// <summary>
/// The card a C4 element is drawn as, wherever it is drawn: as a lifeline's heading in a C4 sequence, and as a box of the
/// graph in a structural one. One piece rather than two, so the two never disagree about what a person looks like.
///
/// <para>
/// A card's outline and the room it leaves for what is written in it are answered together, because they have to agree: a
/// cylinder's caps and a person's head take room the words must not be set in.
/// </para>
/// </summary>
internal static class DiagramCard
{
    /// <summary>How much of a person's card is the head drawn above it.</summary>
    public const double Heading = 14;

    /// <summary>How much deeper a cylinder is than what is written in it, for the cap at each end.</summary>
    public const double Capping = 8;

    /// <summary>How round the corner of a plain card is.</summary>
    public const double Corner = 4;

    /// <summary>The outline a card of that shape is drawn with.</summary>
    public static Geometry Outline(DiagramCardShape shape, Rect bounds) => shape switch
    {
        DiagramCardShape.Database => DiagramShapes.Outline(DiagramShape.Cylinder, bounds),
        DiagramCardShape.Queue => Queued(bounds),
        DiagramCardShape.Person => Headed(bounds),
        _ => Frozen(new RectangleGeometry(bounds, Corner, Corner)),
    };

    /// <summary>The room it leaves for what is written in it.</summary>
    public static Rect Inside(DiagramCardShape shape, Rect bounds) => shape switch
    {
        DiagramCardShape.Database => DiagramShapes.Inside(DiagramShape.Cylinder, bounds),
        DiagramCardShape.Queue => new Rect(bounds.X, bounds.Y, Math.Max(0, bounds.Width - (bounds.Height / 2)), bounds.Height),
        DiagramCardShape.Person => new Rect(bounds.X, bounds.Y + Heading, bounds.Width, Math.Max(0, bounds.Height - Heading)),
        _ => bounds,
    };

    /// <summary>How much deeper than the words in it a card of that shape is.</summary>
    public static double Deeper(DiagramCardShape shape) => shape switch
    {
        DiagramCardShape.Person => Heading,
        DiagramCardShape.Database => Capping * 2,
        _ => 0,
    };

    /// <summary>
    /// And how much of that depth is above what is written in it — a person's head, the upper cap of a cylinder. What a card
    /// is above its words decides where the words land, which is what lets a row of cards line up on their boxes rather than
    /// on the top edges of their outlines.
    /// </summary>
    public static double Above(DiagramCardShape shape) => shape switch
    {
        DiagramCardShape.Person => Heading,
        DiagramCardShape.Database => Capping,
        _ => 0,
    };

    /// <summary>And how much wider.</summary>
    public static double Wider(DiagramCardShape shape) => shape == DiagramCardShape.Queue ? Capping * 2 : 0;

    /// <summary>A person's card: a head above the box holding what they are called.</summary>
    private static Geometry Headed(Rect bounds)
    {
        var body = new Rect(bounds.X, bounds.Y + Heading, bounds.Width, Math.Max(1, bounds.Height - Heading));
        var head = new EllipseGeometry(new Point(bounds.X + (bounds.Width / 2), body.Y), Heading, Heading);

        return Frozen(new CombinedGeometry(GeometryCombineMode.Union, head, new RectangleGeometry(body, Corner, Corner)));
    }

    /// <summary>A queue: a box with one end open, drawn as a half-round.</summary>
    public static Geometry Queued(Rect bounds)
    {
        var radius = bounds.Height / 2;
        var right = Math.Max(bounds.X, bounds.Right - radius);

        var figure = new PathFigure { StartPoint = new Point(bounds.X, bounds.Y), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(right, bounds.Y), true));
        figure.Segments.Add(new ArcSegment(new Point(right, bounds.Bottom), new Size(radius, radius), 0,
                                           false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X, bounds.Bottom), true));

        var path = new PathGeometry();
        path.Figures.Add(figure);

        return Frozen(path);
    }

    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
