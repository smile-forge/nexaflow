using System;
using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

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
    public static Size Tabbed(DiagramWords? word, double pad, Size least) =>
        new(Math.Max(least.Width, (word?.Width ?? 0) + (pad * 2) + Notch),
            Math.Max(least.Height, (word?.Height ?? 0) + pad));

    /// <summary>The tab itself, in the top corner of <paramref name="bounds"/>.</summary>
    public static Geometry Tab(Rect bounds, Size tab)
    {
        var right = bounds.X + tab.Width;
        var bottom = bounds.Y + tab.Height;
        var cut = Math.Min(Notch, Math.Min(tab.Width, tab.Height));

        return Shaped(new Point(bounds.X, bounds.Y), new Point(right, bounds.Y), new Point(right, bottom - cut),
                      new Point(right - cut, bottom), new Point(bounds.X, bottom));
    }

    /// <summary>Where the word saying what kind of frame it is goes, set in the middle of the tab less its notch.</summary>
    public static Point Word(Rect bounds, Size tab, DiagramWords word) =>
        new(bounds.X + Math.Max(0, (tab.Width - Notch - word.Width) / 2),
            bounds.Y + Math.Max(0, (tab.Height - word.Height) / 2));

    /// <summary>Where what the frame holds its contents under is written, past the tab along the top of it.</summary>
    public static Rect Beside(Rect bounds, Size tab, double pad) =>
        new(bounds.X + tab.Width + pad, bounds.Y,
            Math.Max(0, bounds.Width - tab.Width - (pad * 2)), tab.Height);

    /// <summary>
    /// What the frame itself stands in: its border and its tab. What is drawn inside it stands in its own room, so a press
    /// lands on the message rather than on the frame round it, and one on the border or the tab means the frame.
    /// </summary>
    public static Geometry Round(Rect bounds, Size? tab = null)
    {
        var outline = new RectangleGeometry(bounds);
        if (bounds.Width <= Edge * 2 || bounds.Height <= Edge * 2) return Frozen(outline);

        var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outline,
                                        new RectangleGeometry(Rect.Inflate(bounds, -Edge, -Edge)));

        return tab is not { Width: > 0, Height: > 0 } corner
            ? Frozen(ring)
            : Frozen(new CombinedGeometry(GeometryCombineMode.Union, ring, Tab(bounds, corner)));
    }

    private static Geometry Shaped(params Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        for (var at = 1; at < points.Length; at++) figure.Segments.Add(new LineSegment(points[at], true));

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
