using System.IO;
using Nexaflow.Features.Pdf.Reading;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// The edited document written whole, for what reads a document whole — its properties and outline, its scans, the
/// assistant's reads and searches — so none of them has to know pages were moved. It is written in the background a
/// moment after the last edit, a newer edit cancelling a write that is under way, and each copy replaces the one
/// before, which is deleted once nothing reads it.
/// </summary>
internal sealed class PdfWorkingCopy : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private readonly string _folder;
    private readonly Func<PdfPageArrangement, Stream, CancellationToken, Task> _write;
    private readonly Func<string, PdfSourceDocument> _open;
    private readonly object _gate = new();

    private CancellationTokenSource? _writing;
    private TaskCompletionSource<PdfSourceDocument?> _latest = Completed(null);
    private PdfSourceDocument? _current;
    private int _written;
    private bool _disposed;

    /// <param name="folder">Where copies are written: the edit's own draft folder.</param>
    /// <param name="write">Writes an arrangement as a whole document.</param>
    /// <param name="open">Opens a copy written, to be read.</param>
    public PdfWorkingCopy(string folder, Func<PdfPageArrangement, Stream, CancellationToken, Task> write, Func<string, PdfSourceDocument> open)
    {
        _folder = folder;
        _write = write;
        _open = open;
    }

    /// <summary>
    /// The copy of the latest arrangement asked for, once it is written; null when it could not be written.
    /// </summary>
    public Task<PdfSourceDocument?> Current
    {
        get { lock (_gate) return _latest.Task; }
    }

    /// <summary>Raised on the thread pool when a copy of the latest arrangement is ready to read.</summary>
    public event Action<PdfSourceDocument>? Written;

    /// <summary>
    /// Writes <paramref name="arrangement"/> once edits settle, in place of any write still under way.
    /// </summary>
    public void Request(PdfPageArrangement arrangement)
    {
        CancellationTokenSource writing;
        TaskCompletionSource<PdfSourceDocument?> latest;
        lock (_gate)
        {
            if (_disposed) return;
            _writing?.Cancel();
            _writing = writing = new CancellationTokenSource();
            if (_latest.Task.IsCompleted)
                _latest = new TaskCompletionSource<PdfSourceDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
            latest = _latest;
        }

        _ = Task.Run(() => WriteAsync(arrangement, writing, latest));
    }

    private async Task WriteAsync(PdfPageArrangement arrangement, CancellationTokenSource writing, TaskCompletionSource<PdfSourceDocument?> latest)
    {
        var ct = writing.Token;
        string? path = null;
        try
        {
            await Task.Delay(Settle, ct).ConfigureAwait(false);

            path = Path.Combine(_folder, $"working-{Interlocked.Increment(ref _written)}.pdf");
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await _write(arrangement, file, ct).ConfigureAwait(false);

            var copy = _open(path);
            PdfSourceDocument? replaced;
            lock (_gate)
            {
                if (_disposed || writing != _writing)
                {
                    copy.Dispose();
                    TryDelete(path);
                    return;
                }

                replaced = _current;
                _current = copy;
                latest.TrySetResult(copy);
            }

            Written?.Invoke(copy);
            Retire(replaced);
        }
        catch (OperationCanceledException)
        {
            if (path is not null) TryDelete(path);
        }
        catch
        {
            if (path is not null) TryDelete(path);
            lock (_gate)
            {
                if (writing == _writing) latest.TrySetResult(null);
            }
        }
    }

    /// <summary>
    /// Drops every copy: the document as it stands is the file itself again, after a save or an undo back to it.
    /// </summary>
    public void Clear()
    {
        PdfSourceDocument? replaced;
        lock (_gate)
        {
            _writing?.Cancel();
            _writing = null;
            replaced = _current;
            _current = null;
            _latest.TrySetResult(null);
            _latest = Completed(null);
        }

        Retire(replaced);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Clear();
    }

    /// <summary>
    /// Closes a copy no longer current and deletes its file — after any read of it in flight, which closing waits for.
    /// </summary>
    private static void Retire(PdfSourceDocument? copy)
    {
        if (copy is null) return;
        copy.Dispose();
        TryDelete(copy.Path);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static TaskCompletionSource<PdfSourceDocument?> Completed(PdfSourceDocument? result)
    {
        var completed = new TaskCompletionSource<PdfSourceDocument?>(TaskCreationOptions.RunContinuationsAsynchronously);
        completed.SetResult(result);
        return completed;
    }
}
