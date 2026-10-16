using Nexaflow.IO.Usb.Binding;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

[TestClass]
[CoversNode("io-usb-ledger")]
public class UsbBindingLedgerTests
{
    string _dir = "";

    [TestInitialize]
    public void Setup() => _dir = Directory.CreateTempSubdirectory("nexaflow-usb-ledger-").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    internal static UsbBindingRecord Record(string instanceId, UsbBindingState state) =>
        new(Guid.NewGuid(), instanceId, @"USB\VID_043E&PID_9A39&REV_0417&MI_01", "usbser", "usbser.inf", "UsbSerial_Install",
            @"usb\class_02&subclass_02", new Guid("4d36e978-e325-11ce-bfc1-08002be10318"), UsbBindingRecord.InboxWinUsbStrategy,
            Guid.NewGuid().ToString("B"), DateTimeOffset.Now)
        { State = state, ClassChanged = true, Log = { "DeviceInterfaceGUIDs = …" } };

    [TestMethod]
    public void MissingFile_ReadsAsEmpty() =>
        Assert.AreEqual(0, UsbBindingLedger.AtPath(Path.Combine(_dir, "none", "usb-bindings.json")).Read().Count);

    [TestMethod]
    public void Save_RoundTripsEveryField_AndReplacesById()
    {
        var ledger = UsbBindingLedger.AtPath(Path.Combine(_dir, "usb-bindings.json"));
        var record = Record("USB\\A&MI_01\\1", UsbBindingState.BindIntent);
        ledger.Save(record);
        record.State = UsbBindingState.Bound;
        record.Log.Add("DiInstallDevice: ok");
        ledger.Save(record);

        var back = ledger.Read().Single();
        Assert.AreEqual(record.Id, back.Id);
        Assert.AreEqual(UsbBindingState.Bound, back.State);
        Assert.IsTrue(back.ClassChanged);
        Assert.AreEqual(record.OriginalClassGuid, back.OriginalClassGuid);
        Assert.AreEqual(record.AddedInterfaceGuid, back.AddedInterfaceGuid);
        CollectionAssert.AreEqual(record.Log, back.Log);
        Assert.IsNull(back.PublishedInfName);
        StringAssert.Contains(File.ReadAllText(ledger.FilePath), "\"Bound\"", "states are written by name");
        Assert.IsFalse(File.Exists(ledger.FilePath + ".tmp"));
    }

    [TestMethod]
    public void Active_IsTheNewestRecordNotCleanlyRestored()
    {
        var ledger = UsbBindingLedger.AtPath(Path.Combine(_dir, "usb-bindings.json"));
        ledger.Save(Record("USB\\A&MI_01\\1", UsbBindingState.Restored));
        Assert.IsNull(ledger.Active("usb\\a&mi_01\\1"), "a restored binding is history");

        var crashed = Record("USB\\A&MI_01\\1", UsbBindingState.BindIntent);
        ledger.Save(crashed);
        Assert.AreEqual(crashed.Id, ledger.Active("USB\\A&MI_01\\1")?.Id, "an intent left by a crash is still active — restore finishes it");

        ledger.Save(Record("USB\\B&MI_00\\2", UsbBindingState.RestoreFailed));
        Assert.AreEqual(2, ledger.Read().Count(r => r.IsActive));
    }
}
