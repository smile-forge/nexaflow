using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsSearch.UI;

/// <summary>
/// The one UI journey for Windows Search: run real queries against the live index and drive every control
/// the results page offers — the query field, the verification prompt, and the folder-scan offer with both
/// answers to it.
/// <para>
/// This was five classes and fourteen test methods, each of which launched its own Nexaflow and re-ran the
/// same setup — browse somewhere, type a query, wait for the result list — before asserting one thing. The
/// launches, not the assertions, were the entire runtime. One journey pays that once and then walks the
/// page, which is also closer to how the feature is actually used.
/// </para>
/// <para>
/// Checks are soft (<see cref="UiJourneyTestBase.CheckPresent"/> / <see cref="UiJourneyTestBase.Check"/>),
/// so one broken control still reports the rest — the reason a merge like this does not trade coverage for
/// speed. What each section asserts is unchanged from the methods it replaces.
/// </para>
/// <para>
/// Two sections search the same folder for the same absent term on purpose: the scan offer can only be
/// answered once, so declining it and running it need a fresh offer each.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("win-search")]
public class SearchJourneyTests : UiJourneyTestBase
{
    /// <summary>Long, because a scan offer only appears once the index has been asked and answered.</summary>
    private const int ScanTimeout = 60;

    /// <summary>An extension nothing has, so the index reliably comes back empty and offers the scan.</summary>
    private const string AbsentTerm = "*.zqxwv8813";

    /// <summary>Small enough that a scan of it finishes; big enough to be a real walk.</summary>
    private static readonly string ScanRoot = Path.GetTempPath();

    /// <summary>Deep enough that a scan is still running when we go looking for the Stop button.</summary>
    private static readonly string BigRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // ── Driving ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes the results tab so the file browser is in front again.
    /// <para>
    /// A bare glob only routes to Search while the active page exposes a FileSystemContext, and
    /// <see cref="FileSystemUiTestBase.NavigateFileBrowserTo"/> needs the browser's tree in the automation
    /// tree — neither is true with the results page in front. Each of the five classes this replaces got a
    /// fresh app and so never had to think about it; one journey does.
    /// </para>
    /// </summary>
    private void ReturnToBrowser()
    {
        var close = WaitForId("CloseTab_Search", Affordable(4));
        if (close is not null)
        {
            close.Click();
            Wait.UntilInputIsProcessed();
        }

        // Wait for the browser to actually be back, rather than assuming the click landed. The previous
        // section may still be finishing a background sweep, and on a loaded machine the tab does not close
        // the instant it is asked — submitting the next query into a page that is still the results page
        // routes it somewhere else entirely, and the search that follows silently never happens.
        WaitForId("DirectoryTree", Affordable(10));
    }

