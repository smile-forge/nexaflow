using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.IO.Usb.UsbFixtures;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-changes")]
public class UsbInventoryDiffTests
{
    static UsbInventorySnapshot Snapshot(params UsbDeviceNode[] devices) => new(DateTimeOffset.Now, devices, [], []);

    [TestMethod]
    public void ArrivalAndRemoval()
    {
        var lg = LgMonitor();
        var arrived = UsbInventoryDiff.Between(UsbInventorySnapshot.Empty, Snapshot(lg)).Single();
        Assert.AreEqual((UsbInventoryChangeKind.Arrived, lg.InstanceId), (arrived.Kind, arrived.InstanceId));
        Assert.AreSame(lg, arrived.Device);

        var removed = UsbInventoryDiff.Between(Snapshot(lg), UsbInventorySnapshot.Empty).Single();
        Assert.AreEqual(UsbInventoryChangeKind.Removed, removed.Kind);
        Assert.IsNull(removed.Device);
    }

    [TestMethod]
    public void TakeOver_IsADriverChange()
    {
        var before = LgMonitor();
        var after = LgMonitor(Driver("WINUSB", "winusb.inf"));
        var change = UsbInventoryDiff.Between(Snapshot(before), Snapshot(after)).Single();
        Assert.AreEqual(UsbInventoryChangeKind.DriverChanged, change.Kind);
        Assert.AreEqual(UsbFunctionMode.GenericRaw, change.Device!.Functions[1].Mode);
    }

    [TestMethod]
    public void StringsReadOnWaking_IsAnUpdate()
    {
        var asleep = LgMonitor() with { Strings = UsbDeviceStrings.Unread, Power = UsbPowerState.D3 };
        var awake = LgMonitor();
        Assert.AreEqual(UsbInventoryChangeKind.Updated, UsbInventoryDiff.Between(Snapshot(asleep), Snapshot(awake)).Single().Kind);
    }

    [TestMethod]
    public void NamesFromANewerUsbIds_AreAnUpdate()
    {
        var before = LgMonitor();
        var after = LgMonitor() with { VendorName = "LG Electronics, Inc." };
        Assert.AreEqual(UsbInventoryChangeKind.Updated, UsbInventoryDiff.Between(Snapshot(before), Snapshot(after)).Single().Kind);
    }

    [TestMethod]
    public void PowerStateAlone_IsNotAChange()
    {
        var a = LgMonitor();
        var b = a with { Power = UsbPowerState.D2 };
        Assert.AreEqual(0, UsbInventoryDiff.Between(Snapshot(a), Snapshot(b)).Count);
    }
}
