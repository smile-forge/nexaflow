using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using System.IO;

namespace Nexaflow.Core.Services;

/// <summary>
/// The session's watch on the file system, and the folders a long change is under way in: one watch per file however many
/// pages of however many workspaces follow it, one folder watcher, and one record of busy folders — so two workspaces looking
/// at one folder see the same "please wait" and hear of the same change. A file on a location is watched by the location,
/// through the watching runtime's own file system, since the same path in another runtime may be another place. Used on the
/// UI thread; a change is told there.
/// </summary>
public sealed class FolderActivity : IDisposable
{
    private readonly IUiThread _ui;
    private readonly Func<ShellFolderWatcher> _newFolderWatcher;

    /// <summary>Each file watched, with whoever follows it.</summary>
    private readonly Dictionary<WatchKey, WatchGroup> _groups = new(new WatchKeyComparer());

    /// <summary>The watches each owner holds, let go of together when it ends.</summary>
    private readonly Dictionary<object, List<IDisposable>> _byOwner = new(ReferenceEqualityComparer.Instance);

    /// <summary>Made on the first folder watched: it owns a window on the UI thread.</summary>
    private ShellFolderWatcher? _folderWatcher;

    /// <param name="ui">Where a change is told.</param>
    /// <param name="newFolderWatcher">Makes the folder watcher, the first time a folder is watched.</param>
    internal FolderActivity(IUiThread ui, Func<ShellFolderWatcher> newFolderWatcher)
    {
        _ui               = ui;
        _newFolderWatcher = newFolderWatcher;
    }

    /// <summary>The folders a long change is under way in, and what each says meanwhile.</summary>
    public FolderBusyTracker Busy { get; } = new();

    /// <summary>
    /// Follows <paramref name="path"/> for <paramref name="owner"/>: <paramref name="onChanged"/> runs on the UI thread when it
    /// changes. A file on a location is watched by the location, where it can watch; everything else where it is on disk.
    /// </summary>
    internal IFileWatch WatchFile(object owner, LocationFileSystem files, string path, Action onChanged)
    {
        bool remote = files.GetBacking(path) is VirtualBacking.Remote;
        var key = remote ? new WatchKey(files, path) : new WatchKey(null, SafeFullPath(files, path));
        if (!_groups.TryGetValue(key, out var group))
        {
            group = new WatchGroup();
            if (remote)
            {
                _ = WatchOnLocationAsync(files, path, key, group);
            }
            else
            {
                try
                {
                    group.Watcher = new FileChangeWatcher(key.Path);
                    group.Watcher.Changed += () => OnWatchChanged(key);
                }
                catch { /* missing directory etc. — the handle is inert but still disposable */ }
            }
            _groups[key] = group;
        }

        var handle = new FileWatch(this, owner, key, onChanged);
        group.Subscribers.Add(handle);
        Hold(owner, handle);
        return handle;
    }

    /// <summary>Follows the folder <paramref name="path"/> for <paramref name="owner"/>, telling each change on the UI thread.</summary>
    internal IDisposable WatchFolder(object owner, string path, Action<FolderChange> onChanged)
    {
        var watch = new FolderWatch(this, owner, (_folderWatcher ??= _newFolderWatcher()).Watch(path, onChanged));
        Hold(owner, watch);
        return watch;
    }

    /// <summary>Lets go of every watch <paramref name="owner"/> still holds: it has ended.</summary>
    internal void Release(object owner)
    {
        if (!_byOwner.Remove(owner, out var held)) return;
        foreach (var watch in held.ToList()) watch.Dispose();
    }

    /// <summary>Closes every watch: the session is ending.</summary>
    public void Dispose()
    {
        foreach (var group in _groups.Values)
        {
            group.Watcher?.Dispose();
            group.Location?.Dispose();
        }
        _groups.Clear();
        _byOwner.Clear();
        _folderWatcher?.Dispose();
        _folderWatcher = null;
    }

    private void Hold(object owner, IDisposable watch)
    {
        if (!_byOwner.TryGetValue(owner, out var held)) _byOwner[owner] = held = [];
        held.Add(watch);
    }

