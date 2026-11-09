using System.Windows;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>Where a page's content sits across the room its margins leave.</summary>
public enum PdfMarginAcross
{
    /// <summary>Centred between the left and right margins.</summary>
    Centre,

    /// <summary>Against the left margin.</summary>
    Left,
}

/// <summary>Where a page's content sits down the room its margins leave.</summary>
public enum PdfMarginDown
{
    /// <summary>Against the top margin, so a short page's content starts where every other page's does.</summary>
    Top,

    /// <summary>Centred between the top and bottom margins.</summary>
    Centre,
}

/// <summary>
/// Margins set on a page: how much room, in points, its content leaves at each edge of its paper, and where in the room
/// between them the content sits. The content is moved as one block — never reflowed — and made smaller only where it
/// would not otherwise fit; the paper keeps its size.
/// </summary>
internal readonly record struct PdfMargins(double Left, double Top, double Right, double Bottom, PdfMarginAcross Across, PdfMarginDown Down)
{
    /// <summary>The narrowest a content box is taken to be: a single hairline still has a size to scale by.</summary>
    private const double Epsilon = 0.01;

    /// <summary>The margin each edge is given until another is chosen, in millimetres.</summary>
    public const double DefaultMillimetres = 20;

    /// <summary>Points to the millimetre.</summary>
    public const double PointsPerMillimetre = 72 / 25.4;

    /// <summary>The room the margins leave on <paramref name="paper"/>; null when they leave none.</summary>
    public Rect? Room(Size paper)
    {
        var width = paper.Width - Left - Right;
        var height = paper.Height - Top - Bottom;
        return width > 0 && height > 0 ? new Rect(Left, Top, width, height) : null;
    }

    /// <summary>
    /// How much <paramref name="content"/> must be made smaller to fit the room on <paramref name="paper"/>: 1 where it
    /// already fits, never more; null where the margins leave no room.
    /// </summary>
    public double? Shrink(Rect content, Size paper)
    {
        if (Room(paper) is not { } room) return null;
        return Math.Min(1, Math.Min(room.Width / Math.Max(content.Width, Epsilon), room.Height / Math.Max(content.Height, Epsilon)));
    }

    /// <summary>
    /// The transform that moves <paramref name="content"/>, scaled by <paramref name="scale"/>, to its place in the room on
    /// <paramref name="paper"/>; null where the margins leave no room.
    /// </summary>
    public PdfPageTransform? Place(Rect content, Size paper, double scale)
    {
        if (Room(paper) is not { } room) return null;

        var width = content.Width * scale;
        var height = content.Height * scale;
        var x = Across == PdfMarginAcross.Centre ? room.X + (room.Width - width) / 2 : room.X;
        var y = Down == PdfMarginDown.Centre ? room.Y + (room.Height - height) / 2 : room.Y;
        return PdfPageTransform.Translate(-content.X, -content.Y)
            .Then(PdfPageTransform.Scaled(scale, scale))
            .Then(PdfPageTransform.Translate(x, y));
    }

    /// <summary>
    /// Where each page's content goes: each of <paramref name="pages"/> — its content's box and its paper, both in points
    /// as the page is shown — given the transform that puts it in its room, or null where the margins leave it none. With
    /// <paramref name="sameScale"/>, every page is made as small as the one that has to be made smallest, so the text is
    /// one size throughout; otherwise each only as small as it must be.
    /// </summary>
    public IReadOnlyList<PdfPageTransform?> Placements(IReadOnlyList<(Rect Content, Size Paper)> pages, bool sameScale)
    {
        var margins = this;
        var shrinks = pages.Select(page => margins.Shrink(page.Content, page.Paper)).ToList();
        var common = shrinks.OfType<double>().DefaultIfEmpty(1).Min();
        return pages.Select((page, at) => shrinks[at] is { } own ? margins.Place(page.Content, page.Paper, sameScale ? common : own) : null).ToList();
    }
}
