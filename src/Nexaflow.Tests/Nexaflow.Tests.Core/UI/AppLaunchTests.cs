using FlaUI.Core.Conditions;
using Nexaflow.Tests.Core.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.UI;

[TestClass]
[TestCategory("UI")]
[CoversNode("chrome-frame")]
public class AppLaunchTests : UITestBase
{
    [TestMethod]
    public void App_LaunchesWithoutCrash()
    {
        Assert.IsNotNull(MainWindow);
        Assert.IsFalse(App.HasExited);
    }

    [TestMethod]
    public void MainWindow_HasExpectedTitle()
    {
        var title = MainWindow.Title;
        Assert.IsFalse(string.IsNullOrWhiteSpace(title), "Expected a non-empty window title.");
    }

    [TestMethod]
    public void RibbonControl_IsVisible()
    {
        var ribbon = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"));
        Assert.IsNotNull(ribbon, "RibbonControl automation element not found.");
        Assert.IsFalse(ribbon.IsOffscreen, "RibbonControl should be on screen.");
    }

    [TestMethod]
    public void AiInputBox_IsPresent()
    {
        var input = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AiInputBox"));
        Assert.IsNotNull(input, "AiInputBox automation element not found.");
    }
}
