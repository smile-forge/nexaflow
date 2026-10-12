using System.IO;
using System.Text;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Rtf.ViewModels;
using Nexaflow.Search;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The rich-text page as a searchable page. It searches the words the document draws — a control word is never a match,
/// and a letter written as an escape is found as the letter — and a "?" search lights them up on the page itself.
/// </summary>
[TestClass]
[CoversNode("rtf-find")]
public sealed class RtfSearchableTests : SearchableContentConformanceTests
{
    // "alpha42" appears only in lower case; "alpha\d+" matches it but never appears verbatim.
    private const string Document =
        @"{\rtf1\ansi\ansicpg1252\pard The importer was fixed in {\b alpha42}.\par The caf\'e9 is closed.\par}";

    private readonly List<string> _paths = [];

    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern => @"alpha\d+";

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Func<Task<SearchOutcome>>>()).Returns(call => call.Arg<Func<Task<SearchOutcome>>>()());
        return shell;
    }

    private async Task<RtfViewModel> OpenAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nexasearch_{Guid.NewGuid():N}.rtf");
        File.WriteAllText(path, Document, Encoding.Latin1);
        _paths.Add(path);

        var vm = new RtfViewModel(path, Shell());
        await vm.LoadAsync();
        return vm;
    }

    protected override async Task<ISearchable> CreateAsync() => await OpenAsync();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _paths)
            if (File.Exists(path)) File.Delete(path);
    }

    protected override string Snapshot(ISearchable page)
    {
        var vm = (RtfViewModel)page;
        return $"{vm.SearchMatchCount}|{vm.IsSearchActive}|{vm.CurrentSearchTerm}";
    }

    [TestMethod]
    public void AControlWordIsNeverAMatch() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest("par"), display: false, default);

        Assert.AreEqual(0, outcome.MatchCount, "\\par breaks a paragraph; it is not a word the document says");
        Assert.IsFalse(outcome.Failed);
    });

    [TestMethod]
    public void ALetterWrittenAsAnEscapeIsFoundAsTheLetter() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest("caf\u00E9"), display: false, default);

        Assert.AreEqual(1, outcome.MatchCount);
        Assert.AreEqual("paragraph 2", outcome.Hits.Single().Label, "hits are numbered as rtf_read_text numbers them");
    });

    [TestMethod]
    public void ADisplayedSearchLightsUpTheDocument_AndClearingPutsItOut() => WithPage(async page =>
    {
        var vm = (RtfViewModel)page;
        var cleared = 0;
        vm.FindInDocument = _ => [new RenderedMatch(0, "fixed in alpha42"), new RenderedMatch(1, "alpha42 again")];
        vm.ClearDocument = () => cleared++;
        vm.DocumentMarkPositions = () => [0.1, 0.6];

        var outcome = await vm.SearchAsync(new SearchRequest("alpha42"), display: true, default);

        Assert.AreEqual(2, outcome.MatchCount);
        Assert.AreEqual(2, vm.SearchMatchCount);
        Assert.IsTrue(vm.IsSearchActive);
        CollectionAssert.AreEqual(new[] { 0.1, 0.6 }, vm.MiniMapMarks.ToArray(), "the strip beside the page shows where they are");

        var before = cleared;
        vm.ClearSearchCommand.Execute(null);

        Assert.IsFalse(vm.IsSearchActive);
        Assert.AreEqual(0, vm.MiniMapMarks.Count);
        Assert.IsTrue(cleared > before);
    });

    [TestMethod]
    public void SteppingWalksTheMatchesOnThePage() => WithPage(async page =>
    {
        var vm = (RtfViewModel)page;
        var steps = new List<int>();
        vm.StepDocument = steps.Add;

        vm.FindNextMatchCommand.Execute(null);
        vm.FindPreviousMatchCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { 1, -1 }, steps);
        await Task.CompletedTask;
    });
}
