using Nexaflow.IO.Common;
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
        _path = Path.Combine(TestTemp.Root, "nexasearch_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(_path, Content);

        var vm = new TextViewModel(_path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var vm in _extraVms) vm.Dispose();   // a page keeps its file open
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

    private async Task<TextViewModel> OpenAsync(string content, int window = int.MaxValue)
    {
        var p = Path.Combine(TestTemp.Root, "nexaneedle_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(p, content);
        _extra.Add(p);
        var vm = new TextViewModel(p, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, window) { IsMonitoring = false };
        await vm.LoadAsync(CancellationToken.None);
        _extraVms.Add(vm);
        return vm;
    }

    /// <summary>A window far shorter than <see cref="Spread"/>'s file, so stepping between its matches has to move it.</summary>
    private const int SmallWindow = 100_000;

    /// <summary>Nine thousand lines of some 45 characters, a needle on each line <paramref name="at"/> names — far enough apart that no two share a <see cref="SmallWindow"/>.</summary>
    private static string Spread(params int[] at) => string.Join("\n", Enumerable.Range(0, 9000)
        .Select(i => at.Contains(i) ? $"the needle at marker{i} of the streaming fixture" : $"filler line {i} of the streaming fixture text")) + "\n";

    /// <summary>What is written on the line the caret is on.</summary>
    private static string CaretLineText(TextViewModel vm)
    {
        var document = vm.Document.Current;
        var (start, end) = document.LineAt(vm.Editor.Caret);
        return document.Slice(start, end - start);
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
        // A file too long for its window, with the match near the end.
        var vm = await OpenAsync(Spread(8999), SmallWindow);
        Assert.IsTrue(vm.IsLargeFile, "the fixture must be longer than its window");

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);

        Assert.IsTrue(vm.IsFindBarOpen, "a streaming file opens and populates the find bar");
        Assert.AreEqual("needle", vm.FindText);
        Assert.AreEqual(1, vm.SearchMatchCount);
        StringAssert.Contains(CaretLineText(vm), "marker8999", "the search went to the match, the window brought there");
    });

    [TestMethod]
    public void StreamingFile_FindNextPrevious_CyclesEveryMatch_AcrossWindows() => WithPage(async _ =>
    {
        // Matches far enough apart that no two share a window — so stepping has to move it each time.
        var vm = await OpenAsync(Spread(1000, 4000, 8000), SmallWindow);
        Assert.IsTrue(vm.IsLargeFile);

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);
        Assert.AreEqual(3, vm.SearchMatchCount);
        StringAssert.Contains(CaretLineText(vm), "marker1000", "the search lands on the first match");

        // Next reaches every later match, moving the window each time…
        vm.FindNextCommand.Execute(null);
        StringAssert.Contains(CaretLineText(vm), "marker4000", "Find Next → second match");
        vm.FindNextCommand.Execute(null);
        StringAssert.Contains(CaretLineText(vm), "marker8000", "Find Next → third match");
        // …and wraps to the first.
        vm.FindNextCommand.Execute(null);
        StringAssert.Contains(CaretLineText(vm), "marker1000", "Find Next wraps to the first match");

        // Previous from the first wraps to the last, then keeps stepping backwards through the file.
        vm.FindPreviousCommand.Execute(null);
        StringAssert.Contains(CaretLineText(vm), "marker8000", "Find Previous wraps to the last match");
        vm.FindPreviousCommand.Execute(null);
        StringAssert.Contains(CaretLineText(vm), "marker4000", "Find Previous keeps going back when the previous match is outside the window");
    });

    [TestMethod]
    public void StreamingFile_WindowMove_KeepsTheCaretPut_SoStepsKeepAdvancing() => WithPage(async _ =>
    {
        // Scrolling moves the window under the caret; the caret has to stay on its own line through that, or "next" steps from wherever it
        // was left and finds the same match again.
        var vm = await OpenAsync(Spread(3000, 3100, 3200, 8000), SmallWindow);
        Assert.IsTrue(vm.IsLargeFile);

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);
        Assert.AreEqual(4, vm.SearchMatchCount);

        vm.FindNextCommand.Execute(null);   // 3100
        vm.FindNextCommand.Execute(null);   // 3200
        vm.FindNextCommand.Execute(null);   // 8000 — far, moves the window
        StringAssert.Contains(CaretLineText(vm), "marker8000");
        vm.FindNextCommand.Execute(null);   // wraps to 3000, moves it back
        StringAssert.Contains(CaretLineText(vm), "marker3000");

        // The scroll that follows moves the window again, with the caret's line still inside it.
        var window = vm.Document.Window!;
        var before = window.Start;
        window.MoveTo(window.OffsetOfLine(3000)!.Value - (window.Size / 3));
        Assert.AreNotEqual(before, window.Start, "the window moved");
        StringAssert.Contains(CaretLineText(vm), "marker3000", "moving the window leaves the caret on its own line");

        vm.FindNextCommand.Execute(null);
        StringAssert.Contains(CaretLineText(vm), "marker3100", "the next step advances to the following match");
    });

    [TestMethod]
    public void StreamingFile_FindNext_StartsFromTheCaret_NotTheLastMatch() => WithPage(async _ =>
    {
        var vm = await OpenAsync(Spread(1000, 4000, 8000), SmallWindow);

        await vm.SearchAsync(SearchSyntax.ParseRequest("needle"), display: true, default);
        StringAssert.Contains(CaretLineText(vm), "marker1000", "starts on the first match");

        // The reader clicks between the 2nd and 3rd match.
        var window = vm.Document.Window!;
        var at = window.OffsetOfLine(6000)!.Value;
        window.Reach(at, at);
        vm.Editor.MoveCaretTo((int)(at - window.Start));
        StringAssert.Contains(CaretLineText(vm), "filler line 6000");

        vm.FindNextCommand.Execute(null);
        StringAssert.Contains(CaretLineText(vm), "marker8000", "Find Next steps from the caret's line, landing on the next match after it — not last+1");
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
