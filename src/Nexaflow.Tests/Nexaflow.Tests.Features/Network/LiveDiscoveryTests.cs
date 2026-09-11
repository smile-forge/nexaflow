using Nexaflow.Features.Network.Arp;
using Nexaflow.Features.Network.Ssdp;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// A real sweep, on whatever network this machine is actually on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Interactive, so CI never runs it</b> — it asserts against whatever happens to be plugged in, which is
/// a coin toss on a runner and the whole point on a desk. Everything it exercises has a deterministic test
/// elsewhere; what this adds is the one thing those cannot: that the parts work when the network is real.
/// </para>
/// <para>
/// It sends. One multicast M-SEARCH per usable adapter, which is what any UPnP device on the segment
/// already expects and answers a hundred times a day — and it goes through the same guard and the same
/// budget as the page does, because there is no other route to a socket.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Interactive")]
[NoCoverage("live-network smoke — the deterministic coverage is in the probe and guard tests")]
public class LiveDiscoveryTests
{
    [TestMethod]
    public async Task Both_layers_run_and_whatever_they_find_lands_in_one_graph()
    {
        var adapters = NetworkAdapters.Usable();

        if (adapters.Count == 0) Assert.Inconclusive("No usable adapter on this machine.");

        var guard = new NetworkGuard();
        guard.SetAdapters(adapters);

        var run = new DiscoveryRun(adapters, new UdpTransport(guard, new RunBudget()));

        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await run.SweepAsync([new ArpProbe(), new SsdpProbe()], giveUp.Token);

        var report = new List<string>
        {
            $"adapters: {string.Join(", ", adapters.Select(a => a.Name))}",
            $"observations: {result.Observations}   devices: {run.Graph.Nodes.Count}",
            "",
        };

        report.AddRange(Devices(run.Graph));

        int stubs = run.Graph.Nodes.Count(n => n.Facts.Count == 0);
        if (stubs > 0) report.Add($"  ({stubs} topology stub(s) not shown — edge targets nothing described)");

        report.Add("");
        report.AddRange(result.Log.Select(l => "  log: " + l));

        // Reported rather than asserted on: an empty neighbour table on a quiet network is a real result,
        // and failing on it would make this a test of the network rather than of the code.
        Assert.Inconclusive(string.Join("\n", report));
    }

    [TestMethod]
    public async Task A_sweep_finds_what_the_table_did_not_already_know()
    {
        // This PINGS EVERY ADDRESS on every usable adapter's subnet, granting itself the permission Options →
        // Network would. Being Interactive is not enough for that: a local run of the Network suite would
        // sweep whoever ran it, and one did. It sweeps only when asked for by name.
        if (Environment.GetEnvironmentVariable("NEXAFLOW_LIVE_SWEEP") != "1")
            Assert.Inconclusive("Pings every address on this machine's subnets. Set NEXAFLOW_LIVE_SWEEP=1 to run "
                              + "it, on a network you look after.");

        var adapters = NetworkAdapters.Usable();

        if (adapters.Count == 0) Assert.Inconclusive("No usable adapter on this machine.");

        List<(string AdapterId, string Segment)> networks = [.. adapters.Select(a => (a.Id, a.SegmentId))];

        var guard = new NetworkGuard();
        guard.SetAdapters(adapters);
        guard.SetSweepConsent(networks);

        var before = new DiscoveryRun(adapters, new UdpTransport(guard, new RunBudget()));
        await before.SweepAsync([new ArpProbe()], CancellationToken.None);
        int known = before.Graph.Nodes.Count(n => n.Facts.Count > 0);

        var swept = new DiscoveryRun(adapters, new UdpTransport(guard, new RunBudget()), sweepNetworks: networks);
        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var result = await swept.SweepAsync([new AddressSweepProbe(), new ArpProbe()], giveUp.Token);

        List<string> report =
        [
            $"the neighbour table alone: {known} device(s)",
            $"after a sweep: {swept.Graph.Nodes.Count(n => n.Facts.Count > 0)} device(s)",
            "",
            .. Devices(swept.Graph),
            "",
            .. result.Log.Select(l => "  log: " + l),
        ];

        Assert.Inconclusive(string.Join("\n", report));
    }

    /// <summary>The same two decisions the page makes: a stub is not a discovery, and the two address
    /// families are different columns.</summary>
    private static IEnumerable<string> Devices(DeviceGraph graph)
    {
        foreach (var node in graph.Nodes
                     .Where(n => n.Facts.Count > 0)
                     .OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var found = string.Join(", ", node.Facts.Select(f => f.SourceProbe).Distinct().Order());
            var v4 = node.AllOf(new FactKey("net", "ipv4")).Select(f => f.Value.Text).ToList();
            var v6 = node.AllOf(new FactKey("net", "ipv6")).Select(f => f.Value.Text).ToList();
            var mac = node.Best(new FactKey("link", "mac"))?.Value.Text ?? "";
            var what = node.Best(new FactKey("dev", "firmware"))?.Value.Text
                    ?? node.Best(new FactKey("svc", "type"))?.Value.Text ?? "";

            yield return $"  {node.DisplayName,-22} {string.Join("/", v4),-15} "
                       + $"{string.Join("/", v6),-30} {mac,-18} [{found}] {what}";
        }
    }
}