    /// <summary>
    /// Submits <paramref name="text"/> through the AI input bar, the way a user runs a search. Returns false
    /// — having recorded why — when the bar never became ready for it.
    /// <para>
    /// The wait is the point. The bar is <c>IsEnabled=False</c> while <c>AiIsBusy</c>, so a query typed while
    /// the previous turn is still running goes nowhere at all: no error, no results page, just a bar that
    /// looks normal a moment later. The regex section ahead of this starts a background sweep and the journey
    /// used to follow it with a flat two-second sleep, which is a race — and one this machine lost, silently,
    /// three sections before the first check that mentioned it. Typing the same query by hand always worked,
    /// which is exactly what a race looks like from the outside.
    /// </para>
    /// </summary>
    private bool Submit(string text)
    {
        // Clicked rather than focused with Ctrl+Tab: reliable across the foreground churn of a full run.
        var ai = WaitForId("AiInputBox", Affordable(10));
        if (ai is null)
        {
            Check($"The AI bar is present to submit '{text}'", () => false);
            return false;
        }

        if (!WaitForFs(() => ai.IsEnabled, Affordable(30)))
        {
            Check($"The AI bar finishes its previous turn and accepts '{text}'", () => false);
            return false;
        }

        ai.Click();
        Wait.UntilInputIsProcessed();

        Keyboard.Type(text);
        Wait.UntilInputIsProcessed();

        // The bar holds what we meant to send. A click that missed, or one that landed as the bar was
        // re-enabling, otherwise sends a truncated query and the search that follows reads as a feature defect.
        var typed = ai.AsTextBox().Text;
        if (!typed.Contains(text, StringComparison.Ordinal))
        {
            Check($"The AI bar received '{text}' (it holds '{typed}')", () => false);
            return false;
        }

        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// Runs a search from the file browser, optionally from <paramref name="folder"/>, and waits for the
    /// results page. Returns false when it never opened, so a section can report that and move on rather
    /// than throwing and taking the rest of the journey with it.
    /// </summary>
    private bool Search(string query, string? folder = null, int seconds = 20)
    {
        ReturnToBrowser();

        if (folder is not null)
        {
            NavigateFileBrowserTo(folder);
        }
        else if (WaitForId("DirectoryTree", Affordable(15)) is null)
        {
            // The default This PC tab opens on a deferred dispatcher tick, and a glob submitted before it
            // lands routes into an empty context and opens nothing.
            return false;
        }

        if (!Submit(query)) return false;

        return WaitForId("ResultList", Affordable(seconds)) is not null;
    }

    /// <summary>True once the banner is saying <paramref name="fragment"/>.</summary>
    private bool WaitForBanner(string fragment, int seconds)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("VerificationBannerText"));
            if (el?.Name?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true) return true;
            Thread.Sleep(200);
        }
        while (sw.Elapsed < TimeSpan.FromSeconds(Affordable(seconds)));

        return false;
    }

    private bool Hidden(string automationId)
    {
        var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        return el is null || el.IsOffscreen;
    }

    private int CountResults() =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ResultList"))?.FindAllChildren().Length ?? 0;

    // ── The journey ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("win-search-ui")]
    [CoversNode("search-verify")]
    [CoversNode("win-search-folder-scan")]
    public void Search_Controls_RespondInOnePass()
    {
        // ── A glob search: the page opens and holds together ────────────────────────────
        // A glob scores ≥0.9 on SearchQueryScorer and wins routing without LLM disambiguation.
        Check("A glob query opens the results page", () => Search("*.txt"));
        CheckPresent("Search tab", "TabItem_Search");

        var results = CheckPresent("Result list", "ResultList");
        Check("Result list is on screen", () => results is { IsOffscreen: false });
        Check("The app survives loading a results page", () => !App.HasExited);

        // The banner is a cost signal. Offering to verify after a search that needed none would train the
        // user to ignore it — which is exactly when it matters. (The banner itself may still be visible
        // offering a folder scan, if this machine's index had nothing for the query; that is a different
        // question with different buttons.)
        Check("Verification is not offered when there is nothing to verify",
              () => Hidden("VerifyRemaining") && Hidden("SkipVerification"));

        // An explicit "?" must run the same search the bare glob just ran. Same page, same term, same
        // moment - the only difference is the prefix, so a failure here is the ROUTE, and a pass says the
        // route is fine and the fault is in what an EMPTY result does. Without it the first "?" in this
        // journey is also the first absent term and the first search after a background sweep, and the
        // three are indistinguishable in the report.
        Check("An explicit \x27?\x27 runs the same search as the bare glob", () => Search("?*.txt"));

        // A regex takes the speculative path — translate, widen, classify, and possibly start a background
        // sweep. Whether it matches anything here depends on the machine, but it must not take the app down.
        Submit(@"?/report\d+\.txt/");
        Thread.Sleep(2000);                 // let any sweep start and report
        Check("The app survives a content-regex search", () => !App.HasExited);

        // ── The query field: shown, editable, and it keeps the edit ──────────────────────
        // Guarded on the search actually opening. Without that, a results page left over from the section
        // above satisfies "a query box exists" while holding the *previous* query — so the field checks run
        // against the wrong page and only the content assertion fails, which reads as a query-box defect
        // rather than a search that never ran. That is exactly how this failed in a full run and passed
        // alone: under load the regex sweep above was still finishing, and this search never happened.
        var searched = Search($"?{AbsentTerm}");
        Check("A '?' query opens the results page", () => searched);

        var box = searched ? CheckPresent("Query field", "SearchQueryBox", 15)?.AsTextBox() : null;
        if (searched)
        {
            Check("The query is presented as an editable field, not a label",
                  () => box is { IsReadOnly: false });
            Check("The field holds the query that was run",
                  () => box?.Text.Contains("zqxwv8813") == true);
        }

        // That the edit *replaces* rather than narrows the search is asserted headlessly in
        // SearchViewModelTests.EditingTheQueryReplacesTheSearchRatherThanNarrowingIt — the TextBox shows its
        // own local text either way, so this can only prove the field accepts an edit and survives Enter.
        if (box is not null)
        {
            box.Text = "*.zqxwv9999";
            box.Click();
            Keyboard.Press(VirtualKeyShort.END);
            Keyboard.Press(VirtualKeyShort.RETURN);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(1500);

            Check("The field keeps an edit through Enter", () =>
                MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchQueryBox"))
                          ?.AsTextBox().Text.Contains("zqxwv9999") == true);
        }

        // ── An empty index result offers a folder scan, and declining retires it ─────────
        Check("Searching a folder for an absent term opens the results page",
              () => Search($"?{AbsentTerm}", ScanRoot, ScanTimeout));

        // Asserted on the TEXT, not the Border around it: a Border is not a UIA control and isn't reliably
        // discoverable, so looking for it would pass by finding nothing — which is how a banner assertion
        // ends up proving nothing at all.
        var banner = CheckPresent("Empty-result banner", "VerificationBannerText", ScanTimeout);
        Check("The banner names the way out rather than just reporting emptiness",
              () => banner?.Name.Contains("scan", StringComparison.OrdinalIgnoreCase) == true);

        // The regression this guards: a phase the ViewModel sets but no template shows. Everything else
        // would still pass — the planner returns OfferScan, the command exists — and the user would simply
        // never be able to run a scan.
        CheckPresent("Scan button", "ScanFolder", ScanTimeout);
        CheckPresent("Decline button", "DeclineScan", ScanTimeout);

        // Two prompts share the banner and they must not be confusable: "check these candidates" costs
        // seconds, "scan this tree" costs minutes.
        Check("The verification prompt is not offered in the scan offer's place",
              () => Hidden("VerifyRemaining") && Hidden("SkipVerification"));

        CheckDoes("Decline retires the offer", "DeclineScan", () => Hidden("ScanFolder"));
        // The whole banner goes, not just its buttons. Leaving the text behind would keep telling the user
        // about a decision they have already made.
        Check("Declining takes the banner with it", () => Hidden("VerificationBannerText"));

        // ── The scan, actually run ───────────────────────────────────────────────────────
        // Finding the button proves it is on screen; pressing it proves it is connected to something.
        // Without this the command could be unbound and every other assertion here would still pass.
        Check("The scan offer returns for a fresh search",
              () => Search($"?{AbsentTerm}", ScanRoot, ScanTimeout));
        CheckInvoke("Run scan", "ScanFolder", ScanTimeout);

        // The scan reports through the same banner. Either wording of a finished scan names it, so this
        // holds whether or not the temp folder happens to contain a match.
        Check("The scan reports a result", () => WaitForBanner("Folder scan", ScanTimeout));

        // ── A running scan can be stopped, and stays stopped ─────────────────────────────
        Check("A big folder offers the scan too", () => Search($"?{AbsentTerm}", BigRoot, ScanTimeout));
        CheckInvoke("Run scan (big folder)", "ScanFolder", ScanTimeout);

        // Stop only appears while a scan is running, so finding it is itself the proof that we caught one
        // mid-flight rather than after it had quietly finished.
        var stop = CheckPresent("Stop button, while the scan runs", "StopScan", ScanTimeout);
        if (stop is not null)
        {
            stop.Click();
            Wait.UntilInputIsProcessed();

            // A stopped scan reports what it had found so far — never a total, which it cannot know.
            Check("The scan reports being stopped", () => WaitForBanner("Scan stopped", ScanTimeout));

            // Cancellation that leaves the walk running would keep appending rows behind a banner that
            // claims it stopped — the worst of both, since the user believes they took back control.
            var settled = CountResults();
            Thread.Sleep(3000);
            Check("No rows arrive after the scan reported it had stopped", () => CountResults() == settled);
        }

        AssertJourney();
    }
}
