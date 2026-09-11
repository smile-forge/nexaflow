using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using Nexaflow.Features.Network.Arp;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// The address sweep: what it sends, what stops it, and what it will claim to have found.
/// </summary>
/// <remarks>
/// Run through the sweep's own seams — a table and a transport the test supplies, and waiting that does not —
/// so nothing leaves the machine and no test sits out a pacing delay.
/// </remarks>
[TestClass]
[CoversNode("network-discovery-sweep")]
public class AddressSweepProbeTests
{
    private static readonly Func<TimeSpan, CancellationToken, Task> NoWait = (_, _) => Task.CompletedTask;

    private static async Task<List<ProbeObservation>> Sweep(TestProbeHost host, NetworkAdapterInfo adapter,
                                                            IReadOnlyList<NeighborEntry>? table = null,
                                                            CancellationToken ct = default)
    {
        var probe = new AddressSweepProbe(() => table ?? [], NoWait);
        probe.Attach(host);

        List<ProbeObservation> found = [];
        await foreach (var o in probe.DiscoverAsync(adapter, ct)) found.Add(o);
        return found;
    }

    private static string Ip(ProbeObservation o) => o.Identities.Single(i => i.Kind == IdentityKind.Ip).Value;

    // ── What it sends ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task It_pings_every_host_on_the_subnet_once_and_never_itself()
    {
        var wire = new EchoWire();
        var adapter = NetworkFixtures.Adapter("192.168.1.10");

        await Sweep(new TestProbeHost(wire, null, adapter), adapter);

        var targets = wire.Echoes.Select(e => e.Target).ToList();
        Assert.AreEqual(253, targets.Count, "254 hosts on a /24, less this machine");
        Assert.AreEqual(targets.Count, targets.Distinct().Count(), "each address once");
        CollectionAssert.DoesNotContain(targets, IPAddress.Parse("192.168.1.10"));
        CollectionAssert.DoesNotContain(targets, IPAddress.Parse("192.168.1.0"));
        CollectionAssert.DoesNotContain(targets, IPAddress.Parse("192.168.1.255"));
    }

    [TestMethod]
    public async Task Every_echo_says_it_is_part_of_a_sweep()
    {
        // The guard decides consent on the cost an intent carries. The run stamps it too; the probe does not
        // wait to be made honest.
        var wire = new EchoWire();
        var adapter = NetworkFixtures.Adapter();

        await Sweep(new TestProbeHost(wire, null, adapter), adapter);

        Assert.IsTrue(wire.Echoes.All(e => e.Cost == ProbeCost.Sweep
                                        && e.Layer == SendLayer.Icmp
                                        && e.Initiator == SendInitiator.Probe));
    }

    [TestMethod]
    public async Task A_subnet_bigger_than_the_limit_is_not_swept_at_all()
    {
        var wire = new EchoWire();
        var adapter = NetworkFixtures.Adapter("10.0.0.10", 23);   // 510 hosts
        var host = new TestProbeHost(wire, null, adapter);

        var found = await Sweep(host, adapter);

        Assert.AreEqual(0, wire.Echoes.Count);
        Assert.AreEqual(0, found.Count);
        Assert.IsTrue(host.Said.Any(s => s.Contains("maxHosts")), "the log names the setting that would allow it");
    }

    // ── What stops it ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task A_refusal_every_address_would_get_ends_the_sweep_in_one_line()
    {
        var no = GuardDecision.Deny(GuardRefusal.SweepNotPermitted, "Sweeping 192.168.1.0/24 is not allowed.");
        var wire = new EchoWire { Answer = (_, _) => PingOutcome.Refused(no) };
        var adapter = NetworkFixtures.Adapter();
        var host = new TestProbeHost(wire, null, adapter);

        var found = await Sweep(host, adapter, [NetworkFixtures.Row("192.168.1.20", "aa:00:00:00:00:20")]);

        Assert.IsTrue(wire.Echoes.Count <= 32, $"asked {wire.Echoes.Count} times after the first no");
        Assert.AreEqual(0, found.Count, "a sweep that was not allowed found nothing");
        Assert.AreEqual(1, host.Said.Count(s => s.Contains("stopped")));
    }

    [TestMethod]
    public async Task Too_fast_is_waited_out_rather_than_given_up_on()
    {
        var wire = new EchoWire
        {
            Answer = (_, attempt) => attempt == 1
                ? PingOutcome.Refused(GuardDecision.Deny(GuardRefusal.RateLimited, "too fast"))
                : new PingOutcome(GuardDecision.Allow(), false, TimeSpan.Zero),
        };
        var adapter = NetworkFixtures.Adapter();
        var host = new TestProbeHost(wire, null, adapter);

        await Sweep(host, adapter);

        Assert.AreEqual(253, wire.Echoes.Select(e => e.Target).Distinct().Count(), "every address was reached");
        Assert.IsFalse(host.Said.Any(s => s.Contains("stopped")));
    }

