using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The card a C4 element is drawn as, wherever it is drawn: as a lifeline's heading in a C4 sequence, and as a box of the
/// graph in a structural one. One piece rather than two, so the two never disagree about what a person looks like.
///
/// <para>
/// A card's outline and the room it leaves for what is written in it are answered together, because they have to agree: a
/// cylinder's caps and a person's head take room the words must not be set in.
/// </para>
/// </summary>
internal static class PortableDiagramCard
{
    /// <summary>How much of a person's card is the head drawn above it.</summary>
    public const double Heading = 14;

    /// <summary>How much deeper a cylinder is than what is written in it, for the cap at each end.</summary>
    public const double Capping = 8;

    /// <summary>How round the corner of a plain card is.</summary>
    public const double Corner = 4;

    /// <summary>The outline a card of that shape is drawn with.</summary>
    public static LayoutShape Outline(ILayoutMedium medium, DiagramCardShape shape, LayoutRect bounds) => shape switch
    {
        DiagramCardShape.Database => PortableDiagramShapes.Outline(medium, DiagramShape.Cylinder, bounds),
        DiagramCardShape.Queue => Queued(medium, bounds),
        DiagramCardShape.Person => Headed(medium, bounds),
        _ => medium.Rectangle(bounds, Corner, Corner),
    };

    /// <summary>The room it leaves for what is written in it.</summary>
    public static LayoutRect Inside(DiagramCardShape shape, LayoutRect bounds) => shape switch
    {
        DiagramCardShape.Database => PortableDiagramShapes.Inside(DiagramShape.Cylinder, bounds),
        DiagramCardShape.Queue => new LayoutRect(bounds.X, bounds.Y, Math.Max(0, bounds.Width - (bounds.Height / 2)), bounds.Height),
        DiagramCardShape.Person => new LayoutRect(bounds.X, bounds.Y + Heading, bounds.Width, Math.Max(0, bounds.Height - Heading)),
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
    private static LayoutShape Headed(ILayoutMedium medium, LayoutRect bounds)
    {
        var body = new LayoutRect(bounds.X, bounds.Y + Heading, bounds.Width, Math.Max(1, bounds.Height - Heading));
        var head = medium.Ellipse(new LayoutPoint(bounds.X + (bounds.Width / 2), body.Y), Heading, Heading);

        return medium.Combine(head, medium.Rectangle(body, Corner, Corner), ShapeCombine.Union);
    }

    /// <summary>A queue: a box with one end open, drawn as a half-round.</summary>
    public static LayoutShape Queued(ILayoutMedium medium, LayoutRect bounds)
    {
        var radius = bounds.Height / 2;
        var right = Math.Max(bounds.X, bounds.Right - radius);

        var pen = medium.Outline();
        pen.Begin(new LayoutPoint(bounds.X, bounds.Y), filled: true, closed: true);
        pen.LineTo(new LayoutPoint(right, bounds.Y));
        pen.ArcTo(new LayoutPoint(right, bounds.Bottom), radius, radius, 0, false, true);
        pen.LineTo(new LayoutPoint(bounds.X, bounds.Bottom));

        return pen.Close();
    }

}