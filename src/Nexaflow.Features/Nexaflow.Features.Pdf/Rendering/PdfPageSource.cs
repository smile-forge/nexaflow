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
using Nexaflow.Visuals.Text.Painting;
using Nexaflow.Visuals.Text.Editing.Paged;
using UglyToad.PdfPig.Core;

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
/// each page read as objects from its own source under that source's lock — a sheet, each of its pieces from its own —
/// then laid on the thread pool outside it, so laying one page never holds up reading the next.
/// <para>
/// A page laid is kept, by the sheet it shows, for as long as it stays among the most recently laid — so a page that
/// only moved, or that the reader and a thumbnail both want, is laid once.
/// </para>
/// </summary>
internal sealed class PdfPageSource : IRearrangingPagedSource
{
    private const int KeptLaid = 24;

    private IReadOnlyDictionary<PdfSourceId, PdfOpenedSource> _sources;
    private readonly Brush _paper;
    private readonly ConcurrentDictionary<PdfSheet, string> _texts = new();
    private readonly ConcurrentDictionary<PdfSheet, IReadOnlyList<(Matrix FromUser, Rect Bounds)>> _placements = new();
    private readonly LinkedList<(PdfSheet Key, LaidPage Page)> _laid = new();

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
    public PdfPageArrangement Arrangement { get; private set; }

    /// <inheritdoc />
    public event EventHandler<PagesRearranged>? Rearranged;

    /// <summary>
    /// The pages in a new order, from <paramref name="sources"/>: the same source, so what it has laid — kept by the sheet
    /// each page shows — is not laid again, and the reader is told where each page was.
    /// </summary>
    public void Rearrange(PdfPageArrangement next, IReadOnlyDictionary<PdfSourceId, PdfOpenedSource> sources)
    {
        var previous = Arrangement;
        _sources = sources;
        Arrangement = next;
        Rearranged?.Invoke(this, new PagesRearranged(previous.Before(next), previous.MadeFrom(next)));
    }

    public int PageCount => Arrangement.Count;

    public Size PageSize(int index) => Dips(Arrangement[index].Sheet.ShownSize(ShownSize));

    public async Task<LaidPage> LayAsync(int index, CancellationToken ct)
    {
        var sheet = Arrangement[index].Sheet;
        if (Kept(sheet) is { } kept) return kept;

        var read = await Read(sheet, ct).ConfigureAwait(false);
        var tree = await Task.Run(() => PdfPageLayout.Lay(read, _paper, ct), ct).ConfigureAwait(false);
        var laid = new LaidPage(tree, Dips(read.Size), read.Text);
        Keep(sheet, laid);
        return laid;
    }

    public async Task<string> TextAsync(int index, CancellationToken ct)
    {
        var sheet = Arrangement[index].Sheet;
        if (_texts.TryGetValue(sheet, out var known)) return known;
        return (await Read(sheet, ct).ConfigureAwait(false)).Text;
    }

