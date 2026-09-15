using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.Tabular.UI;

/// <summary>
/// The one UI journey for the Tabular (CSV/TSV) viewer — the integration test registered at the feature's
/// UI node. It opens a sample file via the explicit <b>"As Table"</b> ActionStrip button (not a
/// default-mapping double-click), confirms the toolbar descriptor, then opens each side surface in turn —
/// the Template This popup and the Apply-Template panel — and exercises their controls. Every check is
/// soft, so a single gap doesn't hide the rest.
///
/// Individual controls are asserted by view-model unit tests at their own leaf nodes; this proves the
/// wiring holds end-to-end. A template is saved, applied and deleted again, in the journey's throwaway
/// config dir.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("tabular")]
public class TabularJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("tabular-ui")]
    public void Tabular_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("tabular").First());
        var view = OpenFileVia(TestSampleData.Path("tabular"), file, "As Table", "TabularView");
        Assert.IsNotNull(view, "TabularView did not open via the 'As Table' action.");

        // ── Toolbar ───────────────────────────────────────────────────────────
        // The descriptor is how the user checks the file was read the way they expect.
        var shape = CheckPresent("Detected-shape label", "Tabular_ShapeLabel");
        Check("Shape descriptor resolved past 'Detecting…'",
              () => shape is not null && !shape.Name.Contains("Detecting"));

        // ── Template This popup ───────────────────────────────────────────────
        // Opening seeds the name and scope from the file; the pattern box follows the name-pattern scope.
        Check("Template This opens the popup", OpenTemplatePopup);
        Check("The name is seeded from the file", () =>
            FindInAppWindows("Tabular_TemplateName")?.AsTextBox().Text == Path.GetFileNameWithoutExtension(file));
        Check("The name-pattern scope enables its pattern box", () =>
            SelectRadio("Tabular_ScopeGlob") && WaitForFs(() => FindInAppWindows("Tabular_GlobPattern")?.IsEnabled == true, 2));
        Check("The folder scope disables it again", () =>
            SelectRadio("Tabular_ScopeFolder") && WaitForFs(() => FindInAppWindows("Tabular_GlobPattern")?.IsEnabled == false, 2));
        Check("Cancel closes the popup", () => PressInDialog("Tabular_TemplateCancel"));

        // Saved as selectable-only, so it never auto-applies to another file — and into the journey's throwaway config.
        Check("Template This opens it again", OpenTemplatePopup);
        Check("The template can be renamed", () => TypeInPopup("Tabular_TemplateName", "Journey template"));
        Check("The selectable-only scope", () => SelectRadio("Tabular_ScopeManual"));
        Check("Save stores it and closes the popup", () => PressInDialog("Tabular_TemplateSave"));

        // ── Apply Template panel ──────────────────────────────────────────────
        var templates = 0;
        CheckDoes("Apply Template opens the panel, listing the saved template", "Tabular_ApplyTemplate",
                  () => WaitForFs(() => (templates = CountOf("Tabular_TemplateApply")) > 0, 3));
        CheckPresent("Show only compatible toggle", "Tabular_ShowOnlyCompatible");
        Check("Applying it closes the panel", () =>
            PressNth("Tabular_TemplateApply", 0) && WaitForGone("Tabular_ShowOnlyCompatible"));

        CheckDoes("Apply Template opens the panel again", "Tabular_ApplyTemplate",
                  () => WaitForFs(() => CountOf("Tabular_TemplateApply") == templates, 3));
        Check("✕ deletes the template", () =>
            PressNth("Tabular_TemplateDelete", 0) && WaitForFs(() => CountOf("Tabular_TemplateApply") == templates - 1, 3));
        CheckDoes("Templates panel close", "Tabular_TemplatePanelClose",
                  () => WaitForGone("Tabular_ShowOnlyCompatible"));

        AssertJourney();
    }

    /// <summary>Opens the Template This popup, which is its own window, and waits for its Save.</summary>
    private bool OpenTemplatePopup() =>
        PressNth("Tabular_TemplateThis", 0) && WaitFor(() => FindInAppWindows("Tabular_TemplateSave"), 5) is not null;

    /// <summary>Types into a text box in the popup — <see cref="UiJourneyTestBase.TypeInto"/> searches the main window only.</summary>
    private bool TypeInPopup(string automationId, string text)
    {
        var box = FindInAppWindows(automationId);
        if (box is null) return false;
        box.AsTextBox().Text = text;
        return WaitForFs(() => box.AsTextBox().Text == text, 2);
    }
}
