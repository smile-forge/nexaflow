using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsApps.UI;

/// <summary>
/// The one UI journey for the Installed Apps tab — the integration test registered at the feature's UI
/// node. It opens from the ribbon (there is no file to double-click), waits for the discovery scan to
/// replace "Discovering…" with the app count, then exercises the read-only chrome: the app list and the
/// Refresh button.
/// <para>
/// The per-row "⋯" menu is deliberately never opened here: two of its three entries (Uninstall, Remove
/// from list) mutate the machine's installed-software state, so they are asserted headlessly at their own
/// leaf nodes instead — where the confirmation gate can be declined.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("windowsapps")]
public class WindowsAppsJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "WindowsApps";

    [TestMethod]
    [CoversNode("windowsapps-ui")]
    public void WindowsApps_Controls_RespondInOnePass()
    {
        var view = WaitForId("WindowsAppsView", 15);
        Assert.IsNotNull(view, "WindowsAppsView did not open via --openTab WindowsApps.");

        // The scan is a background task; the list is the thing that proves it landed.
        CheckPresent("App list", "WindowsApps_List", seconds: 30);

        // Refresh re-runs the scan — safe, read-only, and the list must survive it.
        CheckDoes("Refresh", "WindowsApps_Refresh", () => WaitForId("WindowsApps_List", 30) is not null);

        AssertJourney();
    }
}
