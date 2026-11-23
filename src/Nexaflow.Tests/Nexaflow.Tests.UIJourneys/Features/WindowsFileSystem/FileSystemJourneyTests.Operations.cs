using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// The operations panel, in the running app — the half nothing else can reach. The queue, the debounce and the engine all
/// have unit tests, but a wrong resource key or a mistyped binding path in the panel's XAML fails silently — no exception,
/// just an empty row — so the only way to know the panel renders what it is bound to is to look at it through the
/// automation tree.
/// <para>
/// One copy, one zip and one extraction, each large enough to outlast the panel's 600 ms debounce, run together at the end
/// of the journey, because the panel's several rows at once are what the pass exists to look at. The copy is started first
/// and carries the checks every operation shares — the panel appears, sits above the tree rather than over it, offers
/// cancel, collapses and expands — and, while it is still going, a second file browser shows the same work, because a copy
/// belongs to the workspace rather than to the tab that started it. From that browser the zip and the extraction are
/// started, and the panel is then asked for a row naming each, laid one above the next without overlapping. Each result on
/// disk is checked last, and the panel clearing once all three are done. The copy is driven by the action strip rather
/// than a real drag: both enter the same queue.
/// </para>
/// </summary>
public partial class FileSystemJourneyTests
{
    /// <summary>Where the pass writes its payloads; <c>NEXAFLOW_FILEOPS_ROOT</c> puts them on a chosen (slower) disk.</summary>
    private string _operationsRoot = string.Empty;

    /// <summary>
    /// One copy, one zip and one extraction, run together: the panel's several rows at once are what this exists to look at.
    /// Everything they work on is made first, so that once the copy is under way the other two are started in the time it takes,
    /// from a second file browser, rather than after it.
    /// </summary>
    private void RunOperationsPanelPass()
    {
        var root = Environment.GetEnvironmentVariable("NEXAFLOW_FILEOPS_ROOT") is { Length: > 0 } chosen ? chosen : TestTemp.Root;
        _operationsRoot = Path.Combine(root, "nexaflow-fileops-journey-" + Guid.NewGuid().ToString("N"));
        var source      = Path.Combine(_operationsRoot, "source");
        var destination = Path.Combine(_operationsRoot, "destination");
        Directory.CreateDirectory(destination);

        // Large blocks for the bytes, and thousands of small files for the time: a cached block copies in an instant on a fast disk,
        // but each file costs the engine an open, a write and a close however warm the cache is, so the copy is still going while
        // the others are started and looked at.
        var copied = Path.Combine(source, "payload");
        MakePayload(copied, defaultGb: 1.5);
        var smallFiles = MakeManySmallFiles(Path.Combine(copied, "many"), count: 20_000);

        // Random bytes, so deflate cannot shrink them away and the write stays honest work.
        var zipped = Path.Combine(source, "zipme");
        MakePayload(zipped, defaultGb: 0.5);
        var zippedBytes = BytesIn(zipped);

        var bundled = Path.Combine(source, "bundled");
        var entries = MakeManySmallFiles(bundled);
        var extractedBytes = BytesIn(bundled);
        // Stored rather than deflated: this is fixture setup, and compressing it would cost more than the extraction.
        ZipFile.CreateFromDirectory(bundled, Path.Combine(source, "bundle.zip"), CompressionLevel.NoCompression, includeBaseDirectory: false);
        Directory.Delete(bundled, recursive: true);

        if (!StartCopy(source, destination)) return;

        // While the copy is going, so straight away: a copy belongs to the workspace, not to the tab that started it, so a second
        // file browser opened now shows it — laid out above its own tree, where drawing over the tree would be a panel that is
        // present and invisible. Ribbon buttons are "Ribbon_" + label; the bare name would match the folder tree's own "This PC" root.
        if (CheckInvoke("a second file browser from the ribbon, mid-copy", "Ribbon_This PC", 10) is null) return;
        Wait.UntilInputIsProcessed();
        Check("the second file browser shows the copy it did not start, above its own tree", PanelSitsAboveTheTree);

        // From that second browser, the other two are started while the copy runs.
        if (!StartZip(source) || !StartUnzip(source)) return;

        // A bar bound to the wrong thing is still a bar, so each row has to say what it is doing — all three, at the same moment.
        Check("the panel lists the copy, the compression and the extraction together", () =>
            RowTitleContains("Copying", 5) && RowTitleContains("Compressing", 5) && RowTitleContains("Extracting", 5));
        Check("the rows stack without drawing over one another", RowsStackWithoutOverlap);

        CheckPresent("a way to stop the copy", "FileOps_CancelRow", 4);
        CheckPresent("a way to stop everything", "FileOps_CancelAll", 4);

        // Pressed twice so the panel is left expanded — a journey that quietly collapses the thing it is inspecting reports nothing
        // useful about the rest of it.
        CheckInvoke("collapse toggle", "FileOps_Toggle");
        CheckInvoke("expand again", "FileOps_Toggle");
        ShootWindowArea("fs-journey", "operations-together");

        var landed = Path.Combine(destination, "payload");
        var lastBlock = Path.Combine(landed, File.ReadAllText(Path.Combine(copied, "last.txt")));
        Check("the whole folder arrives", () => WaitForFs(() =>
            File.Exists(lastBlock) && Directory.Exists(Path.Combine(landed, "many"))
            && Directory.GetFiles(Path.Combine(landed, "many")).Length == smallFiles, 300));

        // The destination name is claimed the moment the action returns, so existence proves nothing — the archive is finished
        // when it holds the bytes.
        var archive = Path.Combine(source, "zipme.zip");
        Check("the archive is written, not merely named",
              () => WaitForFs(() => File.Exists(archive) && new FileInfo(archive).Length > zippedBytes / 2, 300));

        // Into a sibling folder named after the archive. Measured in bytes rather than file count, because a file exists the moment
        // it is created, which is well before it is written.
        var extracted = Path.Combine(source, "bundle");
        Check($"all {entries} entries arrive", () => WaitForFs(() => BytesIn(extracted) == extractedBytes, 300));

        Check("the panel clears once every operation is done", () => WaitForGone("FileOps_Progress", 60));

        // Gigabytes nothing below needs.
        TryDelete(copied);
        TryDelete(landed);
        TryDelete(zipped);
    }

