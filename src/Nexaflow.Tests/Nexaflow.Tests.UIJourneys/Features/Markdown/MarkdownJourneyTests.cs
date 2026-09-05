using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Markdown.UI;

/// <summary>
/// The one UI journey for the Markdown editor — the integration test registered at the feature's UI node.
/// It opens a sample .md via the explicit <b>"Markdown"</b> ActionStrip button (not a default-mapping
/// double-click) and drives the interactive chrome in a single pass: the toolbar's Source toggle swapping
/// between the two document surfaces, and the Save button's presence. Each check is soft, so one gap
/// doesn't hide the rest.
///
/// Individual controls are asserted by view-model unit tests at their own leaf nodes; this proves the
/// wiring holds end-to-end. Save is presence-only — the document is clean, so the journey never writes.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("markdown")]
public class MarkdownJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("markdown-ui")]
    public void Markdown_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("markdown").First());
        var view = OpenFileVia(TestSampleData.Path("markdown"), file, "Markdown", "MarkdownView");
        Assert.IsNotNull(view, "MarkdownView did not open via the 'Markdown' action.");

        // Default surface: the rendered inline editor is visible, the raw-source box is not.
        CheckPresent("Inline editor", "Markdown_Editor");
        Check("Source box hidden by default", () => WaitForId("Markdown_SourceBox", 1) is null);

        // Source toggle → the raw-source TextBox takes over, and the rendered editor goes away.
        CheckDoes("Source toggle", "Markdown_SourceToggle", () => WaitForId("Markdown_SourceBox", 3) is not null);
        Check("Inline editor hidden in source mode", () => WaitForId("Markdown_Editor", 1) is null);

        // Toggle back → the rendered editor returns and the source box goes away.
        CheckDoes("Source toggle (back)", "Markdown_SourceToggle", () => WaitForId("Markdown_Editor", 3) is not null);
        Check("Source box hidden again", () => WaitForId("Markdown_SourceBox", 1) is null);

        // Zoom, in the footer. The label is a TextBlock with a MouseLeftButtonUp handler rather than a Button,
        // so it supports no Invoke pattern — the base class falls back to a real click, which is what opens the
        // popup. The presets only exist in the UIA tree while it is open.
        CheckInvoke("Zoom label (opens the preset popup)", "Markdown_ZoomLabel");
        Check("Zoom popup opens", () => WaitForId("Markdown_Zoom100", 3) is not null);
        CheckPresent("Zoom 80%",  "Markdown_Zoom80");
        CheckPresent("Zoom 130%", "Markdown_Zoom130");

        // A preset that opens a popup and changes nothing is the failure a present-only check cannot see, so
        // require one to land. 120% is not the default, so the label has to move — and the match is exact,
        // because "contains 120" would also pass on a label still reading 1200%.
        Check("Zoom starts at 100%", () => ZoomLabelReads("Markdown", "100%"));
        CheckDoes("Zoom 120% applies", "Markdown_Zoom120", () => ZoomLabelReads("Markdown", "120%"));

        // Restoring is its own assertion, not a click for tidiness: a chip that can only go up is broken too.
        CheckInvoke("Zoom label (reopen)", "Markdown_ZoomLabel");
        CheckDoes("Zoom 100% restores the default", "Markdown_Zoom100", () => ZoomLabelReads("Markdown", "100%"));

        // Save button — present and correctly disabled on a clean document (invoking it would prove nothing
        // and would risk a write, so assert the state instead).
        var save = CheckPresent("Save", "Markdown_Save");
        Check("Save is disabled while the document is clean", () => save is not null && !save.IsEnabled);

        AssertJourney();
    }
}
