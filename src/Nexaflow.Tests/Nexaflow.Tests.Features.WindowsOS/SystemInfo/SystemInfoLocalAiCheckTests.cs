using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

/// <summary>The Local AI card's check of the machine: offered where the workspace runs models of Nexaflow's own, and
/// its report opened once it has run.</summary>
[TestClass]
[CoversNode("sysinfo-local-ai")]
public class SystemInfoLocalAiCheckTests
{
    private static readonly LocalAiSetup Gemma = new([new LocalAiGpu(0, "NVIDIA GeForce RTX 4090", 24L << 30, 21L << 30)],
        [new LocalAiModel("Gemma 4 E4B", 0, 6L << 30, 32768, OnDemand: false, Displaces: false, Fits: true)], []);

    /// <summary>A page gathered once: the shell runs the gather it is handed and reports it done.</summary>
    private static async Task<(SystemInfoViewModel Vm, IShellServices Shell)> GatheredAsync(IAIService ai)
    {
        IBackgroundTask? task = null;
        Action<bool>? complete = null;
        var shell = Substitute.For<IShellServices>();
        shell.When(s => s.QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>()))
             .Do(ci => (task, complete) = (ci.Arg<IBackgroundTask>(), ci.Arg<Action<bool>>()));

        var vm = new SystemInfoViewModel(shell, ai);
        await task!.RunAsync(default);
        complete!(true);
        return (vm, shell);
    }

    private static SystemInfoSection LocalAiCard(SystemInfoViewModel vm) => vm.Sections.Single(s => s.Title == "Local AI");

    [TestMethod]
    public async Task TheCheck_IsOffered_OnlyWhereTheWorkspaceRunsModelsOfItsOwn()
    {
        var none = Substitute.For<IAIService>();
        var owned = Substitute.For<IAIService>();
        owned.DescribeLocalAiAsync(Arg.Any<CancellationToken>()).Returns(Gemma);

        Assert.IsNull(LocalAiCard((await GatheredAsync(none)).Vm).Action);
        Assert.AreEqual("Check this machine", LocalAiCard((await GatheredAsync(owned)).Vm).Action!.Label);
        Assert.AreEqual("GPU 0 · 6.0 GB · a context of 32,768 tokens", LocalAiCard((await GatheredAsync(owned)).Vm).Items[0].Value);
    }

    [TestMethod]
    public async Task ACheckThatRuns_OpensItsReport_AndSaysItPassed()
    {
        var ai = Substitute.For<IAIService>();
        ai.DescribeLocalAiAsync(Arg.Any<CancellationToken>()).Returns(Gemma);
        ai.CheckLocalAiAsync(Arg.Any<IProgress<LocalAiCheckProgress>>(), Arg.Any<CancellationToken>())
          .Returns(new LocalAiCheck(@"C:\ai\reports\local-ai-check.md", Passed: true));
        var (vm, shell) = await GatheredAsync(ai);
        var check = LocalAiCard(vm).Action!;

        await check.Command.ExecuteAsync(null);

        shell.Received(1).OpenTab("Markdown", Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\ai\reports\local-ai-check.md"),
                                  Arg.Any<IPageView?>(), Arg.Any<bool>());
        Assert.AreEqual(("Passed — the report is open in a tab", SystemInfoStatus.Good), (check.Outcome, check.OutcomeStatus));
        Assert.AreEqual("Check this machine", check.Label, "the button says what it does again once the check is done");
    }

    [TestMethod]
    public async Task ACheckThatCannotRun_SaysWhy_AndOpensNothing()
    {
        var ai = Substitute.For<IAIService>();
        ai.DescribeLocalAiAsync(Arg.Any<CancellationToken>()).Returns(Gemma);
        ai.CheckLocalAiAsync(Arg.Any<IProgress<LocalAiCheckProgress>>(), Arg.Any<CancellationToken>())
          .ThrowsAsync(new InvalidOperationException("The AI host could not be reached."));
        var (vm, shell) = await GatheredAsync(ai);
        var check = LocalAiCard(vm).Action!;

        await check.Command.ExecuteAsync(null);

        shell.DidNotReceive().OpenTab(Arg.Any<string>(), Arg.Any<Dictionary<string, string>?>(), Arg.Any<IPageView?>(), Arg.Any<bool>());
        Assert.AreEqual(("The AI host could not be reached.", SystemInfoStatus.Bad), (check.Outcome, check.OutcomeStatus));
    }
}
