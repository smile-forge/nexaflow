using Nexaflow.IO.Network.Adapters;
using Nexaflow.IO.Network.Guard;
using Nexaflow.Tests.Fixtures;
using System.Net;
using System.Net.NetworkInformation;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// Hostile fixtures against <see cref="NetworkGuard"/>.
///
/// <para>
/// Written deliberately <b>before</b> any real socket exists, because the guard is the one component whose
/// bugs are not recoverable: everything else fails by not working, this fails by sending something it
/// should not have. Every case here is a thing an LLM-authored protocol document would plausibly attempt.
/// </para>
///
/// <para>The guard is a pure function of (intent, budget), which is what lets all of this run with no
/// networking whatsoever.</para>
/// </summary>
[TestClass]
[NoCoverage("send-guard containment — tree nodes land with the Network feature page")]
public class NetworkGuardTests
{
    // 192.168.1.50/24 on a plain Ethernet adapter: prefix 192.168.1.0/24, broadcast 192.168.1.255.
    private static NetworkAdapterInfo LocalAdapter()
    {
        var a = new NetworkAdapterInfo
        {
            Id = "eth0", Name = "Ethernet", Description = "Test adapter",
            Type = NetworkInterfaceType.Ethernet, Status = OperationalStatus.Up,
            MacAddress = "aa:bb:cc:dd:ee:ff", SpeedBitsPerSecond = 1_000_000_000,
        };
        a.Addresses.Add(new AdapterAddress(IPAddress.Parse("192.168.1.50"), 24));
        a.Gateways.Add(IPAddress.Parse("192.168.1.1"));
        return a;
    }

    private static NetworkGuard Guard(GuardLimits? limits = null)
    {
        var g = new NetworkGuard(limits);
        g.SetAdapters([LocalAdapter()]);
        return g;
    }

    private static SendIntent To(string ip, int port = 9, SendInitiator who = SendInitiator.Probe,
                                 bool broadcast = false, SendLayer layer = SendLayer.Udp, int bytes = 102)
        => new()
        {
            Target = IPAddress.Parse(ip), Port = port, Layer = layer, ByteCount = bytes,
            Initiator = who, Broadcast = broadcast, SourceId = "test",
        };

    // ── Target allow-list ─────────────────────────────────────────────────────

    [TestMethod]
    public void Allows_a_unicast_target_on_a_locally_attached_prefix()
    {
        var d = Guard().Evaluate(To("192.168.1.77"), new RunBudget());
        Assert.IsTrue(d.Allowed, d.Reason);
    }

    [TestMethod]
    public void Refuses_a_public_address_the_model_invented()
    {
        var d = Guard().Evaluate(To("8.8.8.8", 53), new RunBudget());
        Assert.IsFalse(d.Allowed);
        Assert.AreEqual(GuardRefusal.TargetNotLocal, d.Refusal);
    }

    [TestMethod]
    public void Refuses_an_off_link_private_address()
    {
        // RFC1918 but on a prefix we are not attached to — "private" is not the test, "attached" is.
        var d = Guard().Evaluate(To("10.9.9.9"), new RunBudget());
        Assert.IsFalse(d.Allowed);
        Assert.AreEqual(GuardRefusal.TargetNotLocal, d.Refusal);
    }

    [TestMethod]
    public void Refuses_loopback_so_the_app_cannot_be_made_to_talk_to_itself()
    {
        // Closes reaching a local dev server, an MCP endpoint, or the privilege bridge's pipe.
        foreach (var target in (string[])["127.0.0.1", "127.0.0.53", "::1"])
        {
            var d = Guard().Evaluate(To(target, 8080), new RunBudget());
            Assert.IsFalse(d.Allowed, $"{target} must be refused");
            Assert.AreEqual(GuardRefusal.TargetIsLocalMachine, d.Refusal, target);
        }
    }

