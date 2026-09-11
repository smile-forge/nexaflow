using System.Net;
using Nexaflow.IO.Network.Adapters;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Network;

/// <summary>The addresses on a prefix — what an address sweep walks, and what it must not.</summary>
[TestClass]
[CoversNode("network-discovery-sweep")]
public class AdapterAddressTests
{
    private static AdapterAddress At(string ip, int prefix) => new(IPAddress.Parse(ip), prefix);

    [TestMethod]
    public void A_24_has_254_hosts_and_neither_end()
    {
        var subnet = At("192.168.1.50", 24);
        var hosts = subnet.Hosts().ToList();

        Assert.AreEqual(254, subnet.HostCount);
        Assert.AreEqual(254, hosts.Count);
        Assert.AreEqual(IPAddress.Parse("192.168.1.1"), hosts[0]);
        Assert.AreEqual(IPAddress.Parse("192.168.1.254"), hosts[^1]);
    }

    [TestMethod]
    public void The_count_is_what_the_walk_yields()
    {
        // The sweep refuses a subnet by its count before walking it, so the two have to agree.
        foreach (var prefix in new[] { 22, 24, 28, 30, 31, 32 })
        {
            var subnet = At("10.1.2.3", prefix);
            Assert.AreEqual(subnet.HostCount, subnet.Hosts().LongCount(), $"/{prefix}");
        }
    }

    [TestMethod]
    public void In_a_31_or_a_32_every_address_is_a_host()
    {
        CollectionAssert.AreEqual(new[] { IPAddress.Parse("10.0.0.4"), IPAddress.Parse("10.0.0.5") },
                                  At("10.0.0.5", 31).Hosts().ToArray());
        CollectionAssert.AreEqual(new[] { IPAddress.Parse("10.0.0.5") }, At("10.0.0.5", 32).Hosts().ToArray());
    }

    [TestMethod]
    public void Nobody_sweeps_IPv6()
    {
        var link = At("fe80::1", 64);

        Assert.AreEqual(0, link.HostCount);
        Assert.AreEqual(0, link.Hosts().Count());
    }

    [TestMethod]
    public void The_network_address_is_the_one_below_the_first_host()
    {
        Assert.AreEqual(IPAddress.Parse("192.168.1.0"), At("192.168.1.50", 24).Network);
        Assert.IsNull(At("fe80::1", 64).Network);
    }
}
