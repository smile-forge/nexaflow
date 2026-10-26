namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// An outline as a diagram draws one: filled, stroked, or both — said the way the kit says it everywhere, as a fill, an ink and a
/// thickness, and made into the stroke the medium draws it with.
/// </summary>
internal static class DiagramMarks
{
    /// <summary>
    /// <paramref name="shape"/> filled with <paramref name="fill"/> and traced in <paramref name="ink"/> at <paramref name="thickness"/>. A
    /// stroke with no ink of its own but a thickness traces in whatever the theme hands it; one with neither is no stroke at all, and
    /// the outline is only filled — in the theme's colour, where nothing fills it either.
    /// </summary>
    public static GeometryMark Outlined(ILayoutMedium medium, LayoutShape shape, LayoutInk? fill, LayoutInk? ink, double thickness,
                                                double[]? dashes = null, StrokeEnds? ends = null) =>
        new(shape, fill, ink is null && thickness <= 0 ? null : medium.Stroke(ink, thickness, dashes, ends));

    /// <summary><paramref name="shape"/> filled, in whatever colour the content did not ask for where it names none.</summary>
    public static GeometryMark Filled(LayoutShape shape, LayoutInk? fill = null) => new(shape, fill, null);
}
