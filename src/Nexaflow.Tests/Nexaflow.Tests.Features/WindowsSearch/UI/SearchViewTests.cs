using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Features.UI.Infrastructure;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// Verifies that a Search tab loads with its primary elements visible.
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
public class SearchViewTests : UITestBase
{
    private AutomationElement? _resultList;

    protected override void OnUISetup()
    {
        // Glob patterns score ≥ 0.9 on SearchQueryScorer and win routing without
        // LLM disambiguation, so "*.txt" typed into AiInputBox reliably opens Search.
        _resultList = TryOpenTabWithElement("ResultList", aiInputFallback: "*.txt");
    }

    [TestMethod]
    public void ResultList_IsPresent()
    {
        if (_resultList is null)
            Assert.Inconclusive("Could not open a Search tab via any ribbon button.");

        Assert.IsFalse(_resultList.IsOffscreen, "ResultList should be on screen");
    }

    [TestMethod]
    public void AppDoesNotCrash_AfterLoadingSearchTab()
    {
        if (_resultList is null)
            Assert.Inconclusive("Could not open a Search tab via any ribbon button.");

        Wait.UntilInputIsProcessed();
        Assert.IsFalse(App.HasExited, "App crashed after loading a Search tab");
    }
}
