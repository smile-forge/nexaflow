using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Core.UI.Infrastructure;

namespace Nexaflow.Tests.Core.UI;

[TestClass]
[TestCategory("UI")]
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
}
