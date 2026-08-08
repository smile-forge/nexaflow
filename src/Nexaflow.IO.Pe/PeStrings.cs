using System.Text;

namespace Nexaflow.IO.Pe;

/// <summary>
/// The classic <c>strings</c> sweep — runs of printable characters, in both ASCII and UTF-16LE,
/// with the file offset of each so a hit can be opened straight in the hex view.
/// <para>
/// Streamed and cancellable by design: this is the one operation that always touches every byte of
/// the image, so it must never be run eagerly alongside the structural parse.
/// </para>
/// </summary>
public static class PeStrings
{
    public const int DefaultMinimumLength = 6;

    private const int ChunkSize = 1 << 20;
    /// <summary>Overlap between chunks so a run straddling a boundary is not cut in half.</summary>
    private const int Overlap = 4096;

    /// <summary>
    /// Extracts printable runs. Lazily evaluated — take what you need and stop; nothing is buffered
    /// beyond the current run.
    /// </summary>
    /// <param name="image">The image to scan.</param>
    /// <param name="minimumLength">Shortest run to report. Below about 4 the result is mostly noise.</param>
    /// <param name="maxResults">Stop after this many, so a pathological file cannot exhaust memory.</param>
    public static IEnumerable<PeString> Extract(
        PeImage image, int minimumLength = DefaultMinimumLength, int maxResults = 200_000,
        CancellationToken ct = default)
    {
        if (minimumLength < 1) minimumLength = 1;

        int found = 0;
        foreach (var hit in Scan(image, minimumLength, ct))
        {
            yield return hit;
            if (++found >= maxResults) yield break;
        }
    }

    private static IEnumerable<PeString> Scan(PeImage image, int minimumLength, CancellationToken ct)
    {
        long length = image.Length;
        if (length <= 0) yield break;

        // Chunks overlap, so a run can be found twice; the last offset emitted filters the repeat.
        long lastAsciiEnd = -1, lastUtf16End = -1;

        for (long start = 0; start < length; start += ChunkSize - Overlap)
        {
            ct.ThrowIfCancellationRequested();

            int  size  = (int)Math.Min(ChunkSize, length - start);
            var  block = image.ReadAt(start, size);
            if (block.Length == 0) break;

            foreach (var hit in ScanAscii(block, start, minimumLength))
            {
                if (hit.Offset <= lastAsciiEnd) continue;
                lastAsciiEnd = hit.Offset;
                yield return hit with { Section = SectionAt(image, hit.Offset) };
            }

            foreach (var hit in ScanUtf16(block, start, minimumLength))
            {
                if (hit.Offset <= lastUtf16End) continue;
                lastUtf16End = hit.Offset;
                yield return hit with { Section = SectionAt(image, hit.Offset) };
            }

            if (size < ChunkSize) break;
        }
    }

    private static IEnumerable<PeString> ScanAscii(byte[] block, long baseOffset, int minimumLength)
    {
        var builder = new StringBuilder();
        int runStart = 0;

        for (int i = 0; i < block.Length; i++)
        {
            if (IsPrintable(block[i]))
            {
                if (builder.Length == 0) runStart = i;
                builder.Append((char)block[i]);
                continue;
            }
            if (builder.Length >= minimumLength)
                yield return new PeString(baseOffset + runStart, PeStringEncoding.Ascii, builder.ToString());
            builder.Clear();
        }

        if (builder.Length >= minimumLength)
            yield return new PeString(baseOffset + runStart, PeStringEncoding.Ascii, builder.ToString());
    }

    /// <summary>
    /// UTF-16LE runs: a printable byte followed by a zero. Restricting to the Latin-1 range would
    /// miss non-Western resource strings, so the high byte is allowed to be non-zero only when the
    /// resulting code point is itself printable.
    /// </summary>
    private static IEnumerable<PeString> ScanUtf16(byte[] block, long baseOffset, int minimumLength)
    {
        var builder  = new StringBuilder();
        int runStart = 0;

        for (int i = 0; i + 1 < block.Length; i += 2)
        {
            char c = (char)(block[i] | (block[i + 1] << 8));
            if (IsPrintable(c))
            {
                if (builder.Length == 0) runStart = i;
                builder.Append(c);
                continue;
            }
            if (builder.Length >= minimumLength)
                yield return new PeString(baseOffset + runStart, PeStringEncoding.Utf16, builder.ToString());
            builder.Clear();
        }

        if (builder.Length >= minimumLength)
            yield return new PeString(baseOffset + runStart, PeStringEncoding.Utf16, builder.ToString());
    }

    private static bool IsPrintable(byte b) => b is >= 0x20 and <= 0x7E || b == '\t';

    private static bool IsPrintable(char c)
        => c is >= ' ' and <= '~' || c == '\t' ||
           (c > 0xA0 && !char.IsControl(c) && !char.IsSurrogate(c));

    private static string? SectionAt(PeImage image, long offset)
    {
        foreach (var section in image.Sections)
            if (section.RawSize > 0 && offset >= section.RawPointer &&
                offset < section.RawPointer + section.RawSize)
                return section.Name;
        return null;
    }
}
