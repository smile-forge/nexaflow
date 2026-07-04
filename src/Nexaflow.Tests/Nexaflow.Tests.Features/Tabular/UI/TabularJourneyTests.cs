using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular.UI;

/// <summary>
/// One-pass UI journey for the Tabular (CSV/TSV) viewer: opens a sample file via the explicit
/// <b>"As Table"</b> ActionStrip button (not a default-mapping double-click), confirms the toolbar
/// descriptor + template buttons, then opens the Apply-Template side panel and exercises its
/// controls — soft-asserting each so a single gap doesn't hide the rest of the coverage.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
public class TabularJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Tabular_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("tabular").First());
        var view = OpenFileVia(TestSampleData.Path("tabular"), file, "As Table", "TabularView");
        Assert.IsNotNull(view, "TabularView did not open via the 'As Table' action.");

        // Always-present toolbar descriptor + template buttons.
        CheckPresent("Detected-shape label", "Tabular_ShapeLabel");
        CheckPresent("Template This", "Tabular_TemplateThis");

        // Apply Template opens the right-side templates panel, revealing its controls.
        CheckInvoke("Apply Template", "Tabular_ApplyTemplate");
        CheckPresent("Show only compatible toggle", "Tabular_ShowOnlyCompatible");
        CheckInvoke("Templates panel close", "Tabular_TemplatePanelClose");

        AssertJourney();
    }
}
