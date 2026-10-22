using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Nexaflow.IO.Common;

/// <summary>What a <see cref="TreeChangeWatcher"/> collected since it was last asked.</summary>
/// <param name="Paths">Full paths reported changed, created, deleted or renamed (both names of a rename) — files and
/// directories alike, since a folder renamed or deleted whole is reported once, by its own name.</param>
/// <param name="Overflowed">The watcher lost track: the system dropped notifications, so <paramref name="Paths"/> is not
/// the whole of what changed and the caller has to look for itself.</param>
public readonly record struct TreeChangeBatch(IReadOnlyCollection<string> Paths, bool Overflowed)
{
    public bool IsEmpty => !Overflowed && Paths.Count == 0;
}

/// <summary>
/// Watches a directory tree and collects what changed beneath it, for a caller that keeps something derived from the
/// tree and wants to redo only what moved.
/// <para>
/// Collecting rather than forwarding is the point. A checkout or a build raises thousands of notifications in a second;
/// the caller wants one call with every path, a quiet moment after the last of them — <see cref="Changed"/> — and the
/// paths themselves when it gets round to asking (<see cref="Take"/>), which may be sooner: someone about to read the
/// derived state can ask <see cref="HasChanges"/> and catch up first rather than wait out the quiet period.
/// </para>
/// <para>
/// <paramref name="relevant"/> runs on the notification thread for every path, so it has to be cheap — a test of the
/// path's text, not of the disk. UI-agnostic: <see cref="Changed"/> is raised on a thread-pool thread.
/// </para>
/// </summary>
public sealed class TreeChangeWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Func<string, bool> _relevant;
    private readonly Timer _quiet;
    private readonly int _quietMs;
    private readonly object _gate = new();
    private HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private bool _overflowed;
    private bool _disposed;

    /// <summary>Raised once per burst, a quiet period after the last relevant notification.</summary>
    public event Action? Changed;

    /// <param name="root">The directory to watch.</param>
    /// <param name="relevant">Whether a full path is worth collecting.</param>
    /// <param name="subdirectories">Whether the whole tree is watched, or <paramref name="root"/>'s own entries only.</param>
    /// <param name="quietMs">How long after the last notification <see cref="Changed"/> is raised.</param>
    public TreeChangeWatcher(string root, Func<string, bool> relevant, bool subdirectories = true, int quietMs = 250)
    {
        _relevant = relevant;
        _quietMs  = quietMs;
        _quiet    = new Timer(_ => Changed?.Invoke());

        // The largest buffer the system allows: a checkout fills the default 8 KB in a blink, and every overflow
        // costs the caller a look at the whole tree.
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = subdirectories,
            NotifyFilter          = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                  | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize    = 64 * 1024,
        };
        _watcher.Changed += OnRaw;
        _watcher.Created += OnRaw;
        _watcher.Deleted += OnRaw;
        _watcher.Renamed += OnRenamed;
        _watcher.Error   += OnError;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Whether anything has been collected since the last <see cref="Take"/>.</summary>
    public bool HasChanges
    {
        get { lock (_gate) return _overflowed || _paths.Count > 0; }
    }

    /// <summary>Hands over everything collected and starts collecting afresh — what arrives while the caller works on
    /// this batch is the next one's.</summary>
    public TreeChangeBatch Take()
    {
        lock (_gate)
        {
            var batch = new TreeChangeBatch(_paths, _overflowed);
            _paths      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _overflowed = false;
            return batch;
        }
    }

    private void OnRaw(object sender, FileSystemEventArgs e) => Note(e.FullPath, null);

    private void OnRenamed(object sender, RenamedEventArgs e) => Note(e.OldFullPath, e.FullPath);

    private void OnError(object sender, ErrorEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _overflowed = true;
        }
        Arm();
    }

    private void Note(string path, string? also)
    {
        var first  = _relevant(path);
        var second = also is not null && _relevant(also);
        if (!first && !second) return;

        lock (_gate)
        {
            if (_disposed) return;
            if (first) _paths.Add(path);
            if (second) _paths.Add(also!);
        }
        Arm();
    }

    private void Arm()
    {
        try { _quiet.Change(_quietMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* disposed between the check and here */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _quiet.Dispose();
    }
}
