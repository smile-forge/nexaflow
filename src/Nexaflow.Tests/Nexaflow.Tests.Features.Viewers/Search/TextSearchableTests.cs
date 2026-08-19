using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.Text.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>The text viewer as a searchable page — it replaced the two hand-written Text query handlers.</summary>
[TestClass]
[CoversNode("text-viewer-find")]
public sealed class TextSearchableTests : SearchableContentConformanceTests
{
    // "alpha42" appears only in lower case; "alpha\d+" matches it but never appears verbatim.
    private const string Content =
        "first line with alpha42 in it\n" +
        "second line, nothing here\n" +
        "third line mentions beta and alpha42 again\n" +
        "fourth line is quiet\n";

    private string? _path;

    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    protected override async Task<ISearchable> CreateAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), "nexasearch_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(_path, Content);

        var vm = new TextViewModel(_path, Substitute.For<IShellServices>()) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var vm in _extraVms) vm.Dispose();   // a large file keeps its overlay handle open
        if (_path is not null && File.Exists(_path)) File.Delete(_path);
        foreach (var p in _extra) if (File.Exists(p)) File.Delete(p);
    }

    protected override string Snapshot(ISearchable page)
    {
        var vm = (TextViewModel)page;
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}|{vm.FindText}|{vm.MatchCase}";
    }

    // ── Text-specific behaviour beyond the shared contract ────────────────────

    [TestMethod]
    public void DisplayingSearch_DrivesTheFindBar() => WithPage(async page =>
    {
        var vm = (TextViewModel)page;

        await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);

        // An AI-bar search should light up exactly what typing in the find bar would.
        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.IsTrue(vm.IsSearchActive);
        Assert.IsTrue(vm.IsFindBarOpen, "the find bar surfaces the executed search");
        Assert.AreEqual(LiteralTermInContent, vm.FindText);
        Assert.IsFalse(vm.UseRegex);
    });

    [TestMethod]
    public void DisplayingRegexSearch_SetsTheRegexToggle() => WithPage(async page =>
    {
        var vm = (TextViewModel)page;

        await vm.SearchAsync(new SearchRequest(RegexOnlyPattern, IsRegex: true), display: true, default);

        Assert.IsTrue(vm.UseRegex, "the bar's regex toggle must reflect how the search actually ran");
        Assert.AreEqual(2, vm.SearchMatchCount);
    });

    [TestMethod]
    public void Hits_CarryLineNumbersAndPreviews() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest(LiteralTermInContent), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount);
        // Ids are 0-based line numbers; ShowResultsAsync parses them back.
        CollectionAssert.AreEqual(new[] { "0", "2" }, outcome.Hits.Select(h => h.Id).ToArray());
        Assert.IsTrue(outcome.Hits.All(h => !string.IsNullOrWhiteSpace(h.Preview)),
            "previews let the agent judge relevance without a second call");
    });

    [TestMethod]
    public void ShowResults_NarrowsTheMatchSetToTheChosenLines() => WithPage(async page =>
    {
        var vm      = (TextViewModel)page;
        var outcome = await vm.SearchAsync(new SearchRequest(LiteralTermInContent), display: true, default);
        Assert.AreEqual(2, vm.SearchMatchCount);

        var rendered = await vm.ShowResultsAsync([outcome.Hits[1]], default);

        Assert.IsTrue(rendered);
        Assert.AreEqual(1, vm.SearchMatchCount, "only the agent's chosen line stays in the match set");
    });

    // ── The reported bugs: the text viewer must use the shared contract ───────

    // A body with the standalone word and the longer word on separate lines.
    private const string NeedleBody = "a needle here\nthis is needless\nNeedless again\n";

    private async Task<TextViewModel> OpenAsync(string content)
    {
        var p = Path.Combine(Path.GetTempPath(), "nexaneedle_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(p, content);
        _extra.Add(p);
        var vm = new TextViewModel(p, Substitute.For<IShellServices>()) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);
        _extraVms.Add(vm);
        return vm;
    }

    private readonly List<string> _extra = [];
    private readonly List<TextViewModel> _extraVms = [];

    [TestMethod]
    public void LiteralWord_DoesNotMatchALongerWord() => WithPage(async _ =>
    {
        var vm = await OpenAsync(NeedleBody);
        // "?needle" is the WORD needle — the reported bug was it matching "needless".
        var outcome = await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: false, default);
        Assert.AreEqual(1, outcome.MatchCount, "only the standalone 'needle', not 'needless'/'Needless'");
    });

    [TestMethod]
    public void WildcardPrefix_MatchesTheLongerWords() => WithPage(async _ =>
    {
        var vm = await OpenAsync(NeedleBody);
        // "?needle*" is a prefix — it should reach needle, needless and Needless (three lines).
        var outcome = await vm.SearchAsync(SearchSyntax.ParseRequest("needle*"), display: false, default);
        Assert.AreEqual(3, outcome.MatchCount, "the reported bug was this matching nothing");
    });

    [TestMethod]
    public void QuotedTerm_IsStillWholeWord() => WithPage(async _ =>
    {
        var vm = await OpenAsync(NeedleBody);
        // "?\"needle\"" is an exact literal — quoting must not turn into a substring that matches "Needless".
        var outcome = await vm.SearchAsync(SearchSyntax.ParseRequest("\"needle\""), display: false, default);
        Assert.AreEqual(1, outcome.MatchCount, "the reported bug was the quoted term matching 'Needless'");
    });

    [TestMethod]
    public void Injecting_ActivatesTheWildcardToggle_ForALiteralQuery() => WithPage(async _ =>
    {
        var vm = await OpenAsync(NeedleBody);
        await vm.SearchAsync(SearchSyntax.ParseRequest("needle*"), display: true, default);

        Assert.IsTrue(vm.IsFindBarOpen);
        Assert.IsTrue(vm.UseWildcards, "a '?' search activates the wildcard toggle so the box reproduces it");
        Assert.IsFalse(vm.UseRegex);
        Assert.AreEqual("needle*", vm.FindText);
    });

    [TestMethod]
    public void Injecting_ActivatesTheRegexToggle_ForARegexQuery() => WithPage(async _ =>
    {
        var vm = await OpenAsync(NeedleBody);
        await vm.SearchAsync(new SearchRequest(@"needl\w+", IsRegex: true), display: true, default);

        Assert.IsTrue(vm.UseRegex, "a regex '?' search activates the regex toggle, not wildcards");
        Assert.IsFalse(vm.UseWildcards);
        Assert.AreEqual(@"needl\w+", vm.FindText);
    });

    [TestMethod]
    public void StreamingFile_OpensAndPopulatesTheFindBar() => WithPage(async _ =>
    {
        // A file past the streaming threshold (100 KB) with the match near the end.
        var big = string.Concat(Enumerable.Repeat("filler line of text\n", 8000)) + "the needle is here\n";
        var vm  = await OpenAsync(big);
        Assert.IsTrue(vm.IsLargeFile, "the fixture must exceed the streaming threshold");

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);

        Assert.IsTrue(vm.IsFindBarOpen, "the reported bug: a streaming file didn't open/populate the find bar");
        Assert.AreEqual("needle", vm.FindText);
        Assert.AreEqual(1, vm.SearchMatchCount);
    });

    [TestMethod]
    public void StreamingFile_FindNextPrevious_CyclesEveryMatch_AcrossWindows() => WithPage(async _ =>
    {
        // Matches spread far enough apart that no two share a resident window — so stepping MUST slide the
        // window each time. This is the case that used to circle only the on-screen matches.
        int[] at = [1000, 4000, 8000];
        var lines = Enumerable.Range(0, 9000)
            .Select(i => at.Contains(i) ? $"the needle at marker{i}" : $"filler line {i}");
        var vm = await OpenAsync(string.Join("\n", lines) + "\n");
        Assert.IsTrue(vm.IsLargeFile);

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);
        Assert.AreEqual(3, vm.SearchMatchCount);
        StringAssert.Contains(vm.Document.Text, "marker1000", "the search lands on the first match");

        // Next reaches every later match, sliding the window each time…
        await vm.FindNextCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Document.Text, "marker4000", "Find Next → second match");
        await vm.FindNextCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Document.Text, "marker8000", "Find Next → third match");
        // …and wraps to the first.
        await vm.FindNextCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Document.Text, "marker1000", "Find Next wraps to the first match");

        // Previous from the first wraps to the LAST, then keeps stepping backwards through the file —
        // the case that used to do nothing because the previous match was outside the window.
        await vm.FindPreviousCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Document.Text, "marker8000", "Find Previous wraps to the last match");
        await vm.FindPreviousCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Document.Text, "marker4000",
            "Find Previous keeps going back even when the previous match is outside the window");
    });

    [TestMethod]
    public void StreamingFile_WindowSlide_KeepsTheCaretPut_SoStepsKeepAdvancing() => WithPage(async _ =>
    {
        // The reported bug. In the app each jump scrolls, the scroll slides the window, and recomposing the
        // document RESETS the editor caret — which the view echoes straight back as the search cursor. The
        // cursor landed at the window start, so "next" kept finding the window's first match forever.
        int[] at = [1000, 1100, 1200, 8000];
        var lines = Enumerable.Range(0, 9000)
            .Select(i => at.Contains(i) ? $"the needle at marker{i}" : $"filler line {i}");
        var vm = await OpenAsync(string.Join("\n", lines) + "\n");
        Assert.IsTrue(vm.IsLargeFile);

        // Stand in for the view: a recomposition resets the editor caret to the window start, and the view's
        // Caret.PositionChanged handler pushes that back into the view-model.
        vm.Document.TextChanged += (_, _) => vm.CurrentCaretOffset = vm.LoadedRealStart;

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);
        Assert.AreEqual(4, vm.SearchMatchCount);

        await vm.FindNextCommand.ExecuteAsync(null);   // 1100
        await vm.FindNextCommand.ExecuteAsync(null);   // 1200
        await vm.FindNextCommand.ExecuteAsync(null);   // 8000 — far jump, slides the window
        StringAssert.Contains(vm.Document.Text, "marker8000");
        await vm.FindNextCommand.ExecuteAsync(null);   // wraps to 1000, slides back
        StringAssert.Contains(vm.Document.Text, "marker1000");

        // The scroll that follows the jump slides the window again — the moment the caret used to be lost.
        await vm.EnsureWindowAsync(970, 1030);
        Assert.AreEqual(1001, vm.Document.GetLineByOffset(vm.CurrentCaretOffset).LineNumber,
            "a window slide must leave the caret on its own line");

        await vm.FindNextCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Document.Text, "marker1100",
            "the next step advances to the following match, not back to the window's first match");
    });

    [TestMethod]
    public void StreamingFile_FindNext_StartsFromTheCaret_NotTheLastMatch() => WithPage(async _ =>
    {
        int[] at = [1000, 4000, 8000];
        var lines = Enumerable.Range(0, 9000)
            .Select(i => at.Contains(i) ? $"the needle at marker{i}" : $"filler line {i}");
        var vm = await OpenAsync(string.Join("\n", lines) + "\n");

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);
        StringAssert.Contains(vm.Document.Text, "marker1000", "starts on the first match");

        // The user clicks somewhere between the 2nd and 3rd match (line 6000 is resident after the search's
        // slide brought the region into view — or a later navigation would; here it stays near the start, so
        // put the caret at a resident filler line just after the first match instead).
        var caretLine = vm.Document.GetLineByNumber(2000);   // between match 1 (1000) and match 2 (4000)
        vm.CurrentCaretOffset = caretLine.Offset;

        await vm.FindNextCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Document.Text, "marker4000",
            "Find Next steps from the caret's line, landing on the next match after it — not last+1");
    });

    [TestMethod]
    public void FindToggles_WildcardsDefaultOn_AndAreExclusiveWithRegex() => WithPage(async _ =>
    {
        var vm = await OpenAsync(NeedleBody);

        Assert.IsTrue(vm.UseWildcards, "wildcards are on by default");
        Assert.IsFalse(vm.UseRegex);

        vm.UseRegex = true;
        Assert.IsFalse(vm.UseWildcards, "turning on regex turns wildcards off — a query is one or the other");

        vm.UseWildcards = true;
        Assert.IsFalse(vm.UseRegex, "and turning wildcards back on turns regex off");
    });
}
