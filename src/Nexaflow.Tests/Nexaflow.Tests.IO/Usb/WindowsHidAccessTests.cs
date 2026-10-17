using Nexaflow.IO.Usb.Hid;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// HID against this machine's real devices. Reading a layout opens a handle with no access rights and sends nothing to
/// the device, so it is safe anywhere; inconclusive on a machine with no USB HID device.
/// </summary>
[TestClass]
[CoversNode("io-usb-hid")]
public class WindowsHidAccessTests
{
    static async Task<List<HidCollection>> CollectionsOnThisMachineAsync()
    {
        var names = UsbIdsDatabase.LoadBundled();
        using var inventory = new WindowsUsbInventory(names);
        var snapshot = await inventory.RefreshAsync(CancellationToken.None);
        var access = new WindowsHidAccess();
        return [.. snapshot.Devices
            .SelectMany(d => d.Functions)
            .Where(f => string.Equals(f.Driver.Service, "HidUsb", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => access.CollectionsOf(f, names))];
    }

    [TestMethod]
    public async Task EveryUsbHidCollection_HasALayout_ReadWithoutTouchingTheDevice()
    {
        var collections = await CollectionsOnThisMachineAsync();
        if (collections.Count == 0) Assert.Inconclusive("no USB HID device on this machine");

        foreach (var c in collections)
        {
            Assert.AreNotEqual((ushort)0, c.UsagePage, $"{c.DevicePath} has no usage page");
            Assert.IsTrue(c.InputLength + c.OutputLength + c.FeatureLength > 0, $"{c.Title} has no reports at all");
            Assert.IsTrue(c.Reports.Count > 0, $"{c.Title}: Windows lists report lengths but no fields");
            Assert.IsTrue(c.Reports.SelectMany(r => r.Fields).All(f => f.Name.Length > 0), $"{c.Title} has an unnamed field");
            StringAssert.Contains(c.Title, "›");
        }
    }

    [TestMethod]
    public async Task KeyboardsAndMice_AreKeptByWindows_ButTheirLayoutIsNot()
    {
        var collections = await CollectionsOnThisMachineAsync();
        var pointers = collections.Where(c => c.UsagePage == 0x01 && c.Usage is 0x02 or 0x06).ToList();
        if (pointers.Count == 0) Assert.Inconclusive("no USB keyboard or mouse on this machine");

        foreach (var c in pointers)
        {
            Assert.AreEqual(HidReach.LayoutOnly, c.Reach, $"{c.Title}: Windows gives its input to the desktop alone");
            Assert.IsTrue(c.Of(HidReportKind.Input).Any(), $"{c.Title}: its input layout is still there to read");
        }
    }
}
