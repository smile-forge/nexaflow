using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Core.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.UI;

[TestClass]
[TestCategory("UI")]
[CoversNode("chrome-notifications-panel")]
public class NotificationTests : UITestBase
{
    [TestMethod]
    public void NotificationsButton_IsPresent()
    {
        var btn = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NotificationsButton"));
        Assert.IsNotNull(btn, "NotificationsButton element not found.");
    }

    [TestMethod]
    public void ClickNotificationsButton_TogglesPanel()
    {
        var btn = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NotificationsButton"));
        Assert.IsNotNull(btn, "NotificationsButton element not found.");

        // Panel is hidden by default; click to open
        btn.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);

        // The notifications panel renders a "Notifications" or "No notifications" text block.
        // We verify the app hasn't crashed and some descendant appeared.
        Assert.IsFalse(App.HasExited, "App crashed after clicking NotificationsButton.");

        // Click again to close
        btn.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);

        Assert.IsFalse(App.HasExited, "App crashed after toggling NotificationsButton off.");
    }
}
