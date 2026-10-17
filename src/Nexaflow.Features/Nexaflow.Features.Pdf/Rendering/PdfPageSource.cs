using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Editing.Paged;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// The one way into an open document: <paramref name="read"/> runs with the document's scope, off the UI thread and
/// under the lock that guards it, and returns default when the document cannot be opened.
/// </summary>
internal interface IPdfDocumentAccess
{
    Task<T?> ReadAsync<T>(Func<PdfDocumentScope, T> read, CancellationToken ct);
}

/// <summary>
/// A document's pages as the reader's paged host asks for them: each page read as objects under the document's lock,
/// then laid on the thread pool outside it, so laying one page never holds up reading the next.
/// </summary>
internal sealed class PdfPageSource : IPagedLayoutSource
{
    private readonly IPdfDocumentAccess _access;
    private readonly PdfDocumentNode _document;
    private readonly Brush _paper;
    private readonly ConcurrentDictionary<int, string> _texts = new();
    private readonly ConcurrentDictionary<int, (Matrix FromUser, double Height)> _placements = new();

    /// <param name="access">The document.</param>
    /// <param name="document">Its pages and fields, read once.</param>
    /// <param name="paper">What a page is laid on — frozen, as everything a layout tree holds is.</param>
    public PdfPageSource(IPdfDocumentAccess access, PdfDocumentNode document, Brush paper)
    {
        _access = access;
        _document = document;
        _paper = paper.IsFrozen ? paper : paper.GetCurrentValueAsFrozen() as Brush ?? Brushes.White;
    }

    /// <summary>The document as objects: its page slots and fields.</summary>
    public PdfDocumentNode Document => _document;

    public int PageCount => _document.Pages.Count;

    public Size PageSize(int index) => Dips(_document.Pages[index].Size);

    public async Task<LaidPage> LayAsync(int index, CancellationToken ct)
    {
        var page = await Read(index, ct).ConfigureAwait(false);
        var tree = await Task.Run(() => PdfPageLayout.Lay(page, _paper, ct), ct).ConfigureAwait(false);
        return new LaidPage(tree, Dips(page.Size), page.Text);
    }

    public async Task<string> TextAsync(int index, CancellationToken ct)
    {
        if (_texts.TryGetValue(index, out var known)) return known;
        return (await Read(index, ct).ConfigureAwait(false)).Text;
    }

    /// <summary>Page <paramref name="index"/> drawn as a PNG <paramref name="width"/> pixels wide, as the reader shows it.</summary>
    public async Task<byte[]?> PictureAsync(int index, int width, CancellationToken ct)
    {
        var laid = await LayAsync(index, ct).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var picture = LayoutPictures.Painted(laid.Tree.Root, new Rect(laid.Size), width / laid.Size.Width,
                Brushes.Black, _paper);
            return picture is null ? null : LayoutPictures.Png(picture);
        }, ct).ConfigureAwait(false);
    }

    private async Task<PdfPageNode> Read(int index, CancellationToken ct)
    {
        var number = _document.Pages[index].Number;
        var page = await _access.ReadAsync(scope => scope.Pages.ReadPage(number, ct), ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Page {number} could not be read: the document is no longer readable.");
        _texts[index] = page.Text;
        _placements[number] = (page.FromUser, page.Size.Height);
        return page;
    }

    /// <summary>
    /// How far down its page, in points, a link's target lies — known once the page has been read, since only the page
    /// says where its own coordinates put it. Null for a target that names no height, or a page not yet read.
    /// </summary>
    public double? OffsetFromTop(PdfPageTarget target)
    {
        if (target.Top is not { } top || !_placements.TryGetValue(target.PageNumber, out var placement)) return null;
        var y = placement.FromUser.Transform(new Point(0, top)).Y;
        return Math.Clamp(y, 0, placement.Height);
    }

    private static Size Dips(Size points) =>
        new(points.Width * PdfPageLayout.DipsPerPoint, points.Height * PdfPageLayout.DipsPerPoint);
}
