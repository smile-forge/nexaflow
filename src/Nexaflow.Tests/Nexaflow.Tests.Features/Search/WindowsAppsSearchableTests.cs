using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Features.WindowsApps.Services;
using Nexaflow.Features.WindowsApps.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// The installed-apps list as a searchable page. Its old query handler could only ever do a literal
/// <c>Contains</c> — becoming <see cref="ISearchable"/> is what forced real regex support, and these
/// inherited conformance tests are what keep it.
/// </summary>
[TestClass]
[CoversNode("windowsapps-filter")]
public sealed class WindowsAppsSearchableTests : SearchableContentConformanceTests
{
    protected override string LiteralTermInContent => "alpha42";
    protected override string RegexOnlyPattern     => @"alpha\d+";

    protected override Task<ISearchable> CreateAsync()
    {
        var vm = new WindowsAppsViewModel(Substitute.For<IShellServices>(), new InstalledAppsService());

        // Seed the list directly — the real load runs on the shell's background queue, which a substituted
        // shell never pumps.
        foreach (var name in new[] { "alpha42 Toolkit", "Beta Reader", "alpha42 Helper", "Gamma Suite" })
            vm.Apps.Add(new InstalledAppItem(new InstalledApp { Name = name, Publisher = "Test", Version = "1.0" }));

        return Task.FromResult<ISearchable>(vm);
    }

    protected override string Snapshot(ISearchable page) => ((WindowsAppsViewModel)page).FilterText;

    // ── Apps-specific behaviour beyond the shared contract ────────────────────

    [TestMethod]
    public void RegexSearch_MatchesAcrossSeveralApps() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest(@"^alpha\d+", IsRegex: true), display: false, default);

        Assert.AreEqual(2, outcome.MatchCount);
        CollectionAssert.AreEquivalent(
            new[] { "alpha42 Toolkit", "alpha42 Helper" },
            outcome.Hits.Select(h => h.Id).ToArray());
    });

    [TestMethod]
    public void DisplayingSearch_PutsTheSyntaxBackInTheFilterBox() => WithPage(async page =>
    {
        var vm = (WindowsAppsViewModel)page;

        await vm.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: true, default);

        // Round-trips through SearchSyntax so the footer box shows what the user typed.
        Assert.AreEqual(@"/alpha\d+/", vm.FilterText);
    });

    [TestMethod]
    public void TypingInTheFooterBox_DropsAnAiInstalledPattern() => WithPage(async page =>
    {
        var vm = (WindowsAppsViewModel)page;
        await vm.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: true, default);

        vm.FilterText = "Gamma";   // the user takes over

        // The stale regex must not keep filtering underneath a literal box the user is now driving.
        var outcome = await vm.SearchAsync(new SearchRequest("Gamma"), display: false, default);
        Assert.AreEqual(1, outcome.MatchCount);
        Assert.AreEqual("Gamma", vm.FilterText);
    });
}
