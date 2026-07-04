using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Markdown.UI;

/// <summary>
/// One-pass UI journey for the Markdown editor: opens a sample .md via the explicit
/// <b>"Markdown"</b> ActionStrip button (not a default-mapping double-click), then exercises the
/// toolbar — flipping the source toggle to reveal the raw-source box and confirming the Save
/// button is present. Each control is soft-asserted so a single gap doesn't hide the rest.
///
/// The source toggle swaps editing surfaces but keeps the view present, so it is safe to invoke
/// mid-journey; Save is only checked for presence (invoking it on a clean document is a no-op, but
/// presence-only keeps the journey free of any disk write).
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
public class MarkdownJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Markdown_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("markdown").First());
        var view = OpenFileVia(TestSampleData.Path("markdown"), file, "Markdown", "MarkdownView");
        Assert.IsNotNull(view, "MarkdownView did not open via the 'Markdown' action.");

        // Default surface: the rendered inline editor is visible.
        CheckPresent("Inline editor", "Markdown_Editor");

        // Source toggle → raw-source TextBox becomes visible.
        CheckInvoke("Source toggle", "Markdown_SourceToggle");
        CheckPresent("Source box", "Markdown_SourceBox");

        // Toggle back → inline editor visible again.
        CheckInvoke("Source toggle (back)", "Markdown_SourceToggle");
        CheckPresent("Inline editor (restored)", "Markdown_Editor");

        // Save button — present (clean document; presence-only avoids any write).
        CheckPresent("Save", "Markdown_Save");

        AssertJourney();
    }
}
