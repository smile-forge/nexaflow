using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.ViewModels;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// The pictures of the Pages tab. A page is asked for as its row comes into view and forgotten as it leaves; pictures
/// are drawn one at a time, the latest asked for first — the rows the reader has scrolled to, not those they scrolled
/// past — from the page source, which lays each page once for the reader and the thumbnails alike. A picture is kept by
/// the page it is, however that page moves, for as long as the pictures kept fit their budget.
/// </summary>
internal sealed class PdfThumbnails : IDisposable
{
    /// <summary>What the pictures kept may take, in bytes.</summary>
    private const long Budget = 32L * 1024 * 1024;

    private readonly Func<PdfPageSource?> _source;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<(PdfPageId Id, int Width), LinkedListNode<Kept>> _kept = new();
    private readonly LinkedList<Kept> _recent = new();
    private readonly List<(PdfPageThumbnail Page, int Width)> _waiting = new();
    private long _bytes;
    private bool _drawing;

    /// <param name="source">The pages as they now stand.</param>
    public PdfThumbnails(Func<PdfPageSource?> source)
    {
        _source = source;
    }

    /// <summary>
    /// Gives <paramref name="page"/> its picture <paramref name="width"/> pixels wide: at once when one is kept, else
    /// once it is drawn.
    /// </summary>
    public void Request(PdfPageThumbnail page, int width)
    {
        if (_kept.TryGetValue((page.Id, width), out var kept))
        {
            _recent.Remove(kept);
            _recent.AddFirst(kept);
            page.Picture = kept.Value.Picture;
            return;
        }

        Cancel(page);
        _waiting.Add((page, width));
        _ = DrawAsync();
    }

    /// <summary>The page's row left view: it is not drawn unless asked for again.</summary>
    public void Cancel(PdfPageThumbnail page) => _waiting.RemoveAll(waiting => ReferenceEquals(waiting.Page, page));

    public void Dispose() => _lifetime.Cancel();

    private async Task DrawAsync()
    {
        if (_drawing) return;
        _drawing = true;
        try
        {
            while (_waiting.Count > 0 && !_lifetime.IsCancellationRequested)
            {
                var (page, width) = _waiting[^1];
                _waiting.RemoveAt(_waiting.Count - 1);

                if (_source() is not { } source) break;
                var index = source.Arrangement.IndexOf(page.Id);
                if (index < 0) continue;

                BitmapSource? picture;
                try
                {
                    picture = await source.PaintedAsync(index, width, _lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    continue;   // the page's document can no longer be read
                }

                if (picture is null) continue;
                Keep(page.Id, width, picture);
                page.Picture = picture;
            }
        }
        finally
        {
            _drawing = false;
        }
    }

    private void Keep(PdfPageId id, int width, BitmapSource picture)
    {
        var key = (id, width);
        if (_kept.Remove(key, out var old))
        {
            _recent.Remove(old);
            _bytes -= old.Value.Bytes;
        }

        var kept = new Kept(key, picture, (long)picture.PixelWidth * picture.PixelHeight * 4);
        _kept[key] = _recent.AddFirst(kept);
        _bytes += kept.Bytes;

        while (_bytes > Budget && _recent.Last is { } oldest && oldest != _recent.First)
        {
            _recent.RemoveLast();
            _kept.Remove(oldest.Value.Key);
            _bytes -= oldest.Value.Bytes;
        }
    }

    private sealed record Kept((PdfPageId Id, int Width) Key, BitmapSource Picture, long Bytes);
}
