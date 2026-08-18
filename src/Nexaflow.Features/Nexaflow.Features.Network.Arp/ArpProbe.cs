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
    private IProbeHost? _host;

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
        var rows = await Task.Run(NativeMethods.ReadNeighborTable, ct).ConfigureAwait(false);

        uint ifIndex = InterfaceIndexOf(adapter);
        var now = DateTimeOffset.UtcNow;
        var segment = adapter.SegmentId;
        int reported = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (ifIndex != 0 && row.InterfaceIndex != ifIndex) continue;
            if (!Wanted(row.State, includeStale, includeUnreachable)) continue;

            // Our own address is not a discovered device.
            if (adapter.Addresses.Any(a => a.Address.Equals(row.Address))) continue;
            if (IPAddress.IsLoopback(row.Address)) continue;

            // A multicast or broadcast address in the table is an artefact, not a neighbour.
            if (IsMulticastOrBroadcast(row.Address, adapter)) continue;

            yield return Build(row, adapter, segment, now);
            reported++;
        }

        _host?.Log.Info($"{ProbeId}: {reported} neighbour(s) on {adapter.Name} "
                      + $"(table held {rows.Count} row(s) across all interfaces).");
    }

    private ProbeObservation Build(NeighborEntry row, NetworkAdapterInfo adapter, string segment, DateTimeOffset now)
    {
        var obs = new ProbeObservation { SourceProbe = ProbeId, ObservedUtc = now };

        // MAC is scoped to the segment: the same address genuinely can appear on two isolated segments,
        // and fusing those would be wrong. Asserted, because the kernel resolved it on this link.
        obs.Identities.Add(new IdentityClaim(IdentityKind.Mac, row.Mac, segment, Confidence.Asserted));
        obs.Identities.Add(new IdentityClaim(IdentityKind.Ip, row.Address.ToString(), segment, Confidence.Strong));

        // A Permanent/Reachable row was confirmed on the wire; a Stale one is remembered, not observed —
        // and that difference must reach the graph, or a device that left looks as live as one that answered.
        var confidence = row.State is NeighborState.Reachable or NeighborState.Permanent
            ? Confidence.Asserted
            : Confidence.Likely;

        obs.Facts.Add(Fact(new FactKey("link", "mac"), FactValue.OfText(row.Mac), now, Confidence.Asserted,
                           $"neighbour table, interface {row.InterfaceIndex}"));

        obs.Facts.Add(Fact(
            row.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? new FactKey("net", "ipv6")
                : new FactKey("net", "ipv4"),
            FactValue.OfAddress(row.Address.ToString()), now, confidence,
            $"neighbour table, state {row.State}"));

        obs.Facts.Add(Fact(new FactKey("link", "adapter"), FactValue.OfText(adapter.Name), now, Confidence.Asserted,
                           "observed via this adapter"));
        obs.Facts.Add(Fact(new FactKey("link", "segment"), FactValue.OfText(segment), now, Confidence.Asserted,
                           "adapter prefix"));

        // Only claim reachability when the OS actually confirmed it. A stale row is not evidence of presence,
        // and treating it as such is how a device that left keeps looking alive.
        if (row.State is NeighborState.Reachable or NeighborState.Permanent)
            obs.Facts.Add(Fact(new FactKey("net", "reachable"), FactValue.OfBool(true), now, Confidence.Strong,
                               $"neighbour state {row.State}", ttl: TimeSpan.FromMinutes(5)));

        // The router flag is the cheapest gateway signal we get, and it costs nothing.
        if (row.IsRouter)
            obs.Facts.Add(Fact(new FactKey("dev", "class"), FactValue.OfText("router"), now, Confidence.Likely,
                               "neighbour table IsRouter flag"));

        // Edge to the default gateway, so the topology graph has a spine from layer 0 alone.
        foreach (var gw in adapter.Gateways)
        {
            if (gw.Equals(row.Address)) continue;
            if (!adapter.Addresses.Any(a => a.Contains(gw))) continue;

            obs.Edges.Add(new ProbeObservation.PendingEdge(
                new IdentityClaim(IdentityKind.Ip, gw.ToString(), segment, Confidence.Strong),
                EdgeKind.DefaultGateway, Confidence.Strong, adapter.Name));
        }

        return obs;
    }

    private DeviceFact Fact(FactKey key, FactValue value, DateTimeOffset now, Confidence confidence,
                            string detail, TimeSpan? ttl = null)
        => new()
        {
            Key = key, Value = value, SourceProbe = ProbeId, SourceDetail = detail,
            ObservedUtc = now, Confidence = confidence, Ttl = ttl,
            Layer = FactOntology.Describe(key).Layer,
        };

    private static bool Wanted(NeighborState state, bool includeStale, bool includeUnreachable) => state switch
    {
        NeighborState.Reachable or NeighborState.Permanent => true,
        NeighborState.Stale or NeighborState.Delay or NeighborState.Probe => includeStale,
        NeighborState.Unreachable => includeUnreachable,
        _ => false,   // Incomplete: we asked and nobody answered — that is not a device
    };

    private static bool IsMulticastOrBroadcast(IPAddress addr, NetworkAdapterInfo adapter)
    {
        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return addr.IsIPv6Multicast;

        var b = addr.GetAddressBytes();
        if (b[0] >= 224) return true;                                   // 224.0.0.0/4 multicast + 240/4 reserved
        return adapter.Addresses.Any(a => a.DirectedBroadcast is { } d && d.Equals(addr));
    }

    private bool Flag(string name, bool @default)
        => _host?.Setting(name) is { Length: > 0 } s && bool.TryParse(s, out var v) ? v : @default;

    /// <summary>Maps our adapter snapshot back to the OS interface index the table rows carry. Returns 0
    /// when it cannot be determined, which makes the caller report every row rather than none — losing
    /// per-adapter attribution is much better than silently finding nothing.</summary>
    private static uint InterfaceIndexOf(NetworkAdapterInfo adapter)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!string.Equals(nic.Id, adapter.Id, StringComparison.OrdinalIgnoreCase)) continue;

                var props = nic.GetIPProperties();
                try { return (uint)props.GetIPv4Properties().Index; }
                catch (NetworkInformationException) { }
                try { return (uint)props.GetIPv6Properties().Index; }
                catch (NetworkInformationException) { }
            }
        }
        catch (NetworkInformationException) { }
        return 0;
    }
}
