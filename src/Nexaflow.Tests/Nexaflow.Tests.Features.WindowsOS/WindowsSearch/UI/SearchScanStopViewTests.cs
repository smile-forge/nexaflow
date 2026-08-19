using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// Stopping a folder scan that is actually running.
/// <para>
/// Scoped to the Windows directory on purpose. A scan of somewhere small finishes before the test can
/// press Stop, and a "stop" test that only ever runs against an already-finished scan asserts nothing —
/// it would pass just as happily if the button were dead. The point of this test is the mid-flight case,
/// so the tree has to be big enough to still be in flight.
/// </para>
/// The scan is read-only and cancellation is what is being verified, so it stops within moments.
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
/// <remarks>
/// Not parallelised. Each case reads its way through a large directory tree, and two of those racing the
/// other UI tests starves the machine enough that they time out waiting for a window — a failure that looks
/// exactly like the feature being broken.
/// </remarks>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("win-search-folder-scan")]
public class SearchScanStopViewTests : UITestBase
{
    private const int Timeout = 60;

    /// <summary>Large enough that a content-reading walk of it cannot finish quickly.</summary>
    private static readonly string BigRoot =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>Matches nothing, so the index returns empty and the scan is offered.</summary>
    private const string AbsentTerm = "*.zqxwv8813";

    protected override void OnUISetup()
    {
        Assert.IsNotNull(WaitForElement("DirectoryTree", 15), "Default FileSystem tab did not load.");

        Type($">{BigRoot}");
        Thread.Sleep(1500);
        Type($"?{AbsentTerm}");

        Assert.IsNotNull(WaitForElement("ResultList", 20), "Search tab did not open.");
    }

    private void Type(string text)
    {
        var ai = WaitForElement("AiInputBox", 10);
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Click();
        Wait.UntilInputIsProcessed();
        Keyboard.Type(text);
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
    }

    [TestMethod]
    public void AScanInProgressCanBeStopped()
    {
        var scan = WaitForElement("ScanFolder", Timeout);
        Assert.IsNotNull(scan, "the scan was never offered, so there is nothing to stop");

        scan!.Click();
        Wait.UntilInputIsProcessed();

        // Stop only appears while a scan is running, so finding it is itself the proof that we caught one
        // mid-flight rather than after it had quietly finished.
        var stop = WaitForElement("StopScan", Timeout);
        Assert.IsNotNull(stop, "no Stop button appeared while the scan was running");

        stop!.Click();
        Wait.UntilInputIsProcessed();

        // A stopped scan reports what it had found so far — never a total, which it cannot know.
        Assert.IsTrue(WaitForText("Scan stopped", Timeout),
            "the scan did not report being stopped — the button did nothing, or it ran to completion anyway");
    }

    [TestMethod]
    public void AStoppedScanDoesNotKeepAddingResults()
    {
        var scan = WaitForElement("ScanFolder", Timeout);
        Assert.IsNotNull(scan);
        scan!.Click();
        Wait.UntilInputIsProcessed();

        var stop = WaitForElement("StopScan", Timeout);
        Assert.IsNotNull(stop);
        stop!.Click();

        Assert.IsTrue(WaitForText("Scan stopped", Timeout), "scan did not stop");

        // Cancellation that leaves the walk running would keep appending rows behind a banner that claims
        // it stopped — the worst of both, since the user believes they took back control.
        var settled = CountResults();
        Thread.Sleep(3000);

        Assert.AreEqual(settled, CountResults(), "rows kept arriving after the scan reported it had stopped");
    }

    private int CountResults()
    {
        var list = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ResultList"));
        return list?.FindAllChildren().Length ?? 0;
    }

    private bool WaitForText(string fragment, int seconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("VerificationBannerText"));
            if (el?.Name?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    private AutomationElement? WaitForElement(string automationId, int seconds = 8)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el is not null && !el.IsOffscreen) return el;
            Thread.Sleep(200);
        }
        return null;
    }
}
