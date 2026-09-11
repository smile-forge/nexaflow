using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace Nexaflow.Features.Network.Arp;

/// <summary>
/// Discovery layer 0 — the OS neighbour table.
///
/// <para>
/// Cheapest layer there is: it reads a table the kernel already keeps, sends nothing, and needs no
/// elevation. It is also the only layer that yields a <b>MAC</b>, which is the strongest identity key the
/// model has — every other layer's claims correlate through what this one establishes.
/// </para>
///
/// <para>
/// The honest limitation: the table holds only neighbours this host has recently exchanged traffic with.
/// On a quiet network it is nearly empty, which is what the opt-in sweep exists to fix — deliberately a
/// separate, consented step, because a sweep is visible to an IDS and is not something to do on a
/// corporate network without the user knowing.
/// </para>
/// </summary>
[Subfeature("network", "arp",
    DisplayName = "ARP / neighbour table",
    Description = "Reads the operating system's ARP and IPv6 neighbour table. Sends no packets, needs no "
                + "elevation, and is the only layer that yields a MAC address — which is what every other "
                + "layer's findings are correlated against.",
    Order = 0)]
public sealed class ArpProbe : INetworkProbe
{
    private readonly Func<IReadOnlyList<NeighborEntry>> _readTable;
    private IProbeHost? _host;

    public ArpProbe() : this(NativeMethods.ReadNeighborTable) { }

    /// <summary>The seam a test reads a table through, instead of the kernel's.</summary>
    internal ArpProbe(Func<IReadOnlyList<NeighborEntry>> readTable) => _readTable = readTable;

    public string ProbeId => "network.arp";
    public string DisplayName => "ARP / neighbour table";

    /// <summary>Reading a kernel table puts nothing on the wire, so this layer can always run.</summary>
    public ProbeCost Cost => ProbeCost.Passive;

    public IReadOnlyList<ProbeSetting> Settings =>
    [
        new ProbeSetting(
            "includeStale",
            "Also report neighbours the OS has marked stale — devices seen recently but not confirmed just "
          + "now. Including them finds more, at the cost of listing some that have since gone away.",
            ProbeSettingType.Bool,
            Default: "true"),

        new ProbeSetting(
            "includeUnreachable",
            "Also report entries the OS could not reach. Almost always noise: these are addresses we asked "
          + "about and got no answer for.",
            ProbeSettingType.Bool,
            Default: "false"),
    ];

    public void Attach(IProbeHost host) => _host = host;

    /// <summary>Any adapter with addresses. The table is global, but rows are attributed per interface, so
    /// each adapter sees only its own neighbours.</summary>
    public bool AppliesTo(NetworkAdapterInfo adapter) => adapter.IsUsable;

    public async IAsyncEnumerable<ProbeObservation> DiscoverAsync(
        NetworkAdapterInfo adapter,
        [EnumeratorCancellation] CancellationToken ct)
    {
        bool includeStale = Flag("includeStale", @default: true);
        bool includeUnreachable = Flag("includeUnreachable", @default: false);

        // The interop is synchronous and can take a few ms on a large table — keep it off the caller's
        // thread so a sweep across adapters stays responsive to cancellation.
        var rows = await Task.Run(_readTable, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var devices = NeighborRows.OnAdapter(rows, adapter, includeStale, includeUnreachable);
        var shared = NeighborRows.SharedMacs(devices);

        foreach (var row in devices)
        {
            ct.ThrowIfCancellationRequested();
            yield return NeighborRows.Observe(row, adapter, ProbeId, now, withMac: !shared.Contains(row.Mac));
        }

        foreach (var mac in shared)
            _host?.Log.Warn($"{ProbeId}: {mac} answers for more than {NeighborRows.MostAddressesPerMac} addresses on "
                          + $"{adapter.Name} — proxy ARP, or an access point isolating its clients. Those rows are "
                          + "listed by address alone rather than fused into one device.");

        _host?.Log.Info($"{ProbeId}: {devices.Count} neighbour(s) on {adapter.Name} "
                      + $"(table held {rows.Count} row(s) across all interfaces).");
    }

    private bool Flag(string name, bool @default)
        => _host?.Setting(name) is { Length: > 0 } s && bool.TryParse(s, out var v) ? v : @default;
}
