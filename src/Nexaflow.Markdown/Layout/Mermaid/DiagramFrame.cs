namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// The frame UML draws round a run of a diagram — a sequence diagram's <c>alt</c>, <c>loop</c> and <c>opt</c>: a rectangle
/// with a tab in its top corner saying what kind of frame it is, its corner cut away so the tab reads as a tab rather than a
/// box, and what it holds its contents under written beside it.
/// </summary>
internal static class DiagramFrame
{
    /// <summary>How much of the tab's far corner is cut away, which is what makes it a tab.</summary>
    public const double Notch = 8;

    /// <summary>How thick a frame's border is drawn, which is also how wide a press on it has to land.</summary>
    public const double Edge = 4;

    /// <summary>How big the tab has to be for the word in it, and never smaller than <paramref name="least"/> asks.</summary>
    public static LayoutSize Tabbed(DiagramWords? word, double pad, LayoutSize least) =>
        new(Math.Max(least.Width, (word?.Width ?? 0) + (pad * 2) + Notch),
            Math.Max(least.Height, (word?.Height ?? 0) + pad));

    /// <summary>The tab itself, in the top corner of <paramref name="bounds"/>.</summary>
    public static LayoutShape Tab(ILayoutMedium medium, LayoutRect bounds, LayoutSize tab)
    {
        var right = bounds.X + tab.Width;
        var bottom = bounds.Y + tab.Height;
        var cut = Math.Min(Notch, Math.Min(tab.Width, tab.Height));

        return Shaped(medium, new LayoutPoint(bounds.X, bounds.Y), new LayoutPoint(right, bounds.Y), new LayoutPoint(right, bottom - cut),
                      new LayoutPoint(right - cut, bottom), new LayoutPoint(bounds.X, bottom));
    }

    /// <summary>Where the word saying what kind of frame it is goes, set in the middle of the tab less its notch.</summary>
    public static LayoutPoint Word(LayoutRect bounds, LayoutSize tab, DiagramWords word) =>
        new(bounds.X + Math.Max(0, (tab.Width - Notch - word.Width) / 2),
            bounds.Y + Math.Max(0, (tab.Height - word.Height) / 2));

    /// <summary>Where what the frame holds its contents under is written, past the tab along the top of it.</summary>
    public static LayoutRect Beside(LayoutRect bounds, LayoutSize tab, double pad) =>
        new(bounds.X + tab.Width + pad, bounds.Y,
            Math.Max(0, bounds.Width - tab.Width - (pad * 2)), tab.Height);

    /// <summary>
    /// What the frame itself stands in: its border and its tab. What is drawn inside it stands in its own room, so a press
    /// lands on the message rather than on the frame round it, and one on the border or the tab means the frame.
    /// </summary>
    public static LayoutShape Round(ILayoutMedium medium, LayoutRect bounds, LayoutSize? tab = null)
    {
        var outline = medium.Rectangle(bounds);
        if (bounds.Width <= Edge * 2 || bounds.Height <= Edge * 2) return outline;

        var ring = medium.Combine(outline, medium.Rectangle(LayoutRect.Inflate(bounds, -Edge, -Edge)), ShapeCombine.Exclude);

        return tab is not { Width: > 0, Height: > 0 } corner
            ? ring
            : medium.Combine(ring, Tab(medium, bounds, corner), ShapeCombine.Union);
    }

    private static LayoutShape Shaped(ILayoutMedium medium, params LayoutPoint[] points)
    {
        var pen = medium.Outline();
        pen.Begin(points[0], filled: true, closed: true);
        for (var at = 1; at < points.Length; at++) pen.LineTo(points[at]);

        return pen.Close();
    }

}
