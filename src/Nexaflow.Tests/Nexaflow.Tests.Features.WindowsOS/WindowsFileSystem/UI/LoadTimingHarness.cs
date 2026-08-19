using System;
using System.Threading;
using Nexaflow.Tests.Features.UI.Infrastructure;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// Measurement harness — drives the file browser through This PC and then a list of folders, letting the
/// app's own <c>Timing</c> instrumentation record where each load spends its time. It asserts nothing about
/// duration; it exists so a number can be produced on demand, before and after a change.
/// <para>
/// Run with <c>NEXAFLOW_TIMING=1</c> and <c>NEXAFLOW_TIMING_LOG=&lt;file&gt;</c>, and set
/// <c>NEXAFLOW_TIMING_PATHS</c> to a semicolon-separated list of folders (default <c>C:\</c>) — so it can be
/// pointed at whatever actually feels slow (a big folder, a spinning disk, a network share) without a
/// rebuild. Note <c>NEXAFLOW_STARTUP_TIMING</c> is a different switch and the wrong one here: it makes the
/// app shut down at first render.
/// </para>
/// Interactive desktop only.
/// </summary>
[TestClass]
[NoCoverage("performance harness — measures, asserts nothing")]
public class LoadTimingHarness : UiJourneyTestBase
{
    // No LaunchTabKind: the default file-browser tab lands on This PC, which is measurement one.
    [TestMethod]
    [TestCategory("Interactive")]
    public void Measure_ThisPc_ThenEachPath()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 30), "file browser did not open");
        Thread.Sleep(6000);   // let every drive's background probe report

        var paths = Environment.GetEnvironmentVariable("NEXAFLOW_TIMING_PATHS") is { Length: > 0 } list
            ? list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [@"C:\"];

        foreach (var path in paths)
        {
            // Never fail the run on navigation: what was gathered so far is already on disk, and a path
            // that does not exist on this machine should skip rather than lose the whole measurement.
            try { NavigateFileBrowserTo(path); } catch { continue; }
            Thread.Sleep(8000);   // let the streaming load finish reporting
        }
    }
}
