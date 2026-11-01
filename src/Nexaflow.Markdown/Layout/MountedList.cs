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
/// </summary>
internal sealed class MountedList<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T>[] _owns;
    private readonly Func<T, T>[] _seen;

    /// <summary>Where in this list each tree's items begin, and how many of its first it leaves out.</summary>
    private readonly int[] _from;
    private readonly int[] _skipped;

    /// <param name="parts">Each tree's own list, in the order the trees are put down, with how one of its items is seen where it is.</param>
    /// <param name="same">Whether an item where one tree meets the next is the same as the one before it, both seen where they are.</param>
    public MountedList(IReadOnlyList<(IReadOnlyList<T> Own, Func<T, T> Seen)> parts, Func<T, T, bool> same)
    {
        _owns = new IReadOnlyList<T>[parts.Count];
        _seen = new Func<T, T>[parts.Count];
        _from = new int[parts.Count];
        _skipped = new int[parts.Count];

        var count = 0;
        T? last = default;
        var any = false;

        for (var at = 0; at < parts.Count; at++)
        {
            var (own, seen) = parts[at];
            (_owns[at], _seen[at], _from[at]) = (own, seen, count);

            if (own.Count == 0) continue;

            var first = seen(own[0]);
            if (any && same(last!, first)) _skipped[at] = 1;

            count += own.Count - _skipped[at];
            (last, any) = (seen(own[^1]), own.Count > _skipped[at] || any);
        }

        this.Count = count;
    }

    public int Count { get; }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= this.Count) throw new ArgumentOutOfRangeException(nameof(index));

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
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var at = 0; at < _owns.Length; at++)
            for (var item = _skipped[at]; item < _owns[at].Count; item++)
                yield return _seen[at](_owns[at][item]);
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
