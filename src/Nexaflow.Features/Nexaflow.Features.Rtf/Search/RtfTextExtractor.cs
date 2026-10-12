using System.IO;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Rtf.Reading;
using Nexaflow.Markdown.Rtf;

namespace Nexaflow.Features.Rtf.Search;

/// <summary>
/// Makes rich-text documents searchable by content. Without this an <c>.rtf</c> reaches the search verifier's last-resort
/// reader, which reads it as plain text — control words and all, so a search for "par" matches every paragraph break and
/// a word written with an accented letter (<c>caf\'e9</c>) is never found.
/// <para>
/// The words are the ones the page draws: the RTF reader's own, a paragraph to a line. Stateless, as the contract
/// requires — every piece of working state lives in the call.
/// </para>
/// </summary>
public sealed class RtfTextExtractor : IFileTextExtractor
{
    private const string Extension = ".rtf";

    // Asked for every candidate file in a sweep, so it stays an extension test and never opens anything.
    public bool CanExtract(string path)
        => !string.IsNullOrEmpty(path) && string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The document's words, or null when it could not be read or is not RTF after all — null sends the verifier to its own
    /// reading, where an empty string would be taken as proof the document has no words. Read no further than
    /// <paramref name="maxBytes"/>; what lies past that is not searched.
    /// </summary>
    public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct)
    {
        // Cancelling before the work starts skips it entirely rather than scheduling and then throwing.
        return Task.Run(() => Extract(path, maxBytes), ct);
    }

    private static string? Extract(string path, long maxBytes)
    {
        if (maxBytes <= 0) return null;

        try
        {
            var source = RtfDocumentFile.Read(path, maxBytes);
            return RtfDocumentFile.IsRtf(source) ? RtfParser.Text(source) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Locked, gone, or unreadable: "couldn't tell", never a crash.
            return null;
        }
    }
}
