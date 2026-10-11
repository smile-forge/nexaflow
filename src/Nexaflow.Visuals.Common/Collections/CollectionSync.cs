using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexaflow.Visuals.Common.Collections;

/// <summary>
/// Brings a bound collection to a new list by changing only what differs.
/// <para>
/// Clearing and refilling a collection throws away every row a view made for it, and with them what UI Automation
/// holds for those rows. When the items compare by value — records — that second loss is a fault rather than a cost:
/// the automation layer finds its old peer for an item equal to the new one and keeps it, pointing at a row that no
/// longer exists, so the item is listed with nothing in it to read or press. Syncing keeps an item that is still
/// there, so its row and its peer stay, and a refresh that changed nothing changes nothing.
/// </para>
/// </summary>
public static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="target"/> hold <paramref name="items"/>, in that order: an item already present (by
    /// <see cref="EqualityComparer{T}.Default"/>) is kept or moved, a new one inserted, and one no longer listed removed.
    /// </summary>
    public static void SyncTo<T>(this ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        var comparer = EqualityComparer<T>.Default;

        for (var i = 0; i < items.Count; i++)
        {
            var wanted = items[i];
            if (i < target.Count && comparer.Equals(target[i], wanted)) continue;

            var at = IndexOf(target, wanted, i + 1, comparer);
            if (at >= 0) target.Move(at, i);
            else target.Insert(i, wanted);
        }

        while (target.Count > items.Count)
            target.RemoveAt(target.Count - 1);
    }

    private static int IndexOf<T>(ObservableCollection<T> target, T item, int from, IEqualityComparer<T> comparer)
    {
        for (var i = from; i < target.Count; i++)
            if (comparer.Equals(target[i], item)) return i;
        return -1;
    }
}
