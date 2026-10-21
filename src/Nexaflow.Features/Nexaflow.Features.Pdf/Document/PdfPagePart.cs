using System.Windows;
using UglyToad.PdfPig.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Part of a page as it is shown — one page of several printed to a sheet — as fractions of the shown page measured
/// from its top left, so a part is the same whatever size the page is drawn at and however the page is turned.
/// </summary>
internal readonly record struct PdfPagePart(double Left, double Top, double Width, double Height)
{
    public static PdfPagePart Whole { get; } = new(0, 0, 1, 1);

    /// <summary>
    /// A page cut into <paramref name="count"/> equal parts: side by side, left to right, when cut
    /// <paramref name="across"/>, otherwise one above another, top to bottom.
    /// </summary>
    public static IReadOnlyList<PdfPagePart> Split(int count, bool across)
    {
        if (count < 2)
            throw new ArgumentOutOfRangeException(nameof(count), count, "A page is split into two parts or more.");

        var share = 1.0 / count;
        return Enumerable.Range(0, count)
            .Select(i => across ? new PdfPagePart(i * share, 0, share, 1) : new PdfPagePart(0, i * share, 1, share))
            .ToList();
    }

    /// <summary>The lines a page is cut along into <see cref="Split"/>'s parts: one between each part and the next.</summary>
    public static IReadOnlyList<PdfPageCut> Cuts(int count, bool across) =>
        Split(count, across).Skip(1).Select(part => new PdfPageCut(across ? part.Left : part.Top, across)).ToList();

    /// <summary>This part of <paramref name="outer"/> — a part of a part — as a part of the whole page.</summary>
    public PdfPagePart Of(PdfPagePart outer) =>
        new(outer.Left + Left * outer.Width, outer.Top + Top * outer.Height, Width * outer.Width, Height * outer.Height);

    /// <summary>The part's size on a page shown <paramref name="shown"/> in size.</summary>
    public Size Of(Size shown) => new(shown.Width * Width, shown.Height * Height);

    /// <summary>
    /// The part in the page's default user space, for a page whose crop box is <paramref name="crop"/> and which is
    /// shown turned <paramref name="rotation"/> degrees clockwise.
    /// </summary>
    public PdfRectangle InUserSpace(PdfRectangle crop, int rotation)
    {
        var (x1, y1) = ToUser(crop, rotation, Left, Top);
        var (x2, y2) = ToUser(crop, rotation, Left + Width, Top + Height);
        return new PdfRectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    /// <summary>
    /// A point of the shown page, as fractions from its top left, in user space: the shown page is the crop box turned
    /// clockwise by the page's rotation.
    /// </summary>
    private static (double X, double Y) ToUser(PdfRectangle crop, int rotation, double across, double down) =>
        (((rotation % 360) + 360) % 360) switch
        {
            90  => (crop.Left + down * crop.Width, crop.Bottom + across * crop.Height),
            180 => (crop.Right - across * crop.Width, crop.Bottom + down * crop.Height),
            270 => (crop.Right - down * crop.Width, crop.Top - across * crop.Height),
            _   => (crop.Left + across * crop.Width, crop.Top - down * crop.Height),
        };
}
