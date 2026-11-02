using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Services.Printing;

/// <summary>
/// A picture printed — a photo as edited, or a canvas's composite — a page for each of its tiles, each drawn from the picture
/// rendered at full size for the printer, never the screen's preview.
/// </summary>
public sealed class PicturePaginator : DocumentPaginator
{
    private readonly BitmapSource _picture;
    private readonly IReadOnlyList<PrintedTile> _tiles;

    /// <param name="picture">The picture at the size it prints at, frozen.</param>
    /// <param name="tiles">Its pages, their sources in <paramref name="picture"/>'s pixels.</param>
    /// <param name="page">The printable area, in device-independent pixels.</param>
    public PicturePaginator(BitmapSource picture, IReadOnlyList<PrintedTile> tiles, Size page)
    {
        _picture = picture;
        _tiles   = tiles;
        PageSize = page;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _tiles.Count;

    public override Size PageSize { get; set; }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _tiles.Count) return DocumentPage.Missing;
        var tile = _tiles[pageNumber];
        var source = new Int32Rect(tile.Source.Left, tile.Source.Top,
                                   Math.Min(tile.Source.Width, _picture.PixelWidth - tile.Source.Left),
                                   Math.Min(tile.Source.Height, _picture.PixelHeight - tile.Source.Top));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (source.Width > 0 && source.Height > 0)
            {
                var part = new CroppedBitmap(_picture, source);
                part.Freeze();
                dc.DrawImage(part, new Rect(tile.Page.Left, tile.Page.Top, tile.Page.Width, tile.Page.Height));
            }
        }
        var area = new Rect(PageSize);
        return new DocumentPage(visual, PageSize, area, area);
    }
}
