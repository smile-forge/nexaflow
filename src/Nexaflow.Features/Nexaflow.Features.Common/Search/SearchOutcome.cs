using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>
/// What a page's <see cref="ISearchable.SearchAsync"/> found.
/// <para>
/// <see cref="Failed"/> is the important distinction: "the search ran and matched nothing" is
/// <c>MatchCount == 0</c>, whereas "this page could not run that search at all" (a malformed regex, or a
/// backend with no regex support) is <c>Failed</c> with a <see cref="Message"/> saying why. Never report
/// an unsupported search as zero matches — silently answering a regex with a literal scan, or with an
/// empty result, is exactly the failure the conformance tests exist to catch.
/// </para>
/// </summary>
/// <param name="MatchCount">Total matches found — may exceed <paramref name="Hits"/>, which is capped.</param>
/// <param name="Hits">The matches themselves, newest-first or document order, capped by the page.</param>
/// <param name="Message">Text to show the user / model. Null when the page renders its own results.</param>
/// <param name="Failed">True when the search could not be performed (see remarks).</param>
public sealed record SearchOutcome(
    int MatchCount,
    IReadOnlyList<SearchHit> Hits,
    string? Message = null,
    bool Failed = false)
{
    /// <summary>The search ran and matched nothing.</summary>
    public static SearchOutcome None(string? message = null) => new(0, [], message);

    /// <summary>The search ran and matched. <paramref name="count"/> defaults to the hit count.</summary>
    public static SearchOutcome Found(IReadOnlyList<SearchHit> hits, int? count = null, string? message = null)
        => new(count ?? hits.Count, hits, message);

    /// <summary>The page could not run this search — a bad pattern, or a mode it doesn't support.</summary>
    public static SearchOutcome Unsupported(string message) => new(0, [], message, Failed: true);

    /// <summary>
    /// The search ran but against a narrower field than the user may have meant — a regex tested against
    /// file names when the same words typed literally would also have searched inside files. Not a failure:
    /// the results are real, but the user is told what was actually matched so an empty result doesn't read
    /// as "nothing here".
    /// </summary>
    public static SearchOutcome Narrowed(IReadOnlyList<SearchHit> hits, string message)
        => new(hits.Count, hits, message);
}
