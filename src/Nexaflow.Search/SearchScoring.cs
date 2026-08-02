using System.Text.RegularExpressions;

namespace Nexaflow.Search;

/// <summary>
/// Shared rules for how hard a searchable page competes for <em>un-prefixed</em> AI-bar input. An explicit
/// <c>?</c> always wins outright — these only decide what happens to bare text.
/// <para>
/// Scoring runs on every keystroke (it drives the status dot as well as routing), so it must stay cheap and
/// side-effect free: no page may speculatively run its search to see whether anything matches. On a text
/// tab that is a full file scan per character, and on an indexed backend a live query.
/// </para>
/// </summary>
public static class SearchScoring
{
    /// <summary>Score for a page that doesn't override <c>ISearchable.ScoreQuery</c>. Deliberately
    /// below the router's clear-winner bar, so bare text is disambiguated rather than silently searched.</summary>
    public const float Default = 0.5f;

    /// <summary>Beyond this many terms, bare input is prose meant for the agent, not a search term.</summary>
    public const int MaxSearchTerms = 4;

    // Whitespace-split, but a quoted phrase counts as one term.
    private static readonly Regex TermSplitter = new(@"""[^""]*""|'[^']*'|\S+", RegexOptions.Compiled);

    /// <summary>Number of search terms in <paramref name="input"/>, counting a quoted phrase as one.</summary>
    public static int TermCount(string input) =>
        string.IsNullOrWhiteSpace(input) ? 0 : TermSplitter.Matches(input.Trim()).Count;

    /// <summary>
    /// True when bare input reads as a sentence rather than a search term. Such input is left to the agent —
    /// which can still search on the user's behalf via the page's search tool, at the cost of one model call.
    /// That round trip is the deliberate trade: a wrong silent search is worse than an extra call.
    /// </summary>
    public static bool LooksLikeProse(string input) => TermCount(input) > MaxSearchTerms;
}
