using System;
using System.IO;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Executable.UI;

/// <summary>
/// One-pass UI journey for the PE inspector. Opens a real Windows binary through the "Inspect"
/// action, then walks every section and presses every control that is safe to press — the point
/// being that a section which throws on activation takes the whole tab down, and only a journey
/// catches that.
/// <para>
/// Each section is checked <em>after</em> it is selected, because the expensive work is deliberately
/// lazy: the dependency walk, the string sweep and the signature check only start when their tab is
/// first shown. Clicking through is therefore what actually exercises them.
/// </para>
/// <para>
/// Two controls are deliberately not pressed: "Extract all…" and the resource "Extract…" open a
/// modal file picker, which would block the journey until it timed out, so they are covered by unit
/// tests instead. The antivirus scan <em>is</em> pressed — it is background work with no modal
/// surface, and it is the one control that reaches into another subsystem, so it is worth proving
/// it cannot take the app down.
/// </para>
/// <para>Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[CoversNode("executable-inspector")]
public class ExecutableJourneyTests : UiJourneyTestBase
{
    private const string SampleBinary = "Nexaflow.IO.Pe.dll";

    /// <summary>
    /// Stages one of our own build outputs in a folder of its own.
    /// <para>
    /// Deliberately not a system binary: <c>notepad.exe</c> is a removable optional feature on
    /// Windows 11, so a machine can legitimately not have it, and a UI test that fails on a
    /// perfectly good build is worse than no test. This assembly is referenced by the test project,
    /// so it is beside the test binary wherever the suite runs.
    /// </para>
    /// <para>
    /// Copied to its own folder rather than opened where it sits, because the file browser has to
    /// select the row: in a folder of several hundred build outputs the row is virtualised
    /// off-screen and would never be found.
    /// </para>
    /// </summary>
    private static string StageSampleBinary()
    {
        string folder = Path.Combine(Path.GetTempPath(), "nexaflow-pe-journey");
        Directory.CreateDirectory(folder);

        string source = Path.Combine(AppContext.BaseDirectory, SampleBinary);
        Assert.IsTrue(File.Exists(source),
            $"'{SampleBinary}' should sit beside the test assembly; the journey needs a real PE to open.");

        string target = Path.Combine(folder, SampleBinary);
        File.Copy(source, target, overwrite: true);
        return folder;
    }

    [TestMethod]
    [CoversNode("ui-2")]
    public void Executable_Sections_RespondInOnePass()
    {
        string folder = StageSampleBinary();

        var view = OpenFileVia(folder, SampleBinary, "Inspect", "ExecutableView", seconds: 25);
        Assert.IsNotNull(view, "ExecutableView did not open via the Inspect action.");

        // ── Overview: the parse has to land before anything else is meaningful ──
        Check("Header shows the file name", () => WaitForName(SampleBinary, 10) is not null);
        CheckPresent("Sections tree", "Executable_SectionTree", 20);

        // ── Imports / exports ───────────────────────────────────────────────────
        CheckInvoke("Imports / Exports tab", "Executable_Tab_ImportsExports");
        CheckPresent("Import tree", "Executable_ImportTree");
        CheckPresent("Export list", "Executable_ExportList");

        // ── Dependencies: the walk starts on first activation ───────────────────
        CheckInvoke("Dependencies tab", "Executable_Tab_Dependencies");
        CheckDoes("Tree/diagram toggle", "Executable_DependencyTreeToggle",
                  () => WaitForId("Executable_DependencyTree", 10) is not null);
        CheckInvoke("Collapse all", "Executable_CollapseDependencies");

        // ── Resources. Extract is not pressed: it opens a modal picker. ─────────
        CheckInvoke("Resources tab", "Executable_Tab_Resources");
        CheckPresent("Resource tree", "Executable_ResourceTree");

        // ── Manifest, decoded and raw ───────────────────────────────────────────
        CheckInvoke("Manifest tab", "Executable_Tab_Manifest");
        CheckDoes("Raw XML toggle", "Executable_RawManifest",
                  () => WaitForId("Executable_ManifestXml", 8) is not null);

        // ── .NET: the sample is a managed assembly, so this section must be offered ──
        // Checked by automation id, not by name: values render in read-only TextBoxes, whose UIA
        // Name is not their text, so a name lookup would never see them.
        CheckInvoke(".NET tab", "Executable_Tab_Dotnet");
        CheckPresent("CLR / assembly cards", "Executable_DotnetCards");

        // ── Strings: the sweep starts on first activation ───────────────────────
        CheckInvoke("Strings tab", "Executable_Tab_Strings");
        CheckPresent("String list", "Executable_StringList", 30);
        CheckPresent("Minimum length box", "Executable_StringLength");
        CheckInvoke("Rescan strings", "Executable_RescanStrings");
        Check("String list survives a rescan", () => WaitForId("Executable_StringList", 30) is not null);

        // ── Analysis: signature verification starts on first activation ─────────
        CheckInvoke("Analysis tab", "Executable_Tab_Analysis");
        CheckPresent("Entropy heatmap", "Executable_EntropyHeatmap", 20);
        CheckInvoke("Antivirus scan", "Executable_ScanButton");

        // Back to where we started: selection round-trips without losing the page.
        CheckInvoke("Overview tab", "Executable_Tab_Overview");
        CheckPresent("Sections tree still present", "Executable_SectionTree");

        AssertJourney();
    }
}
