using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.Features.UsbProbe.HidTools;
using Nexaflow.IO.Usb.Hid;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// The HID tool over collections the test makes up: a layout to read, input to watch, feature reports to read, and
/// reports to send — only with a yes, and only where Windows lets anything be sent.
/// </summary>
[TestClass]
[CoversNode("usbprobe-hid")]
public class UsbHidToolTests
{
    sealed class Port(HidCollection collection) : IHidChannel
    {
        public readonly Channel<HidReport> Input = Channel.CreateUnbounded<HidReport>();
        public readonly List<(HidReportKind Kind, byte[] Bytes)> Sent = [];
        public bool Disposed;

        public HidCollection Collection => collection;

        public async IAsyncEnumerable<HidReport> ReadInputAsync([EnumeratorCancellation] CancellationToken ct)
        {
            while (true)
            {
                HidReport report;
                try { report = await Input.Reader.ReadAsync(ct); }
                catch (OperationCanceledException) { yield break; }
                yield return report;
            }
        }

        public Task<HidReport> GetFeatureAsync(byte reportId, CancellationToken ct) =>
            Task.FromResult(new HidReport(DateTimeOffset.Now, HidReportKind.Feature, reportId, [reportId, 0x10], [new HidValue("Brightness", "16")]));

        public Task SetFeatureAsync(byte[] report, CancellationToken ct) { Sent.Add((HidReportKind.Feature, report)); return Task.CompletedTask; }
        public Task SetOutputAsync(byte[] report, CancellationToken ct) { Sent.Add((HidReportKind.Output, report)); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }

    sealed class Access(HidCollection collection) : IHidAccess
    {
        public Port? Opened;
        public IReadOnlyList<HidCollection> CollectionsOf(UsbFunctionNode function, UsbIdsDatabase? names) => [collection];
        public IHidChannel Open(HidCollection c) => Opened = new Port(c);
    }

    static HidCollection Collection(HidReach reach) => new(
        @"HID\VID_043E&PID_9A39&MI_00\9&1", @"\\?\hid#test", 0xFF00, 0x01, "Vendor Specific › 0x01", "LG Monitor Controls",
        InputLength: 3, OutputLength: 3, FeatureLength: 3,
        Reports:
        [
            new(HidReportKind.Input, 1, [new HidField(HidReportKind.Input, 1, 0x09, 1, 3, true, 1, 3, 0, 1, true, 0, "Buttons 1–3")]),
            new(HidReportKind.Feature, 2, [new HidField(HidReportKind.Feature, 2, 0x82, 0x10, 0x10, false, 8, 1, 0, 100, true, 0, "Brightness")]),
            new(HidReportKind.Output, 3, [new HidField(HidReportKind.Output, 3, 0x08, 1, 1, false, 8, 1, 0, 255, true, 0, "LED")]),
        ],
        reach);

    RecordingHost _host = null!;

    [TestInitialize]
    public void Setup() => _host = new RecordingHost();

    async Task<(HidTool Tool, Access Access)> ToolAsync(HidReach reach)
    {
        var access = new Access(Collection(reach));
        var tool = (HidTool)new UsbHidToolProvider(access).Offer(UsbProbeSamples.Lg(), _host).Single();
        await tool.ActivateAsync(CancellationToken.None);
        return (tool, access);
    }

    [TestMethod]
    public void TheHidFunction_IsOffered_AndTheSerialOneIsNot()
    {
        var tools = new UsbHidToolProvider(new Access(Collection(HidReach.ReadWrite))).Offer(UsbProbeSamples.Lg(), _host);
        Assert.AreEqual(1, tools.Count);
        StringAssert.Contains(tools[0].FunctionInstanceId, "MI_00");
    }

    [TestMethod]
    public async Task ItShows_TheLayout_AndTheFeatureReports()
    {
        var (tool, _) = await ToolAsync(HidReach.ReadWrite);

        Assert.AreEqual(3, tool.Fields.Count);
        Assert.AreEqual("0x01", tool.Fields[0].ReportId);
        Assert.AreEqual("8", tool.Fields[1].Bits);
        Assert.AreEqual("0 … 100", tool.Fields[1].Range);
        CollectionAssert.AreEqual(new byte[] { 2 }, tool.FeatureIds.ToArray());
        Assert.IsTrue(tool.CanMonitor && tool.CanWrite && tool.HasOutput);
        Assert.IsNull(tool.ReachNote);

        await tool.ReadFeatureCommand.ExecuteAsync(null);
        StringAssert.Contains(tool.FeatureResult, "02 10");
        StringAssert.Contains(tool.FeatureResult, "Brightness 16");
    }

