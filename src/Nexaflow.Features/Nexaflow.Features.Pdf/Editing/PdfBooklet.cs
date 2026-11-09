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

    /// <summary>
    /// <paramref name="read"/> — a booklet's pages in reading order — folded into its sides, each side's first half then its
    /// second, side after side: <see cref="Unfold{T}"/> undone. A booklet is printed on whole sheets, two sides each, so the
    /// pages are padded to a multiple of four with blank halves — <c>null</c> — at the end, before the back cover.
    /// </summary>
    public static IReadOnlyList<T?> Fold<T>(IReadOnlyList<T> read) where T : class
    {
        var pages = (read.Count + 3) / 4 * 4;
        var halves = new T?[pages];
        for (var side = 0; side < pages / 2; side++)
        {
            var (first, second) = side % 2 == 0 ? (pages - side, side + 1) : (side + 1, pages - side);
            halves[2 * side] = first <= read.Count ? read[first - 1] : null;
            halves[2 * side + 1] = second <= read.Count ? read[second - 1] : null;
        }

        return halves;
    }

    /// <summary>
    /// <paramref name="sides"/> — a booklet's sheets' sides, each sheet's front then its back — put on the sides of the papers
    /// they print on, as <paramref name="cut"/> says: each paper side's booklet sides, top to bottom, a paper's front then its
    /// back. Stacked, paper <c>j</c> carries booklet sheet <c>j</c> above and sheet <c>j</c> plus half the papers below, so the
    /// pile's top halves laid on its bottom halves are the sheets in order; a last paper short of a sheet leaves its lower
    /// half blank — null.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<T?>> OnPapers<T>(IReadOnlyList<T> sides, PdfBookletCut cut) where T : class
    {
        if (sides.Count % 2 != 0)
            throw new ArgumentException("A booklet's sheets each have two sides.", nameof(sides));

        switch (cut)
        {
            case PdfBookletCut.Uncut:
                return sides.Select(side => (IReadOnlyList<T?>)[side]).ToList();
            case PdfBookletCut.Copies:
                return sides.Select(side => (IReadOnlyList<T?>)[side, side]).ToList();
            case PdfBookletCut.Stacked:
                var sheets = sides.Count / 2;
                var papers = (sheets + 1) / 2;
                var printed = new List<IReadOnlyList<T?>>(papers * 2);
                for (var paper = 0; paper < papers; paper++)
                {
                    var below = paper + papers;
                    for (var face = 0; face < 2; face++)
                        printed.Add([sides[2 * paper + face], below < sheets ? sides[2 * below + face] : null]);
                }

                return printed;
            default:
                throw new ArgumentOutOfRangeException(nameof(cut), cut, "Not a way of printing a booklet's sheets.");
        }
    }
}
