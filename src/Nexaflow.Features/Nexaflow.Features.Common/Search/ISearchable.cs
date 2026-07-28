using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>
/// Implemented by a page ViewModel that can answer a search <em>scoped to what the page is about</em>.
/// Declaring it is all a page has to do: the shell's <c>?</c> query handler routes AI-bar searches here,
/// and the agent is automatically given tools to run the same search itself. No per-page query handler,
/// and no registration — <c>?</c> has exactly one owner.
/// <para>
/// Deliberately <em>not</em> limited to filtering what is on screen. A text tab searches its file and
/// highlights in place; a file browser searches the folder tree it is showing and opens the results as a
/// new Search tab. Both are "search here" from the user's point of view, and both want the same syntax,
/// the same status dot and the same agent tools — which is why they share one contract and differentiate
/// through <see cref="ScoreQuery"/> and what <see cref="SearchAsync"/> does with <c>display</c>.
/// </para>
/// <para>
/// <b>Regex is not optional.</b> There is deliberately no "supports regex" flag: a user who types
/// <c>?/pattern/</c> should get regex semantics on every page, and a capability that varies page to page is
/// worse than none. A backend whose query language has no regex operator translates the pattern into the
/// widest query that still covers it and re-filters the results with the real regex — see
/// <c>AqsRegexTranslator</c> for the worked example against Windows Search. Widening is always safe because
/// it over-matches; the post-filter restores exactness. Conformance tests assert that regex really is
/// compiled and not quietly answered with a substring scan.
/// </para>
/// </summary>
public interface ISearchable
{
    /// <summary>
    /// What this page searches, as a noun phrase for the model ("the open file 'notes.md'",
    /// "files under 'C:\projects'", "the installed application list"). Shown in the agent's tool catalogue.
    /// </summary>
    string SearchTargetDescription { get; }

    /// <summary>
    /// Extra term kinds this page's queries can contain, beyond plain text and delimited regular
    /// expressions. Default: none.
    /// <para>
    /// This is how a filename glob reaches a file browser and never reaches a text viewer. A page that
    /// can't judge filenames simply doesn't offer the glob recogniser, so the parser hands it only terms it
    /// can honour — rather than the parser guessing, or every page having to defend against term kinds that
    /// make no sense for it.
    /// </para>
    /// </summary>
    IReadOnlyList<ISearchTermRecognizer> TermRecognizers => [];

    /// <summary>
    /// Runs <paramref name="request"/> over this page's content.
    /// <para>
    /// <paramref name="display"/> false means the caller (the agent) wants the matches as data and the page
    /// must NOT change what the user sees — no highlights, no filtering, no navigation, and no new tab.
    /// True means show the results the way this page shows results: highlight in place, filter the list, or
    /// open the surface that presents them.
    /// </para>
    /// </summary>
    Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct);

    /// <summary>
    /// Narrows the visible result set to <paramref name="hits"/> — the ids come from a previous
    /// <see cref="SearchAsync"/>, letting the agent search broadly, filter by meaning, then show the user
    /// only what survived. Returns false when the page can't render an arbitrary subset (the default), so
    /// the agent can fall back to describing the matches instead of silently doing nothing.
    /// </summary>
    Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct) => Task.FromResult(false);

    /// <summary>
    /// How strongly this page claims <em>un-prefixed</em> input (0–1); a typed <c>?</c> bypasses this
    /// entirely. Override when the page can tell a search term from a question better than term-counting
    /// can — a file browser recognising globs and property filters, say. Must be cheap and side-effect
    /// free: see <see cref="SearchScoring"/>.
    /// </summary>
    float ScoreQuery(string input) => SearchScoring.Default;
}