    [TestMethod]
    public void Refuses_this_machines_own_address()
    {
        var d = Guard().Evaluate(To("192.168.1.50", 445), new RunBudget());
        Assert.IsFalse(d.Allowed);
        Assert.AreEqual(GuardRefusal.TargetIsLocalMachine, d.Refusal);
    }

    [TestMethod]
    public void Allows_a_target_the_user_typed_themselves()
    {
        var g = Guard();
        Assert.IsFalse(g.Evaluate(To("10.9.9.9"), new RunBudget()).Allowed);

        g.ApproveUserTarget(IPAddress.Parse("10.9.9.9"));
        Assert.IsTrue(g.Evaluate(To("10.9.9.9"), new RunBudget()).Allowed,
            "a user-entered target is the one way the allow-list widens — and only a user gesture can do it");
    }

    // ── Broadcast ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Allows_the_directed_broadcast_of_a_local_prefix()
    {
        // Wake-on-LAN's actual send.
        var d = Guard().Evaluate(To("192.168.1.255", 9, broadcast: true), new RunBudget());
        Assert.IsTrue(d.Allowed, d.Reason);
    }

    [TestMethod]
    public void Refuses_broadcast_to_a_prefix_we_are_not_on()
    {
        var d = Guard().Evaluate(To("10.0.0.255", 9, broadcast: true), new RunBudget());
        Assert.IsFalse(d.Allowed);
        Assert.AreEqual(GuardRefusal.Forbidden, d.Refusal);
    }

    [TestMethod]
    public void Refuses_the_limited_broadcast_address()
    {
        // 255.255.255.255 is not attributable to a segment, so it can never be budgeted or audited properly.
        var d = Guard().Evaluate(To("255.255.255.255", 9, broadcast: true), new RunBudget());
        Assert.IsFalse(d.Allowed);
    }

    [TestMethod]
    public void Allows_link_local_multicast_for_discovery()
    {
        foreach (var group in (string[])["224.0.0.251", "224.0.0.252"])   // mDNS, LLMNR
            Assert.IsTrue(Guard().Evaluate(To(group, 5353, broadcast: true), new RunBudget()).Allowed, group);
    }

    [TestMethod]
    public void An_unreviewed_draft_may_never_broadcast()
    {
        var d = Guard().Evaluate(
            To("192.168.1.255", 9, who: SendInitiator.AiDraft, broadcast: true), new RunBudget());

        Assert.IsFalse(d.Allowed);
        Assert.AreEqual(GuardRefusal.DraftRestriction, d.Refusal);
        StringAssert.Contains(d.Reason, "review",
            "the refusal must tell the user what would make it work, or it reads as a bug");
    }

    // ── Layer ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Refuses_raw_ip_and_ethernet_layers()
    {
        foreach (var layer in (SendLayer[])[SendLayer.RawIp, SendLayer.Ethernet])
        {
            var d = Guard().Evaluate(To("192.168.1.77", 0, layer: layer), new RunBudget());
            Assert.IsFalse(d.Allowed, $"{layer} must be refused");
            Assert.AreEqual(GuardRefusal.LayerNotPermitted, d.Refusal);
        }
    }

    // ── Budgets ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enforces_the_per_run_packet_ceiling()
    {
        var g = Guard(new GuardLimits { MaxPacketsPerRun = 3, MaxPacketsPerSecondPerTarget = 1000 });
        var budget = new RunBudget();

        for (int i = 0; i < 3; i++)
        {
            var intent = To("192.168.1.77");
            Assert.IsTrue(g.Evaluate(intent, budget).Allowed, $"packet {i} should be allowed");
            budget.Record(intent);
        }

        var refused = g.Evaluate(To("192.168.1.77"), budget);
        Assert.IsFalse(refused.Allowed);
        Assert.AreEqual(GuardRefusal.RunBudgetExceeded, refused.Refusal);
    }

