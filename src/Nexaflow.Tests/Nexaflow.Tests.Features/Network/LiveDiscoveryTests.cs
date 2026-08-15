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
[NoCoverage("live-network smoke — the deterministic coverage is in SsdpProbeTests and NetworkGuardTests")]
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
            $"observations: {result.Observations}   devices: {result.Devices}",
            "",
        };

        foreach (var node in run.Graph.Nodes.OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var found = string.Join(", ", node.Facts.Select(f => f.SourceProbe).Distinct().Order());
            var ip = node.Best(new FactKey("net", "ipv4"))?.Value.Text ?? "";
            var mac = node.Best(new FactKey("link", "mac"))?.Value.Text ?? "";
            var what = node.Best(new FactKey("dev", "firmware"))?.Value.Text
                    ?? node.Best(new FactKey("svc", "type"))?.Value.Text ?? "";

            report.Add($"  {node.DisplayName,-24} {ip,-16} {mac,-18} [{found}] {what}");
        }

        report.Add("");
        report.AddRange(result.Log.Select(l => "  log: " + l));

        // Reported rather than asserted on: an empty neighbour table on a quiet network is a real result,
        // and failing on it would make this a test of the network rather than of the code.
        Assert.Inconclusive(string.Join("\n", report));
    }
}
