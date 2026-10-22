using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Nexaflow.Features.UsbProbe.RawTools;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Raw;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// The take-over tile over a broker that changes nothing, and the raw access tile over a function the test plays:
/// what each offers, what it asks first, and what reaches the device. No driver is changed and no device is opened.
/// </summary>
[TestClass]
[CoversNode("usbprobe-takeover")]
[CoversNode("usbprobe-raw")]
public class UsbRawToolsTests
{
    /// <summary>A function the test plays: control answers it is given, reads from a channel, everything it is sent kept.</summary>
    sealed class Wire : IRawUsbTransport
    {
        public readonly Channel<byte[]> Incoming = Channel.CreateUnbounded<byte[]>();
        public readonly List<UsbControlSetup> Setups = [];
        public readonly List<byte[]> ControlData = [];
        public readonly List<(byte Endpoint, byte[] Data)> Written = [];
        public bool Disposed;

        public IReadOnlyList<UsbPipe> Pipes { get; } = [new(0, 0x81, UsbEndpointType.Bulk, 512, 0), new(0, 0x01, UsbEndpointType.Bulk, 512, 0)];

        public Task<byte[]> ControlInAsync(UsbControlSetup setup, CancellationToken ct)
        {
            Setups.Add(setup);
            return Task.FromResult(setup.Request == UsbStandardRequests.GetDescriptor ? Bytes(UsbProbeSamples.GadgetDevice) : new byte[] { 0x01, 0x00 });
        }

