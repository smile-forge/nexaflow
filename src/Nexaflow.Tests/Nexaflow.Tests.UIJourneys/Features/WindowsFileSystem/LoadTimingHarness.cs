using System;
using System.Threading;
using Nexaflow.Tests.UIJourneys.Infrastructure;

using Nexaflow.Tests.Fixtures;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// Measurement harness — drives the file browser through This PC and then a list of folders, letting the
/// app's own <c>Timing</c> instrumentation record where each load spends its time. It asserts nothing about
/// duration; it exists so a number can be produced on demand, before and after a change.
/// <para>
/// It switches that instrumentation on in the app it launches, and the app writes its lines to
/// <c>TestResults\load-timing\</c> under the checkout — which Windows Sandbox maps back, so a run there (slower than
/// a developer's machine, and nearer what most people have) leaves its numbers on the host. The log's path and its
/// lines are also written to the test's output.
/// </para>
/// <para>
/// Set <c>NEXAFLOW_TEST_TIMING_PATHS</c> to a semicolon-separated list of folders (default <c>C:\</c>) to point it at
/// whatever actually feels slow — a big folder, a spinning disk, a network share — without a rebuild. The sandbox
/// forwards every <c>NEXAFLOW_TEST_*</c> variable set when it is started. Note <c>NEXAFLOW_STARTUP_TIMING</c> is a
/// different switch and the wrong one here: it makes the app shut down at first render.
/// </para>
/// Interactive desktop only.
/// </summary>
[TestClass]
[NoCoverage("performance harness — measures, asserts nothing")]
public class LoadTimingHarness : UiJourneyTestBase
{
    private const string PathsVariable = "NEXAFLOW_TEST_TIMING_PATHS";

    /// <summary>Where the launched app appends its timing lines — one file per run.</summary>
    private readonly string _timingLog = Path.Combine(
        TestSampleData.RepoRoot, "TestResults", "load-timing", $"{DateTime.Now:yyyyMMdd-HHmmss}.log");

    // The app appends to the log and falls back to stderr when it cannot, so the folder must exist before launch.
    public LoadTimingHarness() => Directory.CreateDirectory(Path.GetDirectoryName(_timingLog)!);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment => new Dictionary<string, string>
    {
        ["NEXAFLOW_TIMING"]     = "1",
        ["NEXAFLOW_TIMING_LOG"] = _timingLog,
    };

    // No LaunchTabKind: the default file-browser tab lands on This PC, which is measurement one.
    [TestMethod]
    [TestCategory("Interactive")]
    public void Measure_ThisPc_ThenEachPath()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 30), "file browser did not open");
        Thread.Sleep(6000);   // let every drive's background probe report

        var paths = Environment.GetEnvironmentVariable(PathsVariable) is { Length: > 0 } list
            ? list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [@"C:\"];

        foreach (var path in paths)
        {
            // Never fail the run on navigation: what was gathered so far is already on disk, and a path
            // that does not exist on this machine should skip rather than lose the whole measurement.
            try { NavigateFileBrowserTo(path); } catch { continue; }
            Thread.Sleep(8000);   // let the streaming load finish reporting
        }

        TestContext.WriteLine($"Timing log: {_timingLog}");
        if (File.Exists(_timingLog))
            foreach (var line in File.ReadAllLines(_timingLog)) TestContext.WriteLine(line);
    }
}
