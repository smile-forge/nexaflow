using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Where a frame of a layout stands on the page: the point its nought lands on, and how what is measured in it is turned or
/// scaled on the way there.
///
/// <para>
/// Nearly every frame of nearly everything is only moved — a piece's anchor is its parent's with its own offset added — and for
/// those <see cref="Shaped"/> is null and every answer is the addition it always was. Only inside a piece drawn in a frame of
/// its own (<see cref="LayoutPaint"/> — a turned label, a formula's rotated box, a diagram drawn smaller to fit the room it was
/// given) is there a matrix to carry, and it is carried down to everything inside: what is painted turned or scaled is pressed,
/// caretted and washed turned or scaled, because the painter and every question asked of the layout go through the same frame.
/// </para>
/// </summary>
/// <param name="Anchor">Where the frame's nought lands on the page, past whatever <paramref name="Shaped"/> moves it by.</param>
/// <param name="Shaped">How what is measured in the frame is turned or scaled on its way to the page, or null where it is only moved.</param>
internal readonly record struct LayoutFrame(Vector Anchor, Matrix? Shaped)
{
    /// <summary>
    /// The frame of a piece placed at <paramref name="offset"/> inside this one, before anything of its own is applied — where
    /// the piece's box is measured.
    /// </summary>
    public LayoutFrame Placing(Vector offset) =>
        new(Anchor + (Shaped is { } shaped ? shaped.Transform(offset) : offset), Shaped);

    /// <summary>
    /// This frame with <paramref name="applied"/> applied inside it — where a piece drawn in a frame of its own measures its
    /// marks, its words, its shape and what it holds. Itself where nothing is.
    /// </summary>
    public LayoutFrame Inside(Matrix? applied) =>
        applied is not { } own ? this : new(Anchor, Shaped is { } shaped ? own * shaped : own);

    /// <summary>Where a point of the frame is on the page.</summary>
    public Point Out(Point local) => (Shaped is { } shaped ? shaped.Transform(local) : local) + Anchor;

    /// <summary>Where a rectangle of the frame is on the page — the box round where it lands, where the frame turns it.</summary>
    public Rect Out(Rect local)
    {
        if (local.IsEmpty) return local;
        if (Shaped is { } shaped) local = Rect.Transform(local, shaped);

        return new Rect(local.X + Anchor.X, local.Y + Anchor.Y, local.Width, local.Height);
    }

    /// <summary>
    /// Where a shape of the frame is on the page: itself, where the frame stands at the page's nought unturned — and otherwise
    /// grouped rather than given a transform of its own, so a transform the shape already carries still applies.
    /// </summary>
    public Geometry Out(Geometry local)
    {
        if (Shaped is null && Anchor is { X: 0, Y: 0 }) return local;

        Transform placing;
        if (Shaped is { } shaped)
        {
            shaped.Translate(Anchor.X, Anchor.Y);
            placing = new MatrixTransform(shaped);
        }
        else
        {
            placing = new TranslateTransform(Anchor.X, Anchor.Y);
        }

        var placed = new GeometryGroup { Transform = placing };
        placed.Children.Add(local);
        placed.Freeze();
        return placed;
    }

    /// <summary>
    /// Where a point of the page is in the frame. A frame that flattens everything to nothing has nowhere to answer, and gives
    /// the point back only moved.
    /// </summary>
    public Point Back(Point page)
    {
        var at = page - Anchor;
        if (Shaped is not { HasInverse: true } shaped) return at;

        shaped.Invert();
        return shaped.Transform(at);
    }
}
