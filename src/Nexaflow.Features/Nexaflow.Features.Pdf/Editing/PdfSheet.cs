using System.Collections.Immutable;
using System.Windows;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>How large page <paramref name="number"/> of <paramref name="source"/> is shown, in points: its crop box, turned.</summary>
internal delegate Size PdfShownSize(PdfSourceId source, int number);

/// <summary>
/// Part of one page of a source, put on a sheet: which page, which part of it as it is shown, and how that part is put
/// there — its top left at the sheet's top left, then moved, scaled or turned by <see cref="Place"/>.
/// </summary>
internal readonly record struct PdfSheetPiece(PdfSourceId Source, int Number, PdfPagePart Part, PdfPageTransform Place)
{
    /// <summary>The piece's own size, in points: its part of the page, before it is placed.</summary>
    public Size Size(PdfShownSize sizes) => PdfSheet.Rounded(Part.Of(sizes(Source, Number)));

    /// <summary>Where the piece lies on its sheet, in points.</summary>
    public Rect Bounds(PdfShownSize sizes) => Place.Apply(new Rect(Size(sizes)));

    /// <summary>
    /// From the whole page, as shown, to the sheet: the same for every piece of a page placed as the page itself lies, which
    /// is what lets them be joined back into it.
    /// </summary>
    public PdfPageTransform PageToSheet(PdfShownSize sizes)
    {
        var shown = sizes(Source, Number);
        return PdfPageTransform.Translate(-Part.Left * shown.Width, -Part.Top * shown.Height).Then(Place);
    }
}

/// <summary>
/// What a page of the edited document shows: a page of a source as it is, or a sheet of pieces of pages — the parts of a
/// page split, pages merged several to a sheet, a page set on paper of another size. Immutable and compared by value, so
/// a sheet is what a laid page is kept by.
/// <para>
/// Every sheet made is put in its one form: pieces of a page that lie on the sheet as they lie on the page are joined
/// back into one, so a page split and merged back is the page it was, and a sheet of one page as it is is that page.
/// </para>
/// </summary>
internal sealed class PdfSheet : IEquatable<PdfSheet>
{
    /// <summary>Less of a page than this, in points across or down, is no piece of it.</summary>
    private const double Sliver = 0.01;

    /// <summary>Pieces placed this close, in points, are placed alike.</summary>
    private const double Near = 0.01;

    /// <summary>Parts whose edges are this close, as fractions of their page, meet.</summary>
    private const double Meets = 1e-9;

    private PdfSheet(Size? size, ImmutableArray<PdfSheetPiece> pieces)
    {
        Size = size;
        Pieces = pieces;
    }

    /// <summary>The sheet's size, in points; null for a page of a source as it is, whose size is its own.</summary>
    public Size? Size { get; }

    /// <summary>What the sheet shows, in the order it is read: each piece's text after the one before's.</summary>
    public ImmutableArray<PdfSheetPiece> Pieces { get; }

    /// <summary>Whether the sheet is a page of a source, or a part of one, as it is: one piece, not moved.</summary>
    public bool IsPlain => Size is null;

    /// <summary>Page <paramref name="number"/> of <paramref name="source"/>, or the <paramref name="part"/> of it, as it is.</summary>
    public static PdfSheet Page(PdfSourceId source, int number, PdfPagePart? part = null) =>
        new(null, [new PdfSheetPiece(source, number, (part ?? PdfPagePart.Whole).Snapped(), PdfPageTransform.Identity)]);

    /// <summary>
    /// A sheet <paramref name="size"/> large of <paramref name="pieces"/>, in its one form: of a piece that runs off the
    /// sheet, only the part on it.
    /// </summary>
    public static PdfSheet Of(Size size, IEnumerable<PdfSheetPiece> pieces, PdfShownSize sizes)
    {
        size = Rounded(size);
        var sheet = new Rect(size);
        var kept = pieces
            .Select(piece => Within(piece, sheet, sizes))
            .OfType<PdfSheetPiece>()
            .Select(piece => piece with { Part = piece.Part.Snapped() })
            .ToList();

        while (JoinOnce(kept, sizes)) { }

        if (kept.Count == 1 && kept[0].Place.IsCloseTo(PdfPageTransform.Identity, Near)
            && kept[0].Size(sizes) is var only && Math.Abs(only.Width - size.Width) <= Near && Math.Abs(only.Height - size.Height) <= Near)
            return Page(kept[0].Source, kept[0].Number, kept[0].Part);

        return new PdfSheet(size, [.. kept]);
    }

    /// <summary>
    /// <paramref name="sheets"/> side by side <paramref name="across"/>, or one above another, each centred in a cell
    /// <paramref name="cell"/> large; a null leaves its cell empty.
    /// </summary>
    public static PdfSheet Merged(IReadOnlyList<PdfSheet?> sheets, bool across, Size cell, PdfShownSize sizes)
    {
        var pieces = new List<PdfSheetPiece>();
        for (var i = 0; i < sheets.Count; i++)
        {
            if (sheets[i] is not { } sheet) continue;

            var shown = sheet.ShownSize(sizes);
            var x = (across ? i * cell.Width : 0) + (cell.Width - shown.Width) / 2;
            var y = (across ? 0 : i * cell.Height) + (cell.Height - shown.Height) / 2;
            var moved = PdfPageTransform.Translate(x, y);
            pieces.AddRange(sheet.Pieces.Select(piece => piece with { Place = piece.Place.Then(moved) }));
        }

        var size = across ? new Size(cell.Width * sheets.Count, cell.Height) : new Size(cell.Width, cell.Height * sheets.Count);
        return Of(size, pieces, sizes);
    }

