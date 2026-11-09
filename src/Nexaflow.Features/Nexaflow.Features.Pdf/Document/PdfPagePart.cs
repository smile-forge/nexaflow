using System.Windows;
using UglyToad.PdfPig.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// How a page is cut into parts: <paramref name="Columns"/> side by side and <paramref name="Rows"/> one above another,
/// the parts read left to right, top to bottom.
/// </summary>
internal readonly record struct PdfSplitLayout(int Columns, int Rows)
{
    public int Count => Columns * Rows;

    /// <summary><paramref name="count"/> parts in a row: side by side when cut <paramref name="across"/>, otherwise one above another.</summary>
    public static PdfSplitLayout Strip(int count, bool across) => across ? new(count, 1) : new(1, count);

    /// <summary><paramref name="count"/> parts as a grid as many down as across — four, two by two; null when there is none.</summary>
    public static PdfSplitLayout? Square(int count)
    {
        var side = (int)Math.Round(Math.Sqrt(count));
        return side > 1 && side * side == count ? new PdfSplitLayout(side, side) : null;
    }
}

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

        return Split(PdfSplitLayout.Strip(count, across));
    }

    /// <summary>A page cut into equal parts as <paramref name="layout"/> says, read left to right, top to bottom.</summary>
    public static IReadOnlyList<PdfPagePart> Split(PdfSplitLayout layout)
    {
        if (layout.Columns < 1 || layout.Rows < 1 || layout.Count < 2)
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "A page is split into two parts or more.");

        var across = 1.0 / layout.Columns;
        var down = 1.0 / layout.Rows;
        return Enumerable.Range(0, layout.Count)
            .Select(i => new PdfPagePart(i % layout.Columns * across, i / layout.Columns * down, across, down).Snapped())
            .ToList();
    }

    /// <summary>The lines a page is cut along into <see cref="Split(int, bool)"/>'s parts: one between each part and the next.</summary>
    public static IReadOnlyList<PdfPageCut> Cuts(int count, bool across) => Cuts(PdfSplitLayout.Strip(count, across));

    /// <summary>The lines a page is cut along into <paramref name="layout"/>'s parts: down between columns, across between rows.</summary>
    public static IReadOnlyList<PdfPageCut> Cuts(PdfSplitLayout layout)
    {
        var parts = Split(layout);
        return parts.Select(part => part.Left).Where(at => at > 0).Distinct().Select(at => new PdfPageCut(at, true))
            .Concat(parts.Select(part => part.Top).Where(at => at > 0).Distinct().Select(at => new PdfPageCut(at, false)))
            .ToList();
    }

    /// <summary>This part of <paramref name="outer"/> — a part of a part — as a part of the whole page.</summary>
    public PdfPagePart Of(PdfPagePart outer) =>
        new(outer.Left + Left * outer.Width, outer.Top + Top * outer.Height, Width * outer.Width, Height * outer.Height);

    /// <summary>The part's size on a page shown <paramref name="shown"/> in size.</summary>
    public Size Of(Size shown) => new(shown.Width * Width, shown.Height * Height);

    /// <summary>Whether this part and <paramref name="other"/> share some of the page.</summary>
    public bool Overlaps(PdfPagePart other) =>
        Left < other.Left + other.Width && other.Left < Left + Width
        && Top < other.Top + other.Height && other.Top < Top + Height;

    /// <summary>
    /// The part with its fractions rounded to a trillionth, so parts that cutting a page different ways reaches are equal.
    /// </summary>
    public PdfPagePart Snapped() => new(Round(Left), Round(Top), Round(Width), Round(Height));

    // Adding nothing turns a negative zero into zero, which hashes as zero does.
    private static double Round(double fraction) => Math.Round(fraction, 12) + 0.0;

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
