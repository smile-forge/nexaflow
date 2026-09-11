using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Model;

namespace Nexaflow.Features.Network.Arp;

/// <summary>
/// The neighbour table, read into what a probe reports — shared by the layer that reads it and the layer that
/// fills it first.
/// </summary>
/// <remarks>
/// Both must agree on what counts as a device, because the graph fuses their findings by the hardware address
/// they both claim: a stale row is remembered rather than observed, an Incomplete one is not a device at all,
/// and this machine's own address is not a discovery. Two copies of those rules would drift, and a drift here
/// reads as one device found twice.
/// </remarks>
internal static class NeighborRows
{
    /// <summary>More addresses than this behind one hardware address is not a device. It is proxy ARP, or an
    /// access point isolating its clients by answering for every one of them.</summary>
    public const int MostAddressesPerMac = 8;

    /// <summary>The rows that are devices on <paramref name="adapter"/> — optionally only those inside
    /// <paramref name="within"/>.</summary>
    public static List<NeighborEntry> OnAdapter(IReadOnlyList<NeighborEntry> rows, NetworkAdapterInfo adapter,
                                                bool includeStale, bool includeUnreachable,
                                                AdapterAddress? within = null)
    {
        uint ifIndex = InterfaceIndexOf(adapter);
        List<NeighborEntry> kept = [];

        foreach (var row in rows)
        {
            if (ifIndex != 0 && row.InterfaceIndex != ifIndex) continue;
            if (!Wanted(row.State, includeStale, includeUnreachable)) continue;

            // Our own address is not a discovered device.
            if (adapter.Addresses.Any(a => a.Address.Equals(row.Address))) continue;
            if (IPAddress.IsLoopback(row.Address)) continue;

            // A multicast or broadcast address in the table is an artefact, not a neighbour.
            if (IsMulticastOrBroadcast(row.Address, adapter)) continue;

            if (within is { } prefix && !prefix.Contains(row.Address)) continue;

            kept.Add(row);
        }

        return kept;
    }

    /// <summary>Hardware addresses answering for more IPv4 addresses than a device plausibly has.</summary>
    public static IReadOnlySet<string> SharedMacs(IEnumerable<NeighborEntry> rows)
        => rows.Where(r => r.Address.AddressFamily == AddressFamily.InterNetwork)
               .GroupBy(r => r.Mac, StringComparer.OrdinalIgnoreCase)
               .Where(g => g.Select(r => r.Address).Distinct().Count() > MostAddressesPerMac)
               .Select(g => g.Key)
               .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>One row, as an observation from <paramref name="probeId"/>.</summary>
    /// <param name="withMac">False where the hardware address answers for too many addresses to be anybody's
    /// identity — see <see cref="SharedMacs"/>. The row is then reported by its address alone.</param>
    public static ProbeObservation Observe(NeighborEntry row, NetworkAdapterInfo adapter, string probeId,
                                           DateTimeOffset now, bool withMac = true)
    {
        var segment = adapter.SegmentId;
        var obs = new ProbeObservation { SourceProbe = probeId, ObservedUtc = now };

        // MAC is scoped to the segment: the same address genuinely can appear on two isolated segments,
        // and fusing those would be wrong. Asserted, because the kernel resolved it on this link.
        if (withMac)
            obs.Identities.Add(new IdentityClaim(IdentityKind.Mac, row.Mac, segment, Confidence.Asserted));
        obs.Identities.Add(new IdentityClaim(IdentityKind.Ip, row.Address.ToString(), segment, Confidence.Strong));

        // A Permanent/Reachable row was confirmed on the wire; a Stale one is remembered, not observed —
        // and that difference must reach the graph, or a device that left looks as live as one that answered.
        var confidence = row.State is NeighborState.Reachable or NeighborState.Permanent
            ? Confidence.Asserted
            : Confidence.Likely;

        if (withMac)
            obs.Facts.Add(Fact(probeId, new FactKey("link", "mac"), FactValue.OfText(row.Mac), now,
                               Confidence.Asserted, $"neighbour table, interface {row.InterfaceIndex}"));

        obs.Facts.Add(Fact(probeId,
            row.Address.AddressFamily == AddressFamily.InterNetworkV6
                ? new FactKey("net", "ipv6")
                : new FactKey("net", "ipv4"),
            FactValue.OfAddress(row.Address.ToString()), now, confidence,
            $"neighbour table, state {row.State}"));

        obs.Facts.Add(Fact(probeId, new FactKey("link", "adapter"), FactValue.OfText(adapter.Name), now,
                           Confidence.Asserted, "observed via this adapter"));
        obs.Facts.Add(Fact(probeId, new FactKey("link", "segment"), FactValue.OfText(segment), now,
                           Confidence.Asserted, "adapter prefix"));

        // Only claim reachability when the OS actually confirmed it. A stale row is not evidence of presence,
        // and treating it as such is how a device that left keeps looking alive.
        if (row.State is NeighborState.Reachable or NeighborState.Permanent)
            obs.Facts.Add(Fact(probeId, new FactKey("net", "reachable"), FactValue.OfBool(true), now,
                               Confidence.Strong, $"neighbour state {row.State}", ttl: TimeSpan.FromMinutes(5)));

        // The router flag is the cheapest gateway signal we get, and it costs nothing.
        if (row.IsRouter)
            obs.Facts.Add(Fact(probeId, new FactKey("dev", "class"), FactValue.OfText("router"), now,
                               Confidence.Likely, "neighbour table IsRouter flag"));

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

    public static DeviceFact Fact(string probeId, FactKey key, FactValue value, DateTimeOffset now,
                                  Confidence confidence, string detail, TimeSpan? ttl = null)
        => new()
        {
            Key = key, Value = value, SourceProbe = probeId, SourceDetail = detail,
            ObservedUtc = now, Confidence = confidence, Ttl = ttl,
            Layer = FactOntology.Describe(key).Layer,
        };

    public static bool Wanted(NeighborState state, bool includeStale, bool includeUnreachable) => state switch
    {
        NeighborState.Reachable or NeighborState.Permanent => true,
        NeighborState.Stale or NeighborState.Delay or NeighborState.Probe => includeStale,
        NeighborState.Unreachable => includeUnreachable,
        _ => false,   // Incomplete: we asked and nobody answered — that is not a device
    };

    public static bool IsMulticastOrBroadcast(IPAddress addr, NetworkAdapterInfo adapter)
    {
        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
            return addr.IsIPv6Multicast;

        var b = addr.GetAddressBytes();
        if (b[0] >= 224) return true;                                   // 224.0.0.0/4 multicast + 240/4 reserved
        return adapter.Addresses.Any(a => a.DirectedBroadcast is { } d && d.Equals(addr));
    }

    /// <summary>Maps our adapter snapshot back to the OS interface index the table rows carry. Returns 0
    /// when it cannot be determined, which makes the caller report every row rather than none — losing
    /// per-adapter attribution is much better than silently finding nothing.</summary>
    public static uint InterfaceIndexOf(NetworkAdapterInfo adapter)
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
