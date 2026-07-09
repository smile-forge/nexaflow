using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlaUI.Core.AutomationElements;
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

            ActivateFileBrowser();
            try { NavigateFileBrowserTo(TestSampleData.Path(subDir)); }
            catch (Exception ex) { failures.Add($"{subDir}/(navigate): {First(ex.Message)}"); continue; }

            foreach (var path in TestSampleData.Files(subDir))
            {
                var fileName = Path.GetFileName(path);
                var label = $"{subDir}/{fileName} → {viewerId}";
                TestContext.WriteLine("opening " + label);

                ActivateFileBrowser();          // a viewer tab may be active from the previous file
                try { OpenFile(fileName); }
                catch (Exception ex) { failures.Add($"{label}: open threw — {First(ex.Message)}"); continue; }

                if (WaitForId(viewerId, 15) is null)
                    failures.Add(label + ": did not open in the expected viewer");
                if (App.HasExited) { failures.Add(label + ": app crashed on open"); alive = false; break; }

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

    /// <summary>Opens a file by double-clicking its row in the file list (the default-open route).</summary>
    private void OpenFile(string fileName)
    {
        var row = WaitForName(fileName, 8);
        Assert.IsNotNull(row, $"File '{fileName}' not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();
    }

    /// <summary>Re-selects the file-browser tab so the next file opens from it (a viewer tab may be active).</summary>
    private void ActivateFileBrowser()
    {
        var tab = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabItem_FileSystem"));
        tab?.Click();
        Wait.UntilInputIsProcessed();
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
