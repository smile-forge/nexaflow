using System.Diagnostics;

namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// What every discovering backend (<see cref="ILocationDiscovery"/>) finds, kept current: looked for again when a backend
/// says its set changed, and when a backend registers. One per process, as the backends are. A runtime reads
/// <see cref="All"/> without waiting when it remounts, and remounts on <see cref="Changed"/>. Thread-safe.
/// </summary>
public sealed class DiscoveredLocations
{
    private readonly LocationProviderRegistry _providers;
    private readonly Lock _gate = new();
    private readonly Dictionary<ILocationDiscovery, ConnectionProfile[]> _byBackend = new(ReferenceEqualityComparer.Instance);
    private ConnectionProfile[] _all = [];
    private Task _looking = Task.CompletedTask;
    private bool _running;
    private bool _again;
    private bool _started;

    public DiscoveredLocations(LocationProviderRegistry providers) => _providers = providers;

    /// <summary>Every connection found, one per id — the first backend to find an id keeps it.</summary>
    public IReadOnlyList<ConnectionProfile> All => Volatile.Read(ref _all);

    /// <summary>Raised when <see cref="All"/> has changed. May fire on any thread.</summary>
    public event Action? Changed;

    /// <summary>Starts following the backends and looks once; idempotent. Completes when that look has.</summary>
    public Task StartAsync()
    {
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                _providers.Changed += OnSourcesChanged;
            }
        }
        return RefreshAsync();
    }

    /// <summary>Looks again. A look already under way is followed by one more, so a change made during it is never missed;
    /// the task completes when the last of them has.</summary>
    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_running)
            {
                _again = true;
                return _looking;
            }
            _running = true;
            return _looking = Task.Run(LookAsync);
        }
    }

    private void OnSourcesChanged() => _ = RefreshAsync();

    private async Task LookAsync()
    {
        while (true)
        {
            foreach (var provider in _providers.Providers)
            {
                if (provider is not ILocationDiscovery discovery) continue;
                lock (_gate)
                {
                    if (!_byBackend.ContainsKey(discovery))
                    {
                        _byBackend[discovery] = [];
                        discovery.Changed += OnSourcesChanged;
                    }
                }
                try
                {
                    var found = await discovery.DiscoverAsync(CancellationToken.None).ConfigureAwait(false);
                    lock (_gate) _byBackend[discovery] = [.. found];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LocationException
                                              or InvalidOperationException)
                {
                    // A backend that cannot look this time keeps what it found last, and hides nothing of another's.
                    Trace.TraceWarning($"{provider.Kind} discovery failed: {ex.Message}");
                }
            }

            ConnectionProfile[] next;
            lock (_gate)
            {
                next = [.. _byBackend.Values.SelectMany(found => found).DistinctBy(c => c.Id, StringComparer.OrdinalIgnoreCase)];
            }
            if (!next.SequenceEqual(Volatile.Read(ref _all)))
            {
                Volatile.Write(ref _all, next);
                Changed?.Invoke();
            }

            lock (_gate)
            {
                if (!_again)
                {
                    _running = false;
                    return;
                }
                _again = false;
            }
        }
    }
}
