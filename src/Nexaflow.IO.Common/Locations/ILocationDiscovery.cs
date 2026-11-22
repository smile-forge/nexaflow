namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// A provider whose locations are found on this machine rather than saved by a person — each installed WSL distro. Every
/// runtime mounts what is found beside its workspace's saved connections and follows it as it changes; nothing found is
/// ever written to a workspace. Optional. <see cref="DiscoveredLocations"/> asks every provider that has it.
/// </summary>
public interface ILocationDiscovery
{
    /// <summary>The connections there are now; none found is an empty list. Fails only when it cannot look.</summary>
    ValueTask<IReadOnlyList<ConnectionProfile>> DiscoverAsync(CancellationToken ct);

    /// <summary>Raised when what <see cref="DiscoverAsync"/> finds may have changed. May fire on any thread.</summary>
    event Action? Changed;
}
