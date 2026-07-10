using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel.UI;

/// <summary>
/// One-pass UI journey for the "As Code" page: opens a structured C# sample via the explicit <b>"As Code"</b>
/// ActionStrip button, then drives the code-map panel toggles — collapse hides the map and surfaces the reopen
/// tab; reopen restores it. Each is soft-asserted so a single gap doesn't hide the rest.
///
/// The code-map / diagram wheel-scroll behaviour is already covered by <c>CodeViewUiTests</c>; this journey
/// only exercises the newly-tagged toolbar controls.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("code-map-toggle")]
public class CodeJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Code_Controls_RespondInOnePass()
    {
        // hello.cs has real structure, so the code-map panel opens and its toggle is on-screen.
        var file = "hello.cs";
        Check("code sample exists", () => TestSampleData.Files("code").Any(p => Path.GetFileName(p) == file));

        var view = OpenFileVia(TestSampleData.Path("code"), file, "As Code", "CodeView");
        Assert.IsNotNull(view, "CodeView did not open via the 'As Code' action.");

        // Collapse the code map → the header toggle hides the panel and the reopen tab appears.
        CheckInvoke("Code map: collapse", "Code_MapToggle");
        CheckPresent("Code map: reopen tab appears", "Code_MapReopen");

        // Reopen the code map → the panel comes back (its header toggle is on-screen again).
        CheckInvoke("Code map: reopen", "Code_MapReopen");
        CheckPresent("Code map: toggle back on-screen", "Code_MapToggle");

        AssertJourney();
    }
}
