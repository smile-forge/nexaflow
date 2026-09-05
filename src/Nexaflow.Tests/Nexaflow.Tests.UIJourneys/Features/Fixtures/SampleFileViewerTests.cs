using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Every sample file in a family that <see cref="ViewerMap.BySet"/> maps to a viewer is opened through the
/// real shell: navigate the file browser to the sample's folder, open the file (the double-click "default
/// open" route), assert it loads in the expected in-app viewer, then close that viewer tab with Ctrl+W and
/// confirm the app is still alive before moving on. Same coverage as the old one-app-per-file
/// (<c>[DynamicData]</c>) version, but a whole group of families per app launch, so the set still runs in a
/// fraction of the time. Every file's outcome is accumulated and reported together, so one failure doesn't
/// hide the rest.
///
/// <para>
/// The map is deliberately narrower than <see cref="TestSampleData"/>: <c>code</c>, <c>notebook</c> and
/// <c>archive</c> are sample sets with no row here because their features carry their own open tests, and
/// the <c>ui</c> folder beside them on disk is not a sample set at all — it is where the suites that own
/// each format leave the fixtures the journeys click on. So the dataset directory holds more folders than
/// this test sweeps, on purpose.
/// </para>
///
/// <para>
/// The corpus is split across three tests rather than swept in one, because as a single method it was by
/// far the slowest thing in this suite — a three-minute serial block where every other journey is under a
/// minute. The two largest families carry most of that weight (markdown is a third of the ~110 mapped
/// fixtures and images a further fifth), so each takes a test of its own and the remaining thirteen share
/// the tail. Splitting costs one extra app launch apiece — about a second, measured — and buys a failure
/// that names which group broke and a crash that no longer takes the other groups' results with it, each
/// test having its own app, isolated config dir and crash log.
/// </para>
///
/// <para>
/// Per-family elapsed time goes to <see cref="JourneyTimings"/> on every run, which is what says where the
/// weight actually is. Markdown is the floor at roughly a minute on its own: no regrouping of the tail can
/// take the slowest test below that, only splitting markdown itself would.
/// </para>
///
/// Requires an interactive desktop session — run with --filter "TestCategory=UI". The app launches against
/// an isolated NEXAFLOW_CONFIG_DIR (see <c>UITestBase</c>), so the file-type map is seeded fresh from the
/// bundled defaults and open routing is deterministic on any machine.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("sample-file corpus")]
public class SampleFileViewerTests : FileSystemUiTestBase
{
    /// <summary>
    /// Families large enough to be worth their own test, and therefore excluded from the tail. A family is
    /// named here exactly once: <see cref="Only"/> refuses to sweep one that is missing from this list, so a
    /// new dedicated test cannot silently leave its family running in <see cref="OtherSamples_OpenInExpectedViewer_AndCloseWithoutCrashing"/> as well.
    /// </summary>
    private static readonly string[] OwnTest = ["markdown", "images"];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void MarkdownSamples_OpenInExpectedViewer_AndCloseWithoutCrashing() => Sweep(Only("markdown"));

    [TestMethod]
    public void ImageSamples_OpenInExpectedViewer_AndCloseWithoutCrashing() => Sweep(Only("images"));

    [TestMethod]
    public void OtherSamples_OpenInExpectedViewer_AndCloseWithoutCrashing()
        => Sweep(ViewerMap.BySet.Where(set => !OwnTest.Contains(set.SubDir)));

    /// <summary>The one family <paramref name="subDir"/> names, having first checked it is excluded from
    /// the tail — the two halves of giving a family its own test, kept together so neither can be forgotten.</summary>
    private static IEnumerable<(string SubDir, string ViewerId)> Only(string subDir)
    {
        Assert.IsTrue(OwnTest.Contains(subDir),
            $"'{subDir}' has a test of its own but is not listed in {nameof(OwnTest)}, so it would be swept "
            + "twice — once here and again in the tail. Add it there.");

        var family = ViewerMap.BySet.Where(set => set.SubDir == subDir).ToList();
        Assert.AreEqual(1, family.Count, $"Expected exactly one ViewerMap row for '{subDir}'.");
        return family;
    }

