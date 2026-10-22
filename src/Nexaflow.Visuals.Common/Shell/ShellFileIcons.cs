using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>The two sizes the shell draws a list's icons at, both at the system DPI.</summary>
internal enum ShellIconSize { Small, Large }

/// <summary>What a row asks to be drawn as.</summary>
/// <param name="Path">The row's path. For a <paramref name="NameOnly"/> row, anything ending in its name — only the extension is read.</param>
/// <param name="NameOnly">The row is not on disk (an entry inside an archive or a disk image), so only the kind of file it is counts.</param>
/// <param name="Stamp">The row's last-write time in ticks, so a file that changed is asked about again.</param>
internal readonly record struct FileIconQuery(string Path, bool IsFolder, bool NameOnly, long Stamp, ShellIconSize Size);

/// <summary>
/// What a request gives back at once: the best icon already in hand, or null when there is none yet; and, while a better
/// one is still to come, the ticket that stops waiting for it.
/// </summary>
internal readonly record struct FileIconLookup(ImageSource? Icon, IDisposable? Pending);

/// <summary>
/// Explorer's icons for every file list in the app, in two layers. Each kind of file — an extension, a folder — is drawn
/// once and kept for good; a file that draws its own icon (a program, a shortcut) and a customised folder are drawn one by
/// one and kept in a bounded most-recently-used set. A request answers at once from either; what is not in hand is drawn
/// through a <see cref="ShellRequestQueue{TKey,TResult}"/>, rows on screen first.
/// </summary>
internal sealed class ShellFileIcons
{
    /// <summary>The app's one set: operating-system icons are the same for every window and workspace.</summary>
    public static ShellFileIcons Shared { get; } =
        new(new SystemShellIconSource(), quiet: TimeSpan.FromMilliseconds(60), patience: TimeSpan.FromMilliseconds(200), pathCapacity: 4096);

    private readonly IShellIconSource _source;
    private readonly int _pathCapacity;
    private readonly ShellRequestQueue<IconKey, ImageSource> _queue;

    private readonly object _gate = new();
    private readonly Dictionary<IconKey, ImageSource?> _typeIcons = [];
    private readonly Dictionary<string, bool> _ownIcons = [];
    private readonly Dictionary<IconKey, LinkedListNode<(IconKey Key, ImageSource? Icon)>> _pathIcons = [];
    private readonly LinkedList<(IconKey Key, ImageSource? Icon)> _recent = new();

    /// <param name="quiet">How long requests must stop arriving before drawing starts.</param>
    /// <param name="patience">The longest drawing waits for that quiet, so a scroll that never stops is still drawn.</param>
    /// <param name="pathCapacity">How many one-off icons are kept.</param>
    internal ShellFileIcons(IShellIconSource source, TimeSpan quiet, TimeSpan patience, int pathCapacity)
    {
        _source       = source;
        _pathCapacity = pathCapacity;
        _queue        = new ShellRequestQueue<IconKey, ImageSource>("Shell icons", DrawAndKeep, quiet, patience);
    }

    /// <summary>The size the shell draws at for an icon <paramref name="pixels"/> wide on screen.</summary>
    public static ShellIconSize SizeFor(double pixels) => pixels <= ShellInterop.SmallIconPixels ? ShellIconSize.Small : ShellIconSize.Large;

    /// <summary>
    /// The icon for <paramref name="query"/>, as far as it is known now. When a better one is to come it is handed to
    /// <paramref name="arrived"/> on <paramref name="dispatcher"/>'s thread, unless the returned ticket is disposed first.
    /// Cheap enough to call while a row is drawn: it never touches the disk or the shell.
    /// </summary>
    public FileIconLookup Request(FileIconQuery query, Dispatcher dispatcher, Action<ImageSource> arrived)
    {
        string extension = query.IsFolder ? string.Empty : System.IO.Path.GetExtension(query.Path).ToLowerInvariant();
        var typeKey = new IconKey(null, extension, query.IsFolder, 0, query.Size);

        ImageSource? typeIcon;
        IconKey wanted;
        lock (_gate)
        {
            bool typeKnown = _typeIcons.TryGetValue(typeKey, out typeIcon);
            bool byPath = !query.NameOnly
                       && (query.IsFolder || !_ownIcons.TryGetValue(extension, out bool own) || own);

            if (!byPath)
            {
                if (typeKnown) return new(typeIcon, null);
                wanted = typeKey;
            }
            else
            {
                wanted = typeKey with { Path = query.Path, Stamp = query.Stamp };
                if (_pathIcons.TryGetValue(wanted, out var kept))
                {
                    _recent.Remove(kept);
                    _recent.AddFirst(kept);
                    return new(kept.Value.Icon ?? typeIcon, null);
                }
            }
        }
        return new(typeIcon, _queue.Enqueue(wanted, dispatcher, arrived));
    }

    /// <summary>Draws what <paramref name="key"/> asks for, on the worker, and keeps a one-off icon. A one-off icon the shell cannot draw falls back to its kind's.</summary>
    private ImageSource? DrawAndKeep(IconKey key)
    {
        if (key.Path is null) return TypeIconOf(key);

        bool ownIcon = key.IsFolder ? _source.IsCustomizedFolder(key.Path) : DrawsItsOwnIcon(key.Extension);
        var icon = ownIcon ? _source.PathIcon(key.Path, key.Size) ?? TypeIconOf(key) : TypeIconOf(key);

        // A file that wears its kind's icon is answered from its kind from now on, so only one-off icons are kept by path.
        if (key.IsFolder || ownIcon)
            lock (_gate) Keep(key, icon);
        return icon;
    }

    private ImageSource? TypeIconOf(IconKey key)
    {
        var typeKey = key with { Path = null, Stamp = 0 };
        lock (_gate)
            if (_typeIcons.TryGetValue(typeKey, out var known)) return known;

        var icon = _source.TypeIcon(typeKey.Extension, typeKey.IsFolder, typeKey.Size);
        lock (_gate) _typeIcons[typeKey] = icon;
        return icon;
    }

    private bool DrawsItsOwnIcon(string extension)
    {
        lock (_gate)
            if (_ownIcons.TryGetValue(extension, out bool known)) return known;

        bool own = _source.DrawsItsOwnIcon(extension);
        lock (_gate) _ownIcons[extension] = own;
        return own;
    }

    /// <summary>Keeps a one-off icon as the most recent, letting the least recent go past capacity. Called under the gate.</summary>
    private void Keep(IconKey key, ImageSource? icon)
    {
        if (_pathIcons.Remove(key, out var stale)) _recent.Remove(stale);
        _pathIcons[key] = _recent.AddFirst((key, icon));

        while (_pathIcons.Count > _pathCapacity && _recent.Last is { } oldest)
        {
            _recent.RemoveLast();
            _pathIcons.Remove(oldest.Value.Key);
        }
    }

    /// <summary>A kind of file (no <paramref name="Path"/>), or one file or folder as it stood at <paramref name="Stamp"/>.</summary>
    private readonly record struct IconKey(string? Path, string Extension, bool IsFolder, long Stamp, ShellIconSize Size);
}
