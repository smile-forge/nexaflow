using Nexaflow.IO.Network.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// Correlation rules for <see cref="DeviceGraph"/> — the model everything else binds to.
///
/// <para>
/// These encode the two decisions that are expensive to get wrong, because node ids are referenced by
/// actions, protocol runs and the audit log: <b>two different MACs never merge</b> (a router's interfaces
/// stay separate nodes joined by an edge), and <b>an IP seen with different hardware is rebound, not
/// merged</b> (DHCP churn must never fuse two devices).
/// </para>
/// </summary>
[TestClass]
[NoCoverage("device-graph correlation — tree nodes land with the Network feature page")]
public class DeviceGraphTests
{
    private const string Seg = "192.168.1.0/24";
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ProbeObservation Obs(string probe, DateTimeOffset when) => new() { SourceProbe = probe, ObservedUtc = when };

    private static void Mac(ProbeObservation o, string mac)
        => o.Identities.Add(new IdentityClaim(IdentityKind.Mac, mac, Seg, Confidence.Asserted));

    private static void Ip(ProbeObservation o, string ip)
        => o.Identities.Add(new IdentityClaim(IdentityKind.Ip, ip, Seg, Confidence.Strong));

    private static void Fact(ProbeObservation o, string ns, string name, string value,
                             Confidence c = Confidence.Strong)
        => o.Facts.Add(new DeviceFact
        {
            Key = new FactKey(ns, name), Value = FactValue.OfText(value),
            SourceProbe = o.SourceProbe, ObservedUtc = o.ObservedUtc, Confidence = c,
        });

    // ── Basic correlation ─────────────────────────────────────────────────────

    [TestMethod]
    public void Two_probes_seeing_the_same_mac_land_on_one_node()
    {
        var g = new DeviceGraph();

        var arp = Obs("network.arp", T0);
        Mac(arp, "aa:bb:cc:00:00:01"); Ip(arp, "192.168.1.10");
        Fact(arp, "link", "mac", "aa:bb:cc:00:00:01", Confidence.Asserted);
        g.Observe(arp);

        var mdns = Obs("network.mdns", T0.AddSeconds(1));
        Mac(mdns, "aa:bb:cc:00:00:01");
        Fact(mdns, "name", "hostname", "printer.local");
        g.Observe(mdns);

        Assert.AreEqual(1, g.Nodes.Count, "the same MAC is the same device");
        var node = g.Nodes.Single();
        Assert.AreEqual("printer.local", node.Best(new FactKey("name", "hostname"))?.Value.Text);
        Assert.AreEqual(2, node.Facts.Count(f => f.SourceProbe is "network.arp" or "network.mdns"
                                                 && f.SupersededUtc is null) >= 2 ? 2 : 0,
            "both probes' facts survive on the merged node");
    }

    [TestMethod]
    public void A_hostname_only_observation_correlates_through_a_shared_ip()
    {
        var g = new DeviceGraph();

        var arp = Obs("network.arp", T0);
        Mac(arp, "aa:bb:cc:00:00:02"); Ip(arp, "192.168.1.20");
        Fact(arp, "link", "mac", "aa:bb:cc:00:00:02", Confidence.Asserted);
        g.Observe(arp);

        var ssdp = Obs("network.ssdp", T0.AddSeconds(2));
        Ip(ssdp, "192.168.1.20");
        ssdp.Identities.Add(new IdentityClaim(IdentityKind.Uuid, "uuid:1234", "", Confidence.Strong));
        Fact(ssdp, "dev", "model", "SuperNAS 9000");
        g.Observe(ssdp);

        Assert.AreEqual(1, g.Nodes.Count);
        Assert.AreEqual("SuperNAS 9000", g.Nodes.Single().Best(new FactKey("dev", "model"))?.Value.Text);
    }

    // ── The rule that matters most ────────────────────────────────────────────

