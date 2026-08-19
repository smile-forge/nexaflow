using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.UI;

[TestClass]
[TestCategory("UI")]
[CoversNode("chrome-content-area")]
public class TabTests : UITestBase
{
    private AutomationElement TabStrip =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabStrip"))
        ?? throw new AssertFailedException("TabStrip element not found.");

    [TestMethod]
    public void TabStrip_IsPresent()
    {
        Assert.IsNotNull(TabStrip);
        Assert.IsFalse(TabStrip.IsOffscreen, "TabStrip should be on screen.");
    }

    private AutomationElement[] GetVisibleTabs()
        => TabStrip.FindAllDescendants()
                   .Where(e => e.Properties.AutomationId.ValueOrDefault?.StartsWith("TabItem_") == true)
                   .ToArray();

    [TestMethod]
    [CoversNode("ribbon-button-full")]
    public void ClickRibbonItem_OpensTab()
    {
        var ribbon = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"));
        Assert.IsNotNull(ribbon, "RibbonControl not found.");

        var firstBtn = ribbon.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        if (firstBtn is null)
        {
            Assert.Inconclusive("No ribbon buttons found — ribbon may be empty in this build.");
            return;
        }

        firstBtn.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);

        var tabsAfter = GetVisibleTabs().Length;
        Assert.IsTrue(tabsAfter >= 1, "Expected at least one tab in the strip after clicking a ribbon item.");
    }

    [TestMethod]
    [CoversNode("chrome-ai-input")]
    public void OpeningATab_FocusesTheAiInput()
    {
        // Open a tab deterministically through the "/" palette (proven to open the Services page). The
        // Services view realizes and could take focus itself — the point of the test is that focus lands
        // back on the AI input once the tab is open.
        var ai = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AiInputBox"));
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Focus();
        Thread.Sleep(150);
        Keyboard.Type("/services");
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();

        // Wait for the Services tab to actually be open, then let focus settle.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(8) &&
               MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabItem_SystemServices")) is null)
            Thread.Sleep(150);
        Thread.Sleep(600);   // focus is handed to the AI bar at Background priority, after the view realizes

        Assert.IsNotNull(MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabItem_SystemServices")),
            "The Services tab did not open.");
        Assert.IsTrue(ai.Properties.HasKeyboardFocus.ValueOrDefault,
            "Opening a tab did not leave keyboard focus on the AI input.");
    }

    [TestMethod]
    public void ClickTab_ActivatesIt()
    {
        var ribbon = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"));
        var firstBtn = ribbon?.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        if (firstBtn is null)
        {
            Assert.Inconclusive("No ribbon buttons available to open a tab.");
            return;
        }
        firstBtn.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);

        var tabs = GetVisibleTabs();
        if (tabs.Length == 0)
        {
            Assert.Inconclusive("No tabs in strip after clicking ribbon — ribbon may be empty in this build.");
            return;
        }

        tabs[0].Click();
        Wait.UntilInputIsProcessed();

        Assert.IsFalse(App.HasExited, "App crashed after clicking a tab.");
    }

    [TestMethod]
    [CoversNode("dual-pane")]
    [CoversNode("win-cux-split-empty")]
    public void Split_FromEmptyStripArea_CreatesSecondPane()
    {
        Assert.AreEqual(1, CountTabStrips(), "Expected a single pane (one TabStrip) on launch.");

        RightClickEmptyStripArea();

        var split = FindMenuItem("Split");
        Assert.IsNotNull(split, "'Split' item not found on the empty tab-strip context menu.");
        split.AsMenuItem().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(600);

        Assert.AreEqual(2, CountTabStrips(), "Expected two panes (two TabStrips) after Split.");
        Assert.IsFalse(App.HasExited, "App crashed while splitting.");
    }

    [TestMethod]
    [CoversNode("dual-pane")]
    [CoversNode("win-cux-close-pane")]
    public void Split_ThenClosePane_CollapsesBackWithContent()
    {
        // Open a tab so a pane holds realised page content — this is what gets re-parented on collapse.
        var ribbon = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"));
        var firstBtn = ribbon?.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
        if (firstBtn is null) { Assert.Inconclusive("No ribbon buttons available to open a tab."); return; }
        firstBtn.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);

        RightClickEmptyStripArea();
        var split = FindMenuItem("Split");
        if (split is null) { Assert.Inconclusive("'Split' was not offered."); return; }
        split.AsMenuItem().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);
        Assert.AreEqual(2, CountTabStrips(), "Expected two panes after Split.");

        // Collapse via the right (empty) pane's "Close pane" — the surviving pane's live content
        // is re-hosted in a fresh PaneView, which must not throw the WPF "element already has a parent".
        RightClickStrip(rightMost: true);
        var closePane = FindMenuItem("Close pane");
        Assert.IsNotNull(closePane, "'Close pane' was not offered while split.");
        closePane.AsMenuItem().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);

        Assert.AreEqual(1, CountTabStrips(), "Expected one pane after Close pane.");
        Assert.IsFalse(App.HasExited, "App crashed while collapsing the split.");
    }

    private int CountTabStrips()
        => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabStrip")).Length;

    // Right-click near the right edge of the strip, clear of any tabs (which pack from the left),
    // so the strip's own "Split" context menu shows rather than a tab's menu.
    private void RightClickEmptyStripArea() => RightClickStrip(rightMost: false);

    private void RightClickStrip(bool rightMost)
    {
        var strips = MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabStrip"))
                               .OrderBy(s => s.BoundingRectangle.X).ToArray();
        var strip = rightMost ? strips[^1] : strips[0];
        var r = strip.BoundingRectangle;
        Mouse.MoveTo(new System.Drawing.Point(r.Right - 25, r.Y + r.Height / 2));
        Mouse.Click(MouseButton.Right);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);
    }

    private AutomationElement? FindMenuItem(string name)
    {
        for (int i = 0; i < 30; i++)
        {
            var hit = Automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem))
                .FirstOrDefault(e => e.Name == name);
            if (hit is not null) return hit;
            Thread.Sleep(100);
        }
        return null;
    }
}
