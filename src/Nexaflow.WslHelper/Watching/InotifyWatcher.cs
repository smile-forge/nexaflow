using System.Runtime.InteropServices;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl.Protocol;
using Nexaflow.WslHelper.Files;
using Nexaflow.WslHelper.Native;

namespace Nexaflow.WslHelper.Watching;

/// <summary>
/// The helper's watches, on one inotify instance read by one thread of its own, both kept for the life of the helper.
/// A watch is a request id with a root and the inotify watches it holds; a recursive one holds one for every folder under
/// the root, those there at the start and those made or moved in later. Two watches over one folder share its inotify
/// watch, each knowing the folder by its own path. A change is reported as the full path of what changed, at most once per
/// path per <see cref="Window"/> milliseconds: the first at once, the rest of a burst as one when the window closes.
/// A watch reports nothing before <see cref="Activate"/> or after <see cref="Remove"/>, which run under the lock changes
/// are sent under, so no change reaches the backend before a watch's Done or after its end.
/// </summary>
internal sealed unsafe class InotifyWatcher(ReplyChannel replies)
{
    private const uint Modified = 0x2, AttributesChanged = 0x4, ClosedWritten = 0x8, MovedFrom = 0x40, MovedTo = 0x80,
                       Created = 0x100, Deleted = 0x200, DeletedSelf = 0x400, MovedSelf = 0x800, QueueOverflow = 0x4000,
                       Ignored = 0x8000, OnlyDirectory = 0x01000000, DontFollow = 0x02000000, ExcludeUnlinked = 0x04000000,
                       IsDirectory = 0x40000000;
    private const uint Changes = Created | Deleted | Modified | ClosedWritten | MovedFrom | MovedTo | AttributesChanged
                                 | DeletedSelf | MovedSelf | ExcludeUnlinked;
    // A folder under a recursive watch: a folder still, and never reached through a link.
    private const uint SubFolderChanges = Changes | OnlyDirectory | DontFollow;
    private const int NonBlocking = 0x800, CloseOnExec = 0x80000;   // IN_NONBLOCK, IN_CLOEXEC
    private const short Readable = 1;                               // POLLIN
    private const int EventHeader = 16;                             // struct inotify_event before its name
    private const long Window = 100;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, WatchedFolder> _folders = [];
    private readonly Dictionary<int, Subscription> _subscriptions = [];
    private readonly Dictionary<(int Watch, string Path), ChangeGate> _gates = [];
    private int _descriptor = -1;

    /// <summary>Sets up watch <paramref name="id"/> on <paramref name="root"/>. A sub-folder that cannot be watched is left
    /// out, and the root reported changed once the watch is active, so the backend reads it again.</summary>
    public void Add(int id, string root, bool recursive, CancellationToken ct)
    {
        lock (_gate)
        {
            Open();
            if (_subscriptions.ContainsKey(id)) throw new HelperFailure(WslFailure.Other, $"watch {id} is already set up");

            int wd = Libc.InotifyAddWatch(_descriptor, root, Changes);
            if (wd < 0) throw WatchFailure(Marshal.GetLastPInvokeError(), root);

            var subscription = new Subscription(id, root, recursive);
            _subscriptions[id] = subscription;
            Attach(subscription, wd, root);
            try
            {
                if (recursive && PosixFiles.Stat(root, followLink: true, out var at) == 0 && at.Kind == WireKind.Directory
                    && !AttachTree(subscription, root, ct))
                    MarkChanged(subscription, root, Environment.TickCount64);
                ct.ThrowIfCancellationRequested();
            }
            catch
            {
                Detach(subscription);
                throw;
            }
        }
    }

