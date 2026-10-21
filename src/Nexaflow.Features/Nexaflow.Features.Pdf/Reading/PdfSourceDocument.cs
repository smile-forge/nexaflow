using System.IO;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Pdf.Reading;

/// <summary>
/// One PDF a tab reads from — the file it shows, or one inserted into it — opened on first use and every reader
/// serialised behind one lock: PdfPig's document is not thread-safe, and re-parsing the cross-reference table per
/// question is the difference between a fast answer and a slow one on a large report.
/// <para>
/// The parsed document is dropped after a spell nobody reads it, and opened again on the next read. While edits depend
/// on the file it can also be <see cref="Hold"/>en: a handle that refuses writers, so the file cannot change under them.
/// </para>
/// </summary>
internal sealed class PdfSourceDocument : IPdfDocumentAccess, IDisposable
{
    private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private PdfDocumentScope? _scope;
    private bool _failed;               // TryOpen already said no; don't keep retrying per read
    private DateTime _lastUsedUtc = DateTime.UtcNow;
    private FileStream? _hold;
    private bool _disposed;

    public PdfSourceDocument(PdfSourceId id, string path)
    {
        Id = id;
        Path = path;
    }

    public PdfSourceId Id { get; }

    /// <summary>The document as opened — a path inside an archive is read through the virtual file system.</summary>
    public string Path { get; }

    /// <summary>Its page slots, once <see cref="ReadSlotsAsync"/> has read them.</summary>
    public PdfDocumentNode? Slots { get; private set; }

    /// <summary>Whether a handle refusing writers is open on the file.</summary>
    public bool IsHeld => _hold is not null;

    /// <summary>Whether the document is closed for good: a read of it gives nothing.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Reads the document's page slots, once; null when the document cannot be read at all.
    /// </summary>
    public async Task<PdfDocumentNode?> ReadSlotsAsync(CancellationToken ct)
    {
        if (Slots is { } known) return known;
        Slots = await ReadAsync(scope => scope.Pages.ReadDocument(ct), ct).ConfigureAwait(false);
        return Slots;
    }

    /// <summary>
    /// Runs <paramref name="read"/> against the open document, opening it first when it is not, off the UI thread and
    /// under the lock every reader of it shares. Default when the document cannot be opened at all.
    /// </summary>
    public async Task<T?> ReadAsync<T>(Func<PdfDocumentScope, T> read, CancellationToken ct)
    {
        CancellationTokenSource linked;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        }
        catch (ObjectDisposedException)
        {
            return default;   // closed for good while the read was on its way
        }

        using var _ = linked;
        var token = linked.Token;

        try
        {
            await _lock.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposed && !ct.IsCancellationRequested)
        {
            return default;
        }
        catch (ObjectDisposedException)
        {
            return default;
        }
        try
        {
            return await Task.Run(() =>
            {
                if (_scope is null && !_failed)
                {
                    _scope = PdfDocumentScope.TryOpen(Path, token);
                    _failed = _scope is null;
                }

                _lastUsedUtc = DateTime.UtcNow;
                return _scope is null ? default : read(_scope);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Opens a handle on the file that refuses writers, so the file stays as the edits that depend on it read it.
    /// A file inside an archive is held by its archive's own rules, not here.
    /// </summary>
    /// <exception cref="IOException">Another program has the file open for writing.</exception>
    public void Hold()
    {
        if (_hold is not null || VirtualFileSystem.Instance.TryResolveReal(Path) is not { } real) return;
        _hold = new FileStream(real, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Release()
    {
        _hold?.Dispose();
        _hold = null;
    }

    /// <summary>
    /// Drops the parsed document when nobody has read it for a while: a large scan holds a sizeable object graph and
    /// a file handle open, and a background tab nobody has asked anything about shouldn't. The next read reopens it.
    /// </summary>
    public void CloseIfIdle()
    {
        if (_disposed || _scope is null || DateTime.UtcNow - _lastUsedUtc < IdleAfter) return;
        if (!_lock.Wait(0)) return;   // busy — try again on the next tick rather than blocking one
        try
        {
            if (DateTime.UtcNow - _lastUsedUtc >= IdleAfter) CloseScope();
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Drops the parsed document now, waiting for a read in flight: before the file is replaced.
    /// </summary>
    public async Task CloseAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try { CloseScope(); }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }

        // Under the lock so a read in flight isn't parsing a document being torn out from under it. If it doesn't
        // yield in time, close anyway: the read is cancelled and unwinding, and a tab closing must not wait on it.
        var held = false;
        try { held = _lock.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { CloseScope(); }
        catch { }
        finally
        {
            if (held) _lock.Release();
        }

        Release();
        _lock.Dispose();
        _cts.Dispose();
    }

    private void CloseScope()
    {
        _scope?.Dispose();
        _scope = null;
        _failed = false;   // a fresh open may well succeed; the earlier one wasn't a verdict
    }
}
