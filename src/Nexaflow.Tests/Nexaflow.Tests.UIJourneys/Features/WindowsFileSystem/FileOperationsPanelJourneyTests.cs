using System;
using System.IO;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// The operations panel, in the running app.
/// <para>
/// This is the half nothing else can reach. The queue, the debounce and the engine all have unit
/// tests, but a wrong resource key or a mistyped binding path in the panel's XAML fails silently —
/// no exception, just an empty row — so the only way to know the panel actually renders what it is
/// bound to is to look at it through the automation tree.
/// </para>
/// <para>
/// It copies a folder big enough to still be going after the 600 ms debounce and asserts the panel
/// appears; the previous behaviour was a window that froze with nothing on screen to say why.
/// The copy is driven by clipboard paste rather than a real drag: both enter the same queue, and a
/// synthesised OLE drag is not something a journey can do reliably.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("file-operations-panel")]
public class FileOperationsPanelJourneyTests : UiJourneyTestBase
{
    private string _root = null!;
    private string _source = null!;
    private string _destination = null!;

    protected override void OnUISetup()
    {
        // NEXAFLOW_FILEOPS_ROOT puts the fixtures on a chosen disk. A slower one is the point when you
        // want the copies to overlap rather than finish before the panel has decided to appear.
        var root = Environment.GetEnvironmentVariable("NEXAFLOW_FILEOPS_ROOT") is { Length: > 0 } r
            ? r : Path.GetTempPath();
        _root        = Path.Combine(root, "nexaflow-fileops-journey-" + Guid.NewGuid().ToString("N"));
        _source      = Path.Combine(_root, "source");
        _destination = Path.Combine(_root, "destination");

        Directory.CreateDirectory(_destination);
        // Each test writes only the fixtures it needs: at these sizes a payload the other one ignores is
        // several gigabytes of pointless IO before it even starts.
    }