    [TestMethod]
    public void A_draft_gets_a_far_smaller_packet_budget_than_a_probe()
    {
        var g = Guard(new GuardLimits
        {
            MaxPacketsPerRun = 500, MaxPacketsPerDraftRun = 2, MaxPacketsPerSecondPerTarget = 1000,
        });
        var budget = new RunBudget();

        for (int i = 0; i < 2; i++)
        {
            var intent = To("192.168.1.77", who: SendInitiator.AiDraft);
            Assert.IsTrue(g.Evaluate(intent, budget).Allowed);
            budget.Record(intent);
        }

        Assert.IsFalse(g.Evaluate(To("192.168.1.77", who: SendInitiator.AiDraft), budget).Allowed,
            "a draft is capped well below the ordinary run budget");
        Assert.IsTrue(g.Evaluate(To("192.168.1.77", who: SendInitiator.User), budget).Allowed,
            "the same budget still has room for a user-initiated send");
    }

    [TestMethod]
    public void Enforces_the_per_run_byte_ceiling()
    {
        var g = Guard(new GuardLimits { MaxBytesPerRun = 1000, MaxPacketsPerSecondPerTarget = 1000 });
        var budget = new RunBudget();

        var big = To("192.168.1.77", bytes: 900);
        Assert.IsTrue(g.Evaluate(big, budget).Allowed);
        budget.Record(big);

        Assert.IsFalse(g.Evaluate(To("192.168.1.77", bytes: 900), budget).Allowed);
    }

    [TestMethod]
    public void Rate_limits_a_single_target()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var g = Guard(new GuardLimits { MaxPacketsPerSecondPerTarget = 2 });
        var budget = new RunBudget(() => now);          // frozen clock: no sleeping in a rate-limit test

        for (int i = 0; i < 2; i++)
        {
            var intent = To("192.168.1.77");
            Assert.IsTrue(g.Evaluate(intent, budget).Allowed);
            budget.Record(intent);
        }

        var refused = g.Evaluate(To("192.168.1.77"), budget);
        Assert.IsFalse(refused.Allowed);
        Assert.AreEqual(GuardRefusal.RateLimited, refused.Refusal);
    }

    [TestMethod]
    public void A_refused_send_does_not_consume_budget()
    {
        // Otherwise one refusal cascades into refusing everything after it.
        var g = Guard(new GuardLimits { MaxPacketsPerRun = 1, MaxPacketsPerSecondPerTarget = 1000 });
        var budget = new RunBudget();

        Assert.IsFalse(g.Evaluate(To("8.8.8.8"), budget).Allowed);
        Assert.AreEqual(0, budget.Packets);
        Assert.IsTrue(g.Evaluate(To("192.168.1.77"), budget).Allowed);
    }

    // ── Kill switch ───────────────────────────────────────────────────────────

    [TestMethod]
    public void The_kill_switch_refuses_everything_including_discovery()
    {
        var g = Guard();
        g.Enabled = false;

        foreach (var who in (SendInitiator[])[SendInitiator.Probe, SendInitiator.User,
                                              SendInitiator.ReviewedDocument, SendInitiator.AiDraft])
        {
            var d = g.Evaluate(To("192.168.1.77", who: who), new RunBudget());
            Assert.IsFalse(d.Allowed, $"{who} must be refused when disabled");
            Assert.AreEqual(GuardRefusal.Disabled, d.Refusal);
        }
    }

    // ── Adapter exclusion ─────────────────────────────────────────────────────

    [TestMethod]
    public void Excluding_an_adapter_removes_its_prefix_from_the_allow_list()
    {
        // Hiding a VPN/Hyper-V adapter from the UI and making it not-a-legal-target must be one decision;
        // if they diverge, "excluded" becomes cosmetic.
        var g = Guard();
        Assert.IsTrue(g.Evaluate(To("192.168.1.77"), new RunBudget()).Allowed);

        g.SetAdapters([]);
        var d = g.Evaluate(To("192.168.1.77"), new RunBudget());
        Assert.IsFalse(d.Allowed);
        Assert.AreEqual(GuardRefusal.TargetNotLocal, d.Refusal);
    }
}
