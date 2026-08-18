using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Network.Ssdp;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// The seam: a probe that puts nothing on the wire itself.
/// </summary>
/// <remarks>
/// <para>
/// Every octet this probe sends is written by <c>GraphCodec</c> against <c>Protocols/ssdp.json</c>, and
/// every reply is read the same way. So these tests are the first thing to check that the DynamicProtocol
/// engine works <b>outside a test of itself</b> — a description loaded from a file beside a shipped
/// assembly, driven by a caller that knows nothing about graphs.
/// </para>
/// <para>
/// No socket. The transport is substituted, which is the containment story paying off: a probe cannot
/// reference a socket type, so replacing the transport replaces the whole of its reach.
/// </para>
/// </remarks>
[TestClass]
[CoversNode("network-discovery")]
public class SsdpProbeTests
{
    // ── A transport that never leaves the machine ─────────────────────────────

    private sealed class Recorded : IGuardedTransport
    {
        public SendIntent? Intent { get; private set; }
        public byte[] Sent { get; private set; } = [];
        public List<byte[]> Replies { get; } = [];

        /// <summary>Where each reply claims to come from; defaults to a device on the segment.</summary>
        public List<IPAddress> From { get; } = [];

        public async IAsyncEnumerable<ReceivedDatagram> SendAndCollectAsync(
            SendIntent intent, ReadOnlyMemory<byte> payload, TimeSpan window,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Intent = intent;
            Sent = payload.ToArray();

            for (int i = 0; i < Replies.Count; i++)
            {
                var from = i < From.Count ? From[i] : IPAddress.Parse("192.168.1.42");

                yield return new ReceivedDatagram(
                    new IPEndPoint(from, 1900), Replies[i], DateTimeOffset.UtcNow, "{TEST}");
                await Task.Yield();
            }
        }

