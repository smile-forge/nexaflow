using System.Windows.Threading;
using Nexaflow.IO.Common.Cloud;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>What a row asks its sync provider about.</summary>
/// <param name="Listed">What the folder's listing already said of it; a different listing is a different question.</param>
/// <param name="Revision">Moved on by the row's list whenever the shell says the file changed in ways a listing cannot show.</param>
internal readonly record struct CloudStatusQuery(string Path, CloudFileState Listed, long Revision);

/// <summary>What a request gives back at once: the provider's word if it was already read, and the ticket while it is being read.</summary>
internal readonly record struct CloudStatusLookup(CloudFileStatus? Status, IDisposable? Pending);

/// <summary>Where <see cref="ShellCloudStatuses"/> reads a file's sync status from — its provider, or a test's stand-in.</summary>
internal interface ICloudStatusSource
{
    CloudFileStatus? Read(string path);
}

internal sealed class ProviderCloudStatusSource : ICloudStatusSource
{
    public CloudFileStatus? Read(string path) => CloudProviderStatus.Read(path);
}

/// <summary>
/// Sync providers' word on the rows a reader can see. A provider answers one file at a time and slowly (OneDrive's handler
/// takes tens of milliseconds a file), so each answer is read through a <see cref="ShellRequestQueue{TKey,TResult}"/>, rows
/// on screen first, and kept against the question that was asked — the same row scrolled past and back is not read again
/// until its listing or its revision says something changed.
/// </summary>
internal sealed class ShellCloudStatuses
{
    /// <summary>The app's one set: a provider's word on a file is the same for every window.</summary>
    public static ShellCloudStatuses Shared { get; } =
        new(new ProviderCloudStatusSource(), quiet: TimeSpan.FromMilliseconds(60), patience: TimeSpan.FromMilliseconds(250), capacity: 4096);

    private readonly ICloudStatusSource _source;
    private readonly int _capacity;
    private readonly ShellRequestQueue<CloudStatusQuery, CloudFileStatus> _queue;

    private readonly object _gate = new();
    private readonly Dictionary<CloudStatusQuery, LinkedListNode<(CloudStatusQuery Query, CloudFileStatus? Status)>> _kept = [];
    private readonly LinkedList<(CloudStatusQuery Query, CloudFileStatus? Status)> _recent = new();

    internal ShellCloudStatuses(ICloudStatusSource source, TimeSpan quiet, TimeSpan patience, int capacity)
    {
        _source   = source;
        _capacity = capacity;
        _queue    = new ShellRequestQueue<CloudStatusQuery, CloudFileStatus>("Cloud sync status", ReadAndKeep, quiet, patience);
    }

    /// <summary>
    /// The provider's word on <paramref name="query"/> if it is in hand; otherwise it is read and handed to
    /// <paramref name="arrived"/> on <paramref name="dispatcher"/>'s thread, unless the returned ticket is disposed first. A
    /// provider with nothing to say answers no one, and the row keeps what its listing said.
    /// </summary>
    public CloudStatusLookup Request(CloudStatusQuery query, Dispatcher dispatcher, Action<CloudFileStatus> arrived)
    {
        lock (_gate)
        {
            if (_kept.TryGetValue(query, out var kept))
            {
                _recent.Remove(kept);
                _recent.AddFirst(kept);
                return new(kept.Value.Status, null);
            }
        }
        return new(null, _queue.Enqueue(query, dispatcher, arrived));
    }

    private CloudFileStatus? ReadAndKeep(CloudStatusQuery query)
    {
        var status = _source.Read(query.Path);
        lock (_gate)
        {
            if (_kept.Remove(query, out var stale)) _recent.Remove(stale);
            _kept[query] = _recent.AddFirst((query, status));

            while (_kept.Count > _capacity && _recent.Last is { } oldest)
            {
                _recent.RemoveLast();
                _kept.Remove(oldest.Value.Query);
            }
        }
        return status;
    }
}
