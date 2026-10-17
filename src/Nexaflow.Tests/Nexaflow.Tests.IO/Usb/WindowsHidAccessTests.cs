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

    [TestMethod]
    public async Task EveryUsbHidCollection_GivesUpItsPreparsedData_ForTheDebugDetails()
    {
        var collections = await CollectionsOnThisMachineAsync();
        if (collections.Count == 0) Assert.Inconclusive("no USB HID device on this machine");

        var access = new WindowsHidAccess();
        foreach (var c in collections)
        {
            var blob = access.PreparsedData(c);
            Assert.IsNotNull(blob, $"{c.Title}: no preparsed data");
            CollectionAssert.AreEqual("HidP KDR"u8.ToArray(), blob.AsSpan(0, 8).ToArray(), $"{c.Title}: not the layout hidapi reads");
            Assert.IsTrue(blob.Length > 44, $"{c.Title}: the header alone");
        }
    }

    [TestMethod]
    public async Task EachHidInterface_GivesItsReportDescriptor_OrSaysWhyNot()
    {
        using var inventory = new WindowsUsbInventory(null);
        var snapshot = await inventory.RefreshAsync(CancellationToken.None);
        var devices = snapshot.Devices.Where(d => d.ActiveConfiguration?.DefaultSettings.Any(i => i.HidReportDescriptorLength is > 0) == true).ToList();
        if (devices.Count == 0) Assert.Inconclusive("no USB HID device on this machine");

        foreach (var device in devices)
            foreach (var read in UsbHidReportReader.Read(device))
                Assert.IsTrue(read.Bytes is { Length: > 0 } != (read.Problem is not null), $"{device.DisplayName} interface {read.Interface}: bytes or a reason, one or the other");
    }
}
