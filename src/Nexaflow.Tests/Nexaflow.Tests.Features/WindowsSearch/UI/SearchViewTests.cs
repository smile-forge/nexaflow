using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// Drives Search the way a user does: the shell opens on the default "This PC" FileSystem tab, a glob
/// typed into the AI input routes deterministically to Windows Search — which opens and immediately
/// activates a Search tab. No ribbon interaction.
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("win-search-ai-act")]
public class SearchViewTests : UITestBase
{
    private AutomationElement? _searchTab;
    private AutomationElement? _resultList;

    protected override void OnUISetup()
    {
        // The default "This PC" FileSystem tab opens on a *deferred* dispatcher tick after the window
        // appears (ShellServices.OpenDefaultTabs), and WindowsSearchQueryHandler only routes a glob to
        // Search while the active page exposes a FileSystemContext. So we must wait for that tab to load
        // before submitting — otherwise the query lands in an empty context and no Search tab opens. Run
        // alone the tab happens to win the race; in a full suite run it doesn't (the machine is busier),
        // which is why these tests passed individually but failed as part of the set.
        Assert.IsNotNull(WaitForElement("DirectoryTree", 15), "Default FileSystem tab did not load.");

        // Focus the AI input by clicking it — reliable across the foreground churn of a full suite run,
        // where Ctrl+Tab (which depends on the window being foreground) is not.
        var ai = WaitForElement("AiInputBox", 10);
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Click();
        Wait.UntilInputIsProcessed();

        // A glob scores ≥0.9 on SearchQueryScorer and wins routing without LLM disambiguation.
        Keyboard.Type("*.txt");
        Keyboard.Press(VirtualKeyShort.RETURN);

        // The Search tab opens and is activated immediately; the result list lives inside it.
        _searchTab  = WaitForElement("TabItem_Search");
        _resultList = WaitForElement("ResultList");
    }

    [TestMethod]
    public void SearchTab_OpensAndActivates()
    {
        Assert.IsNotNull(_searchTab, "A Search tab should be created after submitting a glob query.");
    }

    [TestMethod]
    public void ResultList_IsPresent()
    {
        Assert.IsNotNull(_resultList, "Search tab did not open — ResultList not found.");
        Assert.IsFalse(_resultList.IsOffscreen, "ResultList should be on screen");
    }

    [TestMethod]
    public void AppDoesNotCrash_AfterLoadingSearchTab()
    {
        Wait.UntilInputIsProcessed();
        Assert.IsFalse(App.HasExited, "App crashed after loading a Search tab");
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
