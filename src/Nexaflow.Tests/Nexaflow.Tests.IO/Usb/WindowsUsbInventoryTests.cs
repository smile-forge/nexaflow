using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// The inventory against this machine's real USB tree. It only reads — no privilege, no driver change, no
/// non-standard request — so it is safe anywhere, and inconclusive on a machine with no USB hubs.
/// </summary>
[TestClass]
[CoversNode("io-usb-inventory")]
public class WindowsUsbInventoryTests
{
    [TestMethod]
    public async Task Pass_DescribesEveryAttachedDevice_Unprivileged()
    {
        using var inventory = new WindowsUsbInventory(UsbIdsDatabase.LoadBundled());
        var snapshot = await inventory.RefreshAsync(CancellationToken.None);
        if (snapshot.Hubs.Count == 0) Assert.Inconclusive("this machine exposes no USB hubs");

        Assert.IsTrue(snapshot.Hubs.All(h => h.OpenError is null), string.Join("; ", snapshot.Hubs.Select(h => h.OpenError).OfType<string>()));
        foreach (var device in snapshot.Devices)
        {
            Assert.IsTrue(device.Location.PortChain.Count >= 1 && device.Location.Bus >= 1, $"{device.InstanceId} has no place in the tree");
            Assert.IsTrue(device.Configurations.Count >= 1, $"{device.InstanceId}: the hub serves configurations from its cache whatever the power state");
            Assert.IsTrue(device.Functions.Count >= 1 || device.Driver.Service is null, $"{device.InstanceId} has a driver but no function");
            foreach (var function in device.Functions)
                Assert.IsTrue(function.InterfaceNumbers.Count >= 1, $"{function.InstanceId} owns no interface");
        }
    }

    [TestMethod]
    public async Task WarmPass_ReusesDescriptors_AndReportsNoChange()
    {
        using var inventory = new WindowsUsbInventory(null);
        var first = await inventory.RefreshAsync(CancellationToken.None);
        if (first.Devices.Count == 0) Assert.Inconclusive("no USB devices attached");

        var changes = new List<UsbInventoryChange>();
        inventory.Changed += (_, e) => changes.AddRange(e.Changes);
        var started = Environment.TickCount64;
        var second = await inventory.RefreshAsync(CancellationToken.None);
        long warmMs = Environment.TickCount64 - started;

        Assert.AreEqual(first.Devices.Count, second.Devices.Count);
        Assert.AreEqual(0, changes.Count(c => c.Kind != UsbInventoryChangeKind.Updated), string.Join("; ", changes.Select(c => $"{c.Kind} {c.InstanceId}")));
        Assert.IsTrue(warmMs < 1000, $"a warm pass took {warmMs} ms — spec D1 wants changes shown within a second");
    }
}
