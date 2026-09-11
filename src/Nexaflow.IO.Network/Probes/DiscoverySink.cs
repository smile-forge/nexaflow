using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Model;

namespace Nexaflow.IO.Network.Probes;

/// <summary>
/// Where a run's findings go as they arrive.
/// </summary>
/// <remarks>
/// <para>
/// A run does not write a graph itself. The page's graph is read on the UI thread — by the list, by the panel,
/// by an action's result — and a sweep writing it from a worker while the panel enumerated a device's facts
/// was a race waiting for a sweep long enough to lose it. So the run hands over batches, and whoever owns the
/// graph decides which thread applies them.
/// </para>
/// <para>
/// <see cref="CompletedAsync"/> is what "looked here" means: a probe that finished an adapter without failing
/// covered that adapter's segment, and only a covered segment can say a device on it was not found.
/// </para>
/// </remarks>
public interface IDiscoverySink
{
    /// <summary>Some of one probe's findings on one adapter, in the order they arrived.</summary>
    Task ObservedAsync(string probeId, NetworkAdapterInfo adapter, IReadOnlyList<ProbeObservation> batch);

    /// <summary>One probe finished one adapter without failing.</summary>
    Task CompletedAsync(string probeId, NetworkAdapterInfo adapter);
}

/// <summary>
/// Applies a run's findings to a graph, on whichever thread calls it, and remembers what it saw.
/// </summary>
/// <remarks>
/// Not thread-safe, on purpose: it is the graph's one writer, and the graph's owner decides which thread that
/// is. A headless caller runs it inline; the page runs it on its UI thread.
/// </remarks>
public sealed class GraphApplier(DeviceGraph graph) : IDiscoverySink
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<(string ProbeId, string Segment)> _covered = [];

    public DeviceGraph Graph => graph;

    public int Observations { get; private set; }

    /// <summary>The (probe, segment) pairs that ran to completion this run.</summary>
    public IReadOnlyList<(string ProbeId, string Segment)> Covered => _covered;

    public void Apply(IReadOnlyList<ProbeObservation> batch)
    {
        foreach (var observed in batch)
        {
            Observations++;
            if (graph.Observe(observed) is { } node) _seen.Add(node.Id);
        }
    }

    public void Completed(string probeId, NetworkAdapterInfo adapter)
        => _covered.Add((probeId, adapter.SegmentId));

    /// <summary>
    /// Ends a run that finished: anything the graph knew and nothing saw this time is absent, not gone.
    /// </summary>
    /// <param name="started">When the run began, so a node that merged into view part-way through is not
    /// marked absent for having been seen under another id.</param>
    public void Finish(DateTimeOffset started) => graph.MarkAbsentExcept(_seen, started);

    Task IDiscoverySink.ObservedAsync(string probeId, NetworkAdapterInfo adapter,
                                      IReadOnlyList<ProbeObservation> batch)
    {
        Apply(batch);
        return Task.CompletedTask;
    }

    Task IDiscoverySink.CompletedAsync(string probeId, NetworkAdapterInfo adapter)
    {
        Completed(probeId, adapter);
        return Task.CompletedTask;
    }
}
