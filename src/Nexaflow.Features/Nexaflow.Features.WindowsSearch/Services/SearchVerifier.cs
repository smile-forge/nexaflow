using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Settles the speculative half of a file-corpus regex search: the index can narrow by name but can't
/// evaluate a pattern against file contents, so it returns "might match" rows that this pass proves or
/// rejects one at a time, off the UI thread.
/// <para>
/// Names are settled for free and up front — a candidate whose <em>name</em> matches the regex is already
/// proven, so it never needs a file read. Only rows the index returned whose name doesn't match are real
/// unknowns, which is what keeps the verify set (and the reading) small.
/// </para>
/// </summary>
public sealed class SearchVerifier
{
    /// <summary>Bytes read per file before giving up on finding a match in it.</summary>
    public const long MaxBytesPerFile = 4 * 1024 * 1024;

    private readonly Func<string, IFileTextExtractor?> _resolveExtractor;
    private readonly PlainTextExtractor _fallback = new();

    /// <param name="resolveExtractor">
    /// Supplies the format-aware extractor for a path, or null when none understands it — normally
    /// <c>IShellServices.GetFileTextExtractor</c>. The shell owns those instances (built through the feature
    /// DI, cached per workspace), so the verifier only ever borrows one; passing null here means every file
    /// is read as plain text.
    /// </param>
    public SearchVerifier(Func<string, IFileTextExtractor?>? resolveExtractor = null)
        => _resolveExtractor = resolveExtractor ?? (static _ => null);

    /// <summary>
    /// The state a hit deserves before anything is read: proven when the name matches, otherwise a
    /// candidate that only a content read can settle. Free, so callers apply it to the whole result set
    /// immediately and show the user a truthful "N confirmed, M possible" split.
    /// </summary>
    public static SearchHitState ClassifyByName(SearchHit hit, SearchRequest request)
    {
        // A name-scoped term the name failed (a "*.txt" filter on a .pdf) can never be rescued by reading
        // the file, so that row is settled here rather than queued for a pointless read.
        if (request.NameRulesItOut(hit.Label)) return SearchHitState.Rejected;

        return request.MatchesName(hit.Label) ? SearchHitState.Verified : SearchHitState.Candidate;
    }

    /// <summary>
    /// Reads <paramref name="hit"/>'s file and decides. A file whose text can't be read comes back
    /// <see cref="SearchHitState.Unreadable"/> — not <see cref="SearchHitState.Rejected"/>, because it may
    /// well match and we simply can't tell, and not <see cref="SearchHitState.Candidate"/>, because a
    /// candidate is something re-checking could still settle and this isn't.
    /// </summary>
    public async Task<SearchHitState> VerifyAsync(SearchHit hit, SearchRequest request, CancellationToken ct)
    {
        var byName = ClassifyByName(hit, request);
        if (byName is SearchHitState.Verified or SearchHitState.Rejected) return byName;

        var path = hit.Source ?? hit.Id;
        if (string.IsNullOrEmpty(path)) return SearchHitState.Unreadable;

        var extracted = await ExtractAsync(path, ct);
        if (extracted is null) return SearchHitState.Unreadable;

        // Each term may be met by the name OR the contents — "*.txt report" wants a .txt file mentioning
        // "report", which testing the whole query against one or the other would miss.
        var found = request.MatchesFile(hit.Label, extracted.Text);

        // A properly decoded read settles it either way.
        if (extracted.Fidelity == TextFidelity.Decoded)
            return found ? SearchHitState.Verified : SearchHitState.Rejected;

        // A raw scan of a format we don't understand: a hit is real but might be incidental, and a miss
        // proves nothing at all — a container's text is compressed and was never in these bytes.
        return found ? SearchHitState.Uncertain : SearchHitState.Unreadable;
    }

    /// <summary>
    /// Verifies <paramref name="hits"/> in order, reporting each result as it lands so the UI can settle
    /// row by row rather than freezing until the whole sweep finishes. Stops cleanly on cancellation.
    /// </summary>
    public async Task VerifyAllAsync(
        IReadOnlyList<SearchHit> hits,
        SearchRequest request,
        Func<SearchHit, SearchHitState, Task> onSettled,
        CancellationToken ct)
    {
        foreach (var hit in hits)
        {
            ct.ThrowIfCancellationRequested();
            var state = await VerifyAsync(hit, request, ct);
            await onSettled(hit, state);
        }
    }

    // A format-aware extractor first — one that claims a file understands it, so its text is trustworthy.
    // Plain text is the fallback and can never out-compete one.
    //
    // Whichever extractor claims the file is the only one asked: one feature owns a format, so a null from
    // its claimant means "I understand this and there is no text in it to read", not "try someone else".
    // That null still falls through to the raw scan rather than being reported as empty text, because the
    // extractor uses null for "couldn't tell" — an empty string is how it says "genuinely no text".
    private async Task<ExtractedText?> ExtractAsync(string path, CancellationToken ct)
    {
        IFileTextExtractor? extractor;
        try { extractor = _resolveExtractor(path); }
        catch { extractor = null; }

        if (extractor is not null)
        {
            try
            {
                var text = await extractor.ExtractAsync(path, MaxBytesPerFile, ct);
                if (text is not null) return new ExtractedText(text, TextFidelity.Decoded);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* a broken extractor must not sink the whole sweep — fall through to plain text */ }
        }

        return await _fallback.ExtractAsync(path, MaxBytesPerFile, ct);
    }
}
