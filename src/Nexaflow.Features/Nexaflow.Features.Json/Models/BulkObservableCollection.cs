using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Nexaflow.Features.Json.Models;

/// <summary>
/// ObservableCollection with a bulk-add that fires one Reset notification
/// instead of N individual Add notifications — critical for pre-populating
/// thousands of virtual items without freezing the UI.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> newItems)
    {
        foreach (var item in newItems)
            Items.Add(item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
