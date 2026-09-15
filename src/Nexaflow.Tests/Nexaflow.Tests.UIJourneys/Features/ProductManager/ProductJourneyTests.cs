using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.ProductManager.UI;

/// <summary>
/// The Product journey — one launch across every surface the feature presents, reached the way a user reaches
/// them: the folder viewlet opens a product, the version menu raises its three cards, the root's integrity widget
/// opens the Integrity page, a needs-attention row selects a node, an issue on the Integrity page lands back on
/// that node, and a <c>?</c> search from the product walks a result into the graph viewer.
/// <para>
/// <b>It works on a copy.</b> The seeded product (built by ProductUiFixtureTests, since only a suite that
/// references ProductStore can write one) is copied into this test's throwaway config dir first, because the
/// journey edits the tree — removes and re-adds a concern, removes and adds snaplinks, applies a coverage
/// suggestion — and the next run must start from the same seed. The empty product used for "Add root node" is
/// opened in place: its prompt is cancelled, so nothing is written to it.
/// </para>
/// <para>
/// <b>Present, never pressed:</b> Take snapshot (it exports and, in a git repo, commits and tags); Apply fix and
/// the three target pickers (they re-point a link to whatever the picker offers); Open target file, a snaplink's
/// open ↗ and Open result (each opens a tab over the one being walked); "+ Add selected" (it needs a file picked
/// in the tree); and the coverage suggestion's own Open node (the broken link's is the one pressed).
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("product-ui")]
public class ProductJourneyTests : UiJourneyTestBase
{
    private const string BuiltBy = "ProductUiFixtureTests in Nexaflow.Tests.Features";

    /// <summary>A scan, several tabs and three cards, each waited on; the budget only bites once something is wrong.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromSeconds(300);

