using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Logs.UI;

/// <summary>
/// One-pass UI journey for the Logs viewer: opens a log sample via the explicit <b>"As Log"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar — the level
/// highlight toggles, the pause/follow monitoring toggles, the highlight-term box, the encoding
/// selector and the filter-pattern box — soft-asserting each so a single gap doesn't hide the rest.
/// The buffer is read-only, so toggling these controls does not mutate the sample file.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("log-viewer")]
public class LogsJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("log-viewer-ui")]
    public void Logs_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("logs").First());
        var view = OpenFileVia(TestSampleData.Path("logs"), file, "As Log", "LogView");
        Assert.IsNotNull(view, "LogView did not open via the 'As Log' action.");

        // Level highlight toggles — always present; toggling is a safe view-only recolour.
        CheckInvoke("FATAL highlight toggle", "Log_HighlightFatal");
        CheckInvoke("ERROR highlight toggle", "Log_HighlightError");
        CheckInvoke("WARN highlight toggle",  "Log_HighlightWarning");
        CheckInvoke("INFO highlight toggle",  "Log_HighlightInfo");
        CheckInvoke("DEBUG highlight toggle", "Log_HighlightDebug");

        // Custom highlight term box — present-check (a text input, nothing to invoke).
        CheckPresent("Highlight-term box", "Log_HighlightTerm");

        // Monitoring toggles — safe to flip on a read-only buffer.
        CheckInvoke("Pause toggle",  "Log_Pause");
        CheckInvoke("Follow toggle", "Log_Follow");

        // Copy-selected button — disabled on open (no lines selected), so present-check only;
        // invoking a disabled command-bound button would throw.
        CheckPresent("Copy selected lines", "Log_CopySelected");

        // Encoding selector + filter pattern box — present in the toolbar / filter panel.
        CheckPresent("Encoding selector", "Log_Encoding");
        CheckPresent("Filter regex box",  "Log_FilterRegex");

        AssertJourney();
    }
}
