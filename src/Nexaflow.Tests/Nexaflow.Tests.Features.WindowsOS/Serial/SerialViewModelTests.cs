using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Serial.Services;
using Nexaflow.Features.Serial.ViewModels;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Serial;

/// <summary>
/// The serial terminal against a port the test plays the far end of: connecting with the settings shown, lines made
/// of what arrives, sending asked once per connection, a port that goes away, and the agent's tools.
/// </summary>
[TestClass]
[CoversNode("serial")]
public class SerialViewModelTests
{
    sealed class FarEnd : ISerialLink
    {
        public string? OpenedOn;
        public SerialSettings? OpenedWith;
        public Exception? Refuse;
        public readonly List<byte[]> Written = [];
        public bool IsOpen { get; private set; }
        public bool Dtr { get; set; }
        public bool Rts { get; set; }
        public event Action<byte[]>? Received;
        public event Action<string>? Faulted;

        public void Open(string port, SerialSettings settings)
        {
            if (Refuse is not null) throw Refuse;
            (OpenedOn, OpenedWith, IsOpen) = (port, settings, true);
        }

        public void Close() => IsOpen = false;

        public Task WriteAsync(byte[] data, CancellationToken ct)
        {
            Written.Add(data);
            return Task.CompletedTask;
        }

        public void Send(string text) => Received?.Invoke(Encoding.UTF8.GetBytes(text));
        public void Unplug() => Faulted?.Invoke("The port is closed.");
        public void Dispose() => Close();
    }

    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    FarEnd _device = null!;
    Clock _clock = null!;
    IShellServices _shell = null!;
    bool _confirm = true;
    int _asked;