    [TestCleanup]
    public void RemoveFolders()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public void CopyingALargeFolderShowsAProgressPanel_ThenPutsItAway()
    {
        // 2.5 GB. It has to outlive the 600 ms debounce on an NVMe with the whole thing still in the
        // OS file cache, which a few hundred megabytes does not.
        MakePayload(Path.Combine(_source, "payload"), defaultGb: 2.5);

        // ── Copy the payload folder ───────────────────────────────────────────
        NavigateFileBrowserTo(_source);

        Assert.IsTrue(SelectInFileList("payload"), "The folder to copy is not in the file list.");

        // The ActionStrip rather than Ctrl+C/Ctrl+V: NavigateFileBrowserTo types the path into the AI
        // input bar, so focus is still in that text box and a Ctrl+V would paste into it instead.
        var copy = WaitForId("Copy", 8);
        Assert.IsNotNull(copy, "No Copy action for the selected folder.");
        copy!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        // ── Paste it somewhere else ───────────────────────────────────────────
        NavigateFileBrowserTo(_destination);

        Check("the panel is not there before anything is happening",
              () => WaitForGone("FileOps_Progress", 2));

        var paste = WaitForId("Paste", 8);
        Assert.IsNotNull(paste, "No Paste action — the copy never reached the clipboard.");
        paste!.AsButton().Invoke();

        // ── The panel says what is happening ──────────────────────────────────
        // The bar lives in a template inside the panel, and the panel is clipped to zero height when
        // idle — so finding it on screen at all is the assertion. Generous, because the point is that
        // it appears at all, not that it appears in exactly 600 ms.
        var bar = WaitForId("FileOps_Progress", 10);
        Check("the operations panel appears while a large copy runs", () => bar is not null);

        // A picture of the window mid-copy, so "the automation tree says it is there" can be checked
        // against what is actually on screen.
        var shot = Path.Combine(Path.GetTempPath(), "nexaflow-fileops-panel.png");
        try
        {
            MainWindow.Capture().Save(shot);
            var tree = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DirectoryTree"));
            var cancelAll = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FileOps_CancelAll"));
            File.WriteAllLines(Path.ChangeExtension(shot, ".txt"),
            [
                $"window        {MainWindow.BoundingRectangle}",
                $"DirectoryTree {tree?.BoundingRectangle.ToString() ?? "(not found)"}",
                $"progress bar  {bar?.BoundingRectangle.ToString() ?? "(not found)"}",
                $"bar offscreen {bar?.IsOffscreen.ToString() ?? "-"}",
                $"cancel-all    {cancelAll?.BoundingRectangle.ToString() ?? "(not found)"}",
                $"panel         {MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FileOps_Panel"))?.BoundingRectangle.ToString() ?? "(not found)"}",
            ]);
        }
        catch (Exception ex) { System.Console.WriteLine($"capture failed: {ex.Message}"); }

        // Being in the automation tree is not the same as being on screen, and a bounding rectangle is
        // not enough either: a zero-height container still arranges its children at their natural size,
        // so they report a perfectly good rectangle while being clipped to nothing. The panel has to have
        // pushed the tree down — if it overlaps the tree it is drawing on top of it and is not visible.
        var directoryTree = WaitForId("DirectoryTree", 5);
        Check("the panel pushed the tree down instead of drawing over it",
              () => bar is not null && directoryTree is not null
                 && bar.BoundingRectangle.Bottom <= directoryTree.BoundingRectangle.Top);

        if (bar is not null)
        {
            Check("it offers a way to stop the copy",
                  () => WaitForId("FileOps_CancelRow", 4) is not null);
            Check("and a way to stop everything",
                  () => WaitForId("FileOps_CancelAll", 4) is not null);

            // The header's collapse toggle. Pressed twice so the panel is left expanded for the checks
            // below — a journey that quietly collapses the thing it is inspecting reports nothing useful
            // about the rest of it.
            CheckInvoke("collapse toggle", "FileOps_Toggle");
            CheckInvoke("expand again",    "FileOps_Toggle");
        }

        // ── The copy lands ────────────────────────────────────────────────────
        var landed = Path.Combine(_destination, "payload", File.ReadAllText(Path.Combine(_source, "payload", "last.txt")));
        Check("the whole folder arrives", () => WaitForFs(() => File.Exists(landed), 90));

        // ── And the panel gets out of the way ─────────────────────────────────
        Check("the panel collapses once nothing is happening",
              () => WaitForGone("FileOps_Progress", 20));

        AssertJourney();
    }

    /// <summary>
    /// Three folders on the way at once — the shape of the incident this all came from, where three were
    /// dropped and one arrived. They queue rather than fight each other for the disk, so the panel shows
    /// one running and the rest waiting, and all three land.
    /// </summary>
    [TestMethod]
    public void ThreeCopiesAtOnceAllQueueUpAndAllArrive()
    {
        string[] names = ["alpha", "beta", "gamma"];
        foreach (var name in names) MakePayload(Path.Combine(_source, name), defaultGb: 1.5);

        foreach (var name in names)
        {
            NavigateFileBrowserTo(_source);

            Assert.IsTrue(SelectInFileList(name), $"'{name}' is not in the file list.");

            var copy = WaitForId("Copy", 30);
            Assert.IsNotNull(copy, $"No Copy action for '{name}'.");
            copy!.AsButton().Invoke();
            Wait.UntilInputIsProcessed();

            NavigateFileBrowserTo(_destination);

            var paste = WaitForId("Paste", 30);
            Assert.IsNotNull(paste, $"No Paste action for '{name}'.");
            paste!.AsButton().Invoke();
            Wait.UntilInputIsProcessed();

            // One shot per paste. The middle one is the interesting state — the first copy still running
            // while the next is queued behind it, which is the shape of the drop that went wrong.
            try { MainWindow.Capture().Save(Path.Combine(Path.GetTempPath(), $"nexaflow-fileops-three-{name}.png")); }
            catch { }
            }

        // More than one row: the second and third pastes were accepted while the first was still going,
        // which is exactly what the old code could not do. Whether they are still running by the time this
        // looks is a race against the disk, so the claim stops at "more than one operation is listed".
        var rows = WaitFor(() =>
        {
            var found = MainWindow.FindAllDescendants(cf => cf.ByAutomationId("FileOps_Progress"));
            return found.Length > 1 ? found[0] : null;
        }, 15);
        Check("more than one operation is in the panel at the same time", () => rows is not null);



        foreach (var name in names)
            Check($"'{name}' arrives",
                  () => WaitForFs(() => File.Exists(Path.Combine(_destination, name,
                          File.ReadAllText(Path.Combine(_source, name, "last.txt")))), 300));

        Check("the panel goes away once they have all finished",
              () => WaitForGone("FileOps_Progress", 25));

        AssertJourney();
    }

    /// <summary>
    /// Writes a folder of 8 MB blocks totalling <paramref name="defaultGb"/> gigabytes.
    /// <para>
    /// Sized by <c>NEXAFLOW_FILEOPS_GB</c> so the committed default stays a considerate neighbour — a
    /// journey that writes tens of gigabytes on every run is not one anybody will keep running. Raise it
    /// when you want the copies to visibly overlap rather than merely queue:
    /// <c>NEXAFLOW_FILEOPS_GB=7</c> gives roughly 20 GB across the three-copy case.
    /// </para>
    /// </summary>
    private static void MakePayload(string folder, double defaultGb)
    {
        double gb = double.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_FILEOPS_GB"), out var v) && v > 0
            ? v
            : defaultGb;

        Directory.CreateDirectory(folder);

        var block = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(block);

        int blocks = Math.Max(1, (int)Math.Round(gb * 128));
        for (var i = 0; i < blocks; i++)
            File.WriteAllBytes(Path.Combine(folder, $"blob{i}.bin"), block);

        // The last block's name is what the arrival checks wait for.
        File.WriteAllText(Path.Combine(folder, "last.txt"), $"blob{blocks - 1}.bin");
    }

    /// <summary>
    /// Selects <paramref name="name"/> in the file list and returns once the ActionStrip has caught up.
    /// <para>
    /// Scoped to the list on purpose. A window-wide search by name also matches the folder tree, whose
    /// node may be scrolled out of view — FlaUI then clicks the centre of an off-screen rectangle, which
    /// lands on the desktop, selects nothing, and leaves the next step waiting for an action that never
    /// appears.
    /// </para>
    /// </summary>
    private bool SelectInFileList(string name, int seconds = 30)
    {
        var list = WaitForId("FileListView", seconds);
        if (list is null) return false;

        var row = WaitFor(() => list.FindFirstDescendant(cf => cf.ByName(name)), seconds);
        if (row is null) return false;

        row.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        row.Click();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// A copy belongs to the workspace, not to the tab that started it. Opening a second file browser
    /// while one is running has to show the same work — otherwise the panel looks like a property of
    /// whichever tab you happened to be on when you started it.
    /// </summary>
    [TestMethod]
    public void ASecondFileBrowserOpenedMidCopyShowsTheSameWork()
    {
        MakePayload(Path.Combine(_source, "payload"), defaultGb: 2.5);

        NavigateFileBrowserTo(_source);
        Assert.IsTrue(SelectInFileList("payload"), "The folder to copy is not in the file list.");

        var copy = WaitForId("Copy", 30);
        Assert.IsNotNull(copy, "No Copy action.");
        copy!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        NavigateFileBrowserTo(_destination);
        var paste = WaitForId("Paste", 30);
        Assert.IsNotNull(paste, "No Paste action.");
        paste!.AsButton().Invoke();

        Check("the panel is up on the tab that started the copy",
              () => WaitForId("FileOps_Progress", 15) is not null);

        // Open another file browser from the ribbon while the copy is still going.
        // Ribbon buttons are "Ribbon_" + label. Searching by the bare name would match the folder
        // tree\x27s own "This PC" root, which is not a button and has no Invoke pattern.
        var thisPc = WaitForId("Ribbon_This PC", 10);
        if (thisPc is null)
        {
            Assert.Inconclusive("no ribbon route to a second file browser on this layout");
            return;
        }

        thisPc.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        // The panel has to be laid out in whichever browser is now in front — above its tree, not drawn
        // over it, which is the check that catches a panel that is present but invisible.
        var bar  = WaitForId("FileOps_Progress", 15);
        var tree = WaitForId("DirectoryTree", 10);
        Check("the second file browser shows the copy it did not start",
              () => bar is not null && tree is not null
                 && bar.BoundingRectangle.Bottom <= tree.BoundingRectangle.Top);

        try { MainWindow.Capture().Save(Path.Combine(Path.GetTempPath(), "nexaflow-fileops-second-tab.png")); }
        catch { }

        Check("and the copy still lands",
              () => WaitForFs(() => File.Exists(Path.Combine(_destination, "payload",
                      File.ReadAllText(Path.Combine(_source, "payload", "last.txt")))), 300));

        AssertJourney();
    }
}
