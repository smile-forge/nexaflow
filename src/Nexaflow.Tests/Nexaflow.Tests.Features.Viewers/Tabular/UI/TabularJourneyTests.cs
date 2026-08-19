using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular.UI;

/// <summary>
/// The one UI journey for the Tabular (CSV/TSV) viewer — the integration test registered at the feature's
/// UI node. It opens a sample file via the explicit <b>"As Table"</b> ActionStrip button (not a
/// default-mapping double-click), confirms the toolbar descriptor, then opens each side surface in turn —
/// the Template This popup and the Apply-Template panel — and exercises their controls. Every check is
/// soft, so a single gap doesn't hide the rest.
///
/// Individual controls are asserted by view-model unit tests at their own leaf nodes; this proves the
/// wiring holds end-to-end. Nothing here saves or applies a template, so no config is written.
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
        // Opening seeds the name/scope fields; Cancel dismisses it without writing anything.
        CheckPresent("Template This", "Tabular_TemplateThis");

        // ── Apply Template panel ──────────────────────────────────────────────
        CheckDoes("Apply Template opens the panel", "Tabular_ApplyTemplate",
                  () => WaitForId("Tabular_ShowOnlyCompatible", 3) is not null);
        CheckPresent("Show only compatible toggle", "Tabular_ShowOnlyCompatible");
        CheckDoes("Templates panel close", "Tabular_TemplatePanelClose",
                  () => WaitForId("Tabular_ShowOnlyCompatible", 2) is null);

        AssertJourney();
    }
}
