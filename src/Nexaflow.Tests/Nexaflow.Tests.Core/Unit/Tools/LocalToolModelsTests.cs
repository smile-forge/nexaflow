using System.IO;
using Nexaflow.AiHost.Client;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit.Tools;

/// <summary>A tool's download as everything that offers it sees it: coming while it comes, and when it stops short, to be
/// downloaded again with why.</summary>
[TestClass]
[CoversNode("ai-tools")]
public class LocalToolModelsTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup() => _root = TestTemp.CreateSubdirectory("nexaflow-tool-models-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>Page reading on a machine it runs on, fetched from a store that cannot download — as a download stops when
    /// the network goes.</summary>
    private PageReading Unfetchable()
    {
        var store = new ModelAssetStore(_root, [], downloadsAllowed: false);
        var host = Substitute.For<IAiHost>();
        var nvidia = new ComputeHardwareReport([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 16L << 30, IsIntegrated: false, IsNpu: false)],
                                               CudaRuntimePack.MinimumDriver);
        return new PageReading(new OwnedPageReading(Substitute.For<IBackgroundActivityManager>(), store, host, new HostModelSetup(host), nvidia),
                               store, null);
    }

    [TestMethod]
    public async Task ADownloadThatStops_IsOverWhenItSaysSo_AndOfferedAgainWithWhy()
    {
        var tool = Unfetchable();
        var heard = new List<(ToolModelsState State, string? Failure)>();
        var over = new TaskCompletionSource();
        tool.Changed += (_, _) =>
        {
            lock (heard) heard.Add((tool.State, tool.DownloadFailure));
            if (tool.State == ToolModelsState.NotDownloaded) over.TrySetResult();
        };

        Assert.AreEqual(ToolModelsState.NotDownloaded, tool.State);
        Assert.IsNull(tool.DownloadFailure, "nothing has stopped before anything was asked");

        tool.Download();
        await over.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.AreEqual(ToolModelsState.NotDownloaded, tool.State, "a download that stopped leaves the tool to be downloaded");
        StringAssert.Contains(tool.DownloadFailure, "downloads are off", "and says why");
        Assert.AreEqual((ToolModelsState.NotDownloaded, tool.DownloadFailure), heard[^1],
                        "the last word heard is where the download ended, not that it is still coming");
    }

    [TestMethod]
    public async Task AskingAgain_ForgetsWhyTheLastOneStopped()
    {
        var tool = Unfetchable();
        var over = new TaskCompletionSource();
        tool.Changed += (_, _) => { if (tool.State == ToolModelsState.NotDownloaded) over.TrySetResult(); };
        tool.Download();
        await over.Task.WaitAsync(TimeSpan.FromSeconds(30));

        string? whileComing = "unread";
        tool.Changed += (_, _) => { if (tool.State == ToolModelsState.Downloading) whileComing = tool.DownloadFailure; };
        tool.Download();

        Assert.IsNull(whileComing, "while it comes again, the last stop is not said");
    }
}
