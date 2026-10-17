using System.IO;
using Nexaflow.AiHost.Client;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.AiPaths;

/// <summary>Checking the machine against Nexaflow's own model, for real: it replies, each GPU is measured, and the
/// report says so.</summary>
[TestClass]
[CoversNode("sysinfo-local-ai")]
public class LocalAiCheckTests
{
    [ClassCleanup]
    public static Task Stop() => OwnedModelHarness.StopAsync();

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task TheCheck_HasTheModelReply_MeasuresEachGpu_AndWritesItsReport()
    {
        var model = await OwnedModelHarness.ModelAsync();
        var reports = TestTemp.CreateSubdirectory("nexaflow-check-").FullName;
        var used = new OwnedModelInUse("gemma-4-e4b-it", "Gemma 4 E4B", ModelKind.Language, model, (IHostModelCatalog)model);
        var stages = new List<LocalAiCheckStage>();

        var check = await new LocalAiChecker(OwnedModelHarness.Setup!, [used], reports)
            .RunAsync(new Inline<LocalAiCheckProgress>(p => stages.Add(p.Stage)), default);

        string report = await File.ReadAllTextAsync(check.ReportPath);
        Assert.IsTrue(check.Passed, report);
        CollectionAssert.AreEqual(new[] { LocalAiCheckStage.Describing, LocalAiCheckStage.Loading, LocalAiCheckStage.Replying }, stages.ToArray());
        StringAssert.Contains(report, "✅ **Reply** — first word after");
        StringAssert.Contains(report, "| Gemma 4 E4B |");
        StringAssert.Contains(report, "## Memory");
    }

    /// <summary>Progress reported as it happens, on the reporting thread.</summary>
    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
