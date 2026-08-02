using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// The query in the header is editable in place. It is the only control on this page that can WIDEN a
/// search — refining, scanning and verifying all narrow — so a term added by mistake is unrecoverable
/// without it.
/// <para>
/// A TextBox is a real UIA control with a value pattern, so unlike the banner and the overlay this can be
/// asserted directly rather than inferred from layout.
/// </para>
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("win-search-ui")]
public class SearchQueryEditViewTests : UITestBase
{
    protected override void OnUISetup()
    {
        Assert.IsNotNull(WaitForElement("DirectoryTree", 15), "Default FileSystem tab did not load.");

        var ai = WaitForElement("AiInputBox", 10);
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Click();
        Wait.UntilInputIsProcessed();

        Keyboard.Type("?*.zqxwv8813");
        Keyboard.Press(VirtualKeyShort.RETURN);

        Assert.IsNotNull(WaitForElement("ResultList", 20), "Search tab did not open.");
    }

    [TestMethod]
    public void TheQueryIsShownInAnEditableField()
    {
        var box = WaitForElement("SearchQueryBox", 15)?.AsTextBox();

        Assert.IsNotNull(box, "the query should be presented as an editable field, not a label");
        Assert.IsFalse(box!.IsReadOnly, "a read-only field cannot drop a term");
        StringAssert.Contains(box.Text, "zqxwv8813", "the field should hold the query that was run");
    }

    /// <remarks>
    /// Covers that the field accepts an edit and survives Enter — NOT that the edited query then ran. The
    /// TextBox shows its own local text either way, so it cannot tell the two apart; that the new terms
    /// replace rather than merge is asserted headlessly in
    /// <c>SearchViewModelTests.EditingTheQueryReplacesTheSearchRatherThanNarrowingIt</c>.
    /// </remarks>
    [TestMethod]
    public void TheFieldAcceptsAnEditAndKeepsIt()
    {
        var box = WaitForElement("SearchQueryBox", 15)?.AsTextBox();
        Assert.IsNotNull(box);

        box!.Text = "*.zqxwv9999";
        box.Click();
        Keyboard.Press(VirtualKeyShort.END);
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(1500);

        var after = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchQueryBox"))?.AsTextBox();
        Assert.IsNotNull(after, "the field vanished after Enter");
        StringAssert.Contains(after!.Text, "zqxwv9999", "the edit was discarded");
    }

    private AutomationElement? WaitForElement(string automationId, int seconds = 8)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < System.TimeSpan.FromSeconds(seconds))
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (el is not null && !el.IsOffscreen) return el;
            Thread.Sleep(200);
        }
        return null;
    }
}
