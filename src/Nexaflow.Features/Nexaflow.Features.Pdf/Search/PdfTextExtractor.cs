using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Pdf.Reading;

namespace Nexaflow.Features.Pdf.Search;

/// <summary>
/// Makes PDFs searchable by content. Without this a PDF reaches the search verifier's last-resort reader,
/// which scans the raw bytes — and a PDF's text lives in compressed streams, so that scan finds nothing and
/// can't even report the miss honestly.
/// <para>
/// Stateless, as the contract requires: the shell caches one instance per workspace and a user sweep and an
/// agent query can run through it at the same time.
/// </para>
/// <para>
/// The config is constructor-injected with deliberately <b>no</b> parameterless fallback. Defaulting it when
/// the DI can't supply one would turn a broken registration into a feature that looks like it works while
/// quietly ignoring every setting — the kind of bug that surfaces months later as "my size ceiling does
/// nothing". Requiring the argument means a registration failure instead shows up as no extractor at all,
/// which <c>FileTextExtractorResolutionTests</c> catches.
/// </para>
/// </summary>
public sealed class PdfTextExtractor(PdfConfig config) : IFileTextExtractor
{
    // Asked for every candidate file in a sweep, so it stays an extension test and never opens anything.
    public bool CanExtract(string path)
        => !string.IsNullOrEmpty(path)
           && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The PDF's text, or null when it couldn't be read at all.
    /// <para>
    /// The distinction carries real weight downstream. Any non-null result is treated as authoritative, so a
    /// term the text lacks becomes a confident rejection — which is right for an image-only scan, whose
    /// <b>empty string</b> genuinely proves there is no text to match. Null instead means "couldn't tell",
    /// and sends the verifier to the raw byte scan, which reports an inconclusive result rather than a miss.
    /// Returning empty for a corrupt file would strike it through as definitely-not-a-match; returning null
    /// for a scan would leave every scanned page permanently "possible".
    /// </para>
    /// </summary>
    public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct)
    {
        // Cancelling before the work starts skips it entirely rather than scheduling and then throwing.
        return Task.Run(() => Extract(path, maxBytes, ct), ct);
    }

    private string? Extract(string path, long maxBytes, CancellationToken ct)
    {
        if (maxBytes <= 0 || IsTooLargeToParse(path)) return null;

        using var scope = PdfDocumentScope.TryOpen(path, ct);
        if (scope is null) return null;

        // Cancellation propagates: the sweep is being abandoned, and swallowing it here would report an empty
        // document instead — striking every remaining row through as a definite miss.
        var result = PdfTextReader.Read(scope.Document, maxBytes, config.IncludeMetadata, ct);
        return result.Text;
    }

    // Opening a PDF means reading its cross-reference table before any page exists, and that stretch can't be
    // interrupted. Since the sweep is sequential, one enormous file would hold up every candidate behind it —
    // so an oversized PDF is declined up front and reported as unreadable rather than parsed.
    private bool IsTooLargeToParse(string path)
    {
        try
        {
            var ceiling = (long)Math.Max(1, config.SearchMaxFileSizeMb) * 1024 * 1024;
            return new FileInfo(path).Length > ceiling;
        }
        catch
        {
            return false;   // can't stat it — let the open attempt be the thing that fails
        }
    }
}