        public Task<GuardDecision> SendUdpAsync(SendIntent i, ReadOnlyMemory<byte> p, CancellationToken ct)
            => Task.FromResult(GuardDecision.Allow());
        public IAsyncEnumerable<ReceivedDatagram> ListenMulticastAsync(IPAddress g, int port, string id, CancellationToken ct)
            => AsyncEnumerable.Empty<ReceivedDatagram>();
        public Task<IProtocolStream?> ConnectAsync(SendIntent i, TimeSpan t, CancellationToken ct, Action<GuardDecision>? d = null)
            => Task.FromResult<IProtocolStream?>(null);
        public Task<(bool Ok, TimeSpan Rtt)> PingAsync(IPAddress t, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult((false, TimeSpan.Zero));
        public Task<bool> TcpConnectAsync(IPAddress t, int port, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult(false);

        /// <summary>What a description fetch returns, when a test wants one.</summary>
        public string? Document { get; set; }

        public List<Uri> Fetched { get; } = [];

        public Task<FetchedDocument> FetchAsync(SendIntent i, Uri url, TimeSpan t, CancellationToken ct)
        {
            Fetched.Add(url);

            return Task.FromResult(Document is null
                ? FetchedDocument.Nothing("nothing served here")
                : new FetchedDocument(true, Encoding.UTF8.GetBytes(Document), "text/xml", ""));
        }
    }

    private sealed class Host(IGuardedTransport transport, Dictionary<string, string>? settings = null)
        : IProbeHost, IProbeLog
    {
        private readonly Dictionary<string, string> _settings = settings ?? [];
        public List<string> Said { get; } = [];

        public IReadOnlyList<NetworkAdapterInfo> Adapters => [Adapter()];
        public IGuardedTransport Transport => transport;
        public IProbeLog Log => this;
        public string Setting(string name) => _settings.GetValueOrDefault(name, "");
        public Task<string?> PromptAsync(ValuePrompt p, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string t, string m, CancellationToken ct) => Task.FromResult(false);
        public Task<ElevatedResult> RunElevatedAsync(ElevatedRequest r, CancellationToken ct)
            => throw new NotSupportedException();

        public void Info(string m) => Said.Add(m);
        public void Warn(string m) => Said.Add("warning: " + m);
        public void Error(string m, Exception? ex = null) => Said.Add("error: " + m + " " + ex?.Message);
    }

    private static NetworkAdapterInfo Adapter()
    {
        var a = new NetworkAdapterInfo
        {
            Id = "{TEST}", Name = "Test", Description = "Test adapter",
            Type = NetworkInterfaceType.Ethernet, Status = OperationalStatus.Up,
            MacAddress = "aa:bb:cc:dd:ee:ff",
        };
        a.Addresses.Add(new AdapterAddress(IPAddress.Parse("192.168.1.10"), 24));
        return a;
    }

    private const string Answer =
        "HTTP/1.1 200 OK\r\n" +
        "CACHE-CONTROL: max-age=1800\r\n" +
        "EXT:\r\n" +
        "LOCATION: http://192.168.1.42:49152/description.xml\r\n" +
        "SERVER: Linux/4.9 UPnP/1.1 MiniDLNA/1.3.0\r\n" +
        "ST: upnp:rootdevice\r\n" +
        "USN: uuid:4d696e69-444c-164e-9d41-b827eb2f1c3a::upnp:rootdevice\r\n" +
        "\r\n";

    private static async Task<(Recorded Wire, Host Host, List<ProbeObservation> Found)> Sweep(
        params string[] replies)
    {
        var wire = new Recorded();
        foreach (var r in replies) wire.Replies.Add(Encoding.ASCII.GetBytes(r));

        var host = new Host(wire, new() { ["describe"] = "false" });
        var probe = new SsdpProbe();
        probe.Attach(host);

        List<ProbeObservation> found = [];
        await foreach (var o in probe.DiscoverAsync(Adapter(), CancellationToken.None)) found.Add(o);

        return (wire, host, found);
    }

    // ── Out ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task The_search_it_sends_was_written_by_the_description()
    {
        // Not one octet of this is in C#. The probe hands over five headers and three start-line tokens;
        // ssdp.json decides that a header is a name, a colon, a value and a line ending, and that the run
        // of them stops at a line with nothing on it.
        var (wire, _, _) = await Sweep();

        Assert.AreEqual(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: ssdp:all\r\n" +
            "USER-AGENT: Windows/10.0 UPnP/1.1 Nexaflow/1.0\r\n" +
            "\r\n",
            Encoding.ASCII.GetString(wire.Sent));
    }

    [TestMethod]
    public async Task And_it_goes_where_UPnP_says_and_declares_itself_a_broadcast()
    {
        // The intent is what the guard judges, so it has to be honest before the guard sees it: this is a
        // multicast, and a send that did not say so would be evaluated as a unicast to an address no
        // adapter owns.
        var (wire, _, _) = await Sweep();

        Assert.AreEqual("239.255.255.250", wire.Intent!.Target.ToString());
        Assert.AreEqual(1900, wire.Intent.Port);
        Assert.IsTrue(wire.Intent.Broadcast);
        Assert.AreEqual(SendInitiator.Probe, wire.Intent.Initiator);
        Assert.AreEqual(wire.Sent.Length, wire.Intent.ByteCount);
    }

    [TestMethod]
    public async Task And_it_says_which_segment_it_is_searching()
    {
        // The bug this exists to stop coming back, and it was invisible: bound to the unspecified address
        // an M-SEARCH reaches nothing but this machine's own UPnP service, through the loopback copy that
        // multicast always generates. Six replies, a clean log, and not one device — a discovery that looks
        // exactly like a quiet network. Naming the adapter turned the same sweep into forty-four replies
        // from a television and a set-top box.
        var (wire, _, _) = await Sweep();

        Assert.AreEqual("192.168.1.10", wire.Intent!.Via?.ToString(),
            "the adapter's own address, so the datagram goes out the segment being swept");
    }

    [TestMethod]
    public async Task And_a_setting_the_user_changed_reaches_the_octets()
    {
        var wire = new Recorded();
        var probe = new SsdpProbe();
        probe.Attach(new Host(wire, new() { ["mx"] = "4", ["searchTarget"] = "upnp:rootdevice",
                                            ["describe"] = "false" }));

        await foreach (var _ in probe.DiscoverAsync(Adapter(), CancellationToken.None)) { }

        var sent = Encoding.ASCII.GetString(wire.Sent);
        StringAssert.Contains(sent, "MX: 4\r\n");
        StringAssert.Contains(sent, "ST: upnp:rootdevice\r\n");
    }

    // ── Back ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task A_reply_becomes_facts_about_a_device()
    {
        var (_, _, found) = await Sweep(Answer);

        Assert.AreEqual(1, found.Count);
        var obs = found[0];

        Assert.AreEqual("network.ssdp", obs.SourceProbe);
        Assert.AreEqual("192.168.1.42", obs.Identities.Single().Value,
            "SSDP carries no MAC, so an address is the whole of what it can claim — which is how this "
          + "layer's findings correlate onto ARP's");

        var facts = obs.Facts.ToDictionary(f => f.Key.ToString(), f => f.Value.Text);

        Assert.AreEqual("http://192.168.1.42:49152/description.xml", facts["svc.url"]);
        Assert.AreEqual("Linux/4.9 UPnP/1.1 MiniDLNA/1.3.0", facts["dev.firmware"]);
        Assert.AreEqual("upnp:rootdevice", facts["svc.type"]);
        Assert.AreEqual("4d696e69-444c-164e-9d41-b827eb2f1c3a", facts["dev.uuid"],
            "the uuid out of a USN, without the service it was joined to");
        Assert.AreEqual("true", facts["net.reachable"]);
    }

    [TestMethod]
    public async Task And_a_device_that_answered_is_reachable_because_it_answered()
    {
        // Stronger than the neighbour table's version of the same fact, and worth distinguishing: ARP says
        // the OS remembers this address, SSDP says the device spoke just now.
        var (_, _, found) = await Sweep(Answer);

        var reachable = found[0].Facts.Single(f => f.Key.Name == "reachable");

        Assert.AreEqual(Confidence.Asserted, reachable.Confidence);
        Assert.AreEqual("it replied", reachable.SourceDetail);
        Assert.IsNotNull(reachable.Ttl, "and it decays — a device that answered once is not always there");
    }

    [TestMethod]
    public async Task And_two_replies_are_two_observations()
    {
        var (_, _, found) = await Sweep(Answer, Answer.Replace("MiniDLNA/1.3.0", "OtherThing/2.0"));

        Assert.AreEqual(2, found.Count);
    }

    [TestMethod]
    public async Task And_something_this_does_not_describe_is_refused_rather_than_guessed_at()
    {
        // A NOTIFY arrives on the same socket — it is SSDP's other half, sent unsolicited, and reading it
        // as a search reply would invent a device that never answered anything.
        var (_, host, found) = await Sweep(
            "NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nNTS: ssdp:alive\r\n\r\n");

        Assert.AreEqual(0, found.Count);
        Assert.IsTrue(host.Said.Any(s => s.StartsWith("warning:", StringComparison.Ordinal)),
            "and it says which address sent something it could not read");
    }

    [TestMethod]
    public async Task And_this_machine_answering_its_own_search_is_not_a_discovery()
    {
        // Found by running it for real: Windows keeps a UPnP Device Host, so the loopback copy of our own
        // multicast comes straight back and it answers once per service it advertises. Six replies, all of
        // them us, listed as six devices on the network — the user's own machine among the things found.
        var wire = new Recorded();
        wire.Replies.Add(Encoding.ASCII.GetBytes(Answer));
        wire.Replies.Add(Encoding.ASCII.GetBytes(Answer));
        wire.Replies.Add(Encoding.ASCII.GetBytes(Answer));
        wire.From.Add(IPAddress.Loopback);
        wire.From.Add(IPAddress.Parse("192.168.1.10"));   // this adapter's own address
        wire.From.Add(IPAddress.Parse("192.168.1.42"));   // something else on the segment

        var probe = new SsdpProbe();
        probe.Attach(new Host(wire, new() { ["describe"] = "false" }));

        List<ProbeObservation> found = [];
        await foreach (var o in probe.DiscoverAsync(Adapter(), CancellationToken.None)) found.Add(o);

        Assert.AreEqual(1, found.Count, "only the reply from something that is not us");
        Assert.AreEqual("192.168.1.42", found[0].Identities.Single().Value);
    }

    [TestMethod]
    public async Task And_the_probe_says_what_it_did()
    {
        var (_, host, _) = await Sweep(Answer);

        Assert.IsTrue(host.Said.Any(s => s.Contains("1 reply", StringComparison.Ordinal)),
            $"a probe that found nothing has to be able to explain why. Said: {string.Join(" | ", host.Said)}");
    }

    [TestMethod]
    public async Task And_an_icon_is_fetched_here_rather_than_by_whatever_draws_it()
    {
        // The guard's whole claim is that nothing reaches the wire without it. Handing a device-supplied
        // address to a picture element would have the UI fetch it — off-segment, unbudgeted, unlogged, and
        // past the one component that exists to say no. So the octets travel with the facts.
        var wire = new Recorded { Document = Description };
        wire.Replies.Add(Encoding.ASCII.GetBytes(Answer));

        var probe = new SsdpProbe();
        probe.Attach(new Host(wire));

        List<ProbeObservation> found = [];
        await foreach (var o in probe.DiscoverAsync(Adapter(), CancellationToken.None)) found.Add(o);

        CollectionAssert.Contains(wire.Fetched.Select(u => u.ToString()).ToArray(),
            "http://192.168.1.42:49152/icon.png",
            "the icon was not fetched through the transport");

        var icon = found.SelectMany(o => o.Facts).Single(f => f.Key.Name == "icon");

        Assert.AreEqual(FactValueKind.Bytes, icon.Value.Kind,
            "an icon is a picture that travels with the facts, not an address for something else to chase");
        Assert.IsTrue(icon.Value.Bytes is { Length: > 0 });
    }

    /// <summary>The least a description can be while still carrying an icon.</summary>
    private const string Description = """
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <device>
            <friendlyName>wasabi</friendlyName>
            <manufacturer>Test</manufacturer>
            <iconList>
              <icon><mimetype>image/png</mimetype><width>98</width><height>55</height>
                    <depth>24</depth><url>/icon.png</url></icon>
            </iconList>
          </device>
        </root>
        """;

    // ── What it declares ──────────────────────────────────────────────────────

    [TestMethod]
    public void Asking_the_whole_segment_costs_more_than_reading_a_table()
    {
        // ARP is Passive because it reads a table the kernel keeps. This puts a datagram on the wire that
        // every device sees and raises a firewall prompt on first bind, so it must not claim the same.
        var probe = new SsdpProbe();

        Assert.AreEqual(ProbeCost.Light, probe.Cost);
        Assert.AreEqual("network.ssdp", probe.ProbeId);
        Assert.IsTrue(probe.AppliesTo(Adapter()));
    }
}
