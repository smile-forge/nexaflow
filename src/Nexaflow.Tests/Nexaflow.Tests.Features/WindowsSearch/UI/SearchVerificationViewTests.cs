using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// The verification banner in the real shell. What can be asserted deterministically here is the wiring —
/// that the banner exists in the visual tree and stays hidden for a search the index answered outright.
/// Whether it *appears* depends on the machine's Windows Search index returning content candidates for a
/// given pattern, which no test can arrange; the rule that decides it is covered without an index by
/// <c>VerificationPlannerTests</c>, and the settling itself by <c>SearchVerifierTests</c>.
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("search-verify")]
public class SearchVerificationViewTests : UITestBase
{
    protected override void OnUISetup()
    {
        Assert.IsNotNull(WaitForElement("DirectoryTree", 15), "Default FileSystem tab did not load.");

        var ai = WaitForElement("AiInputBox", 10);
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Click();
        Wait.UntilInputIsProcessed();

        // A literal glob: the index answers it outright, so no row is speculative.
        Keyboard.Type("*.txt");
        Keyboard.Press(VirtualKeyShort.RETURN);

        Assert.IsNotNull(WaitForElement("ResultList"), "Search tab did not open.");
    }

    [TestMethod]
    public void LiteralSearch_ShowsNoVerificationBanner()
    {
        // The banner is a cost signal. Showing it for a search that needed no verification would train the
        // user to ignore it — which is exactly when it matters.
        var banner = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("VerificationBanner"));

        Assert.IsTrue(banner is null || banner.IsOffscreen,
            "a literal query is fully answered by the index, so nothing should be pending verification");
    }

    [TestMethod]
    public void VerificationControlsAreNotOfferedWhenThereIsNothingToVerify()
    {
        foreach (var id in new[] { "VerifyRemaining", "SkipVerification" })
        {
            var button = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(id));
            Assert.IsTrue(button is null || button.IsOffscreen, $"'{id}' should be hidden outside the prompt phase");
        }
    }

    [TestMethod]
    public void AppSurvivesARegexSearch()
    {
        // A regex takes the speculative path — translate, widen, classify, and possibly start a background
        // sweep. Whether it matches anything here depends on the machine, but it must not take the app down.
        var ai = WaitForElement("AiInputBox", 10);
        Assert.IsNotNull(ai);
        ai!.Click();
        Wait.UntilInputIsProcessed();

        Keyboard.Type(@"?/report\d+\.txt/");
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(2000);   // let any sweep start and report

        Assert.IsFalse(App.HasExited, "App crashed running a content-regex search");
    }

    /// <summary>Polls for an element by automation id for a few seconds (the tab opens async).</summary>
    private AutomationElement? WaitForElement(string automationId, int seconds = 8)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el is not null && !el.IsOffscreen) return el;
            System.Threading.Thread.Sleep(150);
        }
        return null;
    }
}
