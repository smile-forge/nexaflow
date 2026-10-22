using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Raw;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Usb;

/// <summary>
/// Raw access below the tools: the setup packet taken apart and put together, the session log every transfer goes
/// through, and which functions WinUSB can open — plus, where this machine has one, a real open of a WinUSB function
/// that asks it only for its device descriptor.
/// </summary>
[TestClass]
[CoversNode("io-usb-raw")]
public class RawUsbTests
{
    /// <summary>A transport the test feeds: control answers it is given, reads from a channel, writes recorded.</summary>
    sealed class Wire : IRawUsbTransport
    {
        public readonly Channel<byte[]> Incoming = Channel.CreateUnbounded<byte[]>();
        public readonly List<(byte Endpoint, byte[] Data)> Written = [];
        public byte[] Answer = [0x12, 0x01, 0x00, 0x02];
        public Exception? Fail;
        public bool Disposed;

        public IReadOnlyList<UsbPipe> Pipes { get; } = [new(0, 0x81, UsbEndpointType.Bulk, 64, 0), new(0, 0x01, UsbEndpointType.Bulk, 64, 0)];

        public Task<byte[]> ControlInAsync(UsbControlSetup setup, CancellationToken ct) => Fail is { } e ? Task.FromException<byte[]>(e) : Task.FromResult(Answer);

        public Task ControlOutAsync(UsbControlSetup setup, ReadOnlyMemory<byte> data, CancellationToken ct) => Fail is { } e ? Task.FromException(e) : Task.CompletedTask;

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

    // ── The setup packet ───────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ASetupPacket_IsPutTogether_AndTakenApart_AsUsb20Says()
    {
        var vendorIn = UsbControlSetup.Of(UsbDirection.In, UsbRequestType.Vendor, UsbRequestRecipient.Device, 0x51, 0, 0, 2);
        Assert.AreEqual(0xC0, vendorIn.RequestType, "IN | vendor | device");
        Assert.AreEqual((UsbDirection.In, UsbRequestType.Vendor, UsbRequestRecipient.Device), (vendorIn.Direction, vendorIn.Type, vendorIn.Recipient));
        Assert.AreEqual("0x51", vendorIn.RequestName, "a vendor request has no standard name");

        var classOut = UsbControlSetup.Of(UsbDirection.Out, UsbRequestType.Class, UsbRequestRecipient.Interface, 0x22, 3, 1, 0);
        Assert.AreEqual(0x21, classOut.RequestType, "CDC's SET_CONTROL_LINE_STATE type: OUT | class | interface");

        var descriptor = new UsbControlSetup(0x80, UsbStandardRequests.GetDescriptor, 0x0100, 0, 18);
        StringAssert.StartsWith(descriptor.Describe(), "GET_DESCRIPTOR IN standard device");
        StringAssert.Contains(descriptor.Describe(), "wValue 0x0100");
    }

    [TestMethod]
    public void TheReadyMadeRequests_OnlyAsk_AndAskTheInterfaceTheyAreFor()
    {
        var reads = UsbStandardRequests.Reads(3);
        Assert.IsTrue(reads.All(r => r.Setup.Direction == UsbDirection.In && r.Setup.Type == UsbRequestType.Standard), "nothing offered ready-made changes the device");
        Assert.IsTrue(reads.Where(r => r.Setup.Recipient == UsbRequestRecipient.Interface).All(r => r.Setup.Index == 3));
        Assert.AreEqual(18, reads.Single(r => r.Name == "GET_DESCRIPTOR (device)").Setup.Length);
    }

    [TestMethod]
    public void Hex_IsReadHoweverItIsTyped_AndRefusedWhenItIsNotHex()
    {
        Assert.IsTrue(UsbHex.TryParse("02 01 FF", out var spaced));
        CollectionAssert.AreEqual(new byte[] { 2, 1, 0xFF }, spaced);
        Assert.IsTrue(UsbHex.TryParse("0x02,0x01;ff", out var prefixed));
        CollectionAssert.AreEqual(new byte[] { 2, 1, 0xFF }, prefixed);
        Assert.IsTrue(UsbHex.TryParse("1 2", out var nibbles));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, nibbles, "an odd token is read with a leading zero");
        Assert.IsFalse(UsbHex.TryParse("02 GG", out _));
        Assert.IsFalse(UsbHex.TryParse("  ", out _));
    }