    [TestInitialize]
    public void Setup()
    {
        _device = new FarEnd();
        _clock = new Clock();
        _shell = Substitute.For<IShellServices>();
        _shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>()();
            return Task.CompletedTask;
        });
        _shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _asked++;
            return Task.FromResult(_confirm);
        });
    }

    SerialViewModel Page(string? port = "COM5") => new(_device,
        () => [new SerialPortInfo("COM3", "Bluetooth link", null), new SerialPortInfo("COM5", "USB Serial Device", @"USB\VID_2341&PID_0043\1")],
        _shell, port, _clock);

    [TestMethod]
    public async Task Connecting_OpensThePortShown_WithTheSettingsShown()
    {
        using var vm = Page();
        Assert.AreEqual("COM5", vm.SelectedPort!.Name);
        Assert.AreEqual("USB Serial Device", vm.SelectedPort.Description, "the port the tab names, with the device behind it");

        vm.BaudRate = 9600;
        vm.Parity = Parity.Even;
        vm.Dtr = true;
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsConnected);
        Assert.AreEqual("COM5", _device.OpenedOn);
        Assert.AreEqual("9600 8E1", _device.OpenedWith!.Summary);
        Assert.IsTrue(_device.OpenedWith.Dtr);
        StringAssert.Contains(vm.Status, "9600 8E1");
        Assert.AreEqual(SerialDirection.Note, vm.Transcript.Single().Direction);
    }

    [TestMethod]
    public async Task APortInUse_IsSaidSo_AndNothingIsOpen()
    {
        using var vm = Page();
        _device.Refuse = new UnauthorizedAccessException("Access to the port 'COM5' is denied.");

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsConnected);
        StringAssert.Contains(vm.Status, "in use by another program");
    }

    [TestMethod]
    public async Task WhatArrives_IsMadeIntoLines()
    {
        using var vm = Page();
        await vm.ConnectCommand.ExecuteAsync(null);

        _device.Send("temp=21");
        _device.Send(".5\r\nhum");
        _device.Send("=40\r\n");
        vm.TakeIn();
        var received = vm.Transcript.Where(l => l.Direction == SerialDirection.Received).ToList();
        CollectionAssert.AreEqual(new[] { "temp=21.5", "hum=40" }, received.Select(l => l.Text).ToArray(), "a newline ends a line, however the reads split it");
        Assert.AreEqual("74 65 6D 70 3D 32 31 2E 35 0D 0A", received[0].Hex);

        _device.Send("prompt> ");
        _clock.Now += TimeSpan.FromSeconds(1);
        _device.Send("next");
        vm.TakeIn();
        Assert.AreEqual("next", vm.Transcript[^1].Text, "a quiet port ends a line too");
        Assert.AreEqual("prompt> ", vm.Transcript[^2].Text);

        _device.Send(new string('x', 600));
        vm.TakeIn();
        Assert.AreEqual(512, vm.Transcript[^2].Length, "and a line never grows past 512 bytes: 'next' and 508 of the 600");
        Assert.AreEqual(92, vm.Transcript[^1].Length, "the rest starts the next");
    }

    [TestMethod]
    public async Task ControlCharacters_ShowAsTheirSymbols()
    {
        using var vm = Page();
        await vm.ConnectCommand.ExecuteAsync(null);
        _device.Send("\x1B[1mbold\x07\r\n");
        vm.TakeIn();
        Assert.AreEqual("␛[1mbold␇", vm.Transcript[^1].Text);
    }

    [TestMethod]
    public async Task Sending_AsksOncePerConnection()
    {
        using var vm = Page();
        await vm.ConnectCommand.ExecuteAsync(null);

        _confirm = false;
        vm.SendText = "AT";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.AreEqual(0, _device.Written.Count, "declined, nothing sent");

        _confirm = true;
        await vm.SendCommand.ExecuteAsync(null);
        vm.SendText = "ATI";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.AreEqual(2, _asked, "asked until agreed, then not again");
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("AT\r\n"), _device.Written[0], "with the line ending chosen");
        Assert.AreEqual("", vm.SendText, "the line clears once sent");
        Assert.AreEqual(SerialDirection.Sent, vm.Transcript[^1].Direction);

        vm.DisconnectCommand.Execute(null);
        await vm.ConnectCommand.ExecuteAsync(null);
        vm.SendText = "x";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.AreEqual(3, _asked, "a new connection asks again");
    }

    [TestMethod]
    public void Encoding_TextOrHex()
    {
        Assert.IsTrue(SerialViewModel.TryEncode("hi", false, SerialLineEnding.LF, out var text, out _));
        CollectionAssert.AreEqual("hi\n"u8.ToArray(), text);
        Assert.IsTrue(SerialViewModel.TryEncode("02 10 ff", true, SerialLineEnding.CRLF, out var hex, out _));
        CollectionAssert.AreEqual(new byte[] { 2, 0x10, 0xFF }, hex, "hex takes no line ending");
        Assert.IsFalse(SerialViewModel.TryEncode("zz", true, SerialLineEnding.None, out _, out var error));
        StringAssert.Contains(error, "not hex");
        Assert.IsFalse(SerialViewModel.TryEncode("", false, SerialLineEnding.None, out _, out _));
    }

    [TestMethod]
    public async Task APortThatGoesAway_IsClosed_AndSaidSo()
    {
        using var vm = Page();
        await vm.ConnectCommand.ExecuteAsync(null);

        _device.Unplug();

        Assert.IsFalse(vm.IsConnected);
        Assert.IsFalse(_device.IsOpen);
        StringAssert.Contains(vm.Status, "stopped working");
        StringAssert.Contains(vm.Transcript[^1].Text, "went away");
    }

    [TestMethod]
    public async Task TheAgent_ReadsFreely_ButConnectsAndSendsWithApproval()
    {
        using var vm = Page();
        var tools = vm.GetClientTools().ToDictionary(t => t.Name);

        Assert.AreEqual(ToolSafety.SafeOperation, tools["serial_read"].Safety);
        Assert.AreEqual(ToolSafety.RequiresApproval, tools["serial_connect"].Safety, "opening can reset a board through DTR");
        Assert.AreEqual(ToolSafety.RequiresApproval, tools["serial_send"].Safety);

        Assert.IsTrue(Run(tools["serial_send"], new() { ["text"] = "AT" }).IsError, "nothing is sent before connecting");
        var connected = Run(tools["serial_connect"], new() { ["baud"] = 57600 });
        Assert.IsFalse(connected.IsError, connected.Summary);
        Assert.AreEqual(57600, _device.OpenedWith!.BaudRate);

        // The far end answers while the tool waits for it.
        _ = Task.Run(async () => { await Task.Delay(50); _device.Send("OK\r\n"); });
        var sent = Run(tools["serial_send"], new() { ["text"] = "AT", ["wait_ms"] = 300 });
        StringAssert.Contains(sent.ModelText, "← OK");
        Assert.AreEqual(0, _asked, "the approval the agent's call had is the one the user gave; the page does not ask again");

        StringAssert.Contains(Run(tools["serial_read"], new() { ["lines"] = 10 }).ModelText, "→ AT");
        StringAssert.Contains(vm.GetContext(), "57600 8N1");
        StringAssert.Contains(Run(tools["serial_ports"], new()).ModelText, "COM5 — USB Serial Device");
    }

    static ToolResult Run(IClientTool tool, JsonObject args) => tool.InvokeAsync(args, CancellationToken.None).GetAwaiter().GetResult();
}
