using System.Net;
using Nexaflow.Features.Network.Actions;
using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// What a user can do to one device, and — mostly — what they are not offered.
/// </summary>
/// <remarks>
/// <para>
/// <c>AppliesTo</c> is the half worth testing hardest. It is what makes "open the management interface, if
/// it has one" a contract rather than a condition inside a click handler: an action that needs a fact the
/// device has not got is never rendered, so the user cannot meet a button that cannot work.
/// </para>
/// <para>
/// Same shape as <c>IFileAction</c> next door, and the same reason: the page holds no list of buttons.
/// </para>
/// </remarks>
[TestClass]
[CoversNode("network-discovery")]
public class DeviceActionTests
{
    private sealed class Host(IGuardedTransport transport) : IDeviceActionHost, IProbeLog
    {
        public List<string> Opened { get; } = [];
        public IGuardedTransport Transport => transport;
        public IProbeLog Log => this;

        public Task OpenAsync(string url, CancellationToken ct) { Opened.Add(url); return Task.CompletedTask; }
        public Task<bool> ConfirmAsync(string t, string m, CancellationToken ct) => Task.FromResult(true);

        public void Info(string m) { }
        public void Warn(string m) { }
        public void Error(string m, Exception? ex = null) { }
    }

    private sealed class Wire(bool pingAnswers = true, TimeSpan? rtt = null) : IGuardedTransport
    {
        public List<IPAddress> Pinged { get; } = [];

        public Task<(bool Ok, TimeSpan Rtt)> PingAsync(IPAddress t, TimeSpan timeout, CancellationToken ct)
        {
            Pinged.Add(t);
            return Task.FromResult((pingAnswers, rtt ?? TimeSpan.FromMilliseconds(7)));
        }

        public Task<GuardDecision> SendUdpAsync(SendIntent i, ReadOnlyMemory<byte> p, CancellationToken ct)
            => Task.FromResult(GuardDecision.Allow());
        public IAsyncEnumerable<ReceivedDatagram> SendAndCollectAsync(SendIntent i, ReadOnlyMemory<byte> p, TimeSpan w, CancellationToken ct)
            => AsyncEnumerable.Empty<ReceivedDatagram>();
        public IAsyncEnumerable<ReceivedDatagram> ListenMulticastAsync(IPAddress g, int port, string id, CancellationToken ct)
            => AsyncEnumerable.Empty<ReceivedDatagram>();
        public Task<IProtocolStream?> ConnectAsync(SendIntent i, TimeSpan t, CancellationToken ct, Action<GuardDecision>? d = null)
            => Task.FromResult<IProtocolStream?>(null);
        public Task<bool> TcpConnectAsync(IPAddress t, int port, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult(false);
    }

    /// <summary>A device carrying exactly the facts a test needs it to.</summary>
    private static DeviceNode Device(params (string Key, string Value)[] facts)
    {
        var graph = new DeviceGraph();
        var obs = new ProbeObservation { SourceProbe = "test", ObservedUtc = DateTimeOffset.UtcNow };
        obs.Identities.Add(new IdentityClaim(IdentityKind.Ip, "192.168.1.42", "192.168.1.0/24",
                                             Confidence.Asserted));

        foreach (var (key, value) in facts)
        {
            var parts = key.Split('.');
            obs.Facts.Add(new DeviceFact
            {
                Key = new FactKey(parts[0], parts[1]),
                Value = FactValue.OfText(value),
                SourceProbe = "test",
                ObservedUtc = DateTimeOffset.UtcNow,
                Confidence = Confidence.Strong,
            });
        }

        return graph.Observe(obs)!;
    }

    // ── What is offered ───────────────────────────────────────────────────────

    [TestMethod]
    public void A_web_interface_is_offered_only_where_the_device_published_one()
    {
        // The whole point of the contract. Trying port 80 on everything would find more and would be a
        // different thing — a scan aimed at devices that never invited it.
        var action = new OpenManagementAction();

        Assert.IsTrue(action.AppliesTo(Device(("svc.url", "http://192.168.1.42:49152/description.xml"))));
        Assert.IsFalse(action.AppliesTo(Device(("link.mac", "aa:bb:cc:dd:ee:ff"))),
            "a device the neighbour table found and nothing else described has published nothing");
    }

    [TestMethod]
    public void And_it_opens_exactly_what_was_advertised()
    {
        var host = new Host(new Wire());
        var device = Device(("svc.url", "http://192.168.1.42:49152/description.xml"));

        var result = new OpenManagementAction().PerformAsync(device, host, CancellationToken.None).Result;

        Assert.IsTrue(result.Ok);
        // The whole address, not its root. Reducing it to scheme and authority was a guess that the
        // interface lives at the root of whatever served the description; on both real devices tested,
        // nothing is there. What was advertised is the only address there is evidence for.
        CollectionAssert.AreEqual(
            new[] { "http://192.168.1.42:49152/description.xml" }, host.Opened);
    }