    [TestMethod]
    public async Task AKeyboardOrMouse_ShowsItsLayout_ButOffersNoMonitorAndNoSending()
    {
        var (tool, access) = await ToolAsync(HidReach.LayoutOnly);

        Assert.IsFalse(tool.CanMonitor);
        Assert.IsFalse(tool.CanWrite);
        Assert.IsNotNull(tool.ReachNote, "the tile says why");
        tool.StartMonitorCommand.Execute(null);
        Assert.IsFalse(tool.IsMonitoring);

        var said = await tool.Operations.Single(o => o.Name == "send_output").RunAsync(new Dictionary<string, string> { ["hex"] = "03 01" }, CancellationToken.None);
        Assert.IsNull(access.Opened, "nothing was opened to send on");
        StringAssert.Contains(said, "does not take reports");
    }

    [TestMethod]
    public async Task TheMonitor_ListsInput_NewestFirst_AndStopsWhenTheTileShuts()
    {
        var (tool, access) = await ToolAsync(HidReach.ReadWrite);
        tool.StartMonitorCommand.Execute(null);
        Assert.IsTrue(tool.IsMonitoring && tool.IsBusy);

        for (byte b = 0; b < 3; b++)
            await access.Opened!.Input.Writer.WriteAsync(new HidReport(DateTimeOffset.Now, HidReportKind.Input, 1, [1, b], [new HidValue("Buttons", b.ToString())]));
        for (int i = 0; i < 100 && tool.ReportCount < 3; i++) await Task.Delay(20);

        Assert.AreEqual(3, tool.ReportCount);
        Assert.AreEqual("01 02", tool.Reports[0].Hex, "newest first");
        Assert.AreEqual("Buttons 2", tool.Reports[0].Values);

        tool.Deactivate();
        Assert.IsFalse(tool.IsMonitoring);
        await tool.ActivateAsync(CancellationToken.None);
        Assert.IsTrue(tool.IsMonitoring, "opened again, it watches again");
        tool.Dispose();
        Assert.IsTrue(access.Opened.Disposed);
    }

    [TestMethod]
    public async Task SendingAReport_AsksFirst()
    {
        var (tool, access) = await ToolAsync(HidReach.ReadWrite);
        tool.FeatureHex = "02 2A";

        _host.Confirm = false;
        await tool.SendFeatureCommand.ExecuteAsync(null);
        Assert.AreEqual(0, access.Opened?.Sent.Count ?? 0, "declined, nothing sent");
        StringAssert.Contains(_host.Confirmations.Single(), "02 2A");

        _host.Confirm = true;
        await tool.SendFeatureCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new byte[] { 2, 0x2A }, access.Opened!.Sent.Single().Bytes);
        Assert.AreEqual(HidReportKind.Feature, access.Opened.Sent.Single().Kind);

        tool.FeatureHex = "zz";
        await tool.SendFeatureCommand.ExecuteAsync(null);
        StringAssert.Contains(tool.SendResult, "not hex");
        Assert.AreEqual(2, _host.Confirmations.Count, "bad hex never reaches the question");
    }

    [TestMethod]
    public async Task TheAgent_ReadsTheLayout_AndListens()
    {
        var (tool, access) = await ToolAsync(HidReach.ReadWrite);
        StringAssert.Contains(await tool.Operations.Single(o => o.Name == "layout").RunAsync(new Dictionary<string, string>(), CancellationToken.None), "Brightness");

        var listening = tool.Operations.Single(o => o.Name == "listen").RunAsync(new Dictionary<string, string> { ["seconds"] = "1" }, CancellationToken.None);
        for (int i = 0; i < 50 && access.Opened is null; i++) await Task.Delay(10);
        await access.Opened!.Input.Writer.WriteAsync(new HidReport(DateTimeOffset.Now, HidReportKind.Input, 1, [1, 5], [new HidValue("Buttons", "1, 3")]));

        StringAssert.Contains(await listening, "Buttons 1, 3");
        Assert.IsFalse(tool.IsMonitoring, "it listened for the agent and stopped again");
        Assert.IsFalse(tool.Operations.Single(o => o.Name == "read_feature").ChangesSomething);
        Assert.IsTrue(tool.Operations.Single(o => o.Name == "send_feature").ChangesSomething);
    }

    [TestMethod]
    public void Hex_IsReadHoweverItIsTyped()
    {
        foreach (var typed in new[] { "02 01 ff", "0201FF", "0x02,0x01,0xff", "2 1 ff" })
        {
            Assert.IsTrue(HidHex.TryParse(typed, out var bytes, out _), typed);
            CollectionAssert.AreEqual(new byte[] { 2, 1, 0xFF }, bytes, typed);
        }
        Assert.IsFalse(HidHex.TryParse("", out _, out _));
        Assert.IsFalse(HidHex.TryParse("0g", out _, out _));
    }
}
