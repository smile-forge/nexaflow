using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Network;

/// <summary>
/// What a run hands each probe, and which probes it runs where.
/// </summary>
/// <remarks>
/// No socket and no real probe: the run is orchestration, so a probe that records what it was given is all
/// either half needs.
/// </remarks>
[TestClass]
[CoversNode("network-discovery")]
public class DiscoveryRunTests
{
    private sealed class Probe(string id, ProbeCost cost, params ProbeSetting[] settings) : INetworkProbe
    {
        public IProbeHost? Host { get; private set; }
        public List<string> RanOn { get; } = [];
        public Func<IProbeHost, NetworkAdapterInfo, Task>? Doing { get; init; }

        public string ProbeId => id;
        public string DisplayName => id;
        public ProbeCost Cost => cost;
        public IReadOnlyList<ProbeSetting> Settings => settings;

        public void Attach(IProbeHost host) => Host = host;

        public async IAsyncEnumerable<ProbeObservation> DiscoverAsync(
            NetworkAdapterInfo adapter, [EnumeratorCancellation] CancellationToken ct)
        {
            RanOn.Add(adapter.Id);
            if (Doing is { } act) await act(Host!, adapter);
            yield break;
        }
    }

    private sealed class Wire : IGuardedTransport
    {
        public List<SendIntent> Sent { get; } = [];

        public Task<GuardDecision> SendUdpAsync(SendIntent i, ReadOnlyMemory<byte> p, CancellationToken ct)
        {
            Sent.Add(i);
            return Task.FromResult(GuardDecision.Allow());
        }

        public IAsyncEnumerable<ReceivedDatagram> SendAndCollectAsync(SendIntent i, ReadOnlyMemory<byte> p, TimeSpan w, CancellationToken ct)
        {
            Sent.Add(i);
            return AsyncEnumerable.Empty<ReceivedDatagram>();
        }

        public IAsyncEnumerable<ReceivedDatagram> ListenMulticastAsync(IPAddress g, int port, string id, CancellationToken ct)
            => AsyncEnumerable.Empty<ReceivedDatagram>();
        public Task<IProtocolStream?> ConnectAsync(SendIntent i, TimeSpan t, CancellationToken ct, Action<GuardDecision>? d = null)
            => Task.FromResult<IProtocolStream?>(null);
        public Task<FetchedDocument> FetchAsync(SendIntent i, Uri url, TimeSpan t, CancellationToken ct)
            => Task.FromResult(FetchedDocument.Nothing("no fetching in this fixture"));

        public Task<PingOutcome> PingAsync(SendIntent i, TimeSpan timeout, CancellationToken ct)
        {
            Sent.Add(i);
            return Task.FromResult(new PingOutcome(GuardDecision.Allow(), true, TimeSpan.FromMilliseconds(1)));
        }

        public Task<bool> TcpConnectAsync(IPAddress t, int port, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult(false);
    }

    private static NetworkAdapterInfo Adapter(string id, string ip)
    {
        var a = new NetworkAdapterInfo
        {
            Id = id, Name = id, Description = "Test adapter",
            Type = NetworkInterfaceType.Ethernet, Status = OperationalStatus.Up, MacAddress = "aa:bb:cc:dd:ee:ff",
        };
        a.Addresses.Add(new AdapterAddress(IPAddress.Parse(ip), 24));
        return a;
    }

    // ── Where a sweep may run ─────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("network-guard-options")]
    public async Task A_sweep_runs_only_on_a_network_the_user_allowed()
    {
        var sweep = new Probe("test.sweep", ProbeCost.Sweep);
        var run = new DiscoveryRun([Adapter("eth0", "192.168.1.50"), Adapter("wifi0", "10.0.0.20")], new Wire(),
                                   sweepNetworks: [("eth0", "192.168.1.0/24")]);

        await run.SweepAsync([sweep], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "eth0" }, sweep.RanOn);
        Assert.IsTrue(run.Messages.Any(m => m.Contains("wifi0") && m.Contains("Options → Network")),
            "a layer that was not run has to say why, and where that can be changed");
    }

