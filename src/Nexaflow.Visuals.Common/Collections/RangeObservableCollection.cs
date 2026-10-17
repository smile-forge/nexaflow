using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Nexaflow.Visuals.Common.Collections;

/// <summary>
/// ObservableCollection with bulk operations that fire a single Reset notification
/// instead of N individual events — lets a view rebuild once rather than per item.
/// Use <see cref="ReplaceAll"/> to swap the whole contents in one shot (the typical
/// fresh-fill case); incremental appends should still use the base <c>Add</c> so the
/// view keeps its scroll position. To bring contents a view is already showing up to date,
/// use <see cref="MatchTo"/>, which says what changed rather than that everything did.
/// </summary>
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Clears and refills the collection, raising one Reset notification.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    /// <summary>Appends many items, raising one Reset notification.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    /// <summary>
    /// Brings the collection to <paramref name="target"/> — its items, in its order — with a notification for each
    /// difference rather than one Reset. An item whose key the collection already holds stays: the same instance, brought
    /// up to date by <paramref name="takeOn"/>, so a view keeps its rows, its selection and the place it is scrolled to.
    /// What is gone is removed, what is new inserted, and what is further down moved up. A Reset says none of that, so a
    /// virtualizing view has to work out its scroll position again, and where it lands is up to its own timing.
    /// </summary>
    /// <param name="keys">Says when two keys name the same item.</param>
    /// <param name="takeOn">Brings a held item up to date from the fresh one with its key, or answers false where it cannot
    /// stand for it — a folder that became a file — and the fresh one takes its place.</param>
    public void MatchTo<TKey>(IReadOnlyList<T> target, Func<T, TKey> keyOf, IEqualityComparer<TKey> keys, Func<T, T, bool> takeOn)
        where TKey : notnull
    {
        var wanted = new HashSet<TKey>(target.Count, keys);
        foreach (var item in target) wanted.Add(keyOf(item));

        // What is gone first, from the end, so every index removed is one the collection still holds.
        for (var i = Count - 1; i >= 0; i--)
            if (!wanted.Contains(keyOf(this[i]))) RemoveAt(i);

        var held = new Dictionary<TKey, T>(Count, keys);
        foreach (var item in this) held.TryAdd(keyOf(item), item);

        // Every place in turn. Everything before i is already target's, so anything held for what belongs at i is at i
        // or further down.
        for (var i = 0; i < target.Count; i++)
        {
            var fresh = target[i];
            if (!held.TryGetValue(keyOf(fresh), out var kept))
            {
                Insert(i, fresh);
                continue;
            }

            if (!keys.Equals(keyOf(this[i]), keyOf(fresh))) Move(IndexOf(kept, i + 1), i);
            if (!takeOn(kept, fresh)) this[i] = fresh;
        }
    }

    /// <summary>Where <paramref name="item"/> is, looking from <paramref name="from"/> on.</summary>
    private int IndexOf(T item, int from)
    {
        var same = EqualityComparer<T>.Default;
        for (var i = from; i < Count; i++)
            if (same.Equals(this[i], item)) return i;
        return -1;
    }

    private void RaiseReset()
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
