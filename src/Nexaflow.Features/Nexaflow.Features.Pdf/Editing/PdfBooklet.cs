namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// A booklet printed as spreads, unfolded into reading order. A saddle-stitched booklet of <c>n</c> pages is printed two
/// to a side, the sheets nested and folded: the first side holds the last page and the first, the second the second and
/// the second-last, and so on in to the centre spread, where the pages meet in order. So a twelve-page booklet's six
/// sides are 12|1, 2|11, 10|3, 4|9, 8|5 and 6|7.
/// </summary>
internal static class PdfBooklet
{
    /// <summary>
    /// <paramref name="halves"/> — each side's first half then its second, side after side — in the order they are read.
    /// </summary>
    public static IReadOnlyList<T> Unfold<T>(IReadOnlyList<T> halves)
    {
        if (halves.Count % 2 != 0)
            throw new ArgumentException("A booklet's sides are each two halves.", nameof(halves));

        var pages = halves.Count;
        var read = new T[pages];
        for (var side = 0; side < pages / 2; side++)
        {
            var (first, second) = side % 2 == 0 ? (pages - side, side + 1) : (side + 1, pages - side);
            read[first - 1] = halves[2 * side];
            read[second - 1] = halves[2 * side + 1];
        }

        return read;
    }
}
