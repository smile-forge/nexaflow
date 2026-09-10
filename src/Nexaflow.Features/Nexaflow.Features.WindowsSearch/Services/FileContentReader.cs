using Nexaflow.Features.Common.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Reads a file's text for a content match: through the format-aware <see cref="IFileTextExtractor"/> that
/// claims it when some feature provides one, else as plain text.
/// <para>
/// The one policy both content paths share — the index's candidate sweep (<see cref="SearchVerifier"/>) and
/// the folder scan (<see cref="WindowsSearchService.WalkAsync"/>). Kept in one place so a PDF or a Word
/// document is read the same way whichever of them asks; the scan used to read everything as plain text, so
/// a document's words were findable from the index but never by walking a folder the index doesn't cover.
/// </para>
/// </summary>
public sealed class FileContentReader
{
    private readonly Func<string, IFileTextExtractor?> _resolveExtractor;
    private readonly PlainTextExtractor _fallback = new();

    /// <param name="resolveExtractor">
    /// Supplies the format-aware extractor for a path, or null when none understands it — normally
    /// <c>IShellServices.GetFileTextExtractor</c>. The shell owns those instances (built through the feature
    /// DI, cached per workspace), so the reader only ever borrows one; passing null here means every file
    /// is read as plain text.
    /// </param>
    public FileContentReader(Func<string, IFileTextExtractor?>? resolveExtractor = null)
        => _resolveExtractor = resolveExtractor ?? (static _ => null);

    // A format-aware extractor first — one that claims a file understands it, so its text is trustworthy.
    // Plain text is the fallback and can never out-compete one.
    //
    // Whichever extractor claims the file is the only one asked: one feature owns a format, so a null from
    // its claimant means "I understand this and there is no text in it to read", not "try someone else".
    // That null still falls through to the raw scan rather than being reported as empty text, because the
    // extractor uses null for "couldn't tell" — an empty string is how it says "genuinely no text".
    public async Task<ExtractedText?> ReadAsync(string path, long maxBytes, CancellationToken ct)
    {
        IFileTextExtractor? extractor;
        try { extractor = _resolveExtractor(path); }
        catch { extractor = null; }

        if (extractor is not null)
        {
            try
            {
                var text = await extractor.ExtractAsync(path, maxBytes, ct);
                if (text is not null) return new ExtractedText(text, TextFidelity.Decoded);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* a broken extractor must not sink the whole sweep — fall through to plain text */ }
        }

        return await _fallback.ExtractAsync(path, maxBytes, ct);
    }
}
