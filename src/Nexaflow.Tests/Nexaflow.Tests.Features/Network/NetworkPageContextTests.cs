using Nexaflow.Features.Common;
using Nexaflow.Features.Network;
using Nexaflow.Features.Network.ViewModels;
using Nexaflow.IO.Network.Model;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Network;

/// <summary>
/// What the Network page tells the assistant: each device it lists, as the user sees it, and the device open in the panel.
/// </summary>
[TestClass]
[CoversNode("network-ai")]
public class NetworkPageContextTests
{
    private static NetworkViewModel Page() => new([], [], new NetworkConfig(), Substitute.For<IShellServices>());

    private static DeviceRow Device(string name, Presence presence, bool isNew = false) => new()
    {
        Name = name,
        Icon = null,
        IPv4 = ["192.168.1.20"],
        IPv6 = [],
        Mac = "aa:bb:cc:dd:ee:01",
        Detail = "Printer",
        FoundBy = "ARP",
        IsNew = isNew,
        Presence = presence,
        Node = new DeviceNode { Id = name },
    };

    [TestMethod]
    public void APageWithNothingFound_SaysSo()
    {
        StringAssert.Contains(Page().GetContext(), "No devices found yet.");
    }

    [TestMethod]
    public void EachDevice_IsDescribedWithItsAddresses_AndWhetherItIsStillThere()
    {
        var page = Page();
        page.Devices.Add(Device("office-printer", Presence.Present, isNew: true));
        page.Devices.Add(Device("old-laptop", Presence.Absent));

        var context = page.GetContext();

        StringAssert.Contains(context, "2 device(s):");
        StringAssert.Contains(context, "office-printer; IPv4 192.168.1.20; MAC aa:bb:cc:dd:ee:01; Printer; found by ARP; present, new");
        StringAssert.Contains(context, "old-laptop; IPv4 192.168.1.20; MAC aa:bb:cc:dd:ee:01; Printer; found by ARP; absent");
    }
}
