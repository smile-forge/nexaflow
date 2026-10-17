using Nexaflow.Elevation.Contracts;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// Assert the gate, not the effect (docs/testing.md): what reaches the bridge, and how its answers are read —
/// nothing here changes a driver.
/// </summary>
[TestClass]
[CoversNode("io-usb-broker")]
public class BridgeUsbDeviceBrokerTests
{
    const string Serial = @"USB\VID_043E&PID_9A39&MI_01\8&F264BB0&0&0001";
    const string Hub = @"USB\VID_0451&PID_8142\MSFT20C303007916F0";

    string _dir = "";
    UsbBindingLedger _ledger = null!;
    readonly List<ElevatedRequest> _sent = [];

    [TestInitialize]
    public void Setup()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-usb-broker-").FullName;
        _ledger = UsbBindingLedger.AtPath(Path.Combine(_dir, "usb-bindings.json"));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    static UsbBindingCandidate Facts(string id) =>
        id == Hub ? new(id, true, "USBHUB3", "USB", "USBHUB3", []) : new(id, true, "usbser", "Ports", "usbccgp", []);

    BridgeUsbDeviceBroker Broker(Func<ElevatedRequest, ElevatedResult> bridge) =>
        new((request, _) => { _sent.Add(request); return Task.FromResult(bridge(request)); }, _ledger, Facts);

    /// <summary>A bridge that does what the real one does to the ledger, without touching a device.</summary>
    ElevatedResult Bound(ElevatedRequest request)
    {
        var record = UsbBindingLedgerTests.Record(request.Operations[0].Args[ElevatedArgs.UsbInstanceId], UsbBindingState.Bound);
        _ledger.Save(record);
        var op = ElevatedOperationResult.Ok(ElevatedOps.UsbBind, "Bound.");
        op.Data[ElevatedArgs.UsbBindingIds] = record.Id.ToString();
        return ElevatedResult.FromOperations([op]);
    }

    [TestMethod]
    public async Task Bind_SendsOnlyTheInstanceIdAndKind_AndReadsTheLedgerBack()
    {
        var outcome = await Broker(Bound).BindGenericAsync(Serial, GenericUsbDriverKind.WinUsb, CancellationToken.None);

        var op = _sent.Single().Operations.Single();
        Assert.AreEqual(ElevatedOps.UsbBind, op.Op);
        CollectionAssert.AreEquivalent(new[] { ElevatedArgs.UsbInstanceId, ElevatedArgs.UsbDriverKind }, op.Args.Keys.ToArray());
        Assert.AreEqual(Serial, op.Args[ElevatedArgs.UsbInstanceId]);
        Assert.AreEqual("WinUsb", op.Args[ElevatedArgs.UsbDriverKind]);

        Assert.AreEqual(UsbBindingOutcomeKind.Succeeded, outcome.Kind);
        Assert.AreEqual(UsbBindingState.Bound, outcome.Records.Single().State);
    }

    [TestMethod]
    public async Task RefusedTarget_NeverReachesTheBridge()
    {
        var outcome = await Broker(_ => throw new AssertFailedException("the bridge was asked")).BindGenericAsync(Hub, GenericUsbDriverKind.WinUsb, CancellationToken.None);
        Assert.AreEqual(UsbBindingOutcomeKind.Refused, outcome.Kind);
        StringAssert.Contains(outcome.Message, "hub");
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public async Task DeclinedPrompt_IsDeclined_AndChangesNothing()
    {
        var outcome = await Broker(_ => ElevatedResult.Declined()).BindGenericAsync(Serial, GenericUsbDriverKind.WinUsb, CancellationToken.None);
        Assert.AreEqual(UsbBindingOutcomeKind.Declined, outcome.Kind);
        Assert.AreEqual(0, _ledger.Read().Count);
    }

    [TestMethod]
    public async Task BridgeFailureAndBridgeRefusal_AreTold()
    {
        var failed = await Broker(_ => ElevatedResult.FromOperations([ElevatedOperationResult.Fail(ElevatedOps.UsbBind, ElevatedErrorKind.OperationFailed, "DiInstallDevice: Win32 5")]))
            .BindGenericAsync(Serial, GenericUsbDriverKind.WinUsb, CancellationToken.None);
        Assert.AreEqual(UsbBindingOutcomeKind.Failed, failed.Kind);
        StringAssert.Contains(failed.Message, "Win32 5");

        var refusedOp = ElevatedOperationResult.Fail(ElevatedOps.UsbBind, ElevatedErrorKind.OperationFailed, "Not taken over: it backs a disk.");
        refusedOp.Data[ElevatedArgs.UsbRefused] = "true";
        var refused = await Broker(_ => ElevatedResult.FromOperations([refusedOp])).BindGenericAsync(Serial, GenericUsbDriverKind.WinUsb, CancellationToken.None);
        Assert.AreEqual(UsbBindingOutcomeKind.Refused, refused.Kind, "the bridge's own check outranks the client's");
    }

    [TestMethod]
    public async Task Restore_AsksOnlyWhenTheLedgerHoldsABinding()
    {
        var broker = Broker(_ => ElevatedResult.FromOperations([ElevatedOperationResult.Ok(ElevatedOps.UsbRestore, "Restored.")]));
        var nothing = await broker.RestoreAsync(Serial, CancellationToken.None);
        Assert.AreEqual(UsbBindingOutcomeKind.Refused, nothing.Kind);
        Assert.AreEqual(0, _sent.Count);

        _ledger.Save(UsbBindingLedgerTests.Record(Serial, UsbBindingState.Bound));
        await broker.RestoreAsync(Serial, CancellationToken.None);
        var op = _sent.Single().Operations.Single();
        Assert.AreEqual(ElevatedOps.UsbRestore, op.Op);
        CollectionAssert.AreEqual(new[] { ElevatedArgs.UsbInstanceId }, op.Args.Keys.ToArray(), "the ledger record itself is never sent");
    }

    [TestMethod]
    public async Task RestoreAll_IsOneRequest_OrNoneWhenNothingIsBound()
    {
        var broker = Broker(_ => ElevatedResult.FromOperations([ElevatedOperationResult.Ok(ElevatedOps.UsbRestoreAll, "Done.")]));
        Assert.AreEqual(UsbBindingOutcomeKind.Succeeded, (await broker.RestoreAllAsync(CancellationToken.None)).Kind);
        Assert.AreEqual(0, _sent.Count);

        _ledger.Save(UsbBindingLedgerTests.Record(Serial, UsbBindingState.Bound));
        _ledger.Save(UsbBindingLedgerTests.Record(@"USB\VID_1234&PID_0001&MI_00\X", UsbBindingState.RestoreFailed));
        await broker.RestoreAllAsync(CancellationToken.None);
        Assert.AreEqual(ElevatedOps.UsbRestoreAll, _sent.Single().Operations.Single().Op);
        Assert.AreEqual(1, _sent.Single().Operations.Count, "one prompt for every binding");
    }
}