    /// <summary>
    /// Page <paramref name="index"/> drawn <paramref name="width"/> pixels wide, as the reader shows it; frozen, so any
    /// thread may hand it on.
    /// </summary>
    public async Task<BitmapSource?> PaintedAsync(int index, int width, CancellationToken ct)
    {
        var laid = await LayAsync(index, ct).ConfigureAwait(false);
        return await Task.Run(() => WpfPainter.Painted(laid.Tree.Root, new Rect(laid.Size), width / laid.Size.Width, Brushes.Black, _paper), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Page <paramref name="index"/> drawn as a PNG <paramref name="width"/> pixels wide, as the reader shows it.</summary>
    public async Task<byte[]?> PictureAsync(int index, int width, CancellationToken ct)
    {
        var picture = await PaintedAsync(index, width, ct).ConfigureAwait(false);
        return picture is null ? null : await Task.Run(() => WpfPainter.Png(picture), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Where a link's target is: the page it names — of the source of the piece the link is on — and how far down the page
    /// showing it, in points, once that page has been read, since only the page says where its own coordinates put it. Of
    /// a page split into parts, or set on several sheets, the piece holding the target's point, as the file written sends
    /// it there. Null when the page is no longer in the document.
    /// </summary>
    /// <param name="source">The source whose page the target names: the source of the piece the link is on.</param>
    public (int Index, double? Offset)? Locate(PdfSourceId source, PdfPageTarget target)
    {
        (int Index, int Piece)? first = null;
        for (var index = 0; index < Arrangement.Count; index++)
        {
            var pieces = Arrangement[index].Sheet.Pieces;
            for (var at = 0; at < pieces.Length; at++)
            {
                if (pieces[at].Source != source || pieces[at].Number != target.PageNumber) continue;

                if (HoldsTarget(Region(pieces[at]), target)) return (index, TargetOffset(index, at, target));
                first ??= (index, at);
            }
        }

        return first is { } found ? (found.Index, TargetOffset(found.Index, found.Piece, target)) : null;
    }

    /// <summary>Whether <paramref name="region"/> holds the point <paramref name="target"/> names, on the axes it names.</summary>
    private static bool HoldsTarget(PdfRectangle region, PdfPageTarget target) =>
        (target.Left is not { } x || (x >= region.Left && x <= region.Right))
        && (target.Top is not { } y || (y >= region.Bottom && y <= region.Top));

    /// <summary>
    /// How far down page <paramref name="index"/> the target's height is, on its piece <paramref name="piece"/>, in points,
    /// once the page has been read; null before, or when the target names no height.
    /// </summary>
    private double? TargetOffset(int index, int piece, PdfPageTarget target)
    {
        var sheet = Arrangement[index].Sheet;
        if (target.Top is not { } top || !_placements.TryGetValue(sheet, out var placements)) return null;

        var (fromUser, bounds) = placements[piece];
        var y = fromUser.Transform(new Point(target.Left ?? Region(sheet.Pieces[piece]).Left, top)).Y;
        return Math.Clamp(y, bounds.Top, bounds.Bottom);
    }

    /// <summary>What of its source page <paramref name="piece"/> shows, in the page's default user space.</summary>
    private PdfRectangle Region(PdfSheetPiece piece)
    {
        var slot = Slot(piece.Source, piece.Number);
        return piece.Part.InUserSpace(slot.Crop, slot.Rotation);
    }

    /// <summary>
    /// The sheet read: each of its pieces read onto it from its own source, one after another, so each knows where its
    /// text starts in the sheet's.
    /// </summary>
    private async Task<PdfPageNode> Read(PdfSheet sheet, CancellationToken ct)
    {
        var pieces = new List<(PdfPageNode Read, Rect Bounds)>(sheet.Pieces.Length);
        var textStart = 0;
        foreach (var piece in sheet.Pieces)
        {
            var placement = new PdfReadingPlacement(piece.Part, piece.Place.Matrix, textStart);
            var number = piece.Number;
            var read = await _sources[piece.Source].Access.ReadAsync(scope => scope.Pages.ReadPage(number, placement, ct), ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException($"Page {number} could not be read: the document is no longer readable.");
            pieces.Add((read, piece.Bounds(ShownSize)));
            textStart += read.Text.Length + 1;
        }

        var page = sheet.IsPlain ? pieces[0].Read : PdfSheetComposer.Compose(sheet.ShownSize(ShownSize), pieces);
        _texts[sheet] = page.Text;
        _placements[sheet] = pieces.Select(piece => (piece.Read.FromUser, piece.Bounds)).ToList();
        return page;
    }

    private Size ShownSize(PdfSourceId source, int number) => Slot(source, number).Size;

    private PdfPageSlot Slot(PdfSourceId source, int number)
    {
        var pages = _sources[source].Document.Pages;
        var at = number - 1;
        if (at >= 0 && at < pages.Count && pages[at].Number == number) return pages[at];
        foreach (var slot in pages)
            if (slot.Number == number) return slot;
        throw new InvalidOperationException($"Page {number} is not one of its document's.");
    }

    private LaidPage? Kept(PdfSheet key)
    {
        lock (_laid)
        {
            for (var node = _laid.First; node is not null; node = node.Next)
            {
                if (!node.Value.Key.Equals(key)) continue;
                _laid.Remove(node);
                _laid.AddFirst(node);
                return node.Value.Page;
            }
        }

        return null;
    }

    private void Keep(PdfSheet key, LaidPage page)
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
