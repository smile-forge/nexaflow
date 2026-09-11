using System.Net;
using System.Net.NetworkInformation;
using Nexaflow.Features.Network.Arp;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// What the ARP layer declares about itself, and what it makes of the table it reads.
/// </summary>
/// <remarks>
/// <para>
/// The reading is the interesting half — a stale row is remembered rather than observed, an Incomplete row is
/// not a device, our own address is not a discovery — and it used to be out of reach, behind a table read
/// with nothing to substitute. The probe now takes its table through a seam, so the rules are tested here
/// rather than trusted.
/// </para>
/// <para>
/// The declared contract still matters. Cost decides whether the shell may run a layer without asking, and a
/// probe that reads a table the kernel already keeps must say Passive or it will be gated behind a consent it
/// does not need.
/// </para>
/// </remarks>
[TestClass]
[CoversNode("network-discovery-arp")]
public class ArpProbeTests
{
    private static NetworkAdapterInfo Adapter(OperationalStatus status = OperationalStatus.Up,
                                              NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
                                              bool addressed = true)
    {
        var adapter = new NetworkAdapterInfo
        {
            Id = "{TEST-ADAPTER}",
            Name = "Test",
            Description = "Test adapter",
            Type = type,
            Status = status,
            MacAddress = "aa:bb:cc:dd:ee:ff",
        };

        if (addressed)
        {
            adapter.Addresses.Add(new AdapterAddress(IPAddress.Parse("192.168.1.10"), 24));
            adapter.Gateways.Add(IPAddress.Parse("192.168.1.1"));
        }

        return adapter;
    }

    [TestMethod]
    public void The_neighbour_table_layer_costs_nothing_to_run()
    {
        // It reads a table the OS already keeps and sends no packets, so nothing about it needs consent.
        // Anything above Passive puts it behind the same gate as a sweep, which is visible to an IDS.
        var probe = new ArpProbe();

        Assert.AreEqual(ProbeCost.Passive, probe.Cost);
        Assert.AreEqual("network.arp", probe.ProbeId);
    }

    [TestMethod]
    public void And_it_offers_the_two_choices_that_change_what_counts_as_a_device()
    {
        // Both are about believing the OS less than it is willing to say: a stale row is a device seen
        // recently and not confirmed now, and an unreachable one is an address we asked about and got
        // nothing for. The defaults say which of those is worth reporting.
        var settings = new ArpProbe().Settings.ToDictionary(s => s.Name, StringComparer.Ordinal);

        Assert.AreEqual("true", settings["includeStale"].Default,
            "a stale neighbour is worth listing, marked as remembered rather than observed");
        Assert.AreEqual("false", settings["includeUnreachable"].Default,
            "an unreachable one is noise — we asked and nobody answered");

        Assert.IsTrue(settings.Values.All(s => s.Type == ProbeSettingType.Bool));
        Assert.IsTrue(settings.Values.All(s => s.Description.Length > 40),
            "a setting a user is asked about has to say what it costs them");
    }

    [TestMethod]
    public void And_it_applies_to_adapters_that_could_have_neighbours()
    {
        // The table is global but its rows are attributed per interface, so the question is only whether
        // this adapter is one a neighbour could be on.
        var probe = new ArpProbe();

        Assert.IsTrue(probe.AppliesTo(Adapter()));
        Assert.IsFalse(probe.AppliesTo(Adapter(status: OperationalStatus.Down)), "nothing is on a down link");
        Assert.IsFalse(probe.AppliesTo(Adapter(addressed: false)), "nor on one with no address of its own");
        Assert.IsFalse(probe.AppliesTo(Adapter(type: NetworkInterfaceType.Loopback)),
            "and loopback carries no LAN, which is the same decision that keeps it off the guard's "
          + "allow-list");
    }

    // ── What it makes of the table ────────────────────────────────────────────

