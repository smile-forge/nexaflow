using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Icons;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>
/// Icons kept in Windows files' resources (<see cref="ShellIconRef"/>) — a registered verb's program, a ShellNew type's
/// icon, a configured app's — for every view in the app. Each is drawn once per size through a
/// <see cref="ShellRequestQueue{TKey,TResult}"/>, so drawing never holds up a window's thread, and kept for good: what names
/// these icons is the registry and the user's configured programs, a small and settled set.
/// </summary>
public sealed class ShellResourceIcons
{
    /// <summary>The app's one set: a file's icons are the same for every window and workspace.</summary>
    public static ShellResourceIcons Shared { get; } =
        new(ShellInterop.ResourceIcon, quiet: TimeSpan.FromMilliseconds(16), patience: TimeSpan.FromMilliseconds(100));

    private readonly Func<ShellIconRef, int, ImageSource?> _draw;
    private readonly ShellRequestQueue<IconKey, ImageSource> _queue;

    private readonly object _gate = new();
    private readonly Dictionary<IconKey, ImageSource?> _icons = [];

    /// <param name="draw">Draws an icon at an edge in pixels, on the worker; null when the file holds no such icon.</param>
    /// <param name="quiet">How long requests must stop arriving before drawing starts.</param>
    /// <param name="patience">The longest drawing waits for that quiet.</param>
    internal ShellResourceIcons(Func<ShellIconRef, int, ImageSource?> draw, TimeSpan quiet, TimeSpan patience)
    {
        _draw  = draw;
        _queue = new ShellRequestQueue<IconKey, ImageSource>("Shell resource icons", DrawAndKeep, quiet, patience);
    }

    /// <summary>
    /// The icon <paramref name="icon"/> names, <paramref name="pixels"/> wide, as far as it is known now. One still to be
    /// drawn is handed to <paramref name="arrived"/> on <paramref name="dispatcher"/>'s thread, unless the returned ticket is
    /// disposed first; one the file does not hold never arrives, and is answered as none from then on. Cheap enough to call
    /// while an element is drawn: it never touches the disk.
    /// </summary>
    public ShellIconLookup Request(ShellIconRef icon, int pixels, Dispatcher dispatcher, Action<ImageSource> arrived)
    {
        var key = new IconKey(icon, pixels);
        lock (_gate)
            if (_icons.TryGetValue(key, out var known)) return new(known, null);
        return new(null, _queue.Enqueue(key, dispatcher, arrived));
    }

    private ImageSource? DrawAndKeep(IconKey key)
    {
        var icon = _draw(key.Icon, key.Pixels);
        lock (_gate) _icons[key] = icon;
        return icon;
    }

    private readonly record struct IconKey(ShellIconRef Icon, int Pixels);
}
