using System;
using System.IO;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using FlaUI.Core.AutomationElements;

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
        _root        = Path.Combine(Path.GetTempPath(), "nexaflow-fileops-journey-" + Guid.NewGuid().ToString("N"));
        _source      = Path.Combine(_root, "source");
        _destination = Path.Combine(_root, "destination");

        Directory.CreateDirectory(_destination);
        var payload = Directory.CreateDirectory(Path.Combine(_source, "payload"));

        // Big enough that the copy outlives the debounce on any disk this would run on, and small
        // enough not to be rude about it.
        var block = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(block);
        for (var i = 0; i < 24; i++)
            File.WriteAllBytes(Path.Combine(payload.FullName, $"blob{i}.bin"), block);
    }

    [TestCleanup]
    public void RemoveFolders()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public void CopyingALargeFolderShowsAProgressPanel_ThenPutsItAway()
    {
        // ── Copy the payload folder ───────────────────────────────────────────
        NavigateFileBrowserTo(_source);

        var row = WaitForName("payload", 8);
        Assert.IsNotNull(row, "The folder to copy is not in the file list.");
        row!.Click();
        Wait.UntilInputIsProcessed();

        // The ActionStrip rather than Ctrl+C/Ctrl+V: NavigateFileBrowserTo types the path into the AI
        // input bar, so focus is still in that text box and a Ctrl+V would paste into it instead.
        var copy = WaitForId("Copy", 8);
        Assert.IsNotNull(copy, "No Copy action for the selected folder.");
        copy!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        // ── Paste it somewhere else ───────────────────────────────────────────
        NavigateFileBrowserTo(_destination);

        var paste = WaitForId("Paste", 8);
        Assert.IsNotNull(paste, "No Paste action — the copy never reached the clipboard.");
        paste!.AsButton().Invoke();

        // ── The panel says what is happening ──────────────────────────────────
        // The bar lives in a template inside the panel, and the panel is clipped to zero height when
        // idle — so finding it on screen at all is the assertion. Generous, because the point is that
        // it appears at all, not that it appears in exactly 600 ms.
        var bar = WaitForId("FileOps_Progress", 10);
        Check("the operations panel appears while a large copy runs", () => bar is not null);

        if (bar is not null)
        {
            Check("it offers a way to stop the copy",
                  () => WaitForId("FileOps_CancelRow", 4) is not null);
            Check("and a way to stop everything",
                  () => WaitForId("FileOps_CancelAll", 4) is not null);
        }

        // ── The copy lands ────────────────────────────────────────────────────
        var landed = Path.Combine(_destination, "payload", "blob23.bin");
        Check("the whole folder arrives", () => WaitForFs(() => File.Exists(landed), 90));

        // ── And the panel gets out of the way ─────────────────────────────────
        Check("the panel collapses once nothing is happening",
              () => WaitForGone("FileOps_Progress", 20));

        AssertJourney();
    }
}