        public Task ControlOutAsync(UsbControlSetup setup, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            Setups.Add(setup);
            ControlData.Add(data.ToArray());
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadEndpointAsync(byte endpoint, int packetSize, [EnumeratorCancellation] CancellationToken ct)
        {
            while (true)
            {
                byte[] packet;
                try { packet = await Incoming.Reader.ReadAsync(ct); }
                catch (OperationCanceledException) { yield break; }
                yield return packet;
            }
        }

        public Task WriteEndpointAsync(byte endpoint, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            Written.Add((endpoint, data.ToArray()));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    sealed class Access(Wire wire) : IRawUsbAccess
    {
        public int Opens;
        public string? Reason;

        public string? UnreachableReason(UsbFunctionNode function) => Reason;

        public IRawUsbTransport Open(UsbFunctionNode function)
        {
            Opens++;
            return wire;
        }
    }

    static byte[] Bytes(string hex) => UsbHex.TryParse(hex, out var bytes) ? bytes : throw new FormatException(hex);

    static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
    }

    RecordingHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new RecordingHost();

    static UsbBindingRecord Bound(string id) =>
        new(Guid.NewGuid(), id, @"USB\VID_CAFE&PID_0001", "usbser", "usbser.inf", "Install", @"USB\Class_02", null, UsbBindingRecord.InboxWinUsbStrategy, "{1}", DateTimeOffset.Now)
        {
            State = UsbBindingState.Bound,
            Log = { $"selected winusb.inf for {id}", "DiInstallDevice: ok" },
        };

    // ── Take-over (R1, R2) ─────────────────────────────────────────────────────────────────────────────────

    UsbTakeOverTool TakeOver(UsbDeviceNode device) => (UsbTakeOverTool)new UsbTakeOverToolProvider().Offer(device, _host).Single();

    [TestMethod]
    public void TakeOver_IsOfferedWhereThePolicyLetsGo_AndRestoreWhereNexaflowHoldsTheDriver()
    {
        Assert.AreEqual(1, new UsbTakeOverToolProvider().Offer(UsbProbeSamples.Gadget(), _host).Count, "a device with no driver can be taken over");

        _host.Broker.Refusals[UsbProbeSamples.GadgetId] = "it is a hub";
        Assert.AreEqual(0, new UsbTakeOverToolProvider().Offer(UsbProbeSamples.Gadget(), _host).Count, "what the policy keeps gets no tile");

        _host.Broker.Refusals[UsbProbeSamples.GadgetId] = "it is already on WinUSB — raw access needs no take-over";
        _host.Broker.Bindings.Add(Bound(UsbProbeSamples.GadgetId));
        Assert.AreEqual(1, new UsbTakeOverToolProvider().Offer(UsbProbeSamples.Gadget("WINUSB"), _host).Count, "what Nexaflow took over is offered back");

        var ejected = UsbProbeSamples.Gadget() with { Driver = UsbDevNodeDriver.None with { ProblemCode = Nexaflow.IO.Usb.Browsing.UsbProblemCodes.PreparedForRemoval } };
        Assert.AreEqual(0, new UsbTakeOverToolProvider().Offer(ejected, _host).Count, "an ejected device has no live tools");
    }

    [TestMethod]
    public async Task TakingOver_SaysWhatItStops_AsksFirst_AndThenAsksTheBroker()
    {
        var tool = TakeOver(UsbProbeSamples.Gadget());
        await tool.ActivateAsync(CancellationToken.None);
        Assert.IsTrue(tool.CanTakeOver);
        StringAssert.Contains(tool.Impact, "nothing stops working", "a function with no driver loses nothing");
        StringAssert.Contains(tool.Impact, "same model", "the binding follows the hardware id");

        _host.Confirm = false;
        await tool.TakeOverCommand.ExecuteAsync(null);
        Assert.AreEqual(0, _host.Broker.Asked.Count, "no means no UAC prompt either");
        StringAssert.Contains(_host.Confirmations.Single(), tool.Impact);

        _host.Confirm = true;
        await tool.TakeOverCommand.ExecuteAsync(null);
        Assert.AreEqual($"bind {UsbProbeSamples.GadgetId} WinUsb", _host.Broker.Asked.Single());
        StringAssert.Contains(tool.Outcome, "Taken over");
        Assert.IsTrue(_host.Logged.Contains(tool.Outcome!), "the device's log says what was done");
        Assert.IsFalse(tool.IsWorking);
    }

    [TestMethod]
    public async Task ADeclinedPrompt_ChangesNothing_AndSaysSo()
    {
        _host.Broker.Answer = UsbBindingOutcomeKind.Declined;
        var tool = TakeOver(UsbProbeSamples.Gadget());
        await tool.TakeOverCommand.ExecuteAsync(null);
        StringAssert.Contains(tool.Outcome, "declined");
    }

    [TestMethod]
    public void WhatTakingOverStops_IsSaid_ForWhatWindowsUsesItAs()
    {
        var mic = UsbProbeSamples.UsbMicrophone();
        var audio = mic with { Functions = [mic.Functions[0] with { Driver = mic.Functions[0].Driver with { ClassName = "MEDIA" } }] };
        var impact = TakeOver(audio).Impact;
        StringAssert.Contains(impact, "audio device");
        StringAssert.Contains(impact, "usbaudio2", "it names the driver Windows would stop using");
    }

    [TestMethod]
    public async Task ATakenOverFunction_OffersRestore_AndRestoreAll_WhenOthersAreTakenOverToo()
    {
        _host.Broker.Bindings.Add(Bound(UsbProbeSamples.GadgetId));
        _host.Broker.Bindings.Add(Bound(@"USB\VID_1209&PID_0001\OTHER"));
        var tool = TakeOver(UsbProbeSamples.Gadget("WINUSB"));
        await tool.ActivateAsync(CancellationToken.None);

        Assert.IsTrue(tool.IsTakenOver);
        Assert.IsFalse(tool.CanTakeOver);
        StringAssert.Contains(tool.BindingState, "usbser", "restoring gives back the driver it had");
        Assert.AreEqual(1, tool.OtherBindings);

        await tool.RestoreCommand.ExecuteAsync(null);
        await tool.RestoreAllCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { $"restore {UsbProbeSamples.GadgetId}", "restore all" }, _host.Broker.Asked);
        StringAssert.Contains(_host.Confirmations[1], "2 interface(s)");
    }

    [TestMethod]
    public async Task TheAgent_IsToldWhyNot_WhenThePolicySaysNo()
    {
        var tool = TakeOver(UsbProbeSamples.Gadget());
        _host.Broker.Refusals[UsbProbeSamples.GadgetId] = "it is mass storage";
        var said = await tool.Operations.Single(o => o.Name == "take_over").RunAsync(new Dictionary<string, string>(), CancellationToken.None);
        StringAssert.Contains(said, "mass storage");
        Assert.AreEqual(0, _host.Broker.Asked.Count);
        Assert.IsTrue(tool.Operations.All(o => o.ChangesSomething), "every driver change is the user's to approve");
    }

    [TestMethod]
    public async Task TheDebugDetails_CarryTheBridgesSteps_WithTheSerialMasked()
    {
        _host.Broker.Bindings.Add(Bound(UsbProbeSamples.GadgetId));
        var device = UsbProbeSamples.Gadget("WINUSB");
        var details = await TakeOver(device).DebugDetailsAsync(UsbDebugMask.For(device), CancellationToken.None);

        Assert.AreEqual("Bound", (string?)details["binding"]!["state"]);
        Assert.AreEqual("usbser", (string?)details["binding"]!["originalService"]);
        var log = details["binding"]!["log"]!.ToJsonString();
        StringAssert.Contains(log, "DiInstallDevice");
        Assert.IsFalse(log.Contains("GADGET1"), "the instance id's serial is hashed");
    }

