using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>
/// An engine that searches a <em>corpus of files</em> — a folder tree or a set of drives — rather than
/// anything currently on screen. Supplied by whichever feature owns the index (the Windows Search feature)
/// and consumed by any page that searches files it isn't itself displaying, chiefly the file browser.
/// <para>
/// This is the seam that lets the browser be <see cref="ISearchable"/> without referencing the search
/// feature: implementations are found with <c>IShellServices.DiscoverImplementations&lt;IFileCorpusSearch&gt;()</c>
/// and constructed with <c>Activator.CreateInstance</c>, so implementations need a public parameterless
/// constructor. Same pattern as the archive codecs' <c>IArchiveEncryptor</c>.
/// </para>
/// </summary>
public interface IFileCorpusSearch
{
    /// <summary>
    /// Runs <paramref name="request"/> over <paramref name="root"/> — or across
    /// <paramref name="drives"/> when the root is empty — and returns at most <paramref name="max"/> hits.
    /// Returns an empty list rather than throwing when nothing matches or the index is unavailable.
    /// <para>
    /// Implementations MUST honour <see cref="SearchRequest.IsRegex"/> exactly. An index whose query
    /// language has no regex operator should translate the pattern into the widest query that still
    /// covers it and then re-filter the results with the real regex — the semantics the user asked for
    /// are not negotiable just because the backend is a term matcher.
    /// </para>
    /// <para>
    /// <b>A regex matches the file name, not file contents.</b> Exact re-filtering needs the text the
    /// pattern is tested against, and the index hands back names, not bodies — matching contents would
    /// mean opening every candidate. Literal queries still search contents (that is what the index is
    /// for); only the regex mode is name-scoped. Implementations must say so via
    /// <see cref="SearchOutcome.Message"/> rather than quietly returning nothing when a content-intent
    /// pattern matches no filename.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(
        SearchRequest request, string root, IReadOnlyList<string> drives, int max, CancellationToken ct);
}
