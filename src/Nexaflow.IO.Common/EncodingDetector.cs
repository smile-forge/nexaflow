using System.IO;
using System.Text;

namespace Nexaflow.IO.Common;

public sealed record EncodingProbe(Encoding Encoding, bool HadBom, int BomByteCount);

/// <summary>
/// Sniffs a file's text encoding from the head of the stream — BOM first, then a UTF-8 validity scan,
/// with a UTF-16 alternating-NUL heuristic. The shared home for the detection that Logs, Text, Tabular
/// and JSON each used to re-implement.
/// </summary>
public static class EncodingDetector
{
    /// <summary>
    /// Inspects the head of the file: BOM first, then a UTF-8 validity scan on the
    /// sample bytes. Falls back to Latin-1 when high bytes appear but UTF-8 decoding
    /// fails. Does not read more than <paramref name="sampleBytes"/>.
    /// </summary>
    public static EncodingProbe Detect(Stream stream, int sampleBytes = 4096)
    {
        if (!stream.CanSeek) throw new System.ArgumentException("Stream must be seekable", nameof(stream));
        stream.Position = 0;

        var head = new byte[System.Math.Min(sampleBytes, (int)System.Math.Min(stream.Length, sampleBytes))];
        var read = stream.Read(head, 0, head.Length);

        // BOMs
        if (read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            return new(Encoding.UTF8, true, 3);
        if (read >= 4 && head[0] == 0xFF && head[1] == 0xFE && head[2] == 0x00 && head[3] == 0x00)
            return new(Encoding.UTF32, true, 4);
        if (read >= 2 && head[0] == 0xFF && head[1] == 0xFE)
            return new(Encoding.Unicode, true, 2);                  // UTF-16 LE
        if (read >= 2 && head[0] == 0xFE && head[1] == 0xFF)
            return new(Encoding.BigEndianUnicode, true, 2);         // UTF-16 BE

        // UTF-16 LE without BOM heuristic: lots of alternating 0x00 bytes in head
        if (read >= 16)
        {
            int evenZero = 0, oddZero = 0;
            int span = System.Math.Min(read, 256);
            for (int i = 0; i < span; i++)
                if (head[i] == 0) { if (i % 2 == 0) evenZero++; else oddZero++; }
            if (oddZero > span / 4 && evenZero < span / 16) return new(Encoding.Unicode, false, 0);
            if (evenZero > span / 4 && oddZero < span / 16) return new(Encoding.BigEndianUnicode, false, 0);
        }

        // UTF-8 validity check
        if (LooksLikeUtf8(head, read)) return new(Encoding.UTF8, false, 0);

        // Fallback for high-byte 8-bit
        return new(Encoding.Latin1, false, 0);
    }

    private static bool LooksLikeUtf8(byte[] buf, int len)
    {
        int i = 0;
        while (i < len)
        {
            byte b = buf[i];
            if (b < 0x80) { i++; continue; }
            int trail =
                (b & 0xE0) == 0xC0 ? 1 :
                (b & 0xF0) == 0xE0 ? 2 :
                (b & 0xF8) == 0xF0 ? 3 : -1;
            if (trail < 0) return false;
            if (i + trail >= len) return true; // truncated at end of sample — treat as ok
            for (int t = 1; t <= trail; t++)
                if ((buf[i + t] & 0xC0) != 0x80) return false;
            i += trail + 1;
        }
        return true;
    }
}