    /// <summary>
    /// The panel is laid out in whichever browser is in front, above its tree. Being in the automation tree is not being
    /// on screen, and a bounding rectangle is not enough either: a zero-height container still arranges its children at
    /// their natural size, so they report a good rectangle while clipped to nothing. Overlapping the tree means drawn over it.
    /// </summary>
    private bool PanelSitsAboveTheTree()
    {
        var bar  = WaitForId("FileOps_Progress", Affordable(15));
        var tree = WaitForId("DirectoryTree", Affordable(10));
        return bar is not null && tree is not null && bar.BoundingRectangle.Bottom <= tree.BoundingRectangle.Top;
    }

    /// <summary>
    /// Writes a folder of 8 MB blocks totalling <paramref name="defaultGb"/> gigabytes, or <c>NEXAFLOW_FILEOPS_GB</c> —
    /// so the committed default stays a considerate neighbour. The last block's name is what the arrival check waits for.
    /// </summary>
    private static void MakePayload(string folder, double defaultGb)
    {
        double gb = double.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_FILEOPS_GB"), out var v) && v > 0 ? v : defaultGb;

        Directory.CreateDirectory(folder);
        var block = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(block);

        int blocks = Math.Max(1, (int)Math.Round(gb * 128));
        for (var i = 0; i < blocks; i++)
            File.WriteAllBytes(Path.Combine(folder, $"blob{i}.bin"), block);

        File.WriteAllText(Path.Combine(folder, "last.txt"), $"blob{blocks - 1}.bin");
    }

    /// <summary>
    /// Writes <paramref name="count"/> small files. By count rather than by gigabytes, because extraction is dominated by
    /// per-file work: a few large blocks come straight back out of the page cache and can finish inside the debounce.
    /// </summary>
    private static int MakeManySmallFiles(string folder, int count = 12_000)
    {
        Directory.CreateDirectory(folder);
        var block = new byte[8 * 1024];
        Random.Shared.NextBytes(block);

        for (var i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(folder, $"f{i:D5}.bin"), block);
        return count;
    }

    /// <summary>
    /// True once a row in the panel says <paramref name="fragment"/>. The title is a plain <c>TextBlock</c>, so its
    /// automation name is its text; a fragment keeps this from breaking on a wording change that is not the point.
    /// </summary>
    private bool RowTitleContains(string fragment, int seconds = 15)
        => WaitFor(() =>
        {
            var scope = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FileOps_Panel")) ?? MainWindow;
            return Array.Find(scope.FindAllDescendants(),
                e => { try { return e.Name?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true; }
                       catch { return false; } });
        }, Affordable(seconds)) is not null;

    /// <summary>Bytes held directly in <paramref name="dir"/>, or -1 before it exists; tolerant of a folder being written underneath it.</summary>
    private static long BytesIn(string dir)
    {
        try { return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles().Sum(f => f.Length) : -1; }
        catch { return -1; }
    }

