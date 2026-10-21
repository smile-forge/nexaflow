using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Features.Pdf.Rendering;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// The document a PDF tab shows: the file opened, the PDFs inserted into it, and its pages in their current order —
/// what the reader and the page thumbnails draw from, page by page, each from its own source.
/// </summary>
internal sealed class PdfDocumentSession : IDisposable
{
    private readonly Dictionary<PdfSourceId, PdfSourceDocument> _sources = new();
    private readonly Timer _idleTimer;
    private int _nextSource;
    private int _nextPage;
    private bool _disposed;

    public PdfDocumentSession(string path)
    {
        Base = AddSource(path);

        // Thread-pool timer, not a DispatcherTimer: closing an idle document is background housekeeping and must not
        // depend on (or touch) the UI thread.
        _idleTimer = new Timer(_ => CloseIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>The file the tab shows.</summary>
    public PdfSourceDocument Base { get; private set; }

    /// <summary>The pages, in order.</summary>
    public PdfPageArrangement Arrangement { get; private set; } = PdfPageArrangement.Empty;

    /// <summary>
    /// The document as it now stands, for what reads it whole — its properties and outline, its scans, the assistant's
    /// reads and searches.
    /// </summary>
    public IPdfDocumentAccess Document => Base;

    /// <summary>
    /// Reads the file's pages; false when it cannot be read at all.
    /// </summary>
    public async Task<bool> OpenAsync(CancellationToken ct)
    {
        var slots = await Base.ReadSlotsAsync(ct).ConfigureAwait(false);
        if (slots is null) return false;

        Arrangement = PdfPageArrangement.Of(slots.Pages.Select(slot => new PdfArrangedPage(NewPageId(), Base.Id, slot.Number)));
        return true;
    }

    public PdfSourceDocument Source(PdfSourceId id)
    {
        lock (_sources) return _sources[id];
    }

    /// <summary>
    /// Every source whose pages are read, as the page source reads them.
    /// </summary>
    public IReadOnlyDictionary<PdfSourceId, PdfOpenedSource> Opened()
    {
        lock (_sources)
        {
            return _sources.Values
                .Where(source => source.Slots is not null)
                .ToDictionary(source => source.Id, source => new PdfOpenedSource(source, source.Slots!));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _idleTimer.Dispose();
        List<PdfSourceDocument> sources;
        lock (_sources) sources = _sources.Values.ToList();
        foreach (var source in sources)
            source.Dispose();
    }

    private PdfSourceDocument AddSource(string path)
    {
        var source = new PdfSourceDocument(new PdfSourceId(_nextSource++), path);
        lock (_sources) _sources.Add(source.Id, source);
        return source;
    }

    private PdfPageId NewPageId() => new(Interlocked.Increment(ref _nextPage));

    private void CloseIdle()
    {
        if (_disposed) return;
        List<PdfSourceDocument> sources;
        lock (_sources) sources = _sources.Values.ToList();
        foreach (var source in sources)
            source.CloseIfIdle();
    }
}