    /// <summary>
    /// Opens every file of every named sample family in turn, reporting all misses together and recording
    /// each family's elapsed time to <see cref="JourneyTimings"/>.
    /// <para>
    /// The file browser is waited for once up front rather than being left to the first family's navigate,
    /// so the app's launch tail lands outside every timed region instead of being charged to whichever
    /// family happens to run first. Each family's clock then covers its own navigate plus its own files,
    /// which is the work a release-to-release comparison is actually about.
    /// </para>
    /// </summary>
    private void Sweep(IEnumerable<(string SubDir, string ViewerId)> sets)
    {
        // Both outside every family clock: generating the dataset is disk work that belongs to no family,
        // and the browser tab finishing its first paint is the launch tail, which is what a timing run is
        // meant to exclude.
        _ = TestSampleData.Root;
        Assert.IsNotNull(WaitForId("DirectoryTree", 30), "File browser tab did not load.");

        var failures = new List<string>();

        foreach (var (subDir, viewerId) in sets)
        {
            var before = failures.Count;
            var clock = Stopwatch.StartNew();
            var canContinue = SweepFamily(subDir, viewerId, failures, out var attempted);
            clock.Stop();

            TestContext.WriteLine(JourneyTimings.Describe(subDir, attempted, clock.Elapsed));
            JourneyTimings.Record(nameof(SampleFileViewerTests), subDir, attempted, clock.Elapsed,
                                  failures.Count - before);

            if (!canContinue) break;
        }

        Assert.IsFalse(App.HasExited,
            "App crashed during the sample sweep:\n" + string.Join("\n", failures));
        Assert.AreEqual(0, failures.Count,
            $"{failures.Count} sample file(s) failed:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Opens every file of one sample family, appending a line to <paramref name="failures"/> for each miss
    /// and reporting how many files were actually reached in <paramref name="attempted"/>.
    /// Returns false when the sweep can go no further — the app died, or the file-browser tab it opens
    /// from is gone — so the caller stops rather than grinding every later family through its full timeouts.
    /// </summary>
    private bool SweepFamily(string subDir, string viewerId, List<string> failures, out int attempted)
    {
        attempted = 0;

        if (!ActivateFileBrowser())
        {
            failures.Add($"{subDir}/(navigate): the file-browser tab is gone — sweep aborted");
            return false;
        }
        try { NavigateFileBrowserTo(TestSampleData.Path(subDir)); }
        catch (Exception ex) { failures.Add($"{subDir}/(navigate): {First(ex.Message)}"); return true; }

        foreach (var path in TestSampleData.Files(subDir))
        {
            var fileName = Path.GetFileName(path);
            var label = $"{subDir}/{fileName} → {viewerId}";
            TestContext.WriteLine("opening " + label);
            attempted++;

            if (!ActivateFileBrowser())     // a viewer tab may be active from the previous file
            {
                failures.Add(label + ": the file-browser tab is gone — sweep aborted");
                return false;
            }
            try { OpenFile(fileName); }
            catch (Exception ex) { failures.Add($"{label}: open threw — {First(ex.Message)}"); continue; }

            var opened = WaitForId(viewerId, 15) is not null;
            if (App.HasExited) { failures.Add(label + ": app crashed on open"); return false; }

            if (!opened)
            {
                // The viewer never appeared, so NO viewer tab is active — the file browser is. Sending
                // Ctrl+W here would close the file-browser tab itself, and with it gone every later file
                // fails to find its row: the whole sweep collapses to a blank, tab-less window. Record
                // this one file and move on with the browser intact, so a single flaky viewer stays a
                // single reported failure instead of taking the rest of the run down with it.
                failures.Add(label + ": did not open in the expected viewer");
                continue;
            }

            if (!CloseViewerWithCtrlW(viewerId))
                failures.Add(label + ": viewer tab did not close on Ctrl+W");
            if (App.HasExited) { failures.Add(label + ": app crashed on close"); return false; }
        }

        return true;
    }

    private static string First(string message) => message.Split('\n')[0].Trim();

    /// <summary>Opens a file by selecting its row and pressing Shift+Enter — the keyboard equivalent of the
    /// double-click default-open route, and the only reliable one here. Clicking a row by coordinate cannot be
    /// made robust: a trailing row straddles the list's bottom edge (music-lilypond.md, last of 27 markdown
    /// fixtures, sat 25px below it while still reporting on-screen), UIA's cached ClickablePoint keeps aiming
    /// at the pre-scroll position, and moving the mouse in re-scrolls the list out from under the cursor.
    /// Selecting via UIA sidesteps all three. Focus must leave the AI input too — the shell skips a page's
    /// IKeyboardHandler entirely while any TextBox is focused.</summary>
    private void OpenFile(string fileName)
    {
        var cell = WaitForName(fileName, 8);
        Assert.IsNotNull(cell, $"File '{fileName}' not found in the file list.");

        // ByName matches the row's text cell; the selectable unit is its DataItem ancestor (a WPF
        // ListView+GridView surfaces as DataGrid/DataItem, not List/ListItem).
        var row = cell;
        while (row is not null && row.ControlType != ControlType.DataItem) row = row.Parent;

        var target = row ?? cell!;
        target.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        target.Patterns.SelectionItem.PatternOrDefault?.Select();
        target.Focus();                                  // move focus off the AI input so the handler runs
        Wait.UntilInputIsProcessed();

        using (Keyboard.Pressing(VirtualKeyShort.SHIFT))
            Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
    }

    /// <summary>Re-selects the file-browser tab so the next file opens from it (a viewer tab may be active).
    /// Returns false if the tab is gone — the sweep can't open anything after that, so the caller stops
    /// rather than grinding every remaining file through its full timeout against a tab-less window.</summary>
    private bool ActivateFileBrowser()
    {
        var tab = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabItem_FileSystem"));
        if (tab is null) return false;
        tab.Click();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>Closes the active viewer tab via the shell's Ctrl+W shortcut and waits for it to disappear.
    /// Focus is first moved to the AI input (a WPF element) so Ctrl+W reaches the window's key handler even
    /// when the viewer hosts a native surface (e.g. the video viewer's libVLC airspace). Returns whether the
    /// viewer actually went away.</summary>
    private bool CloseViewerWithCtrlW(string viewerId)
    {
        WaitForId("AiInputBox", 5)?.Click();
        Wait.UntilInputIsProcessed();

        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Type(VirtualKeyShort.KEY_W);

        return WaitForGone(viewerId, 8);
    }
}