    [TestMethod]
    public void And_a_published_address_that_is_not_a_web_address_is_not_offered()
    {
        Assert.IsFalse(new OpenManagementAction().AppliesTo(
            Device(("svc.url", "rtsp://192.168.1.42:554/stream"))),
            "a stream is not something to open in a browser");
    }

    [TestMethod]
    public void A_ping_is_offered_to_anything_with_an_address()
    {
        Assert.IsTrue(new PingAction().AppliesTo(Device(("net.ipv4", "192.168.1.42"))));
        Assert.IsFalse(new PingAction().AppliesTo(Device(("link.mac", "aa:bb:cc:dd:ee:ff"))),
            "a device known only by hardware address has nothing to aim at");
    }

    // ── What running one produces ─────────────────────────────────────────────

    [TestMethod]
    public async Task A_ping_that_answers_is_a_fact_with_a_short_life()
    {
        // Every other fact on the page is a memory. This one is the only thing that says the device is
        // there NOW — which is exactly why it must not be believed for long.
        var wire = new Wire(pingAnswers: true, rtt: TimeSpan.FromMilliseconds(12));
        var device = Device(("net.ipv4", "192.168.1.42"));

        var result = await new PingAction().PerformAsync(device, new Host(wire), CancellationToken.None);

        Assert.IsTrue(result.Ok);
        StringAssert.Contains(result.Message, "12 ms");
        CollectionAssert.AreEqual(new[] { IPAddress.Parse("192.168.1.42") }, wire.Pinged);

        var learned = result.Learned!;
        var reachable = learned.Facts.Single(f => f.Key.Name == "reachable");

        Assert.AreEqual("true", reachable.Value.Text);
        Assert.AreEqual(Confidence.Asserted, reachable.Confidence);
        Assert.IsNotNull(reachable.Ttl, "a device that answered once is not always there");

        Assert.AreEqual(12d, learned.Facts.Single(f => f.Key.Name == "rtt").Value.Number);
    }

    [TestMethod]
    public async Task And_silence_is_recorded_rather_than_treated_as_a_failure()
    {
        // Plenty of devices refuse ICMP by policy while being perfectly reachable, so "it did not answer"
        // is an observation about the ping and not a fault in the action.
        var device = Device(("net.ipv4", "192.168.1.42"));

        var result = await new PingAction()
            .PerformAsync(device, new Host(new Wire(pingAnswers: false)), CancellationToken.None);

        Assert.IsTrue(result.Ok, "the action did what it was asked; the device chose not to answer");
        StringAssert.Contains(result.Message, "refuse pings");

        var reachable = result.Learned!.Facts.Single(f => f.Key.Name == "reachable");
        Assert.AreEqual("false", reachable.Value.Text);
        Assert.IsFalse(result.Learned.Facts.Any(f => f.Key.Name == "rtt"),
            "and there is no round-trip time for a round trip that did not happen");
    }

    [TestMethod]
    public async Task And_what_it_learned_lands_on_the_device_it_was_run_against()
    {
        // An action is another way of finding out about a device, so its result goes into the graph
        // through the same door a probe's observation does — and has to resolve to the same node.
        var graph = new DeviceGraph();
        var first = new ProbeObservation { SourceProbe = "network.arp", ObservedUtc = DateTimeOffset.UtcNow };
        first.Identities.Add(new IdentityClaim(IdentityKind.Ip, "192.168.1.42", "seg", Confidence.Asserted));
        first.Facts.Add(new DeviceFact
        {
            Key = new FactKey("net", "ipv4"), Value = FactValue.OfAddress("192.168.1.42"),
            SourceProbe = "network.arp", ObservedUtc = DateTimeOffset.UtcNow, Confidence = Confidence.Strong,
        });

        var device = graph.Observe(first)!;
        int before = graph.Nodes.Count;

        var result = await new PingAction().PerformAsync(device, new Host(new Wire()), CancellationToken.None);
        var after = graph.Observe(result.Learned!);

        Assert.AreEqual(device.Id, after!.Id, "the ping described the device that was clicked");
        Assert.AreEqual(before, graph.Nodes.Count, "and did not invent a second one");
        Assert.IsTrue(device.Facts.Any(f => f.SourceProbe == "network.ping"));
    }

    // ── What they declare ─────────────────────────────────────────────────────

    [TestMethod]
    public void Neither_of_these_changes_anything_on_the_device()
    {
        // IsDestructive is what the host renders differently and confirms before running. Asking a device
        // a question is not that, and marking either of these destructive would train the confirmation
        // away.
        // Through the interface, because that is how the page sees them: both are default members, so a
        // class that never mentions them is still saying something.
        IDeviceAction ping = new PingAction();
        IDeviceAction open = new OpenManagementAction();

        Assert.IsFalse(ping.IsDestructive);
        Assert.IsFalse(open.IsDestructive);

        Assert.AreEqual(ProbeCost.Light, ping.Cost);
        Assert.AreEqual(ProbeCost.Passive, open.Cost,
            "opening a tab costs the network nothing — the shell does the connecting");
    }
}
