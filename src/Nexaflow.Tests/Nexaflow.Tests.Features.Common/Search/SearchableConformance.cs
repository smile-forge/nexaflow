using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Features.Infrastructure;

namespace Nexaflow.Tests.Features.Search;

/// <summary>
/// Contract every <see cref="ISearchable"/> page must satisfy, regardless of what it searches. Derive a
/// concrete <c>[TestClass]</c> per implementor and the inherited tests run against it — so a new searchable
/// page cannot ship without being held to the same rules.
/// <para>
/// The rule these exist to enforce: a page may decline to support regular expressions, but it may never
/// <em>appear</em> to support them. Answering a regex with a literal scan shows the user confidently wrong
/// results, and nothing else in the system can detect it.
/// </para>
/// </summary>
public abstract class SearchableConformanceTests
{
    /// <summary>A page ready to be searched. Called inside the pumped async context.</summary>
    protected abstract Task<ISearchable> CreateAsync();

    /// <summary>
    /// Runs <paramref name="body"/> against a fresh page, disposing it afterwards. Pumped on a single
    /// thread by default, because a page that loads content needs a UI-like context to continue on.
    /// Override for a page that starts long-lived background work (a channel reader): its continuations
    /// outlive the pumped block and crash the process when they post into the closed pump.
    /// </summary>
    protected virtual void WithPage(Func<ISearchable, Task> body) => AsyncPump.Run(async () =>
    {
        var page = await CreateAsync();
        try { await body(page); }
        finally { (page as IDisposable)?.Dispose(); }
    });

    /// <summary>The <see cref="WithPage"/> body for pages that must not be built inside a pump.</summary>
    protected void WithPageUnpumped(Func<ISearchable, Task> body)
    {
        var page = CreateAsync().GetAwaiter().GetResult();
        try { body(page).GetAwaiter().GetResult(); }
        finally { (page as IDisposable)?.Dispose(); }
    }

    /// <summary>Runs an async test body with no pumped context — for the same pages as
    /// <see cref="WithPageUnpumped"/>, when the test builds its own page.</summary>
    protected static void RunUnpumped(Func<Task> body) => body().GetAwaiter().GetResult();

    [TestMethod]
    public void SearchTargetDescription_IsMeaningful() => WithPage(page =>
    {
        // It goes straight into the agent's tool catalogue; an empty or placeholder value makes the tool
        // undiscoverable in practice.
        Assert.IsFalse(string.IsNullOrWhiteSpace(page.SearchTargetDescription));
        Assert.IsTrue(page.SearchTargetDescription.Length > 5, "describe what is being searched, as a noun phrase");
        return Task.CompletedTask;
    });

    [TestMethod]
    public void ScoreQuery_StaysWithinRange() => WithPage(page =>
    {
        foreach (var probe in new[] { "a", "two terms", "a slightly longer phrase here", "*.cs", "" })
        {
            var score = page.ScoreQuery(probe);
            Assert.IsTrue(score is >= 0f and <= 1f, $"ScoreQuery('{probe}') returned {score}, outside 0–1");
        }
        return Task.CompletedTask;
    });

    [TestMethod]
    public void RegexWithLiteralText_IsNeverRefused() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: false, default);

        // There is no capability opt-out. A backend whose query language lacks regex seeds it with the
        // pattern's literal text and re-filters; "this page can't do patterns" is not an answer the user
        // should get from some pages and not others.
        Assert.IsFalse(outcome.Failed,
            $"every searchable page must honour a regex carrying literal text — this one refused with: {outcome.Message}");
    });

    [TestMethod]
    public void RefusingAPattern_IsAboutThatPattern_NotAboutRegexItself() => WithPage(async page =>
    {
        // A page may decline a pattern it genuinely cannot narrow — "\d{4}" against a whole-drive index
        // means reading every file — but only if a pattern WITH literal text still works. Refusing both is
        // a capability gap wearing a per-query excuse.
        var unseedable = await page.SearchAsync(new SearchRequest(@"\d{4}", IsRegex: true), display: false, default);
        if (!unseedable.Failed) return;

        Assert.IsFalse(string.IsNullOrWhiteSpace(unseedable.Message),
            "explain why this pattern couldn't run, and what would work instead");

        var seedable = await page.SearchAsync(new SearchRequest(@"alpha\d+", IsRegex: true), display: false, default);
        Assert.IsFalse(seedable.Failed,
            "a pattern carrying literal text must still run — otherwise the refusal is blanket, not specific");
    });

    [TestMethod]
    public void MalformedRegex_FailsExplicitly() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest("[unclosed", IsRegex: true), display: false, default);

        Assert.IsTrue(outcome.Failed, "a malformed pattern must be reported, not silently matched as zero");
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.Message));
    });

    /// <summary>An id no page can be holding. Overridable only for a page whose ids are constrained enough
    /// that a GUID could never reach <c>ShowResultsAsync</c> at all (an integer index, say).</summary>
    protected virtual SearchHit UnknownHit => new(Guid.NewGuid().ToString(), "not on this page");

    [TestMethod]
    public void ShowResults_WithAnIdThisPageNeverGave_Declines() => WithPage(async page =>
    {
        // The agent passes back ids it got from SearchAsync, but it composes them freely — filtering by
        // meaning, carrying them across a reload, mixing two searches. So an id this page does not hold WILL
        // arrive, and the only two honest answers are "narrowed to these" or "I can't". Returning true having
        // matched nothing tells the user their view was filtered when it wasn't; emptying the view is worse,
        // because it reads as "your search found nothing".
        //
        // Ten pages asserted this by hand and seventeen did not. It needs nothing but ISearchable, so it
        // belongs here rather than being rewritten per page and forgotten on the majority.
        var narrowed = await page.ShowResultsAsync([UnknownHit], default);

        Assert.IsFalse(narrowed,
            "the agent needs to know it must describe the matches instead — see ShowResultsAsync's contract");
    });
}

