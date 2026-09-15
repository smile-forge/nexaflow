using System;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
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
/// the value list — plus the toolbar's Export/Import, which are present-checked but never pressed, and the
/// input prompt, which is opened from New Key and cancelled before anything is written. Every write path in this view — new-key/rename/delete on the tree, and modify/new/delete
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

        // Present, never pressed: both raise a modal file picker, and Import writes the .reg into the registry.
        CheckPresent("Export toolbar button", "Registry_Export");
        CheckPresent("Import toolbar button", "Registry_Import");

        // The input prompt, opened and dismissed. New Key only raises the prompt — the key is created on
        // Confirm — so Confirm is present-checked and Cancel is what closes it.
        Check("The key tree's context menu offers New Key", OpenNewKeyPrompt);
        CheckPresent("Prompt confirm", "Registry_PromptConfirm");
        CheckDoes("Prompt cancel closes the prompt", "Registry_PromptCancel",
                  () => WaitForGone("Registry_PromptConfirm"));

        AssertJourney();
    }

    /// <summary>
    /// Right-clicks the key tree and picks "New Key…". The menu is a popup in its own window, so its item is
    /// found from the desktop rather than the shell window.
    /// </summary>
    private bool OpenNewKeyPrompt()
    {
        var tree = WaitForId("Registry_KeyTree", 5);
        if (tree is null) return false;
        tree.RightClick();
        Wait.UntilInputIsProcessed();

        var item = Retry.WhileNull(() => Automation.GetDesktop().FindFirstDescendant(cf => cf.ByName("New Key…")),
                                   TimeSpan.FromSeconds(5)).Result;
        if (item is null) return false;
        item.Click();
        Wait.UntilInputIsProcessed();
        return true;
    }
}
