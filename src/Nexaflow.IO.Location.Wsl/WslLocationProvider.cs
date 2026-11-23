using Nexaflow.IO.Common.Locations;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// Every installed WSL distro as a location: found in the registry rather than saved, reached by its helper for listings,
/// metadata, renames and watches and over <c>\\wsl.localhost</c> for bytes, and opening <c>wsl.exe</c> as its shell. Its
/// host is this PC, so nothing read there leaves it, and its files are reachable as Windows paths, which a reader that
/// needs one is handed with no copy.
/// </summary>
public sealed class WslLocationProvider : ILocationProvider, ILocationDiscovery, ILocationNativePaths
{
    /// <summary>The kind a distro's connection has, and the scheme of its display form: <c>wsl://Ubuntu/home</c>.</summary>
    public const string WslKind = "wsl";

    private readonly Lock _gate = new();
    private WslRegistryWatch? _watch;
    private Action? _changed;

    public string Kind => WslKind;

    public LocationCapabilities Capabilities
        => LocationCapabilities.RandomRead | LocationCapabilities.AtomicRename | LocationCapabilities.Watch
           | LocationCapabilities.Symlinks | LocationCapabilities.Permissions | LocationCapabilities.ServerSideCopy
           | LocationCapabilities.CaseSensitive | LocationCapabilities.Shell | LocationCapabilities.SameMachine;

    /// <summary>Starts the distro if it is not running, puts the helper there if this version has not yet, and starts it.
    /// Nothing to sign in to: the session runs as the distro's default user.</summary>
    public async ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile, ICredentialSource credentials, CancellationToken ct)
        => await WslSession.StartAsync(profile.Host, ct).ConfigureAwait(false);

    public ValueTask<IReadOnlyList<ConnectionProfile>> DiscoverAsync(CancellationToken ct)
        => new(Task.Run<IReadOnlyList<ConnectionProfile>>(() => [.. WslRegistry.Read().Select(ConnectionFor)], ct));

    /// <summary>Raised when a distro is installed, removed or made the default. The registry is watched from the first
    /// subscriber on.</summary>
    public event Action? Changed
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
                if (_watch is not null) return;
                _watch = new WslRegistryWatch();
                _watch.Changed += () => Volatile.Read(ref _changed)?.Invoke();
                _watch.Start();
            }
        }
        remove
        {
            lock (_gate) _changed -= value;
        }
    }

    /// <summary>The connection a distro is: <c>wsl-&lt;name&gt;</c>, named and hosted by the distro's name.</summary>
    internal static ConnectionProfile ConnectionFor(WslDistro distro)
        => new($"{WslKind}-{distro.Name.ToLowerInvariant()}", WslKind, distro.Name, distro.Name);

    public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection)
        => [WslNames.NativeRoot(connection.Host), WslNames.LegacyRoot(connection.Host)];

    public string ToNativeName(string name) => WslNames.ToNative(name);

    public string FromNativeName(string nativeName) => WslNames.FromNative(nativeName);
}
