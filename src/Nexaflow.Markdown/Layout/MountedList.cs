using System.Collections;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// What a tree made of the trees it mounts says of itself, put together from what each of them says — each tree's own list, worked
/// out once and kept with that tree, seen where it is put down — rather than gathered again from every piece of every one.
///
/// <para>
/// Made in as many steps as there are mounts, and an item is seen where it is put down only when it is read: a tree mounted again
/// where it was, after an edit elsewhere, costs nothing here. Where two trees meet, the first item of the later is left out where it
/// is the same as the last of the earlier, as a list gathered from every piece leaves out two that coincide.
/// </para>
/// <para>
/// The tree holding the mounts may say something of its own besides: a first item, which goes after the items standing where it does
/// (<see cref="Leading"/>), and a last, after everything — each left out where it is the same as the item before it.
/// </para>
/// </summary>
internal sealed class MountedList<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T>[] _owns;
    private readonly Func<T, T>[] _seen;

    /// <summary>Where in the mounted trees' items each tree's begin, and how many of its first it leaves out.</summary>
    private readonly int[] _from;
    private readonly int[] _skipped;

    /// <summary>How many items the mounted trees come to.</summary>
    private readonly int _mounted;

    /// <summary>What the tree holding the mounts says first, and where among the items it stands — or nothing.</summary>
    private readonly (bool Has, T Item, int At) _first;

    /// <summary>What the tree holding the mounts says last — or nothing.</summary>
    private readonly (bool Has, T Item) _last;

    /// <param name="parts">Each tree's own list, in the order the trees are put down, with how one of its items is seen where it is.</param>
    /// <param name="same">Whether an item where one tree meets the next is the same as the one before it, both seen where they are.</param>
    public MountedList(IReadOnlyList<(IReadOnlyList<T> Own, Func<T, T> Seen)> parts, Func<T, T, bool> same) : this(parts, same, null, default) { }

    /// <param name="parts">Each tree's own list, in the order the trees are put down, with how one of its items is seen where it is.</param>
    /// <param name="same">Whether an item is the same as the one before it, both seen where they are.</param>
    /// <param name="first">What the tree holding the mounts says first, and which items of theirs it goes after — or null.</param>
    /// <param name="last">What the tree holding the mounts says last, where it says anything.</param>
    /// <param name="meet">Whether the trees at two indices, the second straight after the first, can have an item the same where they
    /// meet — said cheaply, so items are seen where they are only where that is worth asking; null where any can.</param>
    public MountedList(IReadOnlyList<(IReadOnlyList<T> Own, Func<T, T> Seen)> parts, Func<T, T, bool> same, Leading? first, (bool Has, T Item) last,
                       Func<int, int, bool>? meet = null)
    {
        _owns = new IReadOnlyList<T>[parts.Count];
        _seen = new Func<T, T>[parts.Count];
        _from = new int[parts.Count];
        _skipped = new int[parts.Count];

        var count = 0;
        var previous = -1;

        for (var at = 0; at < parts.Count; at++)
        {
            var (own, seen) = parts[at];
            (_owns[at], _seen[at], _from[at]) = (own, seen, count);

            if (own.Count == 0) continue;

            // Seen where they are only where the two trees can meet at all: asking where an item stands can cost what it names.
            if (previous >= 0 && (meet?.Invoke(previous, at) ?? true) && same(_seen[previous](_owns[previous][^1]), seen(own[0]))) _skipped[at] = 1;

            count += own.Count - _skipped[at];
            if (own.Count > _skipped[at]) previous = at;
        }

        _mounted = count;

        if (first is { } lead)
        {
            var place = 0;
            while (place < _mounted && lead.After(this.Mounted(place))) place++;

            if (place == 0 || !same(this.Mounted(place - 1), lead.Item)) _first = (true, lead.Item, place);
        }

        if (last.Has && (this.Count == 0 || !same(this[this.Count - 1], last.Item))) _last = (true, last.Item);
    }

    /// <summary>What the tree holding the mounts says first, and which of the mounted trees' items it goes after — those standing where it does.</summary>
    public sealed record Leading(T Item, Func<T, bool> After);

    public int Count => _mounted + (_first.Has ? 1 : 0) + (_last.Has ? 1 : 0);

    /// <summary>How many trees it is put together from.</summary>
    public int Parts => _from.Length;

    /// <summary>Where in this list the items of the tree at <paramref name="part"/> begin.</summary>
    public int StartOf(int part) => _from[part] + (_first.Has && _from[part] >= _first.At ? 1 : 0);

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= this.Count) throw new ArgumentOutOfRangeException(nameof(index));

            if (_last.Has && index == this.Count - 1) return _last.Item;
            if (_first.Has && index >= _first.At) return index == _first.At ? _first.Item : this.Mounted(index - 1);

            return this.Mounted(index);
        }
    }

    /// <summary>The mounted trees' item at <paramref name="index"/>, seen where its tree is put down.</summary>
    private T Mounted(int index)
    {
        // The last tree beginning at or before it: trees holding nothing begin where the next does, so that is the one holding it.
        var (low, high) = (0, _from.Length - 1);

        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_from[middle] <= index) low = middle;
            else high = middle - 1;
        }

        return _seen[low](_owns[low][index - _from[low] + _skipped[low]]);
    }

    public IEnumerator<T> GetEnumerator()
    {
        var index = 0;

        for (var at = 0; at < _owns.Length; at++)
            for (var item = _skipped[at]; item < _owns[at].Count; item++)
            {
                if (_first.Has && index++ == _first.At) yield return _first.Item;

                yield return _seen[at](_owns[at][item]);
            }

        if (_first.Has && index == _first.At) yield return _first.Item;
        if (_last.Has) yield return _last.Item;
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
