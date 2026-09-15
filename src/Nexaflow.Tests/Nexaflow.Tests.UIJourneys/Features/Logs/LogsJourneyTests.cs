using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using FlaUI.Core.AutomationElements;

namespace Nexaflow.Tests.Features.Logs.UI;

/// <summary>
/// One-pass UI journey for the Logs viewer: opens a log sample via the explicit <b>"As Log"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the toolbar — the level
/// highlight toggles, the pause/follow monitoring toggles, the highlight-term box, the encoding
/// selector, the filter-pattern box with its clear cross, and the time range — soft-asserting each so a single gap doesn't hide the rest.
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

        // The filter's clear cross only exists while a filter is active, so set one first.
        Check("A filter pattern activates the filter", () => TypeInto("Log_FilterRegex", "ERROR"));
        CheckDoes("Clear filter empties the pattern", "Log_ClearFilter",
                  () => WaitForFs(() => WaitForId("Log_FilterRegex", 1)?.AsTextBox().Text.Length == 0, 3));
        Check("The clear cross goes with the filter", () => WaitForGone("Log_ClearFilter"));

        // Time range — only shown for a timestamped log, which app_short.log is. Apply with no date narrows
        // nothing, so it can only be asserted not to throw; Clear is proven by the time it wipes.
        Check("The time range accepts a start time", () => TypeInto("Log_FilterStartTime", "09:00:05"));
        CheckInvoke("Apply time range", "Log_ApplyTimeFilter");
        Check("The app survives applying a time range", () => !App.HasExited);
        CheckDoes("Clear time range empties the start time", "Log_ClearTimeFilter",
                  () => WaitForFs(() => WaitForId("Log_FilterStartTime", 1)?.AsTextBox().Text.Length == 0, 3));

        AssertJourney();
    }
}
