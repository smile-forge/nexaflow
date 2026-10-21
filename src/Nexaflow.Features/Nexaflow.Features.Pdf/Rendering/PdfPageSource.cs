using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
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
/// A document pages are drawn from: the way into it, and its page slots, read once.
/// </summary>
internal sealed record PdfOpenedSource(IPdfDocumentAccess Access, PdfDocumentNode Document);

/// <summary>
/// The pages of a document, in the order arranged, as the reader's paged host and the page thumbnails ask for them:
/// each page read as objects from its own source under that source's lock, then laid on the thread pool outside it, so
/// laying one page never holds up reading the next.
/// <para>
/// A page laid is kept, by the source page it is, for as long as it stays among the most recently laid — so a page that
/// only moved, or that the reader and a thumbnail both want, is laid once.
/// </para>
/// </summary>
internal sealed class PdfPageSource : IPagedLayoutSource
{
    private const int KeptLaid = 24;

    private readonly IReadOnlyDictionary<PdfSourceId, PdfOpenedSource> _sources;
    private readonly Brush _paper;
    private readonly ConcurrentDictionary<(PdfSourceId, int), string> _texts = new();
    private readonly ConcurrentDictionary<(PdfSourceId, int), (Matrix FromUser, double Height)> _placements = new();
    private readonly LinkedList<((PdfSourceId, int) Key, LaidPage Page)> _laid = new();

    /// <param name="arrangement">The pages, in order.</param>
    /// <param name="sources">Every source a page of <paramref name="arrangement"/> is from.</param>
    /// <param name="paper">What a page is laid on — frozen, as everything a layout tree holds is.</param>
    public PdfPageSource(PdfPageArrangement arrangement, IReadOnlyDictionary<PdfSourceId, PdfOpenedSource> sources, Brush paper)
    {
        Arrangement = arrangement;
        _sources = sources;
        _paper = paper.IsFrozen ? paper : paper.GetCurrentValueAsFrozen() as Brush ?? Brushes.White;
    }

    /// <summary>
    /// The pages of one document, in its own order.
    /// </summary>
    public static PdfPageSource Of(IPdfDocumentAccess access, PdfDocumentNode document, Brush paper)
    {
        var source = new PdfSourceId(0);
        var arrangement = PdfPageArrangement.Of(document.Pages.Select((slot, index) => new PdfArrangedPage(new PdfPageId(index + 1), source, slot.Number)));
        return new PdfPageSource(arrangement, new Dictionary<PdfSourceId, PdfOpenedSource> { [source] = new(access, document) }, paper);
    }

    /// <summary>The pages, in order.</summary>
    public PdfPageArrangement Arrangement { get; }

    public int PageCount => Arrangement.Count;

    public Size PageSize(int index) => Dips(Slot(Arrangement[index]).Size);

    public async Task<LaidPage> LayAsync(int index, CancellationToken ct)
    {
        var page = Arrangement[index];
        var key = (page.Source, page.Number);
        if (Kept(key) is { } kept) return kept;

        var read = await Read(page, ct).ConfigureAwait(false);
        var tree = await Task.Run(() => PdfPageLayout.Lay(read, _paper, ct), ct).ConfigureAwait(false);
        var laid = new LaidPage(tree, Dips(read.Size), read.Text);
        Keep(key, laid);
        return laid;
    }

    public async Task<string> TextAsync(int index, CancellationToken ct)
    {
        var page = Arrangement[index];
        if (_texts.TryGetValue((page.Source, page.Number), out var known)) return known;
        return (await Read(page, ct).ConfigureAwait(false)).Text;
    }

    /// <summary>
    /// Page <paramref name="index"/> drawn <paramref name="width"/> pixels wide, as the reader shows it; frozen, so any
    /// thread may hand it on.
    /// </summary>
    public async Task<BitmapSource?> PaintedAsync(int index, int width, CancellationToken ct)
    {
        var laid = await LayAsync(index, ct).ConfigureAwait(false);
        return await Task.Run(() => LayoutPictures.Painted(laid.Tree.Root, new Rect(laid.Size), width / laid.Size.Width, Brushes.Black, _paper), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Page <paramref name="index"/> drawn as a PNG <paramref name="width"/> pixels wide, as the reader shows it.</summary>
    public async Task<byte[]?> PictureAsync(int index, int width, CancellationToken ct)
    {
        var picture = await PaintedAsync(index, width, ct).ConfigureAwait(false);
        return picture is null ? null : await Task.Run(() => LayoutPictures.Png(picture), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// How far down its page, in points, a link's target lies — known once the page has been read, since only the page
    /// says where its own coordinates put it. Null for a target that names no height, or a page not yet read.
    /// </summary>
    /// <param name="source">The source whose page the target names: the source of the page the link is on.</param>
    public double? OffsetFromTop(PdfSourceId source, PdfPageTarget target)
    {
        if (target.Top is not { } top || !_placements.TryGetValue((source, target.PageNumber), out var placement)) return null;
        var y = placement.FromUser.Transform(new Point(0, top)).Y;
        return Math.Clamp(y, 0, placement.Height);
    }

    private async Task<PdfPageNode> Read(PdfArrangedPage page, CancellationToken ct)
    {
        var number = page.Number;
        var read = await _sources[page.Source].Access.ReadAsync(scope => scope.Pages.ReadPage(number, ct), ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Page {number} could not be read: the document is no longer readable.");
        _texts[(page.Source, number)] = read.Text;
        _placements[(page.Source, number)] = (read.FromUser, read.Size.Height);
        return read;
    }

    private PdfPageSlot Slot(PdfArrangedPage page)
    {
        var pages = _sources[page.Source].Document.Pages;
        var at = page.Number - 1;
        if (at >= 0 && at < pages.Count && pages[at].Number == page.Number) return pages[at];
        foreach (var slot in pages)
            if (slot.Number == page.Number) return slot;
        throw new InvalidOperationException($"Page {page.Number} is not one of its document's.");
    }

    private LaidPage? Kept((PdfSourceId, int) key)
    {
        lock (_laid)
        {
            for (var node = _laid.First; node is not null; node = node.Next)
            {
                if (node.Value.Key != key) continue;
                _laid.Remove(node);
                _laid.AddFirst(node);
                return node.Value.Page;
            }
        }

        return null;
    }

    private void Keep((PdfSourceId, int) key, LaidPage page)
    {
        lock (_laid)
        {
            _laid.AddFirst((key, page));
            while (_laid.Count > KeptLaid)
                _laid.RemoveLast();
        }
    }

    private static Size Dips(Size points) =>
        new(points.Width * PdfPageLayout.DipsPerPoint, points.Height * PdfPageLayout.DipsPerPoint);
}