/// <summary>
/// Adds the tests that need a page seeded with known content. Implementors that can't be seeded in a unit
/// test (a page whose backend is the OS search index) derive from <see cref="SearchableConformanceTests"/>
/// alone.
/// </summary>
public abstract class SearchableContentConformanceTests : SearchableConformanceTests
{
    /// <summary>A literal term present in the seeded content. Must appear in exactly one letter case, so
    /// upper-casing it produces a string the content does NOT contain (the case-sensitivity probe).</summary>
    protected abstract string LiteralTermInContent { get; }

    /// <summary>A regex that matches the seeded content but does NOT appear in it verbatim — e.g.
    /// <c>alpha\d+</c> against content holding "alpha42". This asymmetry is what proves a page compiled the
    /// pattern instead of running <c>Contains</c> over it.</summary>
    protected abstract string RegexOnlyPattern { get; }

    [TestMethod]
    public void LiteralSearch_FindsSeededContent() => WithPage(async page =>
    {
        var outcome = await page.SearchAsync(new SearchRequest(LiteralTermInContent), display: false, default);

        Assert.IsFalse(outcome.Failed, outcome.Message);
        Assert.IsTrue(outcome.MatchCount > 0, $"'{LiteralTermInContent}' is in the seeded content");
        Assert.IsTrue(outcome.Hits.Count > 0, "matches must come back as hits so the agent can act on them");
        Assert.IsTrue(outcome.Hits.All(h => !string.IsNullOrWhiteSpace(h.Id)),
            "every hit needs an id — the agent passes them back to ShowResultsAsync");
    });

    [TestMethod]
    public void RegexSearch_ActuallyCompilesThePattern() => WithPage(async page =>
    {
        var asRegex   = await page.SearchAsync(new SearchRequest(RegexOnlyPattern, IsRegex: true),  display: false, default);
        var asLiteral = await page.SearchAsync(new SearchRequest(RegexOnlyPattern, IsRegex: false), display: false, default);

        Assert.IsTrue(asRegex.MatchCount > 0,
            $"'{RegexOnlyPattern}' should match the seeded content when run as a regex");

        // The whole point: if the page were quietly doing a literal Contains, both calls would agree.
        Assert.AreEqual(0, asLiteral.MatchCount,
            $"'{RegexOnlyPattern}' must not match as literal text — that it does means the page ignores " +
            "IsRegex and only appears to support regular expressions");
    });

    [TestMethod]
    public void MatchCase_IsHonoured() => WithPage(async page =>
    {
        var shouted = LiteralTermInContent.ToUpperInvariant();
        Assert.AreNotEqual(LiteralTermInContent, shouted, "pick a term with lower-case letters in it");

        var insensitive = await page.SearchAsync(new SearchRequest(shouted), display: false, default);
        var sensitive   = await page.SearchAsync(new SearchRequest(shouted, MatchCase: true), display: false, default);

        Assert.IsTrue(insensitive.MatchCount > 0, "the default search is case-insensitive");
        Assert.AreEqual(0, sensitive.MatchCount,
            "MatchCase must narrow the search — ignoring it silently returns results the user excluded");
    });

    [TestMethod]
    public void SearchWithoutDisplay_LeavesTheVisibleStateAlone() => WithPage(async page =>
    {
        // The agent reads through the same entry point the user's search uses; it must stay invisible.
        var before  = Snapshot(page);
        await page.SearchAsync(new SearchRequest(LiteralTermInContent), display: false, default);

        Assert.AreEqual(before, Snapshot(page),
            "display:false must not change what the user sees — no filtering, highlighting or navigation");
    });

    /// <summary>Page state that a non-displaying search must not disturb, as a comparable string.</summary>
    protected abstract string Snapshot(ISearchable page);

    [TestMethod]
    public void ShowResults_ThatDeclines_LeavesTheViewExactlyAsItWas() => WithPage(async page =>
    {
        // The other half of ShowResults_WithAnIdThisPageNeverGave_Declines, and the half that actually
        // costs a user something: a page that filters to the unknown id first and only then discovers it
        // matched nothing has already blanked the view by the time it returns false.
        var before   = Snapshot(page);
        var narrowed = await page.ShowResultsAsync([UnknownHit], default);

        Assert.IsFalse(narrowed);
        Assert.AreEqual(before, Snapshot(page),
            "declining must be a no-op — narrowing to nothing and then reporting failure still empties the view");
    });

    [TestMethod]
    public void ShowResults_ThatClaimsSuccess_ActuallyChangesTheView() => WithPage(async page =>
    {
        var found = await page.SearchAsync(new SearchRequest(LiteralTermInContent), display: false, default);
        Assert.IsTrue(found.Hits.Count > 0);

        var before   = Snapshot(page);
        var rendered = await page.ShowResultsAsync([found.Hits[0]], default);

        // Declining is allowed (it's the interface default). Claiming success without narrowing is not —
        // the agent would tell the user it had filtered their view when nothing happened.
        if (rendered)
            Assert.AreNotEqual(before, Snapshot(page),
                "ShowResultsAsync returned true, so the visible result set must have changed");
    });
}