    [TestMethod]
    public void Two_different_macs_never_merge_even_when_another_key_is_shared()
    {
        // A router's LAN and WLAN interfaces, both publishing the same UPnP uuid. Merging would destroy
        // the distinction and point an action at whichever interface won.
        var g = new DeviceGraph();

        var lan = Obs("network.arp", T0);
        Mac(lan, "aa:bb:cc:00:00:10"); Ip(lan, "192.168.1.1");
        Fact(lan, "link", "mac", "aa:bb:cc:00:00:10", Confidence.Asserted);
        g.Observe(lan);

        var wlan = Obs("network.arp", T0.AddSeconds(1));
        Mac(wlan, "aa:bb:cc:00:00:11"); Ip(wlan, "192.168.1.2");
        Fact(wlan, "link", "mac", "aa:bb:cc:00:00:11", Confidence.Asserted);
        g.Observe(wlan);

        // Now something claims both identities at once.
        var upnp = Obs("network.ssdp", T0.AddSeconds(2));
        Mac(upnp, "aa:bb:cc:00:00:10");
        Mac(upnp, "aa:bb:cc:00:00:11");
        upnp.Identities.Add(new IdentityClaim(IdentityKind.Uuid, "uuid:router", "", Confidence.Strong));
        g.Observe(upnp);

        Assert.AreEqual(2, g.Nodes.Count, "distinct MACs stay distinct nodes");
        Assert.IsTrue(g.Edges.Any(e => e.Kind == EdgeKind.SameDevice),
            "the relationship is recorded as an edge — reversible — rather than as an irreversible merge");
    }

    [TestMethod]
    public void An_ip_that_moves_to_new_hardware_is_rebound_not_merged()
    {
        var g = new DeviceGraph();

        var first = Obs("network.arp", T0);
        Mac(first, "aa:bb:cc:00:00:20"); Ip(first, "192.168.1.30");
        Fact(first, "link", "mac", "aa:bb:cc:00:00:20", Confidence.Asserted);
        first.Facts.Add(new DeviceFact
        {
            Key = new FactKey("net", "ipv4"), Value = FactValue.OfAddress("192.168.1.30"),
            SourceProbe = "network.arp", ObservedUtc = T0, Confidence = Confidence.Strong,
        });
        g.Observe(first);

        // Same address, different hardware — the DHCP lease moved.
        var second = Obs("network.arp", T0.AddHours(4));
        Mac(second, "aa:bb:cc:00:00:21"); Ip(second, "192.168.1.30");
        Fact(second, "link", "mac", "aa:bb:cc:00:00:21", Confidence.Asserted);
        second.Facts.Add(new DeviceFact
        {
            Key = new FactKey("net", "ipv4"), Value = FactValue.OfAddress("192.168.1.30"),
            SourceProbe = "network.arp", ObservedUtc = T0.AddHours(4), Confidence = Confidence.Strong,
        });
        g.Observe(second);

        Assert.AreEqual(2, g.Nodes.Count, "a DHCP rebind must never fuse two devices");

        var old = g.Nodes.Single(n => n.Identities.Any(i => i.Value == "aa:bb:cc:00:00:20"));
        Assert.IsNull(old.Best(new FactKey("net", "ipv4")),
            "the previous holder no longer claims the address as live");
        Assert.IsTrue(old.Facts.Any(f => f.Key.Equals(new FactKey("net", "ipv4")) && f.SupersededUtc is not null),
            "…but it is kept as superseded history, not deleted — the rebind must stay auditable");

        var now = g.Nodes.Single(n => n.Identities.Any(i => i.Value == "aa:bb:cc:00:00:21"));
        Assert.AreEqual("192.168.1.30", now.Best(new FactKey("net", "ipv4"))?.Value.Text);
    }

    // ── Provenance ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Conflicting_values_are_all_kept_and_the_most_confident_wins()
    {
        var g = new DeviceGraph();

        var weak = Obs("network.ssdp", T0);
        Mac(weak, "aa:bb:cc:00:00:30");
        Fact(weak, "name", "hostname", "guessed-name", Confidence.Guess);
        g.Observe(weak);

        var strong = Obs("network.mdns", T0.AddSeconds(1));
        Mac(strong, "aa:bb:cc:00:00:30");
        Fact(strong, "name", "hostname", "real-name.local", Confidence.Asserted);
        g.Observe(strong);

        var node = g.Nodes.Single();
        var key = new FactKey("name", "hostname");

        Assert.AreEqual("real-name.local", node.Best(key)?.Value.Text);
        Assert.AreEqual(2, node.SourceCount(key), "both sources are retained — that IS the provenance");
        Assert.IsTrue(node.IsContested(key), "a disagreement surfaces rather than being squashed");
    }