    [TestMethod]
    [CoversNode("network-page-cancel")]
    public async Task Stopping_stops_the_sweep()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var adapter = NetworkFixtures.Adapter();

        bool stopped = false;
        try { await Sweep(new TestProbeHost(new EchoWire(), null, adapter), adapter, ct: stop.Token); }
        catch (OperationCanceledException) { stopped = true; }

        Assert.IsTrue(stopped);
    }

    // ── What it claims ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task It_reports_only_what_it_refreshed_inside_the_swept_subnet()
    {
        var adapter = NetworkFixtures.Adapter();

        var found = await Sweep(new TestProbeHost(new EchoWire(), null, adapter), adapter,
        [
            NetworkFixtures.Row("192.168.1.20", "aa:00:00:00:00:20"),                            // resolved just now
            NetworkFixtures.Row("192.168.1.21", "aa:00:00:00:00:21", NeighborState.Stale),       // only remembered
            NetworkFixtures.Row("192.168.1.22", "aa:00:00:00:00:22", NeighborState.Incomplete),  // asked, no answer
            NetworkFixtures.Row("10.0.0.5", "aa:00:00:00:00:05"),                                // another subnet
        ]);

        var only = found.Single();
        Assert.AreEqual("192.168.1.20", Ip(only));
        Assert.AreEqual("network.sweep", only.SourceProbe);
    }

    [TestMethod]
    public async Task A_host_that_answered_the_ping_is_known_to_be_there_now()
    {
        var wire = new EchoWire
        {
            Answer = (i, _) => new PingOutcome(GuardDecision.Allow(),
                                               i.Target.Equals(IPAddress.Parse("192.168.1.20")),
                                               TimeSpan.FromMilliseconds(3)),
        };
        var adapter = NetworkFixtures.Adapter();

        var found = await Sweep(new TestProbeHost(wire, null, adapter), adapter,
                                [NetworkFixtures.Row("192.168.1.20", "aa:00:00:00:00:20"),
                                 NetworkFixtures.Row("192.168.1.30", "aa:00:00:00:00:30")]);

        Assert.IsTrue(found.Single(o => Ip(o) == "192.168.1.20").Facts.Any(
            f => f.Key.Name == "reachable" && f.Confidence == Confidence.Asserted && f.SourceDetail.Contains("ping")));
        Assert.IsFalse(found.Single(o => Ip(o) == "192.168.1.30").Facts.Any(f => f.SourceDetail.Contains("ping")),
            "a device that dropped the ping still answered the resolution — but it did not answer the ping");
    }

    [TestMethod]
    public async Task One_hardware_address_for_the_whole_subnet_is_not_one_device()
    {
        var table = Enumerable.Range(20, NeighborRows.MostAddressesPerMac + 1)
                              .Select(h => NetworkFixtures.Row($"192.168.1.{h}", "aa:00:00:00:00:99"))
                              .ToList();
        var adapter = NetworkFixtures.Adapter();
        var host = new TestProbeHost(new EchoWire(), null, adapter);

        var found = await Sweep(host, adapter, table);

        Assert.AreEqual(table.Count, found.Count);
        Assert.IsFalse(found.Any(o => o.Identities.Any(i => i.Kind == IdentityKind.Mac)));
        Assert.IsTrue(host.Said.Any(s => s.Contains("proxy ARP")));
    }

    // ── What it declares ──────────────────────────────────────────────────────

    [TestMethod]
    public void It_is_off_until_asked_for_and_says_what_it_costs()
    {
        var sweep = new AddressSweepProbe();
        var declared = typeof(AddressSweepProbe).GetCustomAttribute<SubfeatureAttribute>()!;

        Assert.AreEqual(ProbeCost.Sweep, sweep.Cost);
        Assert.AreEqual("network.sweep", sweep.ProbeId);
        Assert.IsFalse(declared.DefaultEnabled, "a layer intrusion detection sees is never on by default");
    }

    [TestMethod]
    public void It_sweeps_IPv4_only()
    {
        var linkOnly = new NetworkAdapterInfo
        {
            Id = "{V6}", Name = "v6", Description = "Test adapter",
            Type = NetworkInterfaceType.Ethernet, Status = OperationalStatus.Up, MacAddress = "aa:bb:cc:dd:ee:ff",
        };
        linkOnly.Addresses.Add(new AdapterAddress(IPAddress.Parse("fe80::1"), 64));

        Assert.IsFalse(new AddressSweepProbe().AppliesTo(linkOnly), "a /64 is not a list anybody pings through");
        Assert.IsTrue(new AddressSweepProbe().AppliesTo(NetworkFixtures.Adapter()));
    }
}
