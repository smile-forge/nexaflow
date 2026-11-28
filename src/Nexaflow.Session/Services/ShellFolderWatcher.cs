using System.IO;
using System.Runtime.InteropServices;

namespace Nexaflow.Session.Services;

/// <summary>
/// Folders watched as Explorer watches them: through the shell's change notifications, which carry both what the file
/// system reports (a file made, deleted, renamed, its attributes changed — a placeholder downloaded or freed) and what
/// software tells the shell about a file the file system knows nothing of, as a sync provider does when a file's sync
/// state moves. One message-only window on the UI thread receives them (<see cref="MessageWindow"/>); each folder is
/// registered once, however many views watch it, and a burst of notifications reaches each view as one
/// <see cref="FolderChange"/> a quiet moment later. Nothing in it is a UI toolkit's.
/// </summary>
internal sealed class ShellFolderWatcher : IFolderWatcher
{
    private const int ChangeMessage = 0x8000 + 0x31;   // WM_APP + n, private to this window
    private const int Sources = 0x0001 | 0x0002 | 0x8000;   // SHCNRF_InterruptLevel | SHCNRF_ShellLevel | SHCNRF_NewDelivery

    /// <summary>RENAMEITEM, CREATE, DELETE, MKDIR, RMDIR, ATTRIBUTES, UPDATEDIR, UPDATEITEM, RENAMEFOLDER.</summary>
    private const int Events = 0x0001 | 0x0002 | 0x0004 | 0x0008 | 0x0010 | 0x0800 | 0x1000 | 0x2000 | 0x20000;

    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    private readonly IUiThread _ui;
    private readonly MessageWindow _window;
    private readonly Dictionary<string, Folder> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="ui">The UI thread, whose message loop delivers the notifications and whose timers settle them.</param>
    public ShellFolderWatcher(IUiThread ui)
    {
        _ui     = ui;
        _window = ui.Run(() => new MessageWindow("Nexaflow folder changes", OnMessage));
    }

    /// <summary>
    /// Watches the folder at <paramref name="path"/> — its own entries, not its subfolders' — until the returned handle is
    /// disposed. A folder the shell cannot name gives a handle that never fires.
    /// </summary>
    public IDisposable Watch(string path, Action<FolderChange> onChanged)
    {
        string folderPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!_folders.TryGetValue(folderPath, out var folder))
        {
            folder = Folder.Register(folderPath, _window.Handle, _ui);
            if (folder is null) return new Subscription(this, null, onChanged);
            _folders[folderPath] = folder;
        }

        var subscription = new Subscription(this, folder, onChanged);
        folder.Subscribers.Add(subscription);
        return subscription;
    }

    public void Dispose()
    {
        foreach (var folder in _folders.Values) folder.Unregister();
        _folders.Clear();
        _ui.Run(_window.Dispose);
    }

    private void Unwatch(Folder folder, Subscription subscription)
    {
        folder.Subscribers.Remove(subscription);
        if (folder.Subscribers.Count > 0) return;
        folder.Unregister();
        _folders.Remove(folder.Path);
    }

    private bool OnMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != ChangeMessage) return false;

        IntPtr locked = SHChangeNotification_Lock(wParam, (uint)lParam.ToInt64(), out IntPtr items, out _);
        if (locked == IntPtr.Zero) return true;

        // A notification names one item, or two for a rename (old, new).
        string? first, second;
        try
        {
            first  = PathOf(Marshal.ReadIntPtr(items));
            second = PathOf(Marshal.ReadIntPtr(items, IntPtr.Size));
        }
        finally { SHChangeNotification_Unlock(locked); }

        Note(first);
        Note(second);
        return true;
    }

    /// <summary>Tells the folder holding <paramref name="path"/> it changed there, and a watched folder named itself that it changed whole.</summary>
    private void Note(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        path = Path.TrimEndingDirectorySeparator(path);

        if (_folders.TryGetValue(path, out var itself)) itself.NoteWhole();
        if (Path.GetDirectoryName(path) is { } parent && _folders.TryGetValue(parent, out var holder)) holder.Note(path);
    }

    private static string? PathOf(IntPtr item)
    {
        if (item == IntPtr.Zero) return null;
        var buffer = new char[32768];
        return SHGetPathFromIDListEx(item, buffer, (uint)buffer.Length, 0) ? new string(buffer, 0, Array.IndexOf(buffer, '\0')) : null;
    }

    /// <summary>One folder's registration with the shell, the views watching it, and what changed since they were last told.</summary>
    private sealed class Folder
    {
        private readonly IntPtr _item;
        private readonly uint _registration;
        private readonly IUiTimer _settle;
        private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
        private bool _whole;

        public string Path { get; }
        public List<Subscription> Subscribers { get; } = [];

        private Folder(string path, IntPtr item, uint registration, IUiThread ui)
        {
            Path          = path;
            _item         = item;
            _registration = registration;
            _settle       = ui.CreateTimer(Quiet, Flush, UiTimerPriority.Background, once: true);
        }

        public static Folder? Register(string path, IntPtr window, IUiThread ui)
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out IntPtr item, 0, out _) != 0 || item == IntPtr.Zero) return null;

            var entry = new ChangeNotifyEntry { Item = item, Recursive = false };
            uint registration = SHChangeNotifyRegister(window, Sources, Events, ChangeMessage, 1, ref entry);
            if (registration == 0)
            {
                ILFree(item);
                return null;
            }
            return new Folder(path, item, registration, ui);
        }

        public void Note(string path)
        {
            _changed.Add(path);
            _settle.Restart();
        }

        public void NoteWhole()
        {
            _whole = true;
            _settle.Restart();
        }

        public void Unregister()
        {
            _settle.Dispose();
            SHChangeNotifyDeregister(_registration);
            ILFree(_item);
        }

        private void Flush()
        {
            var change = new FolderChange(new HashSet<string>(_changed, StringComparer.OrdinalIgnoreCase), _whole);
            _changed.Clear();
            _whole = false;
            foreach (var subscriber in Subscribers.ToArray()) subscriber.Raise(change);
        }
    }

    private sealed class Subscription(ShellFolderWatcher owner, Folder? folder, Action<FolderChange> onChanged) : IDisposable
    {
        private bool _disposed;

        public void Raise(FolderChange change)
        {
            if (!_disposed) onChanged(change);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (folder is not null) owner.Unwatch(folder, this);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChangeNotifyEntry
    {
        public IntPtr Item;
        [MarshalAs(UnmanagedType.Bool)] public bool Recursive;
    }

    [DllImport("shell32.dll")]
    private static extern uint SHChangeNotifyRegister(IntPtr window, int sources, int events, int message, int entryCount, ref ChangeNotifyEntry entries);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotifyDeregister(uint registration);

    [DllImport("shell32.dll")]
    private static extern IntPtr SHChangeNotification_Lock(IntPtr change, uint processId, out IntPtr items, out int eventId);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotification_Unlock(IntPtr locked);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributesWanted, out uint attributes);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListEx(IntPtr item, [Out] char[] path, uint length, int options);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr item);
}