    [TestMethod]
    [CoversNode("product-sunburst")]
    [CoversNode("product-integrity")]
    [CoversNode("product-search-page")]
    [CoversNode("product-graph")]
    public void Product_ViewletMenusIntegrityNodeAndSearch_RespondInOnePass()
    {
        // ── An empty product: the only state that offers "Add root node" ──────────────
        NavigateFileBrowserTo(RequiredFixture.Folder("product-empty", BuiltBy));
        CheckDoes("The viewlet opens the empty product", "Product_ViewletOpen",
                  () => WaitForId("Product_AddRoot", 15) is not null);
        CheckDoes("Add root node asks for a title", "Product_AddRoot",
                  () => WaitForId("ShellPromptCancel", 5) is not null);
        CheckDoes("Cancelling the title adds nothing", "ShellPromptCancel",
                  () => WaitForGone("ShellPromptCancel") && WaitForId("Product_AddRoot", 3) is not null);
        CloseProductTab();

        // ── The seeded product, on a copy ─────────────────────────────────────────────
        var product = Path.Combine(ConfigDir, "product-journey");
        RequiredFixture.CopyInto("product", product, BuiltBy);
        NavigateFileBrowserTo(product);
        CheckDoes("The viewlet opens the product", "Product_ViewletOpen",
                  () => WaitForId("Product_VersionMenu", 15) is not null);

        // ── The version menu's three cards ────────────────────────────────────────────
        Check("Take snapshot opens its card", () =>
            PickFromVersionMenu("Take snapshot") && WaitForId("Product_SnapshotCancel", 5) is not null);
        CheckPresent("Take snapshot", "Product_SnapshotTake");
        CheckDoes("Cancel closes the snapshot card", "Product_SnapshotCancel", () => WaitForGone("Product_SnapshotTake"));

        Check("Restructure opens its card", () =>
            PickFromVersionMenu("Restructure") && WaitForId("Product_RestructureDone", 5) is not null);
        CheckDoes("✕ closes the restructure card", "Product_RestructureCloseX", () => WaitForGone("Product_RestructureDone"));
        Check("Restructure opens again", () =>
            PickFromVersionMenu("Restructure") && WaitForId("Product_RestructureDone", 5) is not null);
        CheckDoes("Done closes the restructure card", "Product_RestructureDone", () => WaitForGone("Product_RestructureCloseX"));

        Check("Settings opens its card", () =>
            PickFromVersionMenu("Settings") && WaitForId("Product_SettingsCancel", 5) is not null);
        var concernDefs = 0;
        Check("A concern name can be typed", () =>
        {
            concernDefs = CountOf("Product_SettingsConcernRemove");
            return TypeInto("Product_SettingsNewConcern", "journey");
        });
        CheckDoes("+ Add adds the concern", "Product_SettingsConcernAdd",
                  () => WaitForFs(() => CountOf("Product_SettingsConcernRemove") == concernDefs + 1, 3));
        Check("✕ removes it again", () =>
            PressNth("Product_SettingsConcernRemove", -1)
            && WaitForFs(() => CountOf("Product_SettingsConcernRemove") == concernDefs, 3));
        CheckDoes("Cancel closes the settings card", "Product_SettingsCancel", () => WaitForGone("Product_SettingsSave"));
        Check("Settings opens again", () =>
            PickFromVersionMenu("Settings") && WaitForId("Product_SettingsSave", 5) is not null);
        CheckDoes("Save closes the settings card", "Product_SettingsSave", () => WaitForGone("Product_SettingsCancel"));

        // ── The root's integrity widget, into the Integrity page ──────────────────────
        CheckPresent("A needs-attention row for the faulted node", "Product_AttentionRow");
        CheckDoes("The integrity widget opens the Integrity page", "Product_RootWidget",
                  () => WaitForId("Integrity_Revalidate", 10) is not null);
        CheckDoes("Re-validate scans the product", "Integrity_Revalidate",
                  () => WaitForId("Integrity_RemoveLink", 30) is not null);

        Check("A broken code link offers its class picker", () => SelectIssueShowing("Integrity_CodeDrop"));
        CheckPresent("'Moved?' file picker", "Integrity_FileDrop");
        CheckPresent("Apply fix", "Integrity_ApplyFix");
        CheckPresent("Open target file", "Integrity_OpenTarget");

        Check("A broken markdown link offers its heading picker", () => SelectIssueShowing("Integrity_MdDrop"));
        var issues = 0;
        Check("Remove snaplink takes the markdown issue off the list", () =>
        {
            issues = IssueRows().Length;
            return PressNth("Integrity_RemoveLink", 0) && WaitForFs(() => IssueRows().Length == issues - 1, 10);
        });

        Check("The coverage suggestion is listed", () => SelectIssueShowing("Integrity_AddDeclaredLink"));
        CheckPresent("Open node in Product, from the suggestion", "Integrity_AdvisoryOpenNode");
        CheckDoes("Add link resolves the suggestion", "Integrity_AddDeclaredLink",
                  () => WaitForGone("Integrity_AddDeclaredLink", 10));

        Check("The stale ast is listed", () => SelectIssueShowing("Integrity_AstOpenNode"));
        CheckPresent("Clear ast", "Integrity_ClearAst");

        // ── Back on the product: the faulted node ─────────────────────────────────────
        Check("The Product tab comes back to the front", () =>
            ClickTab("ProductManager") && WaitForId("Product_AttentionRow", 5) is not null);
        CheckDoes("The needs-attention row selects the node", "Product_AttentionRow",
                  () => WaitForId("Product_StatusCycle", 5) is not null);
        CheckInvoke("The status pill cycles the node's status", "Product_StatusCycle");

        // The seed gives the node 'tests' then 'docs'. The remove ✕ and the 🔗 are transparent until hovered,
        // which UI Automation does not care about: they are in the tree and invoke either way.
        var boxes = 0;
        Check("The node shows its concern boxes", () => (boxes = CountOf("Product_ConcernCycle")) >= 2);
        Check("A concern's status pill cycles", () => PressNth("Product_ConcernCycle", 1));
        Check("✕ removes the docs concern", () =>
            PressNth("Product_ConcernRemove", 1) && WaitForFs(() => CountOf("Product_ConcernCycle") == boxes - 1, 3));
        CheckDoes("+ concern offers docs back", "Product_AddConcern", () => PickMenuItem("docs"));
        Check("Picking it restores the box", () => WaitForFs(() => CountOf("Product_ConcernCycle") == boxes, 3));

        Check("The tests concern's 🔗 menu opens its snaplinks", () =>
            PressNth("Product_ConcernAttach", 0) && PickMenuItem("Modify")
            && WaitForId("Product_SnaplinksClose", 5) is not null);
        CheckPresent("Open a snaplink in a new tab", "Product_SnaplinkOpen");
        CheckPresent("+ Add selected", "Product_SnaplinkAddPicked");
        var links = 0;
        Check("✕ removes a snaplink", () =>
        {
            links = CountOf("Product_SnaplinkRemove");
            return links > 0 && PressNth("Product_SnaplinkRemove", 0)
                && WaitForFs(() => CountOf("Product_SnaplinkRemove") == links - 1, 3);
        });
        Check("The Link URL tab takes a URL", () =>
            SelectTab("Link URL") && TypeInto("Product_SnaplinkUrl", "https://example.com/journey"));
        CheckDoes("+ Add adds the URL as a snaplink", "Product_SnaplinkAddUrl",
                  () => WaitForFs(() => CountOf("Product_SnaplinkRemove") == links, 3));
        CheckDoes("✕ closes the snaplinks card", "Product_SnaplinksCloseX", () => WaitForGone("Product_SnaplinksClose"));

        Check("The node's 🔗 menu opens its attachments", () =>
            PressNth("Product_NodeAttach", 0) && PickMenuItem("Modify")
            && WaitForId("Product_SnaplinksClose", 5) is not null);
        CheckDoes("Close closes the attachments card", "Product_SnaplinksClose", () => WaitForGone("Product_SnaplinksCloseX"));

        // ── From the Integrity page back to the node ──────────────────────────────────
        Check("The Integrity tab comes back to the front", () =>
            ClickTab("ProductIntegrity") && WaitForId("Integrity_Revalidate", 5) is not null);
        Check("The broken code link is still listed", () => SelectIssueShowing("Integrity_CodeDrop"));
        CheckDoes("Open node in Product lands on the node", "Integrity_OpenNode",
                  () => WaitForId("Product_StatusCycle", 10) is not null);

        // ── A '?' search from the product, into the graph ─────────────────────────────
        // Gadget is a graph node with a file behind it and nothing else by that name, so the first result row is
        // one the graph viewer can open in Code.
        Check("A '?' search opens the results page", () =>
            Ask("?Gadget") && WaitForId("ProductSearch_ShowInGraph", 20) is not null);
        CheckPresent("Open result", "ProductSearch_OpenRow");
        CheckDoes("In graph opens the viewer on that node", "ProductSearch_ShowInGraph",
                  () => WaitForId("Graph_OpenInCode", 15) is not null);
        CheckInvoke("Open in Code", "Graph_OpenInCode");
        Check("The app survives opening the node's file", () => !App.HasExited);

        AssertJourney();
    }

