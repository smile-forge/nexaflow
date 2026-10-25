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
/// <param name="Moved">The paths among <paramref name="Paths"/> that were created, deleted or renamed rather than only
/// written to. A directory written to is one whose entries changed, and each of those is reported by its own name; a
/// directory that came, went or was renamed is the only report there is of what is beneath it.</param>
public readonly record struct TreeChangeBatch(IReadOnlyCollection<string> Paths, bool Overflowed,
                                              IReadOnlySet<string> Moved)
{
    public bool IsEmpty => !Overflowed && (Paths is null || Paths.Count == 0);
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
/// A caller whose derived state is read by others while it catches up takes with <see cref="Hold"/> instead, and
/// <see cref="Settle"/>s once what it made is published: until then <see cref="Pending"/> still counts the batch, so a
/// reader that looks in between learns that something is on its way rather than finding nothing and trusting a state
/// the batch is about to replace.
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
    private readonly ITimer _quiet;
    private readonly int _quietMs;
    private readonly object _gate = new();
    private HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _moved = new(StringComparer.OrdinalIgnoreCase);
    private bool _overflowed;

    /// <summary>Batches handed out by <see cref="Hold"/> and not yet settled.</summary>
    private readonly List<TreeChangeBatch> _held = [];
    private bool _disposed;

    /// <summary>Raised once per burst, a quiet period after the last relevant notification.</summary>
    public event Action? Changed;

    /// <param name="root">The directory to watch.</param>
    /// <param name="relevant">Whether a full path is worth collecting.</param>
    /// <param name="subdirectories">Whether the whole tree is watched, or <paramref name="root"/>'s own entries only.</param>
    /// <param name="quietMs">How long after the last notification <see cref="Changed"/> is raised.</param>
    /// <param name="time">The clock the quiet period is measured on: the system's, unless a caller has to hold it still.</param>
    public TreeChangeWatcher(string root, Func<string, bool> relevant, bool subdirectories = true, int quietMs = 250,
                             TimeProvider? time = null)
    {
        _relevant = relevant;
        _quietMs  = quietMs;

        // Without the creator's ambient context: what the watcher raises is the tree's doing, not that of whichever caller
        // happened to start watching it, and a timer would otherwise carry that caller's context into every call it makes.
        var flow = ExecutionContext.IsFlowSuppressed() ? (AsyncFlowControl?)null : ExecutionContext.SuppressFlow();
        try
        {
            _quiet = (time ?? TimeProvider.System).CreateTimer(_ => Changed?.Invoke(), null,
                                                               Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            // The largest buffer the system allows: a checkout fills the default 8 KB in a blink, and every overflow
            // costs the caller a look at the whole tree.
            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = subdirectories,
                NotifyFilter          = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                      | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize    = 64 * 1024,
            };
            _watcher.Changed += OnWritten;
            _watcher.Created += OnMoved;
            _watcher.Deleted += OnMoved;
            _watcher.Renamed += OnRenamed;
            _watcher.Error   += OnError;
            _watcher.EnableRaisingEvents = true;
        }
        finally { flow?.Undo(); }
    }

    /// <summary>Whether anything has been collected since the last <see cref="Take"/> or <see cref="Hold"/>.</summary>
    public bool HasChanges
    {
        get { lock (_gate) return _overflowed || _paths.Count > 0; }
    }

    /// <summary>Hands over everything collected and starts collecting afresh — what arrives while the caller works on
    /// this batch is the next one's.</summary>
    public TreeChangeBatch Take()
    {
        lock (_gate) return TakeCollected();
    }

    /// <summary>
    /// Hands over everything collected, as <see cref="Take"/> does, but goes on counting it in <see cref="Pending"/>
    /// until <see cref="Settle"/> is called with it — for a caller whose readers must not see the batch vanish before
    /// what it is folded into is there to read.
    /// </summary>
    public TreeChangeBatch Hold()
    {
        lock (_gate)
        {
            var batch = TakeCollected();
            if (!batch.IsEmpty) _held.Add(batch);
            return batch;
        }
    }

    /// <summary>The batch <see cref="Hold"/> handed out is accounted for: <see cref="Pending"/> stops counting it.</summary>
    public void Settle(TreeChangeBatch batch)
    {
        if (batch.IsEmpty) return;
        lock (_gate)
        {
            var at = _held.FindIndex(held => ReferenceEquals(held.Paths, batch.Paths));
            if (at >= 0) _held.RemoveAt(at);
        }
    }

    /// <summary>
    /// Everything reported and not yet accounted for — collected, or held and not settled — without taking any of it.
    /// What a reader judges the state it is about to read against.
    /// </summary>
    public TreeChangeBatch Pending()
    {
        lock (_gate)
        {
            if (_held.Count == 0 && _paths.Count == 0) return new TreeChangeBatch([], _overflowed, Empty);

            var paths      = new HashSet<string>(_paths, StringComparer.OrdinalIgnoreCase);
            var moved      = new HashSet<string>(_moved, StringComparer.OrdinalIgnoreCase);
            var overflowed = _overflowed;
            foreach (var held in _held)
            {
                paths.UnionWith(held.Paths);
                moved.UnionWith(held.Moved);
                overflowed |= held.Overflowed;
            }
            return new TreeChangeBatch(paths, overflowed, moved);
        }
    }

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    /// <summary>Under the gate.</summary>
    private TreeChangeBatch TakeCollected()
    {
        var batch = new TreeChangeBatch(_paths, _overflowed, _moved);
        _paths      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _moved      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _overflowed = false;
        return batch;
    }

    private void OnWritten(object sender, FileSystemEventArgs e) => Note(e.FullPath, null, moved: false);

    private void OnMoved(object sender, FileSystemEventArgs e) => Note(e.FullPath, null, moved: true);

    private void OnRenamed(object sender, RenamedEventArgs e) => Note(e.OldFullPath, e.FullPath, moved: true);

    private void OnError(object sender, ErrorEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _overflowed = true;
        }
        Arm();
    }

    private void Note(string path, string? also, bool moved)
    {
        var first  = _relevant(path);
        var second = also is not null && _relevant(also);
        if (!first && !second) return;

        lock (_gate)
        {
            if (_disposed) return;
            if (first) Collect(path, moved);
            if (second) Collect(also!, moved);
        }
        Arm();
    }

    /// <summary>Under the gate.</summary>
    private void Collect(string path, bool moved)
    {
        _paths.Add(path);
        if (moved) _moved.Add(path);
    }

    private void Arm()
    {
        try { _quiet.Change(TimeSpan.FromMilliseconds(_quietMs), Timeout.InfiniteTimeSpan); }
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