    /// <summary>How large the sheet is, in points.</summary>
    public Size ShownSize(PdfShownSize sizes) => Size ?? Pieces[0].Size(sizes);

    /// <summary>Everything on the sheet carried by <paramref name="transform"/> onto a sheet <paramref name="size"/> large.</summary>
    public PdfSheet Transformed(PdfPageTransform transform, Size size, PdfShownSize sizes) =>
        Of(size, Pieces.Select(piece => piece with { Place = piece.Place.Then(transform) }), sizes);

    /// <summary>
    /// The sheet cut into <paramref name="count"/> equal sheets, side by side when cut <paramref name="across"/>, otherwise
    /// one above another: each holding what of every piece falls on it — or null, for a part with nothing on it, such as
    /// the blank half of a booklet's last sheet.
    /// </summary>
    public IReadOnlyList<PdfSheet?> Split(int count, bool across, PdfShownSize sizes)
    {
        var shown = ShownSize(sizes);
        var sheets = new List<PdfSheet?>(count);
        foreach (var share in PdfPagePart.Split(count, across))
        {
            var cell = new Rect(share.Left * shown.Width, share.Top * shown.Height, share.Width * shown.Width, share.Height * shown.Height);
            var toCell = PdfPageTransform.Translate(-cell.X, -cell.Y);
            var pieces = Pieces
                .Select(piece => Within(piece, cell, sizes))
                .OfType<PdfSheetPiece>()
                .Select(piece => piece with { Place = piece.Place.Then(toCell) })
                .ToList();

            sheets.Add(pieces.Count > 0 ? Of(cell.Size, pieces, sizes) : null);
        }

        return sheets;
    }

    /// <summary>
    /// What of <paramref name="piece"/> lies in <paramref name="area"/> of the sheet, where it lay; null when nothing does,
    /// or no more than a sliver.
    /// </summary>
    private static PdfSheetPiece? Within(PdfSheetPiece piece, Rect area, PdfShownSize sizes)
    {
        var own = piece.Size(sizes);
        var bounds = piece.Place.Apply(new Rect(own));
        var common = Rect.Intersect(bounds, area);
        if (common.IsEmpty || common.Width < Sliver || common.Height < Sliver) return null;
        if (Math.Abs(common.Left - bounds.Left) <= Near && Math.Abs(common.Top - bounds.Top) <= Near
            && Math.Abs(common.Right - bounds.Right) <= Near && Math.Abs(common.Bottom - bounds.Bottom) <= Near)
            return piece;

        var within = piece.Place.Inverse.Apply(common);
        var cut = new PdfPagePart(within.X / own.Width, within.Y / own.Height, within.Width / own.Width, within.Height / own.Height);
        return piece with
        {
            Part = cut.Of(piece.Part),
            Place = PdfPageTransform.Translate(within.X, within.Y).Then(piece.Place),
        };
    }

    public bool Equals(PdfSheet? other) =>
        other is not null && (ReferenceEquals(this, other) || (Size == other.Size && Pieces.SequenceEqual(other.Pieces)));

    public override bool Equals(object? obj) => Equals(obj as PdfSheet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Size);
        foreach (var piece in Pieces)
            hash.Add(piece);
        return hash.ToHashCode();
    }

    /// <summary>
    /// <paramref name="size"/> rounded to a millionth of a point, as a transform is: a third of a page, cut from fractions
    /// rounded to a trillionth, is still a third of it.
    /// </summary>
    internal static Size Rounded(Size size) => new(Math.Round(size.Width, 6) + 0.0, Math.Round(size.Height, 6) + 0.0);

    /// <summary>
    /// Joins the first two pieces of one page that lie on the sheet as they lie on the page, their parts meeting along a
    /// whole edge; false when no two do.
    /// </summary>
    private static bool JoinOnce(List<PdfSheetPiece> pieces, PdfShownSize sizes)
    {
        for (var i = 0; i < pieces.Count; i++)
        {
            for (var j = i + 1; j < pieces.Count; j++)
            {
                var (a, b) = (pieces[i], pieces[j]);
                if (a.Source != b.Source || a.Number != b.Number || !Adjoining(a.Part, b.Part, out var union)) continue;

                var page = a.PageToSheet(sizes);
                if (!page.IsCloseTo(b.PageToSheet(sizes), Near)) continue;

                var shown = sizes(a.Source, a.Number);
                pieces[i] = a with
                {
                    Part = union,
                    Place = PdfPageTransform.Translate(union.Left * shown.Width, union.Top * shown.Height).Then(page),
                };
                pieces.RemoveAt(j);
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> meet along a whole edge, making the one part <paramref name="union"/>.</summary>
    private static bool Adjoining(PdfPagePart a, PdfPagePart b, out PdfPagePart union)
    {
        var side = Close(a.Top, b.Top) && Close(a.Height, b.Height)
                   && (Close(a.Left + a.Width, b.Left) || Close(b.Left + b.Width, a.Left));
        var stacked = Close(a.Left, b.Left) && Close(a.Width, b.Width)
                      && (Close(a.Top + a.Height, b.Top) || Close(b.Top + b.Height, a.Top));

        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        union = new PdfPagePart(left, top, Math.Max(a.Left + a.Width, b.Left + b.Width) - left,
            Math.Max(a.Top + a.Height, b.Top + b.Height) - top).Snapped();
        return side || stacked;
    }

    private static bool Close(double a, double b) => Math.Abs(a - b) <= Meets;
}
