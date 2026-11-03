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

    [TestMethod]
    public async Task APictureCheck_FetchesItsClassifierWhenFirstAsked_AndFailsClosedWithWhy_WhenItWillNotCome()
    {
        var store = new ModelAssetStore(_root, [], downloadsAllowed: false);
        var host = Substitute.For<IAiHost>();
        var check = new PictureChecking(new OwnedPictureChecking(Substitute.For<IBackgroundActivityManager>(), store, host, new HostModelSetup(host),
                                                                 new ComputeHardwareReport([], null)), store, null);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            ((Nexaflow.Core.Services.Generation.IPictureCheck)check).FitForGeneralAudienceAsync(new MediaBytes([1], "image/png"), CancellationToken.None));

        StringAssert.Contains(ex.Message, "could not be downloaded", "asking for a check started its download");
        StringAssert.Contains(ex.Message, "downloads are off", "and says why it did not come");
        Assert.AreEqual(ToolModelsState.NotDownloaded, check.State, "left to be downloaded again");
    }

    [TestMethod]
    [DataRow(0.1f, 0.39f, true, DisplayName = "under the line together")]
    [DataRow(0.0f, 0.5f, false, DisplayName = "sexual content alone at the line")]
    [DataRow(0.3f, 0.2f, false, DisplayName = "gore and sexual content together at the line")]
    public void APicture_IsFit_WhileGoreAndSexualContentTogetherStayUnderTheLine(float gore, float sexual, bool fit)
    {
        Assert.AreEqual(fit, PictureChecking.IsFit(new PictureVerdict(gore, sexual, 1 - gore - sexual)));
    }

    /// <summary>A tool with nothing of its own, for asking jobs through its base as every tool does.</summary>
    private sealed class Asking : LocalToolModels
    {
        private Asking(OwnedToolFront front, IModelAssetStore store) : base(front, store, null) { }

        public override string Name => "asking";

        public override string About => "";

        public static Task<T> Ask<T>(Func<Task<T>> run) => AskedAsync(run, CancellationToken.None);

        public static Task<T> Tell<T>(Func<Task<T>> run) => ToldAsync(run, CancellationToken.None);
    }

    /// <summary>A job that fails with each of <paramref name="failures"/> in turn, then answers 7; how many times it was asked.</summary>
    private static (Func<Task<int>> Run, Func<int> Asked) Failing(params Exception[] failures)
    {
        int asked = 0;
        return (() => asked++ < failures.Length ? Task.FromException<int>(failures[asked - 1]) : Task.FromResult(7), () => asked);
    }

    [TestMethod]
    public async Task AHostLostWhileAJobRuns_IsAskedOnceMore_AndTwiceIsInterrupted()
    {
        var (run, asked) = Failing(new AiHostException(AiHostFailure.Lost, "the AI host stopped answering for 10 s"));
        Assert.AreEqual(7, await Asking.Ask(run), "lost once, the job is asked again on a fresh host and answers");
        Assert.AreEqual(2, asked());

        var lost = new AiHostException(AiHostFailure.Lost, "the AI host stopped answering for 10 s");
        (run, asked) = Failing(lost, lost, lost);
        var twice = await Assert.ThrowsExactlyAsync<ToolRunException>(() => Asking.Ask(run));
        Assert.AreEqual(ToolRunFailure.Interrupted, twice.Failure);
        Assert.IsTrue(twice.MayWorkAgain, "a host in the way for now may not be later");
        Assert.AreEqual(2, asked(), "asked once more, never on and on");
    }

    [TestMethod]
    public async Task AJobRefusedOrFailed_IsNeverAskedAgain_AndSaysWhetherAskingAgainCanHelp()
    {
        var (run, asked) = Failing(new AiHostException(AiHostFailure.Request, "no room", Nexaflow.AiHost.Contracts.HostErrorKind.Refused));
        var refused = await Assert.ThrowsExactlyAsync<ToolRunException>(() => Asking.Ask(run));
        Assert.AreEqual((ToolRunFailure.NotNow, true, 1), (refused.Failure, refused.MayWorkAgain, asked()), "no room now: asking again later can work");

        // As the fronts give it: the host's failure inside the front's own words.
        (run, asked) = Failing(new InvalidOperationException("The picture's depth could not be estimated: bad input",
                                                             new AiHostException(AiHostFailure.Request, "bad input", Nexaflow.AiHost.Contracts.HostErrorKind.Runtime)));
        var failed = await Assert.ThrowsExactlyAsync<ToolRunException>(() => Asking.Ask(run));
        Assert.AreEqual((ToolRunFailure.Unworkable, false, 1), (failed.Failure, failed.MayWorkAgain, asked()), "the same ask fails the same way");
        StringAssert.Contains(failed.Message, "could not be estimated", "and says so in the front's words");

        (run, asked) = Failing(new LlmProviderException("does not fit beside the other models on this machine's GPUs"));
        Assert.AreEqual(ToolRunFailure.NotNow, (await Assert.ThrowsExactlyAsync<ToolRunException>(() => Asking.Ask(run))).Failure, "no room beside the others");

        (run, _) = Failing(new InvalidOperationException("A picture to estimate the depth of has a size."));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Asking.Ask(run), "what is not the host's is the caller's own to tell");
    }

    [TestMethod]
    public async Task AJobThatHandsOverAsItGoes_IsNeverAskedAgain_EvenWhereTheHostIsLost()
    {
        var (run, asked) = Failing(new AiHostException(AiHostFailure.Lost, "the AI host exited"));
        var lost = await Assert.ThrowsExactlyAsync<ToolRunException>(() => Asking.Tell(run));
        Assert.AreEqual((ToolRunFailure.Interrupted, 1), (lost.Failure, asked()), "what it handed over would be handed over twice");
    }
}