    private static async Task<(List<ProbeObservation> Found, TestProbeHost Host)> Read(params NeighborEntry[] table)
    {
        var adapter = Adapter();
        var host = new TestProbeHost(new EchoWire(), null, adapter);
        var probe = new ArpProbe(() => table);
        probe.Attach(host);

        List<ProbeObservation> found = [];
        await foreach (var o in probe.DiscoverAsync(adapter, CancellationToken.None)) found.Add(o);
        return (found, host);
    }

    private static string Ip(ProbeObservation o) => o.Identities.Single(i => i.Kind == IdentityKind.Ip).Value;

    [TestMethod]
    public async Task A_confirmed_neighbour_is_there_and_a_stale_one_is_only_remembered()
    {
        var (found, _) = await Read(NetworkFixtures.Row("192.168.1.20", "aa:00:00:00:00:20"),
                                    NetworkFixtures.Row("192.168.1.21", "aa:00:00:00:00:21", NeighborState.Stale));

        var live = found.Single(o => Ip(o) == "192.168.1.20");
        var remembered = found.Single(o => Ip(o) == "192.168.1.21");

        Assert.IsTrue(live.Facts.Any(f => f.Key.Name == "reachable"));
        Assert.IsFalse(remembered.Facts.Any(f => f.Key.Name == "reachable"),
            "a stale row is not evidence the device is there");
        Assert.AreEqual(Confidence.Likely, remembered.Facts.Single(f => f.Key.Name == "ipv4").Confidence);
    }

    [TestMethod]
    public async Task An_incomplete_row_is_not_a_device()
    {
        var (found, _) = await Read(NetworkFixtures.Row("192.168.1.22", "aa:00:00:00:00:22", NeighborState.Incomplete));

        Assert.AreEqual(0, found.Count, "we asked and nobody answered");
    }

    [TestMethod]
    public async Task Nor_is_this_machine_or_a_multicast_or_broadcast_row()
    {
        var (found, _) = await Read(NetworkFixtures.Row("192.168.1.10", "aa:bb:cc:dd:ee:ff"),
                                    NetworkFixtures.Row("224.0.0.251", "01:00:5e:00:00:fb"),
                                    NetworkFixtures.Row("192.168.1.255", "ff:ff:ff:ff:ff:ff"));

        Assert.AreEqual(0, found.Count);
    }

    [TestMethod]
    public async Task A_router_says_so_and_every_neighbour_points_at_the_gateway()
    {
        var (found, _) = await Read(NetworkFixtures.Row("192.168.1.1", "aa:00:00:00:00:01", router: true),
                                    NetworkFixtures.Row("192.168.1.20", "aa:00:00:00:00:20"));

        Assert.AreEqual("router",
            found.Single(o => Ip(o) == "192.168.1.1").Facts.Single(f => f.Key.Name == "class").Value.Text);

        var edge = found.Single(o => Ip(o) == "192.168.1.20").Edges.Single();
        Assert.AreEqual(EdgeKind.DefaultGateway, edge.Kind);
        Assert.AreEqual("192.168.1.1", edge.To.Value);
    }

    [TestMethod]
    public async Task One_hardware_address_behind_many_addresses_is_not_one_device()
    {
        // Proxy ARP, or an access point isolating its clients by answering for all of them. Fused by that
        // MAC the whole subnet would become one device; reported by address, it stays a list.
        var table = Enumerable.Range(20, NeighborRows.MostAddressesPerMac + 1)
                              .Select(h => NetworkFixtures.Row($"192.168.1.{h}", "aa:00:00:00:00:99"))
                              .ToArray();

        var (found, host) = await Read(table);

        Assert.AreEqual(table.Length, found.Count);
        Assert.IsFalse(found.Any(o => o.Identities.Any(i => i.Kind == IdentityKind.Mac)));
        Assert.IsTrue(host.Said.Any(s => s.Contains("aa:00:00:00:00:99")), "and the log says why");
    }
}