    [TestMethod]
    public void A_probe_repeating_itself_refreshes_rather_than_accumulating()
    {
        var g = new DeviceGraph();

        for (int i = 0; i < 5; i++)
        {
            var o = Obs("network.arp", T0.AddSeconds(i));
            Mac(o, "aa:bb:cc:00:00:40");
            Fact(o, "link", "mac", "aa:bb:cc:00:00:40", Confidence.Asserted);
            g.Observe(o);
        }

        var node = g.Nodes.Single();
        Assert.AreEqual(1, node.Facts.Count(f => f.Key.Equals(new FactKey("link", "mac"))),
            "re-observing the same value must not grow the fact list without bound");
        Assert.AreEqual(T0.AddSeconds(4), node.Best(new FactKey("link", "mac"))!.ObservedUtc);
    }

    [TestMethod]
    public void An_unknown_fact_key_is_surfaced_rather_than_dropped()
    {
        var g = new DeviceGraph();
        var o = Obs("network.custom", T0);
        Mac(o, "aa:bb:cc:00:00:50");
        Fact(o, "vendorx", "secretSauce", "42");
        g.Observe(o);

        var node = g.Nodes.Single();
        var fact = node.Facts.Single(f => f.Key.Name == "secretSauce");
        Assert.AreEqual("42", fact.Value.Text);
        Assert.AreEqual(FactOntology.LayerOther, fact.Layer,
            "an undeclared key renders under 'other' — never dropped, never treated as first-class");
    }

    // ── Presence and newness ──────────────────────────────────────────────────

    [TestMethod]
    public void A_device_that_stops_answering_becomes_absent_but_is_never_deleted()
    {
        var g = new DeviceGraph();
        var o = Obs("network.arp", T0);
        Mac(o, "aa:bb:cc:00:00:60");
        g.Observe(o);

        g.MarkAbsentExcept([], T0.AddHours(1));

        Assert.AreEqual(1, g.Nodes.Count, "a sleeping laptop must not vanish and re-appear as a new device");
        Assert.AreEqual(Presence.Absent, g.Nodes.Single().Presence);
    }

    [TestMethod]
    public void The_ledger_distinguishes_a_genuinely_new_key_from_a_returning_one()
    {
        var g = new DeviceGraph();
        var claim = new IdentityClaim(IdentityKind.Mac, "aa:bb:cc:00:00:70", Seg, Confidence.Asserted);

        Assert.IsTrue(g.Ledger.IsUnknown(claim), "never seen before");

        var o = Obs("network.arp", T0);
        o.Identities.Add(claim);
        g.Observe(o);

        Assert.IsFalse(g.Ledger.IsUnknown(claim),
            "…and now known, which is what makes the new-device alert truthful rather than a cache miss");
    }

    // ── Edges ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void An_edge_to_an_undiscovered_gateway_creates_a_stub_rather_than_a_dangling_reference()
    {
        var g = new DeviceGraph();

        var o = Obs("network.arp", T0);
        Mac(o, "aa:bb:cc:00:00:80"); Ip(o, "192.168.1.90");
        o.Edges.Add(new ProbeObservation.PendingEdge(
            new IdentityClaim(IdentityKind.Ip, "192.168.1.1", Seg, Confidence.Strong),
            EdgeKind.DefaultGateway, Confidence.Strong));
        g.Observe(o);

        Assert.AreEqual(2, g.Nodes.Count, "the gateway is real even before a probe describes it");
        Assert.IsTrue(g.Edges.Any(e => e.Kind == EdgeKind.DefaultGateway));
    }

    [TestMethod]
    public void A_node_id_captured_before_a_merge_still_resolves_afterwards()
    {
        // Actions and audit records hold node ids; a merge must not dangle them.
        var g = new DeviceGraph();

        var byUuid = Obs("network.ssdp", T0);
        byUuid.Identities.Add(new IdentityClaim(IdentityKind.Uuid, "uuid:abc", "", Confidence.Strong));
        var first = g.Observe(byUuid)!;
        var capturedId = first.Id;

        var byMac = Obs("network.arp", T0.AddSeconds(1));
        Mac(byMac, "aa:bb:cc:00:00:90");
        Fact(byMac, "link", "mac", "aa:bb:cc:00:00:90", Confidence.Asserted);
        g.Observe(byMac);

        // Something ties them together.
        var both = Obs("network.ssdp", T0.AddSeconds(2));
        Mac(both, "aa:bb:cc:00:00:90");
        both.Identities.Add(new IdentityClaim(IdentityKind.Uuid, "uuid:abc", "", Confidence.Strong));
        g.Observe(both);

        Assert.AreEqual(1, g.Nodes.Count);
        Assert.IsNotNull(g.Find(capturedId), "the pre-merge id must still resolve, via the alias chain");
    }
}
