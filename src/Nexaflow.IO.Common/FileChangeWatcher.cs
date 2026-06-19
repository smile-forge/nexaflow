using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.IO.Common;

/// <summary>
/// Wraps a <see cref="FileSystemWatcher"/> on a single file with the LastWrite+Size filter and a
/// debounce, so a burst of change notifications collapses into one <see cref="Changed"/> event a short
/// quiet period later (the recent end of a growing log fires many raw events). UI-agnostic:
/// <see cref="Changed"/> is raised on a thread-pool thread — the caller marshals to its own dispatcher.
/// </summary>
public sealed class FileChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly int _debounceMs;
    private readonly object _gate = new();
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    /// <summary>Raised once per burst, <c>debounceMs</c> after the last raw change.</summary>
    public event Action? Changed;

    /// <param name="filePath">The single file to watch (its directory + name are used).</param>
    /// <param name="debounceMs">Quiet period that collapses a burst of events into one. Default 300 ms.</param>
    public FileChangeWatcher(string filePath, int debounceMs = 300)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
            throw new ArgumentException("File path must include a directory.", nameof(filePath));

        _debounceMs = debounceMs;
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(filePath))
        {
            NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnRaw;
    }

    private void OnRaw(object sender, FileSystemEventArgs e)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed) return;
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = cts = new CancellationTokenSource();
        }
        _ = DebounceAsync(cts.Token);
    }

    private async Task DebounceAsync(CancellationToken ct)
    {
        try { await Task.Delay(_debounceMs, ct).ConfigureAwait(false); }
        catch (TaskCanceledException) { return; }
        if (!ct.IsCancellationRequested) Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Cancel();
            _debounce?.Dispose();
        }
        _watcher.Changed -= OnRaw;
        _watcher.Dispose();
    }
}
