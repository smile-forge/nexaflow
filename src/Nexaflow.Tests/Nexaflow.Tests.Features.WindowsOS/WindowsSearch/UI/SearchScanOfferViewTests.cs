using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;
using System.IO;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// The folder-scan offer in the real shell. Unlike the verification banner, this one CAN be provoked
/// deterministically: a query no machine's index will match forces the empty-result path, which is exactly
/// when the offer should appear.
/// <para>
/// Worth a UI test because the whole point of the change is that the scan is a decision the user makes.
/// If the buttons aren't reachable, the feature is a slow path nobody can opt into — and that failure is
/// invisible to every headless test, which only sees the planner returning the right phase.
/// </para>
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("win-search-folder-scan")]
public class SearchScanOfferViewTests : UITestBase
{
    /// <summary>
    /// A scoped search is quick, but the index is still a service call. Generous because a wait that is
    /// merely too short reads exactly like a banner that was never wired up — which is the opposite of
    /// what this test is for.
    /// </summary>
    private const int ScanOfferTimeout = 60;

    /// <summary>A glob nothing matches, so the index reliably returns nothing. A glob rather than a bare
    /// word because that is the query shape the browser hands to a Search tab.</summary>
    private const string AbsentTerm = "*.zqxwv8813";

    /// <summary>
    /// A scoped folder rather than This PC. Scoping keeps the query fast, and — more importantly — a
    /// whole-machine search hides the case under test: it is the one path where nothing can be scanned
    /// until the user picks a location.
    /// </summary>
    private static readonly string ScanRoot = Path.GetTempPath();

    protected override void OnUISetup()
    {
        Assert.IsNotNull(WaitForElement("DirectoryTree", 15), "Default FileSystem tab did not load.");

        var ai = WaitForElement("AiInputBox", 10);
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Click();
        Wait.UntilInputIsProcessed();

        // Navigate somewhere real first, so the search that follows is scoped to a folder.
        Keyboard.Type($">{ScanRoot}");
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(1500);

        ai = WaitForElement("AiInputBox", 10);
        Assert.IsNotNull(ai, "AI input box not found after navigating.");
        ai!.Click();
        Wait.UntilInputIsProcessed();

        Keyboard.Type($"?{AbsentTerm}");
        Keyboard.Press(VirtualKeyShort.RETURN);

        Assert.IsNotNull(WaitForElement("ResultList", 20), "Search tab did not open.");
    }

    [TestMethod]
    public void AnEmptyIndexResultOffersTheScan()
    {
        // Asserted on the TEXT, not the Border around it: a Border is not a UIA control and isn't reliably
        // discoverable, so looking for it would pass by finding nothing — which is how a banner assertion
        // ends up proving nothing at all.
        var text = WaitForElement("VerificationBannerText", ScanOfferTimeout);

        Assert.IsNotNull(text, "an empty result must say why rather than showing a bare empty list");
        StringAssert.Contains(text!.Name, "scan",
            "the message has to name the way out, not just report the emptiness");
    }

    [TestMethod]
    public void TheScanButtonsAreReachable()
    {
        // The regression this guards: a phase the ViewModel sets but no template shows. Everything else
        // would still pass — the planner returns OfferScan, the command exists — and the user would simply
        // never be able to run a scan.
        foreach (var id in new[] { "ScanFolder", "DeclineScan" })
        {
            var button = WaitForElement(id, ScanOfferTimeout);
            Assert.IsNotNull(button, $"'{id}' should be offered when the index found nothing");
            Assert.IsFalse(button!.IsOffscreen, $"'{id}' is present but not visible");
        }
    }

    [TestMethod]
    public void VerificationButtonsAreNotOfferedInstead()
    {
        // Two prompts share the banner and they must not be confusable: "check these candidates" costs
        // seconds, "scan this tree" costs minutes.
        foreach (var id in new[] { "VerifyRemaining", "SkipVerification" })
        {
            var button = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(id));
            Assert.IsTrue(button is null || button.IsOffscreen,
                $"'{id}' belongs to the verification prompt, not the scan offer");
        }
    }

    [TestMethod]
    public void ClickingScanActuallyRunsIt()
    {
        // Finding the button proves it is on screen; pressing it proves it is connected to something.
        // Without this the command could be unbound and every other assertion here would still pass.
        var scan = WaitForElement("ScanFolder", ScanOfferTimeout);
        Assert.IsNotNull(scan, "no scan button to press");

        scan!.Click();
        Wait.UntilInputIsProcessed();

        // The scan reports through the same banner. Either wording of a finished scan names it, so this
        // holds whether or not the temp folder happens to contain a match.
        var settled = WaitForText("Folder scan", ScanOfferTimeout);

        Assert.IsTrue(settled, "the scan never reported a result — the button did nothing");
    }

    [TestMethod]
    public void DecliningTheScanDismissesTheOffer()
    {
        var decline = WaitForElement("DeclineScan", ScanOfferTimeout);
        Assert.IsNotNull(decline);

        decline!.Click();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);

        var stillOffered = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ScanFolder"));
        Assert.IsTrue(stillOffered is null || stillOffered.IsOffscreen,
            "declining must retire the offer — an option that survives being declined reads as broken");

        // The whole banner goes, not just its buttons. Leaving the text behind would keep telling the user
        // about a decision they have already made.
        var text = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("VerificationBannerText"));
        Assert.IsTrue(text is null || text.IsOffscreen,
            "the banner should be gone once its question has been answered");
    }

    /// <summary>Waits for the banner to say something containing <paramref name="fragment"/>.</summary>
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
