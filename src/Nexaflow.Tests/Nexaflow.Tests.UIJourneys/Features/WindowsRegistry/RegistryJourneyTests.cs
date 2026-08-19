using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsRegistry.UI;

/// <summary>
/// One-pass UI journey for the Windows Registry browser/editor (PageKind "Registry", root
/// <c>RegistryView</c>). Like SystemInfo it opens from the ribbon rather than a file, so the journey
/// uses <see cref="UITestBase.TryOpenTabWithElement"/> to click ribbon buttons until the view root
/// appears — and, because WindowsRegistry is <b>not</b> on the default ribbon and exposes no file/folder
/// action, it <see cref="Assert.Inconclusive(string)"/>s when the tab can't be opened in a fresh-config run.
/// <para>
/// Only the <b>read-only navigation</b> controls are exercised: the hive/root selector, the key tree, and
/// the value list. Every write path in this view — new-key/rename/delete on the tree, and modify/new/delete
/// on the value list — is approval-gated and potentially mutating, so those controls carry no AutomationId
/// and are never invoked here. The codec / writer / root logic is covered headlessly by the WindowsRegistry
/// unit tests (RegistryValueCodecTests, RegistryWriterTests, RegistryRootTests, RegistryValueRowTests,
/// RegistryTreeNodeTests). Checks are soft, so one gap doesn't hide the rest of the pass.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("win-registry")]
public class RegistryJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "WindowsRegistry";

    [TestMethod]
    public void Registry_ReadOnlyNavControls_RespondInOnePass()
    {
        var view = WaitForId("RegistryView", 15);
        Assert.IsNotNull(view, "RegistryView did not open via --openTab WindowsRegistry.");

        // Read-only navigation controls only — never the approval-gated write/rename/delete/new-value controls.
        CheckPresent("Root selector", "Registry_RootSelector");
        CheckInvoke("Root selector (open)", "Registry_RootSelector");
        CheckPresent("Key tree", "Registry_KeyTree");
        CheckPresent("Value list", "Registry_ValueList");

        AssertJourney();
    }
}
