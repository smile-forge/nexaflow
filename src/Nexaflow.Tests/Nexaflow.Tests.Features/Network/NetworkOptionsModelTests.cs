using System.Net;
using System.Net.NetworkInformation;
using Nexaflow.Features.Network;
using Nexaflow.Features.Network.ViewModels;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// Options → Network's editor: what it shows, what Save writes, and what it leaves alone.
/// </summary>
[TestClass]
[CoversNode("network-guard-options")]
public class NetworkOptionsModelTests
{
    private static NetworkAdapterInfo Adapter(string id, string ip,
                                              NetworkInterfaceType type = NetworkInterfaceType.Ethernet)
    {
        var a = new NetworkAdapterInfo
        {
            Id = id, Name = id, Description = "Test adapter",
            Type = type, Status = OperationalStatus.Up, MacAddress = "aa:bb:cc:dd:ee:ff",
        };
        a.Addresses.Add(new AdapterAddress(IPAddress.Parse(ip), 24));
        return a;
    }

    [TestMethod]
    public void It_shows_what_the_config_says()
    {
        var config = new NetworkConfig
        {
            Enabled = false,
            ExcludedAdapterIds = ["wifi0"],
            SweepNetworks = [new SweepNetwork { AdapterId = "eth0", Segment = "192.168.1.0/24" }],
        };

        var model = new NetworkOptionsModel(config, [Adapter("eth0", "192.168.1.50"), Adapter("wifi0", "10.0.0.20")]);
        var eth = model.Adapters.Single(a => a.Id == "eth0");
        var wifi = model.Adapters.Single(a => a.Id == "wifi0");

        Assert.IsFalse(model.Enabled);
        Assert.IsTrue(eth.Use);
        Assert.IsTrue(eth.AllowSweep);
        Assert.IsFalse(wifi.Use);
        Assert.IsFalse(wifi.AllowSweep);
        Assert.IsFalse(model.HasChanges, "nothing has been edited yet");
    }

    [TestMethod]
    public void Loopback_and_tunnels_are_not_offered()
    {
        // Neither is ever a place the guard sends, so a switch for one would switch nothing.
        var model = new NetworkOptionsModel(new NetworkConfig(),
        [
            Adapter("lo", "127.0.0.1", NetworkInterfaceType.Loopback),
            Adapter("vpn", "10.8.0.2", NetworkInterfaceType.Tunnel),
            Adapter("eth0", "192.168.1.50"),
        ]);

        CollectionAssert.AreEqual(new[] { "eth0" }, model.Adapters.Select(a => a.Id).ToArray());
    }

    [TestMethod]
    public void A_sweep_permission_is_for_the_network_the_adapter_is_on_now()
    {
        var config = new NetworkConfig();
        var model = new NetworkOptionsModel(config, [Adapter("eth0", "192.168.1.50")]);

        model.Adapters.Single().AllowSweep = true;
        model.Apply();

        var granted = config.SweepNetworks.Single();
        Assert.AreEqual("eth0", granted.AdapterId);
        Assert.AreEqual("192.168.1.0/24", granted.Segment);
    }

    [TestMethod]
    public void A_permission_for_the_same_card_on_another_network_is_left_alone()
    {
        // Seen from home, the office permission is not this editor's to take away — nor to show as given.
        var config = new NetworkConfig
        {
            SweepNetworks = [new SweepNetwork { AdapterId = "wifi0", Segment = "10.0.0.0/24" }],
        };
        var model = new NetworkOptionsModel(config, [Adapter("wifi0", "192.168.1.60")]);

        Assert.IsFalse(model.Adapters.Single().AllowSweep, "home was never allowed");

        model.Adapters.Single().AllowSweep = true;
        model.Apply();

        CollectionAssert.AreEquivalent(new[] { "10.0.0.0/24", "192.168.1.0/24" },
                                       config.SweepNetworks.Select(n => n.Segment).ToArray());
    }

    [TestMethod]
    public void An_adapter_that_is_not_here_keeps_what_was_decided_about_it()
    {
        var config = new NetworkConfig { ExcludedAdapterIds = ["dock-eth"] };
        var model = new NetworkOptionsModel(config, [Adapter("eth0", "192.168.1.50")]);

        model.Adapters.Single().Use = false;
        model.Apply();

        CollectionAssert.AreEquivalent(new[] { "dock-eth", "eth0" }, config.ExcludedAdapterIds);
    }

    [TestMethod]
    public void A_sweep_cannot_be_allowed_through_an_adapter_that_is_not_used()
    {
        var config = new NetworkConfig();
        var model = new NetworkOptionsModel(config, [Adapter("eth0", "192.168.1.50")]);
        var row = model.Adapters.Single();

        row.AllowSweep = true;
        row.Use = false;
        Assert.IsFalse(row.SweepAvailable);

        model.Apply();
        Assert.AreEqual(0, config.SweepNetworks.Count,
            "nothing is sent through an adapter nobody uses, so nothing may sweep through it either");
    }

    [TestMethod]
    public void Save_writes_only_what_the_editor_shows()
    {
        // The page records which layers are on in the same config. An Options save that rewrote them would
        // switch layers back to the way they were when Options opened.
        var config = new NetworkConfig { LayerEnabled = { ["arp"] = false } };
        var model = new NetworkOptionsModel(config, [Adapter("eth0", "192.168.1.50")]);

        config.LayerEnabled["ssdp"] = true;   // the page, while Options is open
        model.Enabled = false;
        model.Apply();

        Assert.IsFalse(config.Enabled);
        Assert.IsFalse(config.LayerEnabled["arp"]);
        Assert.IsTrue(config.LayerEnabled["ssdp"]);
    }

    [TestMethod]
    public void A_limit_out_of_range_is_not_saved()
    {
        var config = new NetworkConfig();
        var model = new NetworkOptionsModel(config, []);

        model.MaxPacketsPerRun = "5";
        Assert.IsFalse(model.IsValid);
        Assert.IsTrue(model.ShowLimitsHint);

        model.Apply();
        Assert.AreEqual(512, config.MaxPacketsPerRun, "an invalid editor must not write anything");

        model.MaxPacketsPerRun = "300";
        model.MaxRunSeconds = "90";
        Assert.IsTrue(model.IsValid);
        model.Apply();

        Assert.AreEqual(300, config.MaxPacketsPerRun);
        Assert.AreEqual(90, config.MaxRunSeconds);
    }

    [TestMethod]
    public void Changes_are_tracked_until_they_are_saved()
    {
        var model = new NetworkOptionsModel(new NetworkConfig(), [Adapter("eth0", "192.168.1.50")]);
        int moved = 0;
        model.Changed += (_, _) => moved++;

        model.Adapters.Single().Use = false;
        Assert.IsTrue(model.HasChanges);
        Assert.IsTrue(moved > 0, "the Options panel is told, so Save can light up");

        model.Apply();
        Assert.IsFalse(model.HasChanges);
    }

    [TestMethod]
    public void A_config_edited_by_hand_cannot_lift_the_guard_past_what_Options_offers()
    {
        var limits = new NetworkConfig { MaxPacketsPerRun = 1_000_000, MaxRunSeconds = 1 }.Limits();

        Assert.AreEqual(NetworkConfig.MostPacketsPerRun, limits.MaxPacketsPerRun);
        Assert.AreEqual(TimeSpan.FromSeconds(NetworkConfig.ShortestRunSeconds), limits.MaxRunDuration);
    }
}
