using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>
/// One match a searchable page found. <see cref="Id"/> is the page's own stable handle for the match
/// (a line number, a file path, an app name) — the AI hands a subset of them back to
/// <see cref="ISearchable.ShowResultsAsync"/> to narrow what the user sees, so it must round-trip.
/// </summary>
/// <param name="Id">Stable, page-local identifier for this match.</param>
/// <param name="Label">Short human/model-facing label (e.g. "line 42", a file name).</param>
/// <param name="Preview">Optional surrounding text, so the model can judge relevance without a second call.</param>
public sealed record SearchHit(string Id, string Label, string? Preview = null)
{
    /// <summary>
    /// Whether this hit is proven. Defaults to <see cref="SearchHitState.Verified"/> so a backend that
    /// evaluates the query itself needs to know nothing about verification; only a speculative backend
    /// (the file index answering a content regex) emits <see cref="SearchHitState.Candidate"/>.
    /// </summary>
    public SearchHitState State { get; init; } = SearchHitState.Verified;

    /// <summary>The file path or other locator a verifier re-reads to settle a candidate. Null when the hit
    /// isn't backed by something re-readable — such a hit can never be a candidate.</summary>
    public string? Source { get; init; }
}
