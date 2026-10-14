using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Client;

/// <summary>What holds models through a <see cref="HostModelSetup"/>: it is told where they run whenever the plan
/// places them, and loads them there itself — their files are its own to fetch.</summary>
public interface IModelHolder
{
    /// <summary>Has the host hold this holder's models as <paramref name="placement"/> places them, fetching any build
    /// it newly needs; those it leaves out are unavailable until the plan changes. Never throws.</summary>
    Task ApplyAsync(Placement placement, CancellationToken ct);
}

/// <summary>
/// The machine's setup for the models the host keeps loaded: where each one runs and how long a context each language
/// model is given, planned together — every workspace's models, across the GPUs the host found — and redone whenever
/// a holder takes models on. Each holder whose models the new plan moves is told where they now run, the one taking
/// models on included; a holder letting go leaves the rest where they are, so nothing loads again for it.
/// </summary>
public sealed class HostModelSetup(IAiHost host)
{
    /// <summary>How long describing the host, or unloading a model, may go unanswered — starting the host takes seconds.</summary>
    private static readonly TimeSpan RequestLimit = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _planning = new(1, 1);
    private readonly Dictionary<IModelHolder, IReadOnlyList<PlacementNeed>> _held = [];

    /// <summary>The plan as it stands; null before any model is held.</summary>
    public Placement? Current { get; private set; }

    /// <summary>Takes on <paramref name="holder"/>'s models in place of any it held, plans the whole set again, and
    /// has each holder whose models moved apply the new plan.</summary>
    public async Task<Placement> HoldAsync(IModelHolder holder, IReadOnlyList<PlacementNeed> needs, CancellationToken ct)
    {
        await _planning.WaitAsync(ct);
        try
        {
            var gpus = (await DescribeAsync(ct)).Gpus;
            _held[holder] = needs;
            var before = Current;
            var after = ModelPlacement.Plan(gpus, [.. _held.Values.SelectMany(n => n)]);
            Current = after;
            foreach (var (other, theirs) in _held.ToList())
                if (other == holder || theirs.Any(n => Moved(before, after, n.Id)))
                    await other.ApplyAsync(after, ct);
            return after;
        }
        finally { _planning.Release(); }
    }

    /// <summary>Lets <paramref name="holder"/>'s models go: the host unloads them, and the rest stay where they are.</summary>
    public async Task LetGoAsync(IModelHolder holder, CancellationToken ct)
    {
        await _planning.WaitAsync(ct);
        try
        {
            if (!_held.Remove(holder, out var needs)) return;
            var ids = needs.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            if (Current is { } current)
                Current = current with { Placed = [.. current.Placed.Where(s => !ids.Contains(s.Id))], Unplaced = [.. current.Unplaced.Where(id => !ids.Contains(id))] };
            foreach (var id in ids)
                await foreach (var _ in host.SendAsync(r => new UnloadModel(r, id), RequestLimit, ct)) { }
        }
        finally { _planning.Release(); }
    }

    /// <summary>Where a model loaded on demand runs beside the resident models as they stand; null when no GPU could
    /// ever hold it and it has no CPU build.</summary>
    public async Task<ModelSpec?> PlaceOnDemandAsync(PlacementNeed need, CancellationToken ct)
    {
        await _planning.WaitAsync(ct);
        try
        {
            var residents = Current ?? ModelPlacement.Plan((await DescribeAsync(ct)).Gpus, []);
            return ModelPlacement.PlaceOnDemand(residents, need);
        }
        finally { _planning.Release(); }
    }

    /// <summary>What the host is and the GPUs it found, starting it if it is not running.</summary>
    public async Task<HostDescription> DescribeAsync(CancellationToken ct)
    {
        await foreach (var reply in host.SendAsync(id => new DescribeHost(id), RequestLimit, ct))
            if (reply is HostDescription description) return description;
        throw new AiHostException(AiHostFailure.Request, "The host did not describe itself.", HostErrorKind.Invalid);
    }

    private static bool Moved(Placement? before, Placement after, string id)
        => (before?.Of(id), after.Of(id)) switch
        {
            (null, null)        => before?.Unplaced.Contains(id) != after.Unplaced.Contains(id),
            ({ } was, { } now)  => !was.PlacedAs(now),
            _                   => true,
        };
}