    /// <summary>Lets watch <paramref name="id"/> report, starting with what changed while it was being set up.</summary>
    public void Activate(int id)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(id, out var subscription)) return;
            subscription.Ready = true;
            long now = Environment.TickCount64;
            foreach (var (key, gate) in _gates)
                if (key.Watch == id && gate.Pending) Emit(key, gate, now);
        }
    }

    /// <summary>Ends watch <paramref name="id"/>, keeping any inotify watch another still holds. An id already ended is
    /// fine.</summary>
    public void Remove(int id)
    {
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(id, out var subscription)) Detach(subscription);
        }
    }

    private void Open()
    {
        if (_descriptor >= 0) return;
        int descriptor = Libc.InotifyInit(NonBlocking | CloseOnExec);
        if (descriptor < 0)
        {
            throw new HelperFailure(WslFailure.Other,
                $"inotify is not available: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
        }
        _descriptor = descriptor;
        new Thread(ReadLoop) { IsBackground = true, Name = "inotify" }.Start();
    }

    private void ReadLoop()
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                int timeout;
                lock (_gate) timeout = NextTimeout(Environment.TickCount64);

                var poll = new PollDescriptor { Descriptor = _descriptor, Events = Readable };
                int ready = Libc.Poll(&poll, 1, timeout);
                if (ready < 0)
                {
                    int errno = Marshal.GetLastPInvokeError();
                    if (errno == Errno.EINTR) continue;
                    throw new IOException($"poll: {Marshal.GetPInvokeErrorMessage(errno)}");
                }

                lock (_gate)
                {
                    long now = Environment.TickCount64;
                    if (ready > 0) Drain(buffer, now);
                    Flush(now);
                }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"nexaflow-wsl-helper: watching stopped: {e.Message}");
        }
    }

    // Reads every event queued, until the descriptor has none.
    private void Drain(byte[] buffer, long now)
    {
        fixed (byte* start = buffer)
        {
            while (true)
            {
                nint length = Libc.Read(_descriptor, start, buffer.Length);
                if (length <= 0)
                {
                    int errno = length == 0 ? Errno.EAGAIN : Marshal.GetLastPInvokeError();
                    if (errno == Errno.EAGAIN) return;
                    if (errno == Errno.EINTR) continue;
                    throw new IOException($"reading inotify: {Marshal.GetPInvokeErrorMessage(errno)}");
                }

                // struct inotify_event: i32 wd, u32 mask, u32 cookie, u32 len, then len bytes of name, NUL-padded.
                var events = new ReadOnlySpan<byte>(start, (int)length);
                for (int at = 0; at + EventHeader <= events.Length;)
                {
                    int wd = MemoryMarshal.Read<int>(events[at..]);
                    uint mask = MemoryMarshal.Read<uint>(events[(at + 4)..]);
                    int nameLength = MemoryMarshal.Read<int>(events[(at + 12)..]);
                    ReadOnlySpan<byte> name = events.Slice(at + EventHeader, nameLength);
                    int end = name.IndexOf((byte)0);
                    Dispatch(wd, mask, nameLength == 0 ? null : PosixNames.FromBytes(end < 0 ? name : name[..end]), now);
                    at += EventHeader + nameLength;
                }
            }
        }
    }

    private void Dispatch(int wd, uint mask, string? name, long now)
    {
        if ((mask & QueueOverflow) != 0)
        {
            // Changes were lost: every watch has its root read again.
            foreach (var subscription in _subscriptions.Values) MarkChanged(subscription, subscription.Root, now);
            return;
        }
        if (!_folders.TryGetValue(wd, out var folder)) return;

        if ((mask & Ignored) != 0)
        {
            // The kernel let the watch go: its folder is gone.
            foreach (var (watch, path) in folder.PathByWatch)
            {
                if (_subscriptions.TryGetValue(watch, out var holder) && holder.Folders.TryGetValue(path, out int held) && held == wd)
                    holder.Folders.Remove(path);
            }
            _folders.Remove(wd);
            return;
        }

        foreach (var (watch, path) in folder.PathByWatch.ToArray())
        {
            if (!_subscriptions.TryGetValue(watch, out var subscription)) continue;
            if (name is null)
            {
                // A sub-folder deleted or moved away is reported by its parent, by the name the backend knows it by.
                if ((mask & (DeletedSelf | MovedSelf)) != 0 && path != subscription.Root) continue;
                MarkChanged(subscription, path, now);
                continue;
            }

            string changed = DistroPath.Join(path, name);
            MarkChanged(subscription, changed, now);
            if (!subscription.Recursive || (mask & IsDirectory) == 0) continue;
            if ((mask & (Created | MovedTo)) != 0) TakeIn(subscription, changed, now);
            else if ((mask & (Deleted | MovedFrom)) != 0) LetGo(subscription, changed);
        }
    }

    // A folder made or moved in under a recursive watch is watched with everything under it. What changed in it before
    // its watch was set is covered by the change already reported for the folder itself.
    private void TakeIn(Subscription subscription, string folder, long now)
    {
        bool complete = AttachFolder(subscription, folder) switch
        {
            FolderWatch.Attached => AttachTree(subscription, folder, CancellationToken.None),
            FolderWatch.Held or FolderWatch.Gone => true,
            _ => false,
        };
        if (!complete) MarkChanged(subscription, subscription.Root, now);
    }

    // A folder deleted or moved away takes everything under it out of the watch.
    private void LetGo(Subscription subscription, string folder)
    {
        string under = folder + "/";
        foreach (string path in subscription.Folders.Keys.Where(p => p == folder || p.StartsWith(under, StringComparison.Ordinal)).ToList())
            DetachFolder(subscription, path);
    }

    // Watches every folder under start (itself already watched), never through a link. False when one could not be
    // watched or read.
    private bool AttachTree(Subscription subscription, string start, CancellationToken ct)
    {
        bool complete = true;
        var waiting = new Stack<string>();
        waiting.Push(start);
        while (waiting.TryPop(out string? folder))
        {
            ct.ThrowIfCancellationRequested();
            var children = SubFolderNames(folder);
            if (children is null)
            {
                complete = false;
                continue;
            }

            foreach (string name in children)
            {
                string child = DistroPath.Join(folder, name);
                switch (AttachFolder(subscription, child))
                {
                    case FolderWatch.Attached:
                        waiting.Push(child);
                        break;
                    case FolderWatch.Refused:
                        complete = false;
                        break;
                    case FolderWatch.LimitReached:
                        // No further watch would be added either.
                        return false;
                }
            }
        }
        return complete;
    }

    private FolderWatch AttachFolder(Subscription subscription, string path)
    {
        int wd = Libc.InotifyAddWatch(_descriptor, path, SubFolderChanges);
        if (wd < 0)
        {
            return Marshal.GetLastPInvokeError() switch
            {
                Errno.ENOSPC => FolderWatch.LimitReached,
                Errno.ENOENT or Errno.ENOTDIR => FolderWatch.Gone,
                _ => FolderWatch.Refused,
            };
        }
        // A folder the watch already holds, reached again through a bind mount, is not walked twice.
        if (_folders.TryGetValue(wd, out var folder) && folder.PathByWatch.ContainsKey(subscription.Id)) return FolderWatch.Held;
        Attach(subscription, wd, path);
        return FolderWatch.Attached;
    }

    private void Attach(Subscription subscription, int wd, string path)
    {
        if (!_folders.TryGetValue(wd, out var folder)) _folders[wd] = folder = new WatchedFolder();
        folder.PathByWatch[subscription.Id] = path;
        subscription.Folders[path] = wd;
    }

    private void Detach(Subscription subscription)
    {
        foreach (string path in subscription.Folders.Keys.ToList()) DetachFolder(subscription, path);
        _subscriptions.Remove(subscription.Id);
        // Removing while enumerating is allowed for Dictionary, and leaves the enumeration whole.
        foreach (var key in _gates.Keys)
            if (key.Watch == subscription.Id) _gates.Remove(key);
    }

    private void DetachFolder(Subscription subscription, string path)
    {
        if (!subscription.Folders.Remove(path, out int wd) || !_folders.TryGetValue(wd, out var folder)) return;
        if (folder.PathByWatch.TryGetValue(subscription.Id, out string? known) && known == path) folder.PathByWatch.Remove(subscription.Id);
        if (folder.PathByWatch.Count > 0) return;
        _folders.Remove(wd);
        Libc.InotifyRemoveWatch(_descriptor, wd);
    }

    private void MarkChanged(Subscription subscription, string path, long now)
    {
        var key = (subscription.Id, path);
        if (!_gates.TryGetValue(key, out var gate)) _gates[key] = gate = new ChangeGate();
        if (subscription.Ready && gate.Opens(now)) Emit(key, gate, now);
        else gate.Pending = true;
    }

    private void Emit((int Watch, string Path) key, ChangeGate gate, long now)
    {
        gate.Pending = false;
        gate.LastSent = now;
        replies.Send(new WslWriter(WslReply.Changed, key.Watch).String(key.Path));
    }

    // Sends what waited out its window, and forgets a path quiet for a whole one.
    private void Flush(long now)
    {
        foreach (var (key, gate) in _gates)
        {
            if (!gate.Opens(now)) continue;
            if (!gate.Pending) _gates.Remove(key);
            else if (_subscriptions.TryGetValue(key.Watch, out var subscription) && subscription.Ready) Emit(key, gate, now);
        }
    }

    // How long poll may sleep: until the first waiting change's window closes, or until an event.
    private int NextTimeout(long now)
    {
        long soonest = long.MaxValue;
        foreach (var (key, gate) in _gates)
        {
            if (gate.Pending && _subscriptions.TryGetValue(key.Watch, out var subscription) && subscription.Ready)
                soonest = Math.Min(soonest, gate.LastSent + Window - now);
        }
        return soonest == long.MaxValue ? -1 : (int)Math.Max(soonest, 0);
    }

    private static HelperFailure WatchFailure(int errno, string path) => errno == Errno.ENOSPC
        ? new HelperFailure(WslFailure.Other, $"{path}: the distro's inotify watch limit is reached (fs.inotify.max_user_watches)")
        : HelperFailure.FromErrno(errno, path);

    // The folders in a folder, each by its exact bytes, never a link to one; null when it cannot be read.
    private static List<string>? SubFolderNames(string folder)
    {
        var entries = new List<(string Name, WireKind Kind)>();
        return PosixFiles.ReadFolder(folder, entries) == 0
            ? [.. entries.Where(entry => entry.Kind == WireKind.Directory).Select(entry => entry.Name)]
            : null;
    }

    private enum FolderWatch
    {
        Attached,

        /// <summary>The watch already holds it.</summary>
        Held,

        /// <summary>It went before it could be watched.</summary>
        Gone,

        /// <summary>It may not be watched — permission, most often.</summary>
        Refused,

        /// <summary>The distro's watch limit is reached.</summary>
        LimitReached,
    }

    /// <summary>One watch request: its root, and the folders it holds by path, with their inotify watch.</summary>
    private sealed class Subscription(int id, string root, bool recursive)
    {
        public int Id { get; } = id;

        public string Root { get; } = root;

        public bool Recursive { get; } = recursive;

        /// <summary>True once its Done is out, so it may report.</summary>
        public bool Ready { get; set; }

        public Dictionary<string, int> Folders { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>One inotify watch, and the path each watch holding it knows it by.</summary>
    private sealed class WatchedFolder
    {
        public Dictionary<int, string> PathByWatch { get; } = [];
    }

    /// <summary>When a path was last reported for a watch, and whether a change waits for its window to close.</summary>
    private sealed class ChangeGate
    {
        // Monotonic milliseconds start at boot, so a gate never sent is open.
        public long LastSent { get; set; } = -Window;

        public bool Pending { get; set; }

        public bool Opens(long now) => now - LastSent >= Window;
    }
}
