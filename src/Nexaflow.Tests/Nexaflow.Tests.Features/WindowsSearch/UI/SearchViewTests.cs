using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// Drives Search the way a user does: the shell opens on the default FileSystem tab, Ctrl+Tab
/// moves focus to the AI input (see MainWindow.xaml.cs), and a glob query routes deterministically
/// to Windows Search — which opens and immediately activates a Search tab. No ribbon interaction.
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
public class SearchViewTests : UITestBase
{
    private AutomationElement? _searchTab;
    private AutomationElement? _resultList;

    protected override void OnUISetup()
    {
        // Focus the app, then Ctrl+Tab to land in the AI input regardless of where focus starts.
        MainWindow.Focus();
        Wait.UntilInputIsProcessed();

        using (Keyboard.Pressing(VirtualKeyShort.CONTROL))
            Keyboard.Type(VirtualKeyShort.TAB);
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
    private AutomationElement? WaitForElement(string automationId)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el is not null && !el.IsOffscreen) return el;
            System.Threading.Thread.Sleep(150);
        }
        return null;
    }
}
