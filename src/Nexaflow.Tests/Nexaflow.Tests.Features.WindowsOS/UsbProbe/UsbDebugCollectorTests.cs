using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe;
using Nexaflow.IO.Usb.Diagnostics;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// Taking a device's debug details: from the USB page every provider is offered the device and each tool asked and let
/// go; on the device page the tools open there are asked and kept; a tool that fails or never answers is recorded and
/// the rest still are; and a tool asked from the USB page can reach no one.
/// </summary>
[TestClass]
[CoversNode("usbprobe-debug")]
public class UsbDebugCollectorTests
{
    static readonly string HidFunction = UsbProbeSamples.LgId.Replace("9A39", "9A39&MI_00");
    static readonly string SerialFunction = UsbProbeSamples.LgId.Replace("9A39", "9A39&MI_01");

    string _dir = "";
    IShellServices _shell = null!;
    readonly List<FakeTool> _made = [];

    [TestInitialize]
    public void Setup()
    {
        _dir = Directory.CreateTempSubdirectory("nexaflow-usbdebug-").FullName;
        _shell = Substitute.For<IShellServices>();
        _shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>()();
            return Task.CompletedTask;
        });
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    UsbDebugCollector Collector(params IUsbToolProvider[] providers) =>
        new([.. providers.Select((p, i) => (ISubfeatureHandle<IUsbToolProvider>)new FakeHandle<IUsbToolProvider>(p, $"p{i}"))], _shell, new UsbIdsStore(_dir, () => null));

    /// <summary>A HID tool that answers with what it was asked, and a serial one with nothing to add.</summary>
    FakeProvider Tools(Func<IUsbToolHost, UsbDebugMask, CancellationToken, Task<JsonObject>> answer) => new("tools", (device, host) =>
    {
        FakeTool[] tools = [new FakeDebugTool("test.hid", device.Function(HidFunction)?.InstanceId, (mask, ct) => answer(host, mask, ct)),
            new FakeTool("test.serial", device.Function(SerialFunction)?.InstanceId)];
        _made.AddRange(tools);
        return tools;
    });

    static JsonNode Parse(string text) => JsonNode.Parse(text)!;

    [TestMethod]
    public async Task FromTheUsbPage_EachToolOffered_IsAsked_ThenLetGo()
    {
        var broken = new FakeProvider("broken", (_, _) => throw new InvalidOperationException("no plugin today"));
        string text = await Collector(Tools((_, _, _) => Task.FromResult(new JsonObject { ["seen"] = "layout" })), broken)
            .TextAsync(UsbProbeSamples.Lg(), null, [], CancellationToken.None);
        var details = Parse(text);
        var tools = details["tools"]!.AsArray();

        Assert.AreEqual(2, tools.Count);
        Assert.AreEqual("layout", (string?)tools[0]!["details"]!["seen"]);
        Assert.IsNull(tools[1]!["details"], "a tool with nothing to add is listed with its status");
        Assert.AreEqual("0 written", (string?)tools[1]!["status"]);
        Assert.IsTrue(details["log"]!.AsArray().Any(l => ((string)l!).StartsWith("p1: offering tools failed") && ((string)l!).Contains("no plugin today")));
        Assert.IsTrue(_made.All(t => t.Disposed), "tools made for the details are let go");
        Assert.IsFalse(text.Contains("902NTPC6R928"), "the serial in the instance id is hidden");
    }

    [TestMethod]
    public async Task OnTheDevicePage_TheOpenTools_AreAsked_AndKept()
    {
        var provider = Tools((_, _, _) => Task.FromResult(new JsonObject()));
        var open = new FakeDebugTool("test.hid", HidFunction, (_, _) => Task.FromResult(new JsonObject { ["open"] = true }));

        var details = Parse(await Collector(provider).TextAsync(UsbProbeSamples.Lg(), [open], ["10:00 HID: opened"], CancellationToken.None));

        Assert.AreEqual(0, provider.Offers, "the page's own tools answer: none are made");
        Assert.IsFalse(open.Disposed);
        Assert.AreEqual(true, (bool?)details["tools"]![0]!["details"]!["open"]);
        Assert.AreEqual("10:00 HID: opened", (string?)details["log"]![0]);
        Assert.IsTrue(details["notCovered"]!.AsArray().Count > 0, "the serial function no open tool works through is named");
    }

    [TestMethod]
    public async Task AToolThatFails_OrNeverAnswers_IsRecorded_AndTheRestStillAnswer()
    {
        var failing = new FakeDebugTool("test.fails", HidFunction, (_, _) => throw new IOException("the device went"));
        var silent = new FakeDebugTool("test.silent", HidFunction, (_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => new JsonObject()));
        var fine = new FakeDebugTool("test.fine", SerialFunction, (_, _) => Task.FromResult(new JsonObject { ["ok"] = true }));

        var tools = Parse(await new UsbDebugCollector([], _shell, new UsbIdsStore(_dir, () => null)) { Patience = TimeSpan.FromMilliseconds(100) }
            .TextAsync(UsbProbeSamples.Lg(), [failing, silent, fine], [], CancellationToken.None))["tools"]!.AsArray();

        StringAssert.Contains((string)tools[0]!["error"]!, "IOException: the device went");
        StringAssert.Contains((string)tools[1]!["error"]!, "no answer");
        Assert.AreEqual(true, (bool?)tools[2]!["details"]!["ok"]);
    }

    [TestMethod]
    public async Task AToolAskedFromTheUsbPage_ReachesNoOne_AndWhatItLogsIsKept()
    {
        bool? confirmed = null;
        var details = Parse(await Collector(Tools(async (host, _, ct) =>
        {
            confirmed = await host.ConfirmAsync("Write?", "write something", ct);
            host.Log(_made[0], "looked, wrote nothing");
            return new JsonObject();
        })).TextAsync(UsbProbeSamples.Lg(), null, [], CancellationToken.None));

        Assert.AreEqual(false, confirmed, "nothing is put to the user");
        await _shell.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default!, default, default, default);
        Assert.IsTrue(details["log"]!.AsArray().Any(l => (string?)l == "Tool test.hid: looked, wrote nothing"));
    }

    [TestMethod]
    public async Task Saved_AsJson_NamedForTheDevice()
    {
        Assert.AreEqual("usb-043e-9a39-debug.json", UsbDebugCollector.FileNameOf(UsbProbeSamples.Lg()));
        string path = Path.Combine(_dir, "lg.json");

        string status = await Collector().SaveAsync(path, UsbProbeSamples.Lg(), null, [], CancellationToken.None);

        StringAssert.Contains(status, path);
        Assert.AreEqual("LG Monitor Controls", (string?)Parse(File.ReadAllText(path))["device"]!["strings"]!["byIndex"]!["3"]);
    }
}