    private static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
    }

    /// <summary>Whether the breadcrumb bar names <paramref name="folder"/> — the folder open, not the one that was.</summary>
    private bool BreadcrumbNames(string folder) =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Chrome_BreadcrumbBar"))?.FindFirstDescendant(cf => cf.ByName(folder)) is not null;

    /// <summary>
    /// Copies the payload folder to the destination by the action strip — rather than Ctrl+C/Ctrl+V, because NavigateFileBrowserTo
    /// types the path into the AI input bar, so focus is still in that text box and a Ctrl+V would paste into it instead — and
    /// looks at the panel the copy brings up. False once the copy cannot be started, so the caller can skip the rest.
    /// </summary>
    private bool StartCopy(string source, string destination)
    {
        NavigateFileBrowserTo(source);
        Check("the folder to copy is listed", () => SelectInFileList("payload"));
        if (CheckInvoke("Copy, for the selected folder", "CopyFiles", 8) is null) return false;
        Wait.UntilInputIsProcessed();

        // The strip's Paste button as it stands for the folder the copy came from: it pastes into that folder until the strip is
        // rebuilt for the one opened next.
        var pasteForSource = RuntimeIdOf("PasteFiles");
        NavigateFileBrowserTo(destination);

        // NavigateFileBrowserTo returns once the folder's "New" button shows, which the folder just left shows too: Paste goes into
        // whichever folder is open, so it waits for the breadcrumb to name the destination. Pasting early copies the folder into the
        // one it came from, and the check that the folder arrives waits out five minutes for a copy that is not coming.
        if (!WaitForFs(() => BreadcrumbNames("destination"), 20))
        {
            Check("the destination is open before anything is pasted", () => false);
            return false;
        }
        // The breadcrumb names the destination before the strip is rebuilt for it, and a Paste pressed in between goes into the folder
        // just left. A new Paste button is the strip rebuilt; it is waited for rather than assumed, and pressed regardless if the
        // strip reuses its buttons.
        WaitForFs(() => RuntimeIdOf("PasteFiles") is { } now && now != pasteForSource, 10);
        Check("the panel is not there before anything is happening", () => WaitForGone("FileOps_Progress", 2));
        if (CheckInvoke("Paste, once something is copied", "PasteFiles", 8) is null) return false;

        // Found on screen at all is the assertion: the panel is clipped to zero height when idle. Generous, because the point is
        // that it appears, not that it appears in exactly 600 ms.
        var bar = WaitForId("FileOps_Progress", Affordable(10));
        Check("the operations panel appears while a large copy runs", () => bar is not null);
        if (bar is null) return false;

        // A paste that went into the folder it was copied from names its copy "Copy of payload" at once. Said here, it is a failure
        // in seconds; left alone it is five minutes of waiting for a destination that nothing is coming to.
        Check("the copy goes into the destination, not beside its source",
              () => !WaitForFs(() => Directory.Exists(Path.Combine(source, "Copy of payload")), 2));
        return !Directory.Exists(Path.Combine(source, "Copy of payload"));
    }

    /// <summary>The automation runtime id of the control <paramref name="automationId"/>, which changes when the control is rebuilt; null when it is not there.</summary>
    private string? RuntimeIdOf(string automationId) =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is { } element
            ? string.Join('.', element.Properties.RuntimeId.ValueOrDefault ?? [])
            : null;

    /// <summary>Zips the "zipme" folder from whichever browser is in front. False once the zip cannot be started.</summary>
    private bool StartZip(string source)
    {
        NavigateFileBrowserTo(source);
        Check("the folder to zip is listed", () => SelectInFileList("zipme"));

        // The action strip's ids are the actions' type names (IFileAction.AutomationId).
        return CheckInvoke("Zip It, for the selected folder", "ZipItAction", 30) is not null;
    }

    /// <summary>Extracts "bundle.zip" beside itself from whichever browser is in front. False once the extraction cannot be started.</summary>
    private bool StartUnzip(string source)
    {
        Check("the archive to extract is listed", () => SelectInFileList("bundle.zip"));

        // Only an archive offers this, so finding it is also the proof the click landed on the zip, not a neighbour.
        return CheckInvoke("Unzip here, for the selected archive", "UnzipHereAction", 30) is not null;
    }

    /// <summary>
    /// Whether the rows the panel shows, one cancel button each, sit one above the next. The panel is the thing that lists
    /// operations together, and rows laid over one another still count as present, so the rectangles are what is compared.
    /// </summary>
    private bool RowsStackWithoutOverlap()
    {
        var rows = MainWindow.FindAllDescendants(cf => cf.ByAutomationId("FileOps_CancelRow"))
                             .Where(row => !row.IsOffscreen)
                             .Select(row => row.BoundingRectangle)
                             .OrderBy(rectangle => rectangle.Top)
                             .ToList();
        if (rows.Count < 2) return false;

        for (var i = 1; i < rows.Count; i++)
            if (rows[i].Top < rows[i - 1].Bottom) return false;
        return true;
    }
}
