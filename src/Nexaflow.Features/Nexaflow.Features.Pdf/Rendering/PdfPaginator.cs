using System;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// A document's pages for a printer: each laid as the reader lays it and drawn centred on the sheet, as large as fits
/// without changing its proportions. Pages are laid one at a time as the print system asks for them, so printing a long
/// document holds one page's layout at a time.
/// </summary>
internal sealed class PdfPaginator : DocumentPaginator
{
    private readonly PdfPageSource _source;
    private readonly CancellationToken _ct;

    /// <param name="source">The document's pages.</param>
    /// <param name="sheet">The printable area of a sheet, in device-independent pixels.</param>
    /// <param name="ct">What abandons the print.</param>
    public PdfPaginator(PdfPageSource source, Size sheet, CancellationToken ct)
    {
        _source = source;
        _ct = ct;
        PageSize = sheet;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _source.PageCount;

    public override Size PageSize { get; set; }

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        // The print system asks synchronously; every await under LayAsync is ConfigureAwait(false), so waiting here never
        // needs this thread back.
        var laid = _source.LayAsync(pageNumber, _ct).GetAwaiter().GetResult();
        var sheet = PageSize;
        var scale = Math.Min(sheet.Width / laid.Size.Width, sheet.Height / laid.Size.Height);
        var drawn = new Size(laid.Size.Width * scale, laid.Size.Height * scale);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform((sheet.Width - drawn.Width) / 2, (sheet.Height - drawn.Height) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            LayoutPainter.Paint(dc, laid.Tree.Root, Brushes.Black);
            dc.Pop();
            dc.Pop();
        }

        return new DocumentPage(visual, sheet, new Rect(sheet), new Rect(sheet));
    }
}
