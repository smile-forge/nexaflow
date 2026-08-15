using System.Net;
using System.Net.NetworkInformation;
using Nexaflow.Features.Network.Arp;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// What the ARP layer declares about itself, which is what the shell acts on before it ever runs.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the contract and not the reading. <c>ArpProbe</c>'s decisions — a stale row is remembered
/// rather than observed, an Incomplete row is not a device, our own address is not a discovery — are the
/// interesting half and are <b>not reachable from a test</b>: they sit behind private statics whose only
/// caller reads the kernel table through <c>NativeMethods.ReadNeighborTable</c>, with nothing to substitute.
/// That is a seam this feature has not got, recorded rather than worked around.
/// </para>
/// <para>
/// What is here still matters. Cost decides whether the shell may run a layer without asking, and a probe
/// that reads a table the kernel already keeps must say Passive or it will be gated behind a consent it
/// does not need.
/// </para>
/// </remarks>
[TestClass]
[NoCoverage("Declared contract of a discovery subfeature — the probe's node covers the reading it cannot reach")]
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

        if (addressed) adapter.Addresses.Add(new AdapterAddress(IPAddress.Parse("192.168.1.10"), 24));

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
}