    private void Forget(object owner, IDisposable watch)
    {
        if (_byOwner.TryGetValue(owner, out var held) && held.Remove(watch) && held.Count == 0) _byOwner.Remove(owner);
    }

    /// <summary>
    /// Sets up the location's own watch for a file on it. A location without <c>Watch</c> answers null, and the handle stays
    /// inert — the viewer reloads when asked. The group may have gone (every subscriber disposed) while the watch was being
    /// set up, and then the watch goes too.
    /// </summary>
    private async Task WatchOnLocationAsync(LocationFileSystem files, string path, WatchKey key, WatchGroup group)
    {
        ILocationWatch? watch;
        try { watch = await files.WatchChangesAsync(path, recursive: false); }
        catch (IOException) { return; }
        if (watch is null) return;

        if (!_groups.TryGetValue(key, out var current) || !ReferenceEquals(current, group))
        {
            watch.Dispose();
            return;
        }
        watch.Changed += _ => OnWatchChanged(key);
        group.Location = watch;
    }

    // The raw watcher event arrives on a thread-pool thread; hop to the UI thread, then fan out.
    private void OnWatchChanged(WatchKey key) => _ui.Post(() =>
    {
        if (!_groups.TryGetValue(key, out var group)) return;
        foreach (var sub in group.Subscribers.ToArray())
            sub.RaiseChanged();
    });

    private void Unwatch(WatchKey key, FileWatch handle)
    {
        Forget(handle.Owner, handle);
        if (!_groups.TryGetValue(key, out var group)) return;
        group.Subscribers.Remove(handle);
        if (group.Subscribers.Count == 0)
        {
            group.Watcher?.Dispose();
            group.Location?.Dispose();
            _groups.Remove(key);
        }
    }

    private static string SafeFullPath(LocationFileSystem files, string path)
    {
        try
        {
            // Watch the outermost real container, so a request to watch a file *inside* an archive
            // (a non-existent OS path) watches the archive that actually changes on disk. Resolving such
            // a container change back to the specific inner view is deferred.
            //
            // SplitOutermostContainer runs FIRST because it is the mount-aware one: Path.GetFullPath
            // cannot parse a "::mount\…" path and would throw straight into the catch, leaving the
            // watcher pointed at a virtual path that no FileSystemWatcher can observe.
            var outer = files.SplitOutermostContainer(path).RealContainer;
            return Path.GetFullPath(files.TryResolveReal(outer) ?? outer);
        }
        catch { return path; }
    }

    /// <summary>A watched file: its path on disk, or its path on a location as one runtime's file system reaches it.</summary>
    private readonly record struct WatchKey(LocationFileSystem? Files, string Path);

    private sealed class WatchKeyComparer : IEqualityComparer<WatchKey>
    {
        public bool Equals(WatchKey x, WatchKey y)
            => ReferenceEquals(x.Files, y.Files) && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(WatchKey key)
            => HashCode.Combine(key.Files is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Files),
                                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Path));
    }

    private sealed class WatchGroup
    {
        public FileChangeWatcher? Watcher;
        /// <summary>The location's own watch, for a file on one; set once the location answers.</summary>
        public ILocationWatch? Location;
        public readonly List<FileWatch> Subscribers = [];
    }

    private sealed class FileWatch(FolderActivity activity, object owner, WatchKey key, Action onChanged) : IFileWatch
    {
        private bool _enabled = true;
        private bool _pending;
        private bool _disposed;

        public object Owner => owner;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (value && _pending) { _pending = false; onChanged(); }
            }
        }

        // Always invoked on the UI thread (from the marshalled fan-out).
        public void RaiseChanged()
        {
            if (_disposed) return;
            if (_enabled) onChanged();
            else _pending = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            activity.Unwatch(key, this);
        }
    }

    private sealed class FolderWatch(FolderActivity activity, object owner, IDisposable watch) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            watch.Dispose();
            activity.Forget(owner, this);
        }
    }
}
