using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Exposes the Windows Search index as an <see cref="IFileCorpusSearch"/>, so a page in another feature
/// assembly (the file browser) can run a real file search without referencing this one. Discovered and
/// activated via <c>IShellServices.DiscoverImplementations</c> — hence the parameterless constructor.
/// <para>
/// Regex is supported even though AQS has no regex operator: the pattern is translated into the widest AQS
/// query that still covers it (<see cref="AqsRegexTranslator"/>) and the returned rows are then filtered
/// with the actual regex. Translation only decides how many candidates come back — correctness comes from
/// the post-filter, so a pattern AQS can't express is slower, never wrong.
/// </para>
/// </summary>
public sealed class WindowsSearchCorpus : IFileCorpusSearch
{
    /// <summary>Rows pulled from the index before post-filtering. A regex that AQS can barely constrain
    /// relies on this headroom, so it is well above the caller's own cap.</summary>
    private const int CandidateMultiplier = 20;
    private const int MaxCandidates       = 5000;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        SearchRequest request, string root, IReadOnlyList<string> drives, int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return [];

        // A regex is seeded by the longest literal it must contain, searched as an ordinary term so file
        // contents are covered; a literal query is already in the parser's own language.
        var seed = request.IsRegex ? AqsRegexTranslator.ToIndexQuery(request.Text) : request.Text;

        // No literal to narrow on ("^.+$") — better to return nothing than to drag back the whole corpus
        // and read every file in it. The caller reports why.
        if (string.IsNullOrEmpty(seed)) return [];

        var parsed = SearchQueryParser.Parse(seed);
        var want   = request.IsRegex ? Math.Min(MaxCandidates, max * CandidateMultiplier) : max;

        try
        {
            var entries = drives.Count > 0 && string.IsNullOrEmpty(root)
                ? await WindowsSearchService.SearchAcrossAsync(parsed, drives, ct)
                : await WindowsSearchService.SearchAsync(parsed, root, ct);

            var rows = entries.Take(want).ToList();

            // A literal query was fully evaluated by the index — every row is proven.
            if (!request.IsRegex)
                return rows.Take(max).Select(Hit).ToList();

            // A regex was only partly evaluated: AQS narrowed by name, and the name check below settles
            // those exactly. Rows that survived AQS but whose name doesn't match may still match on
            // CONTENT, which no index query can decide — they come back as candidates for the verifier
            // rather than being dropped (dropping is what made a content pattern look like an empty folder).
            return rows.Take(max)
                       .Select(e => Hit(e) with { State = SearchVerifier.ClassifyByName(Hit(e), request) })
                       .ToList();

            static SearchHit Hit(SearchResultEntry e) =>
                new(e.FilePath, e.FileName, e.Directory) { Source = e.FilePath };
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // An unavailable or misconfigured index is an empty result for the caller, not a crash —
            // the page reports "no matches" and the user can still browse.
            return [];
        }
    }
}
