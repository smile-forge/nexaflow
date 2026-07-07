using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Nexaflow.Core.Services;

/// <summary>
/// Tracks folders with an in-flight mutation (e.g. a worktree deletion) so any file browser showing one can
/// display a "please wait" state and defer refreshing until it clears. Backs the <c>IShellServices</c>
/// folder-busy API. Pure and thread-safe; <see cref="Changed"/> fires synchronously on the caller's thread,
/// so the shell marshals it to the UI. Ref-counted per path — nested/overlapping marks on the same folder
/// only clear once the last is released.
/// </summary>
public sealed class FolderBusyTracker
{
    private readonly Dictionary<string, (string Message, int Count)> _busy = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Raised whenever any folder's busy state changes (marked or cleared).</summary>
    public event Action? Changed;

    /// <summary>Marks <paramref name="folderPath"/> busy with <paramref name="message"/>. Dispose the returned
    /// handle to clear it (idempotent, thread-safe).</summary>
    public IDisposable Mark(string folderPath, string message)
    {
        var key = Normalize(folderPath);
        lock (_lock)
            _busy[key] = _busy.TryGetValue(key, out var e) ? (message, e.Count + 1) : (message, 1);
        Changed?.Invoke();
        return new Handle(this, key);
    }

    /// <summary>The busy message for <paramref name="folderPath"/>, or null if no mutation is in flight there.</summary>
    public string? MessageFor(string folderPath)
    {
        var key = Normalize(folderPath);
        lock (_lock)
            return _busy.TryGetValue(key, out var e) ? e.Message : null;
    }

    private void Release(string key)
    {
        lock (_lock)
        {
            if (!_busy.TryGetValue(key, out var e)) return;
            if (e.Count <= 1) _busy.Remove(key);
            else _busy[key] = (e.Message, e.Count - 1);
        }
        Changed?.Invoke();
    }

    private static string Normalize(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path; }
    }

    private sealed class Handle(FolderBusyTracker owner, string key) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(key);
        }
    }
}
