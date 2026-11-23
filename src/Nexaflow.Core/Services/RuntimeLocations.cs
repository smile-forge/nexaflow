using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Core.Services;

/// <summary>
/// The connections a runtime has mounted: its workspace's, and the ones found on this machine (<see cref="DiscoveredLocations"/>),
/// each on the runtime's file system as <c>::{id}</c> with its live session from the runtime's pool. A saved connection keeps
/// its id over a found one. Following another set — the workspace switched, a connection was added or removed, a distro
/// installed — holds the fresh set before letting the old one go, so a connection in both keeps its session.
/// </summary>
public sealed class RuntimeLocations : IAsyncDisposable
{
    private readonly LocationFileSystem _vfs;
    private readonly LocationSessionPool _pool;
    private readonly DiscoveredLocations _discovered;
    private readonly Lock _gate = new();
    private WorkspaceConnections? _following;
    private LocationSet? _held;
    private IReadOnlyList<UnreachableLocation> _unreachable = [];
    private bool _disposed;

    public RuntimeLocations(LocationFileSystem vfs, LocationSessionPool pool, DiscoveredLocations discovered)
    {
        _vfs        = vfs;
        _pool       = pool;
        _discovered = discovered;
        _discovered.Changed += Remount;
    }

    /// <summary>What is mounted now; null before anything is followed.</summary>
    public LocationSet? Held
    {
        get { lock (_gate) return _held; }
    }

    /// <summary>The followed connections that are mounted nowhere, and why: no backend reaches their kind, or their id is
    /// already a mount's.</summary>
    public IReadOnlyList<UnreachableLocation> Unreachable
    {
        get { lock (_gate) return _unreachable; }
    }

    /// <summary>Mounts <paramref name="connections"/> with what is found on this machine, and mounts them afresh whenever
    /// either changes. Null mounts only what is found.</summary>
    public void Follow(WorkspaceConnections? connections)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_following, connections))
            {
                if (_following is not null) _following.Changed -= Remount;
                _following = connections;
                if (connections is not null) connections.Changed += Remount;
            }
        }
        Remount();
    }

    /// <summary>Mounts the followed connections and the found ones afresh.</summary>
    public void Remount()
    {
        LocationSet? old;
        List<UnreachableLocation> unreachable;
        lock (_gate)
        {
            if (_disposed) return;
            var saved = _following?.All ?? [];
            var found = _discovered.All.Where(f => !saved.Any(s => string.Equals(s.Id, f.Id, StringComparison.OrdinalIgnoreCase)));
            var fresh = _pool.Acquire([.. saved, .. found]);
            unreachable = new List<UnreachableLocation>(fresh.Unreachable);
            foreach (var mount in fresh.Mounts)
            {
                try { _vfs.RegisterLocation(mount); }
                catch (ArgumentException clash) { unreachable.Add(new UnreachableLocation(mount.Connection, clash.Message)); }
            }
            foreach (var gone in _held?.Mounts ?? [])
                if (!fresh.Mounts.Any(m => string.Equals(m.Id, gone.Id, StringComparison.OrdinalIgnoreCase)))
                    _vfs.UnregisterLocation(gone.Id);

            old          = _held;
            _held        = fresh;
            _unreachable = unreachable;
        }
        _vfs.ReportUnreachable(unreachable);
        _pool.Release(old);
    }

    /// <summary>Unmounts everything and closes every session: the runtime is going.</summary>
    public async ValueTask DisposeAsync()
    {
        LocationSet? held;
        _discovered.Changed -= Remount;
        lock (_gate)
        {
            _disposed = true;
            if (_following is not null) _following.Changed -= Remount;
            _following   = null;
            held         = _held;
            _held        = null;
            _unreachable = [];
            foreach (var mount in held?.Mounts ?? []) _vfs.UnregisterLocation(mount.Id);
        }
        _vfs.ReportUnreachable([]);
        _pool.Release(held);
        await _pool.DisposeAsync().ConfigureAwait(false);
    }
}
