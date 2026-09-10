using System.IO;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Office.Packaging;
using Nexaflow.Features.Office.Word;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Office.Search;

/// <summary>
/// Makes Word documents searchable by content. Without this a <c>.docx</c> reaches the search verifier's
/// last-resort reader, which scans raw bytes — and a Word document is a zip of deflated XML, so that scan finds
/// nothing and can't even report the miss honestly.
/// <para>
/// Stateless, as the contract requires: the shell caches one instance per workspace and a user sweep and an
/// agent query can run through it at the same time. Every piece of working state lives in the call.
/// </para>
/// <para>
/// No size ceiling, unlike the PDF extractor, whose parse can't be interrupted once it starts. Opening a zip
/// reads only its central directory and each part is streamed element by element, so a large document costs
/// only what is read before the byte budget fills — and cancellation is honoured between any two elements.
/// </para>
/// </summary>
public sealed class OfficeTextExtractor : IFileTextExtractor
{
    // The four WordprocessingML package types. Not .doc: binary Word 97-2003 is another format entirely, and
    // claiming it would only swap the raw scan's real chance at its text for a guaranteed "couldn't tell".
    private static readonly string[] WordExtensions = [".docx", ".docm", ".dotx", ".dotm"];

    // Asked for every candidate file in a sweep, so it stays an extension test and never opens anything.
    public bool CanExtract(string path)
        => !string.IsNullOrEmpty(path)
           && WordExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The document's text, or null when it couldn't be read as a Word document at all.
    /// <para>
    /// The distinction carries real weight downstream. Any non-null result is treated as authoritative, so a
    /// term the text lacks becomes a confident rejection — right for a document that genuinely has no words,
    /// whose <b>empty string</b> proves there is nothing to match. Null instead means "couldn't tell" and
    /// sends the verifier to its raw byte scan. So <em>every</em> failure is null — including XML that breaks
    /// off halfway, because text read before the break would otherwise be trusted as the whole document.
    /// </para>
    /// </summary>
    public Task<string?> ExtractAsync(string path, long maxBytes, CancellationToken ct)
    {
        // Cancelling before the work starts skips it entirely rather than scheduling and then throwing.
        return Task.Run(() => Extract(path, maxBytes, ct), ct);
    }

    private static string? Extract(string path, long maxBytes, CancellationToken ct)
    {
        if (maxBytes <= 0) return null;

        try
        {
            // Through the VFS so a document inside an archive works too; for a real path this is a plain
            // FileStream that shares read-write, so a document open in Word right now is still readable.
            using var stream  = VirtualFileSystem.Instance.OpenRead(path);
            using var package = OpcPackage.TryOpen(stream);
            if (package is null) return null;

            var budget = new TextBudget(maxBytes);
            return WordDocumentText.Read(package, budget, ct) ? budget.ToString() : null;
        }
        catch (OperationCanceledException)
        {
            // The sweep is being abandoned. Swallowing this would report an empty document instead — striking
            // every remaining row through as a definite miss.
            throw;
        }
        catch
        {
            // Locked, gone, not a zip, corrupt deflate, malformed XML: "not readable", never a crash.
            return null;
        }
    }
}
