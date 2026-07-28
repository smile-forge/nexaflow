using System.IO;
using System.Text;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>How much to trust the text a file gave up.</summary>
public enum TextFidelity
{
    /// <summary>Decoded with a detected encoding — what matched is genuinely the file's text, and a miss
    /// is a real miss.</summary>
    Decoded,

    /// <summary>Scanned as raw bytes because the format isn't understood. A hit is real but may be
    /// incidental (a string in a header or tag), and a MISS proves nothing: a container's real text is
    /// compressed and was never in these bytes to begin with.</summary>
    Raw,
}

/// <param name="Text">What was read.</param>
/// <param name="Fidelity">How much the result can be trusted.</param>
public sealed record ExtractedText(string Text, TextFidelity Fidelity);

/// <summary>
/// The last-resort content reader: when no format-aware <see cref="Nexaflow.Features.Common.Search.IFileTextExtractor"/>
/// claims a file, read it anyway. Most things worth grepping (source, config, logs, markup) are text without
/// anyone having written an extractor for them, so the fallback carries the majority of real use.
/// <para>
/// A binary file is scanned rather than skipped — a plain-text search over unknown bytes is exactly what
/// Notepad++ does, and it finds real things. What matters is that the caller is told the reading was raw,
/// so a hit can be presented as "probably, have a look" instead of as a confirmed match, and a miss is
/// never mistaken for proof of absence.
/// </para>
/// </summary>
public sealed class PlainTextExtractor
{
    /// <summary>NUL bytes in the head mean the file isn't plain text. Not a reason to skip it — only a
    /// reason to distrust what a scan of it turns up.</summary>
    private const int BinarySniffBytes = 8192;

    public async Task<ExtractedText?> ExtractAsync(string path, long maxBytes, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 64 * 1024, useAsync: true);

            if (stream.Length == 0) return null;

            // Encoding FIRST, then the binary sniff. UTF-16 text is full of NUL bytes, so sniffing before
            // detection would classify every wide-encoded file as binary.
            var probe  = EncodingDetector.Detect(stream);
            var isWide = IsWideEncoding(probe.Encoding);
            var isRaw  = !isWide && await LooksBinaryAsync(stream, ct);

            stream.Position = isRaw ? 0 : probe.BomByteCount;

            // Capped: a verification pass may open thousands of files and must not be able to pull a
            // multi-gigabyte one into memory. A match past the cap is missed — accepted deliberately,
            // since the alternative is an unbounded read on a background sweep.
            var budget = (int)Math.Min(maxBytes, stream.Length - stream.Position);
            if (budget <= 0) return null;

            var buffer = new byte[budget];
            var read   = await stream.ReadAtLeastAsync(buffer, budget, throwOnEndOfStream: false, ct);

            // Latin-1 for a raw scan: it is byte-preserving, so every byte becomes a character and any
            // ASCII run embedded in the file survives to be matched.
            var encoding = isRaw ? Encoding.Latin1 : probe.Encoding;
            return new ExtractedText(encoding.GetString(buffer, 0, read),
                                     isRaw ? TextFidelity.Raw : TextFidelity.Decoded);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Locked, gone, or permission-denied: not searchable, not a crash.
            return null;
        }
    }

    // UTF-16/32 encode ASCII with NUL padding, so the NUL heuristic can't be applied to them.
    private static bool IsWideEncoding(Encoding encoding) =>
        encoding.CodePage is 1200 or 1201 or 12000 or 12001;   // UTF-16 LE/BE, UTF-32 LE/BE

    private static async Task<bool> LooksBinaryAsync(FileStream stream, CancellationToken ct)
    {
        // Rewind first: encoding detection has already run and left the position at the end of its sample,
        // so reading from wherever it stopped sniffs nothing and calls every binary file text.
        stream.Position = 0;

        var head = new byte[(int)Math.Min(BinarySniffBytes, stream.Length)];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        stream.Position = 0;

        for (var i = 0; i < read; i++)
            if (head[i] == 0) return true;
        return false;
    }
}