    // ── Raw access (R3–R6) ─────────────────────────────────────────────────────────────────────────────────

    (UsbRawTool Tool, Wire Wire, Access Access) Raw()
    {
        var wire = new Wire();
        var access = new Access(wire);
        return ((UsbRawTool)new UsbRawToolProvider(access).Offer(UsbProbeSamples.Gadget("WINUSB"), _host).Single(), wire, access);
    }

    [TestMethod]
    public void RawAccess_IsOfferedForWhatTheBackendCanOpen_WithItsEndpointsFromTheDescriptors()
    {
        var access = new Access(new Wire()) { Reason = "it is not on WinUSB — take it over first" };
        Assert.AreEqual(0, new UsbRawToolProvider(access).Offer(UsbProbeSamples.Gadget(), _host).Count);

        var (tool, _, opened) = Raw();
        Assert.AreEqual(0, opened.Opens, "nothing is opened until the first transfer");
        Assert.AreEqual(2, tool.Pipes.Count);
        Assert.AreEqual(0x81, tool.ReadPipe!.Address);
        Assert.AreEqual(0x01, tool.WritePipe!.Address);
    }

    [TestMethod]
    public async Task AStandardRead_OpensTheFunctionOnce_AndShowsWhatCameBack()
    {
        var (tool, wire, access) = Raw();
        tool.SelectedTemplate = tool.Templates.Single(t => t.Name == "GET_DESCRIPTOR (device)");
        await tool.SendControlCommand.ExecuteAsync(null);
        tool.SelectedTemplate = tool.Templates.Single(t => t.Name == "GET_STATUS (device)");
        await tool.SendControlCommand.ExecuteAsync(null);

        Assert.AreEqual(1, access.Opens);
        Assert.IsTrue(tool.IsOpen);
        Assert.AreEqual(0, _host.Confirmations.Count, "a standard read asks nothing");
        Assert.AreEqual((0x80, UsbStandardRequests.GetDescriptor, 0x0100, 18), ((int)wire.Setups[0].RequestType, wire.Setups[0].Request, (int)wire.Setups[0].Value, (int)wire.Setups[0].Length));
        StringAssert.Contains(tool.ControlResult, "01 00");
        Assert.IsTrue(_host.Logged.Any(l => l.Contains("GET_DESCRIPTOR")), "each control transfer is on the Diagnostics tab");
    }

    [TestMethod]
    public async Task AnOutRequest_AsksFirst_AndCarriesItsDataWithItsLength()
    {
        var (tool, wire, _) = Raw();
        tool.Direction = UsbDirection.Out;
        tool.RequestType = UsbRequestType.Vendor;
        tool.Request = "0x22";
        tool.Data = "01 02 03";
        Assert.IsTrue(tool.NeedsWarning, "a vendor request carries the warning");

        _host.Confirm = false;
        await tool.SendControlCommand.ExecuteAsync(null);
        Assert.AreEqual(0, wire.Setups.Count);

        _host.Confirm = true;
        await tool.SendControlCommand.ExecuteAsync(null);
        var sent = wire.Setups.Single();
        Assert.AreEqual((0x40, 0x22, 3), ((int)sent.RequestType, (int)sent.Request, (int)sent.Length), "OUT | vendor | device, wLength from the data");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, wire.ControlData.Single());
    }

    [TestMethod]
    public async Task AFieldOutOfRange_IsSaid_AndNothingIsOpened()
    {
        var (tool, _, access) = Raw();
        tool.Request = "300";
        await tool.SendControlCommand.ExecuteAsync(null);
        StringAssert.Contains(tool.ControlResult, "bRequest");
        Assert.AreEqual(0, access.Opens);
    }

    [TestMethod]
    public async Task TheReader_StreamsAnEndpoint_IntoTheSession_UntilPaused()
    {
        var (tool, wire, _) = Raw();
        await tool.StartReadingCommand.ExecuteAsync(null);
        Assert.IsTrue(tool.IsReading && tool.IsBusy);
        wire.Incoming.Writer.TryWrite([0xDE, 0xAD]);
        wire.Incoming.Writer.TryWrite([0xBE, 0xEF]);
        await WaitFor(() => tool.Operations.Single(o => o.Name == "session_log").RunAsync(new Dictionary<string, string>(), CancellationToken.None).Result.Contains("BE EF"));

        tool.StopReadingCommand.Execute(null);
        await WaitFor(() => !tool.IsReading);

        Assert.AreEqual(2, tool.ReadCount);
        Assert.AreEqual("DE AD", tool.Transfers.Last().Hex);
        Assert.IsTrue(_host.Logged.Any(l => l.Contains("Stopped reading endpoint 0x81") && l.Contains("2 packet(s)")));
    }

