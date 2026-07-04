using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Json.UI;

/// <summary>
/// One-pass UI journey for the JSON viewer: opens a sample file via the explicit <b>"As Json"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar — the view-mode
/// toggles (tree / text / table) and the Format button — soft-asserting each so a single gap doesn't
/// hide the rest. Format re-indents the doc, which marks it modified and reveals the Save button, so
/// Save is checked last.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
public class JsonJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Json_Toolbar_RespondsInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("json").First());
        var view = OpenFileVia(TestSampleData.Path("json"), file, "As Json", "JsonView");
        Assert.IsNotNull(view, "JsonView did not open via the 'As Json' action.");

        // View-mode toggles — always present in the toolbar.
        CheckPresent("Tree mode toggle",  "Json_TreeMode");
        CheckPresent("Text mode toggle",  "Json_TextMode");
        CheckPresent("Table mode toggle", "Json_TableMode");

        // Format re-indents the document → marks it modified → reveals the Save button.
        CheckInvoke("Format", "Json_Format");
        CheckPresent("Save (visible after Format modifies)", "Json_Save");

        AssertJourney();
    }
}