    // ── The session log ────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task EveryTransfer_IsLogged_WithItsBytes_AndAFailureWithWhy()
    {
        var session = new RawUsbSession();
        var wire = new Wire();
        var transport = session.Record(wire);

        var got = await transport.ControlInAsync(new UsbControlSetup(0x80, UsbStandardRequests.GetDescriptor, 0x0100, 0, 18), CancellationToken.None);
        await transport.WriteEndpointAsync(0x01, new byte[] { 0xAA, 0xBB }, CancellationToken.None);
        wire.Fail = new IOException("GET_STATUS: the device stalled it");
        await Assert.ThrowsExactlyAsync<IOException>(() => transport.ControlInAsync(new UsbControlSetup(0x80, UsbStandardRequests.GetStatus, 0, 0, 2), CancellationToken.None));

        CollectionAssert.AreEqual(wire.Answer, got, "the log passes the answer on untouched");
        var log = session.Transfers;
        Assert.AreEqual(3, log.Count);
        Assert.AreEqual((RawUsbTransferKind.ControlIn, true), (log[0].Kind, log[0].Succeeded));
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, log[1].Data);
        Assert.AreEqual(UsbDirection.Out, log[1].Direction);
        StringAssert.Contains(log[2].Error, "stalled");

        var text = session.Text();
        StringAssert.Contains(text, "GET_DESCRIPTOR IN standard device");
        StringAssert.Contains(text, "12 01 00 02");
        StringAssert.Contains(text, "endpoint 0x01 · 2/2 B");
        StringAssert.Contains(text, "stalled");
    }

    [TestMethod]
    public async Task AReader_IsLoggedPacketByPacket_AndStoppingItIsNotAFailure()
    {
        var session = new RawUsbSession();
        var wire = new Wire();
        using var stop = new CancellationTokenSource();
        var seen = new List<byte[]>();
        wire.Incoming.Writer.TryWrite([1, 2, 3]);
        wire.Incoming.Writer.TryWrite([4]);

        await foreach (var packet in session.Record(wire).ReadEndpointAsync(0x81, 64, stop.Token))
        {
            seen.Add(packet.ToArray());
            if (seen.Count == 2) stop.Cancel();
        }

        Assert.AreEqual(2, seen.Count);
        Assert.AreEqual(2, session.Transfers.Count, "the cancelled read that ended it is not in the log");
        Assert.IsTrue(session.Transfers.All(t => t.Kind == RawUsbTransferKind.EndpointIn && t.Succeeded && t.Target == "endpoint 0x81"));
    }

    [TestMethod]
    public async Task OneSession_SpansEveryOpen_AndKeepsOnlyTheNewest()
    {
        var session = new RawUsbSession();
        var first = session.Record(new Wire());
        await first.WriteEndpointAsync(0x01, new byte[] { 1 }, CancellationToken.None);
        await first.DisposeAsync();
        await session.Record(new Wire()).WriteEndpointAsync(0x01, new byte[] { 2 }, CancellationToken.None);
        Assert.AreEqual(2, session.Transfers.Count, "letting the function go and opening it again is one session");

        var flood = session.Record(new Wire());
        for (int i = 0; i < RawUsbSession.Kept + 5; i++) await flood.WriteEndpointAsync(0x01, new byte[] { 3 }, CancellationToken.None);
        Assert.AreEqual(RawUsbSession.Kept, session.Transfers.Count);
        Assert.AreEqual(7, session.Dropped);
        StringAssert.StartsWith(session.Text(), "(7 earlier transfer(s) not kept)");

        session.Clear();
        Assert.AreEqual(0, session.Transfers.Count);
        Assert.AreEqual(0, session.Dropped);
    }

    // ── Who can be opened ──────────────────────────────────────────────────────────────────────────────────

    static UsbFunctionNode Function(string? service, params Guid[] interfaces) =>
        new(@"USB\VID_CAFE&PID_0001\GADGET1", [0], null, UsbDevNodeDriver.None with { Service = service },
            UsbFunctionLayout.Classify(service, null), UsbPowerState.D0, interfaces, null, []);

    [TestMethod]
    public void WinUsb_OpensOnlyWhatIsOnWinUsb_WithADeviceInterface()
    {
        var access = new WinUsbRawAccess();
        StringAssert.Contains(access.UnreachableReason(Function(null)), "take it over first");
        StringAssert.Contains(access.UnreachableReason(Function("usbser")), "take it over first");
        StringAssert.Contains(access.UnreachableReason(Function("libusbK", Guid.NewGuid())), "libusb", "libusbK is generic, but not WinUSB");
        StringAssert.Contains(access.UnreachableReason(Function("WINUSB")), "no device interface");
        Assert.IsNull(access.UnreachableReason(Function("WINUSB", Guid.NewGuid())));
        Assert.ThrowsExactly<IOException>(() => access.Open(Function("usbser")), "what cannot be reached is refused before anything is opened");
    }

    /// <summary>A WinUSB function on this machine, opened for real and asked for its device descriptor — the one
    /// standard read every device answers. Inconclusive where none is plugged in, or another program holds it.</summary>
    [TestMethod]
    public async Task AWinUsbFunction_OnThisMachine_AnswersGetDescriptor_LikeItsHub()
    {
        using var inventory = new WindowsUsbInventory(null);
        var snapshot = await inventory.RefreshAsync(CancellationToken.None);
        var access = new WinUsbRawAccess();
        var found = snapshot.Devices.SelectMany(d => d.Functions.Select(f => (Device: d, Function: f))).FirstOrDefault(x => access.UnreachableReason(x.Function) is null);
        if (found.Function is null) Assert.Inconclusive("no function on WinUSB is plugged in");

        IRawUsbTransport transport;
        try { transport = access.Open(found.Function); }
        catch (IOException e) { Assert.Inconclusive($"{found.Function.InstanceId} could not be opened: {e.Message}"); return; }
        await using (transport)
        {
            Assert.IsTrue(transport.Pipes.All(p => p.Type is not UsbEndpointType.Control), "the default pipe is not one of the listed endpoints");
            var descriptor = await transport.ControlInAsync(new UsbControlSetup(0x80, UsbStandardRequests.GetDescriptor, 0x0100, 0, 18), CancellationToken.None);
            CollectionAssert.AreEqual(found.Device.RawDeviceDescriptor, descriptor, "WinUSB and the hub read the same 18 bytes");
        }
    }

    // ── A broker that asks nothing ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TheQuietBroker_ReadsTheLedgerAndThePolicy_ButChangesNothing()
    {
        var ledger = UsbBindingLedger.AtPath(Path.Combine(TestTemp.CreateSubdirectory("usb-quiet-").FullName, "usb-bindings.json"));
        ledger.Save(UsbBindingLedgerTests.Record(@"USB\VID_CAFE&PID_0001\GADGET1", UsbBindingState.Bound));
        var broker = new ReadOnlyUsbDeviceBroker(ledger, id => new UsbBindingCandidate(id, true, "USBHUB3", "USB", "USBHUB3", []));

        Assert.AreEqual(1, broker.GetBindings().Count);
        StringAssert.Contains(broker.RefusalReason(@"USB\VID_0451&PID_8142\HUB"), "hub", "the policy answers as the real broker's does");
        foreach (var change in new[]
        {
            broker.BindGenericAsync("x", GenericUsbDriverKind.WinUsb, CancellationToken.None),
            broker.RestoreAsync("x", CancellationToken.None),
            broker.RestoreAllAsync(CancellationToken.None),
        })
            Assert.AreEqual(UsbBindingOutcomeKind.Refused, (await change).Kind);
        Assert.AreEqual(UsbBindingState.Bound, ledger.Read().Single().State, "nothing was restored");
    }
}
