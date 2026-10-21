using System.IO;
using System.Windows;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Editing;
using UglyToad.PdfPig.Writer;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// The document a PDF tab shows and edits: the file opened, the PDFs and pictures inserted into it, and its pages in their
/// current order — what the reader and the page thumbnails draw from, page by page, each from its own source.
/// <para>
/// Edits are staged: each makes a new arrangement, kept in a history to undo, and none touches a file until the document
/// is saved. While there are edits the files they depend on are held against writers, and a working copy of the document
/// as it now stands is written in the background for what reads the document whole. The pictures inserted, and the
/// working copies, are written in a draft folder of the session's own, deleted when it is saved or closed.
/// </para>
/// </summary>
internal sealed class PdfDocumentSession : IDisposable
{
    private static readonly PdfSourceId WorkingCopies = new(-1);

    private readonly Dictionary<PdfSourceId, PdfSourceDocument> _sources = new();
    private readonly Dictionary<PdfSourceId, PdfSourceFile> _files = new();
    private readonly Timer _idleTimer;
    private readonly CurrentDocument _document;
    private SnapshotHistory<PdfPageArrangement> _history = new(PdfPageArrangement.Empty);
    private PdfPageArrangement _saved = PdfPageArrangement.Empty;
    private PdfWorkingCopy? _working;
    private string? _drafts;
    private int _nextSource;
    private int _nextPage;
    private int _nextDraft;
    private bool _disposed;

