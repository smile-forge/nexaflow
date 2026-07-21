using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.WindowsFileSystem.UI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// One app launch, then every generated sample file is opened in turn: navigate the file browser to the
/// sample's folder, open the file (the double-click "default open" route), assert it loads in the expected
/// in-app viewer, then close that viewer tab with Ctrl+W and confirm the app is still alive before moving
/// on. This exercises every fixture in <see cref="TestSampleData"/> end-to-end through the real shell — the
/// same coverage the old one-app-per-file (<c>[DynamicData]</c>) version gave, but in a single process, so
/// the whole set runs in a fraction of the time. Every file's outcome is accumulated and reported together,
/// so one failure doesn't hide the rest.
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
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EverySampleFile_OpensInExpectedViewer_AndClosesWithoutCrashing()
    {
        var failures = new List<string>();
        bool alive = true;

        foreach (var (subDir, viewerId) in ViewerMap.BySet)
        {
            if (!alive) break;

            if (!ActivateFileBrowser())
            {
                failures.Add($"{subDir}/(navigate): the file-browser tab is gone — sweep aborted");
                break;
            }
            try { NavigateFileBrowserTo(TestSampleData.Path(subDir)); }
            catch (Exception ex) { failures.Add($"{subDir}/(navigate): {First(ex.Message)}"); continue; }

            foreach (var path in TestSampleData.Files(subDir))
            {
                var fileName = Path.GetFileName(path);
                var label = $"{subDir}/{fileName} → {viewerId}";
                TestContext.WriteLine("opening " + label);

                if (!ActivateFileBrowser())     // a viewer tab may be active from the previous file
                {
                    failures.Add(label + ": the file-browser tab is gone — sweep aborted");
                    alive = false;
                    break;
                }
                try { OpenFile(fileName); }
                catch (Exception ex) { failures.Add($"{label}: open threw — {First(ex.Message)}"); continue; }

                var opened = WaitForId(viewerId, 15) is not null;
                if (App.HasExited) { failures.Add(label + ": app crashed on open"); alive = false; break; }

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
                if (App.HasExited) { failures.Add(label + ": app crashed on close"); alive = false; break; }
            }
        }

        Assert.IsFalse(App.HasExited,
            "App crashed during the sample sweep:\n" + string.Join("\n", failures));
        Assert.AreEqual(0, failures.Count,
            $"{failures.Count} sample file(s) failed:\n" + string.Join("\n", failures));
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

    /// <summary>Polls until no on-screen element carries <paramref name="automationId"/> (the tab closed).</summary>
    private bool WaitForGone(string automationId, int seconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el is null || el.IsOffscreen) return true;
            System.Threading.Thread.Sleep(120);
        }
        return false;
    }
}