    [TestMethod]
    public async Task TheWriter_SendsTextOrHex_AsManyTimesAsAsked_AfterOneYes()
    {
        var (tool, wire, _) = Raw();
        tool.Payload = @"AT\r\n";
        tool.PayloadIsText = true;
        tool.Repeat = "3";
        await tool.WriteCommand.ExecuteAsync(null);

        Assert.AreEqual(1, _host.Confirmations.Count, "a repeated write asks once");
        StringAssert.Contains(_host.Confirmations[0], "3 times");
        Assert.AreEqual(3, wire.Written.Count);
        Assert.IsTrue(wire.Written.All(w => w.Endpoint == 0x01 && w.Data.SequenceEqual("AT\r\n"u8.ToArray())));
        StringAssert.Contains(tool.WriteResult, "Sent 3 time(s)");
        Assert.IsFalse(tool.IsWriting);
    }

    [TestMethod]
    public void TextToSend_IsUtf8_WithItsEscapes_AndBytesWhereAsked()
    {
        Assert.IsTrue(RawPayload.TryParse(@"é\x00\x80\\n", asText: true, out var bytes, out _));
        CollectionAssert.AreEqual(new byte[] { 0xC3, 0xA9, 0x00, 0x80, (byte)'\\', (byte)'n' }, bytes, "é is UTF-8; \\x80 is the byte 0x80 itself");
        Assert.IsFalse(RawPayload.TryParse("ZZ", asText: false, out _, out var error));
        Assert.IsNotNull(error);
        Assert.IsTrue(RawNumber.TryParse("0x1F", 0xFF, out int hex) && hex == 0x1F);
        Assert.IsFalse(RawNumber.TryParse("256", 0xFF, out _));
    }

    [TestMethod]
    public async Task LettingGo_ClosesTheFunction_AndTheSessionIsSavedWhole()
    {
        var (tool, wire, _) = Raw();
        await tool.SendControlCommand.ExecuteAsync(null);
        await tool.LetGoCommand.ExecuteAsync(null);
        Assert.IsTrue(wire.Disposed);
        Assert.IsFalse(tool.IsOpen);

        _host.SavedFile = TestTemp.CreateFile(".txt");
        await tool.SaveSessionCommand.ExecuteAsync(null);
        var text = await File.ReadAllTextAsync(_host.SavedFile, Encoding.UTF8);
        StringAssert.Contains(text, "CAFE:0001");
        StringAssert.Contains(text, "GET_DESCRIPTOR IN standard device");
    }

    [TestMethod]
    public async Task TheAgent_SendsAnyControlTransfer_OnlyWithApproval_AndReadsWithout()
    {
        var (tool, wire, _) = Raw();
        var operations = tool.Operations.ToDictionary(o => o.Name);
        Assert.IsTrue(operations["control_transfer"].ChangesSomething && operations["write_endpoint"].ChangesSomething);
        Assert.IsFalse(operations["standard_request"].ChangesSomething || operations["read_endpoint"].ChangesSomething);

        await operations["control_transfer"].RunAsync(new Dictionary<string, string> { ["bmRequestType"] = "0x40", ["bRequest"] = "1", ["data"] = "AA" }, CancellationToken.None);
        Assert.AreEqual(1, wire.Setups.Single().Length, "an OUT request's wLength is its data's");

        wire.Incoming.Writer.TryWrite([0x55]);
        var read = await operations["read_endpoint"].RunAsync(new Dictionary<string, string> { ["endpoint"] = "0x81", ["seconds"] = "1" }, CancellationToken.None);
        StringAssert.Contains(read, "55");
    }

    [TestMethod]
    public async Task TheDebugDetails_SayHowTheSessionWent_WithoutItsBytes()
    {
        var (tool, _, _) = Raw();
        await tool.Operations.Single(o => o.Name == "write_endpoint")
            .RunAsync(new Dictionary<string, string> { ["endpoint"] = "0x01", ["hex"] = "C0 FF EE" }, CancellationToken.None);
        var details = await tool.DebugDetailsAsync(UsbDebugMask.For(UsbProbeSamples.Gadget("WINUSB")), CancellationToken.None);
        Assert.AreEqual(2, details["pipes"]!.AsArray().Count);
        Assert.AreEqual(1, (int)details["transfers"]!);
        Assert.IsFalse(details.ToJsonString().Contains("C0 FF EE"), "what was sent is not in a dump that leaves the machine");
    }
}
