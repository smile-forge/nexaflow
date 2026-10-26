using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Core.Services;

/// <summary>
/// The connections a runtime has mounted: its workspace's, each on the runtime's file system as <c>::{id}</c> with its live
/// session from the runtime's pool. Following another set — the workspace switched, a connection was added or removed —
/// holds the fresh set before letting the old one go, so a connection in both keeps its session.
/// </summary>
public sealed class RuntimeLocations : IAsyncDisposable
{
    private readonly LocationFileSystem _vfs;
    private readonly LocationSessionPool _pool;
    private readonly Lock _gate = new();
    private WorkspaceConnections? _following;
    private LocationSet? _held;
    private IReadOnlyList<UnreachableLocation> _unreachable = [];

    public RuntimeLocations(LocationFileSystem vfs, LocationSessionPool pool)
    {
        _vfs  = vfs;
        _pool = pool;
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

    /// <summary>Mounts <paramref name="connections"/>, and mounts them afresh whenever they change. Null mounts nothing.</summary>
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

    /// <summary>Mounts the followed connections afresh.</summary>
    public void Remount()
    {
        LocationSet? old;
        lock (_gate)
        {
            var fresh       = _pool.Acquire(_following?.All ?? []);
            var unreachable = new List<UnreachableLocation>(fresh.Unreachable);
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
        _pool.Release(old);
    }

    /// <summary>Unmounts everything and closes every session: the runtime is going.</summary>
    public async ValueTask DisposeAsync()
    {
        LocationSet? held;
        lock (_gate)
        {
            if (_following is not null) _following.Changed -= Remount;
            _following   = null;
            held         = _held;
            _held        = null;
            _unreachable = [];
            foreach (var mount in held?.Mounts ?? []) _vfs.UnregisterLocation(mount.Id);
        }
        _pool.Release(held);
        await _pool.DisposeAsync().ConfigureAwait(false);
    }
}