    [TestMethod]
    public async Task Ordinary_layers_run_everywhere_without_asking()
    {
        var light = new Probe("test.light", ProbeCost.Light);
        var run = new DiscoveryRun([Adapter("eth0", "192.168.1.50"), Adapter("wifi0", "10.0.0.20")], new Wire());

        await run.SweepAsync([light], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "eth0", "wifi0" }, light.RanOn);
    }

    [TestMethod]
    [CoversNode("network-guard-options")]
    public async Task Everything_a_probe_sends_leaves_at_the_cost_it_declared()
    {
        // A sweep-cost probe cannot describe its packets as light and so slip past the consent a sweep needs.
        var wire = new Wire();
        var sweep = new Probe("test.sweep", ProbeCost.Sweep)
        {
            Doing = (host, _) => host.Transport.PingAsync(new SendIntent
            {
                Target = IPAddress.Parse("192.168.1.9"), Port = 0, Layer = SendLayer.Icmp,
                ByteCount = PingOutcome.EchoBytes, Initiator = SendInitiator.Probe, SourceId = "test.sweep",
                Cost = ProbeCost.Passive,
            }, TimeSpan.FromSeconds(1), CancellationToken.None),
        };

        var run = new DiscoveryRun([Adapter("eth0", "192.168.1.50")], wire,
                                   sweepNetworks: [("eth0", "192.168.1.0/24")]);
        await run.SweepAsync([sweep], CancellationToken.None);

        Assert.AreEqual(ProbeCost.Sweep, wire.Sent.Single().Cost);
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task An_unset_setting_is_the_probes_own_declared_default()
    {
        // What IProbeHost.Setting promises, so a probe need not repeat its own defaults in code.
        var probe = new Probe("test.p", ProbeCost.Passive,
                              new ProbeSetting("mx", "How long to wait.", ProbeSettingType.Int, Default: "2"));
        var run = new DiscoveryRun([Adapter("eth0", "192.168.1.50")], new Wire(),
                                   setting: (_, name) => name == "other" ? "configured" : "");

        await run.SweepAsync([probe], CancellationToken.None);

        Assert.AreEqual("2", probe.Host!.Setting("mx"));
        Assert.AreEqual("configured", probe.Host.Setting("other"));
        Assert.AreEqual("", probe.Host.Setting("undeclared"));
    }

    [TestMethod]
    public async Task Each_probe_reads_its_own_settings()
    {
        // One host per probe: a setting is resolved against whoever asks, not whichever probe ran last.
        var a = new Probe("test.a", ProbeCost.Passive);
        var b = new Probe("test.b", ProbeCost.Passive);
        var run = new DiscoveryRun([Adapter("eth0", "192.168.1.50")], new Wire(), setting: (id, _) => id);

        await run.SweepAsync([a, b], CancellationToken.None);

        Assert.AreEqual("test.a", a.Host!.Setting("anything"));
        Assert.AreEqual("test.b", b.Host!.Setting("anything"));
    }

    // ── Stopping ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Cancelling_stops_the_run_before_the_next_layer()
    {
        using var stop = new CancellationTokenSource();
        var first = new Probe("test.first", ProbeCost.Passive)
        {
            Doing = (_, _) => { stop.Cancel(); return Task.CompletedTask; },
        };
        var second = new Probe("test.second", ProbeCost.Passive);
        var run = new DiscoveryRun([Adapter("eth0", "192.168.1.50")], new Wire());

        bool cancelled = false;
        try { await run.SweepAsync([first, second], stop.Token); }
        catch (OperationCanceledException) { cancelled = true; }

        Assert.IsTrue(cancelled, "a cancelled run finished anyway");
        Assert.AreEqual(0, second.RanOn.Count);
    }
}
