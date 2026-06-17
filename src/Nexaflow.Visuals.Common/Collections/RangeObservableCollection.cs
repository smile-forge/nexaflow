using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Nexaflow.Visuals.Common.Collections;

/// <summary>
/// ObservableCollection with bulk operations that fire a single Reset notification
/// instead of N individual events — lets a view rebuild once rather than per item.
/// Use <see cref="ReplaceAll"/> to swap the whole contents in one shot (the typical
/// fresh-fill case); incremental appends should still use the base <c>Add</c> so the
/// view keeps its scroll position.
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

    private void RaiseReset()
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