    // ── Driving ───────────────────────────────────────────────────────────────

    private void CloseProductTab()
    {
        WaitForId("CloseTab_ProductManager", 5)?.Click();
        Wait.UntilInputIsProcessed();
        WaitForId("DirectoryTree", 10);
    }

    private bool ClickTab(string pageKind)
    {
        var tab = WaitForId($"TabItem_{pageKind}", 5);
        if (tab is null) return false;
        tab.Click();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>Types into the AI bar and submits, the way a user runs a search. False when the bar never took it.</summary>
    private bool Ask(string text)
    {
        var bar = WaitForId("AiInputBox", 10);
        if (bar is null || !WaitForFs(() => bar.IsEnabled, 30)) return false;
        bar.Click();
        Keyboard.Type(text);
        Wait.UntilInputIsProcessed();
        if (!bar.AsTextBox().Text.Contains(text, StringComparison.Ordinal)) return false;
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>The version menu is a ContextMenu built in code, so its items carry no ids: picked by label.</summary>
    private bool PickFromVersionMenu(string item)
    {
        var menu = WaitForId("Product_VersionMenu", 5);
        if (menu is null) return false;
        menu.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return PickMenuItem(item);
    }

    private bool SelectTab(string header)
    {
        var tab = WaitFor(() => MainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TabItem).And(cf.ByName(header))), 5);
        if (tab is null) return false;
        tab.AsTabItem().Select();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => tab.AsTabItem().IsSelected, 2);
    }

    private AutomationElement[] IssueRows() =>
        WaitForId("Integrity_IssueList", 5)?.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)) ?? [];

    /// <summary>
    /// Selects each Integrity row in turn until one whose detail shows <paramref name="automationId"/>. The rows
    /// are three kinds of item with three detail templates, and their order depends on the scan, so the journey
    /// asks for the kind it wants rather than a position.
    /// </summary>
    private bool SelectIssueShowing(string automationId)
    {
        foreach (var row in IssueRows())
        {
            row.Patterns.SelectionItem.PatternOrDefault?.Select();
            Wait.UntilInputIsProcessed();
            if (WaitForId(automationId, 1) is not null) return true;
        }
        return false;
    }
}