    public PdfDocumentSession(string path)
    {
        Base = AddSource(path, new PdfSourceFile(path, null));
        _document = new CurrentDocument(this);

        // Thread-pool timer, not a DispatcherTimer: closing an idle document is background housekeeping and must not
        // depend on (or touch) the UI thread.
        _idleTimer = new Timer(_ => CloseIdle(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>The file the tab shows.</summary>
    public PdfSourceDocument Base { get; private set; }

    /// <summary>The pages, in order.</summary>
    public PdfPageArrangement Arrangement => _history.Current;

    /// <summary>Whether the pages are not as the file has them.</summary>
    public bool IsDirty => !Arrangement.Equals(_saved);

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>
    /// The document as it now stands, for what reads it whole — its properties and outline, its scans, the assistant's
    /// reads and searches: the file itself, or once it is edited the working copy of the edits.
    /// </summary>
    public IPdfDocumentAccess Document => _document;

    /// <summary>The pages were rearranged: the arrangement before, and now. Raised on the thread that edited.</summary>
    public event Action<PdfPageArrangement, PdfPageArrangement>? Changed;

    /// <summary>What <see cref="Document"/> reads changed. Raised on any thread.</summary>
    public event Action? DocumentChanged;

    /// <summary><see cref="IsDirty"/> changed.</summary>
    public event Action<bool>? DirtyChanged;

    /// <summary>The document was saved as another file, which it now is.</summary>
    public event Action<string>? Moved;

    /// <summary>
    /// Reads the file's pages; false when it cannot be read at all.
    /// </summary>
    public async Task<bool> OpenAsync(CancellationToken ct)
    {
        var slots = await Base.ReadSlotsAsync(ct);
        if (slots is null) return false;

        var pages = PdfPageArrangement.Of(slots.Pages.Select(slot => new PdfArrangedPage(NewPageId(), Base.Id, slot.Number)));
        _history = new SnapshotHistory<PdfPageArrangement>(pages);
        _saved = pages;
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

    /// <summary>
    /// The size, in points, of the page at <paramref name="index"/> as it is shown.
    /// </summary>
    public Size PageSize(int index)
    {
        var page = Arrangement[index];
        var slots = Source(page.Source).Slots!.Pages;
        var at = page.Number - 1;
        return at >= 0 && at < slots.Count && slots[at].Number == page.Number
            ? slots[at].Size
            : slots.First(slot => slot.Number == page.Number).Size;
    }

    // ── Edits ─────────────────────────────────────────────────────────────

    /// <summary>The pages at <paramref name="indices"/> moved together into <paramref name="gap"/>.</summary>
    public void Move(IReadOnlyCollection<int> indices, int gap) => Commit(Arrangement.Move(indices, gap));

    /// <summary>The pages at <paramref name="indices"/> deleted. A document keeps at least one page.</summary>
    public void Delete(IReadOnlyCollection<int> indices) => Commit(Arrangement.Remove(indices));

    /// <summary>
    /// Every page of each PDF at <paramref name="paths"/>, in order, inserted into <paramref name="gap"/> — one edit.
    /// </summary>
    /// <exception cref="PdfEditRefusedException">A file cannot be read, has an XFA form, or is open for writing elsewhere.</exception>
    public async Task InsertPdfsAsync(int gap, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var added = new List<PdfSourceDocument>();
        var pages = new List<PdfArrangedPage>();
        try
        {
            foreach (var path in paths)
            {
                var source = new PdfSourceDocument(new PdfSourceId(Interlocked.Increment(ref _nextSource)), path);
                added.Add(source);

                if (await source.ReadSlotsAsync(ct) is not { } slots)
                    throw new PdfEditRefusedException(path, PdfEditRefusal.Unreadable);
                if (await source.ReadAsync(scope => PdfDocumentFacts.HasXfa(scope.Document), ct))
                    throw new PdfEditRefusedException(path, PdfEditRefusal.XfaForm);

                pages.AddRange(slots.Pages.Select(slot => new PdfArrangedPage(NewPageId(), source.Id, slot.Number)));
            }

            foreach (var source in added)
                Hold(source);
        }
        catch
        {
            foreach (var source in added)
                source.Dispose();
            throw;
        }

        foreach (var source in added)
            Register(source, new PdfSourceFile(source.Path, null));

        Commit(Arrangement.Insert(gap, pages));
    }

    /// <summary>
    /// Each picture at <paramref name="paths"/>, in order, inserted into <paramref name="gap"/> as a page of its own
    /// the size of its neighbour — one edit. In a tagged document each is a Figure, its file name its alternate text.
    /// </summary>
    /// <exception cref="PdfEditRefusedException">A file is not a picture this machine can decode.</exception>
    public async Task InsertImagesAsync(int gap, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var size = PageSize(gap > 0 ? gap - 1 : gap);
        var folder = Drafts();
        var added = new List<(PdfSourceDocument Source, string Picture)>();
        try
        {
            foreach (var path in paths)
            {
                var page = System.IO.Path.Combine(folder, $"picture-{Interlocked.Increment(ref _nextDraft)}.pdf");
                try
                {
                    await Task.Run(() =>
                    {
                        using var output = new FileStream(page, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        PdfImagePageWriter.Write(path, size, output, ct);
                    }, ct);
                }
                catch (NotSupportedException ex)
                {
                    TryDelete(page);
                    throw new PdfEditRefusedException(path, PdfEditRefusal.NotAPicture, ex);
                }

                var source = new PdfSourceDocument(new PdfSourceId(Interlocked.Increment(ref _nextSource)), page);
                added.Add((source, path));
                if (await source.ReadSlotsAsync(ct) is null)
                    throw new PdfEditRefusedException(path, PdfEditRefusal.NotAPicture);
            }
        }
        catch
        {
            foreach (var (source, _) in added)
            {
                source.Dispose();
                TryDelete(source.Path);
            }

            throw;
        }

        foreach (var (source, picture) in added)
            Register(source, new PdfSourceFile(source.Path, new AssembledContentTag("Figure", System.IO.Path.GetFileName(picture))));

        Commit(Arrangement.Insert(gap, added.Select(item => new PdfArrangedPage(NewPageId(), item.Source.Id, 1)).ToList()));
    }

    public void Undo()
    {
        var previous = Arrangement;
        if (_history.Undo()) Settled(previous);
    }

    public void Redo()
    {
        var previous = Arrangement;
        if (_history.Redo()) Settled(previous);
    }

    // ── Saving ────────────────────────────────────────────────────────────

    /// <summary>
    /// What saving changes about the file beyond its pages: an encrypted source is written plain, and a signed document
    /// is written anew, which invalidates its signatures.
    /// </summary>
    public async Task<PdfSaveWarnings> WarningsAsync(CancellationToken ct)
    {
        var warnings = PdfSaveWarnings.None;
        var used = Arrangement.Pages.Select(page => page.Source).Prepend(Base.Id).Distinct().ToList();
        foreach (var id in used)
        {
            var source = Source(id);
            if (await source.ReadAsync(scope => scope.Document.IsEncrypted, ct))
                warnings |= PdfSaveWarnings.Encrypted;
        }

        if (await Base.ReadAsync(scope => PdfDocumentFacts.IsSigned(scope.Document), ct))
            warnings |= PdfSaveWarnings.Signed;

        return warnings;
    }

    /// <summary>
    /// Writes the document over its own file, or as <paramref name="target"/>, which it then is. A file inside an
    /// archive is written back into it.
    /// </summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file, or its archive, cannot be written.</exception>
    public async Task SaveAsync(string? target, CancellationToken ct)
    {
        var destination = target ?? Base.Path;
        var arrangement = Arrangement;
        var files = Files();
        var real = VirtualFileSystem.Instance.TryResolveReal(destination);
        var written = real is null
            ? System.IO.Path.Combine(Drafts(), $"saving-{Interlocked.Increment(ref _nextDraft)}.pdf")
            : real + ".nexatmp";

        try
        {
            await Task.Run(() =>
            {
                using var output = new FileStream(written, FileMode.Create, FileAccess.Write, FileShare.None);
                PdfArrangementWriter.Write(arrangement, files, Base.Id, output, ct);
            }, ct);

            if (string.Equals(destination, Base.Path, StringComparison.OrdinalIgnoreCase))
            {
                await Base.CloseAsync();
                Base.Release();
            }

            if (real is null)
                VirtualFileSystem.Instance.Replace(destination, await File.ReadAllBytesAsync(written, ct));
            else if (File.Exists(real))
                File.Replace(written, real, null);
            else
                File.Move(written, real);
        }
        finally
        {
            TryDelete(written);
        }

        await RebaseAsync(destination, ct);
    }

    /// <summary>
    /// After a save: the file written is the one source, its pages the arrangement's in order, each keeping its id; the
    /// history starts again from it, and everything the edits needed is let go.
    /// </summary>
    private async Task RebaseAsync(string path, CancellationToken ct)
    {
        var moved = !string.Equals(path, Base.Path, StringComparison.OrdinalIgnoreCase);
        var saved = new PdfSourceDocument(new PdfSourceId(Interlocked.Increment(ref _nextSource)), path);
        if (await saved.ReadSlotsAsync(ct) is null)
        {
            saved.Dispose();
            throw new IOException($"'{path}' was written but cannot be read back.");
        }

        var previous = Arrangement;
        var rebased = PdfPageArrangement.Of(previous.Pages.Select((page, index) => new PdfArrangedPage(page.Id, saved.Id, index + 1)));

        List<PdfSourceDocument> released;
        lock (_sources)
        {
            released = _sources.Values.ToList();
            _sources.Clear();
            _files.Clear();
        }

        Base = saved;
        Register(saved, new PdfSourceFile(path, null));
        foreach (var source in released)
            source.Dispose();

        _working?.Dispose();
        _working = null;
        DeleteDrafts();

        _history = new SnapshotHistory<PdfPageArrangement>(rebased);
        _saved = rebased;

        Changed?.Invoke(previous, rebased);
        DocumentChanged?.Invoke();
        DirtyChanged?.Invoke(false);
        if (moved) Moved?.Invoke(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _idleTimer.Dispose();
        _working?.Dispose();
        List<PdfSourceDocument> sources;
        lock (_sources) sources = _sources.Values.ToList();
        foreach (var source in sources)
            source.Dispose();
        DeleteDrafts();
    }

    // ── Inside ────────────────────────────────────────────────────────────

    private void Commit(PdfPageArrangement next)
    {
        if (next.Equals(Arrangement)) return;
        if (!IsDirty) HoldAll();

        var previous = Arrangement;
        _history.Commit(next);
        Settled(previous);
    }

    /// <summary>
    /// After the arrangement changed: the reader rearranges, the working copy is written again — or let go, when the
    /// pages are back as the file has them — and the dirty state follows.
    /// </summary>
    private void Settled(PdfPageArrangement previous)
    {
        var wasDirty = !previous.Equals(_saved);
        Changed?.Invoke(previous, Arrangement);

        if (IsDirty)
        {
            Working().Request(Arrangement);
        }
        else
        {
            _working?.Clear();
            DocumentChanged?.Invoke();
        }

        if (wasDirty != IsDirty) DirtyChanged?.Invoke(IsDirty);
    }

    private PdfWorkingCopy Working()
    {
        if (_working is not null) return _working;

        _working = new PdfWorkingCopy(
            Drafts(),
            (arrangement, output, ct) => Task.Run(() => PdfArrangementWriter.Write(arrangement, Files(), Base.Id, output, ct), ct),
            path => new PdfSourceDocument(WorkingCopies, path));
        _working.Written += _ => DocumentChanged?.Invoke();
        return _working;
    }

    /// <summary>
    /// Holds the files the edits depend on against writers — the file and every PDF inserted, never a draft of our own.
    /// </summary>
    /// <exception cref="PdfEditRefusedException">Another program has one open for writing.</exception>
    private void HoldAll()
    {
        List<PdfSourceDocument> sources;
        lock (_sources) sources = _sources.Values.ToList();
        foreach (var source in sources)
            Hold(source);
    }

    private void Hold(PdfSourceDocument source)
    {
        if (_drafts is not null && source.Path.StartsWith(_drafts, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            source.Hold();
        }
        catch (IOException ex)
        {
            throw new PdfEditRefusedException(source.Path, PdfEditRefusal.InUse, ex);
        }
    }

    private IReadOnlyDictionary<PdfSourceId, PdfSourceFile> Files()
    {
        lock (_sources) return new Dictionary<PdfSourceId, PdfSourceFile>(_files);
    }

    private PdfSourceDocument AddSource(string path, PdfSourceFile file)
    {
        var source = new PdfSourceDocument(new PdfSourceId(_nextSource), path);
        Register(source, file);
        return source;
    }

    private void Register(PdfSourceDocument source, PdfSourceFile file)
    {
        lock (_sources)
        {
            _sources.Add(source.Id, source);
            _files.Add(source.Id, file);
        }
    }

    private PdfPageId NewPageId() => new(Interlocked.Increment(ref _nextPage));

    /// <summary>The session's own draft folder, made when first needed.</summary>
    private string Drafts() =>
        _drafts ??= System.IO.Path.GetDirectoryName(DraftLocation.NewFile("pages.pdf"))!;

    private void DeleteDrafts()
    {
        if (_drafts is null) return;
        try { Directory.Delete(_drafts, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        _drafts = null;
    }

    private void CloseIdle()
    {
        if (_disposed) return;
        List<PdfSourceDocument> sources;
        lock (_sources) sources = _sources.Values.ToList();
        foreach (var source in sources)
            source.CloseIfIdle();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The document as it now stands, for every whole-document reader to hold: each read goes to the file, or to the
    /// working copy of the edits once it is written — and again to the next copy should the one it found be replaced
    /// while it waited.
    /// </summary>
    private sealed class CurrentDocument(PdfDocumentSession session) : IPdfDocumentAccess
    {
        public async Task<T?> ReadAsync<T>(Func<PdfDocumentScope, T> read, CancellationToken ct)
        {
            while (true)
            {
                if (!session.IsDirty || session._working is not { } working)
                    return await session.Base.ReadAsync(read, ct).ConfigureAwait(false);

                var copy = await working.Current.WaitAsync(ct).ConfigureAwait(false);
                if (copy is null) return default;

                var result = await copy.ReadAsync(read, ct).ConfigureAwait(false);
                if (!copy.IsDisposed) return result;
            }
        }
    }
}
