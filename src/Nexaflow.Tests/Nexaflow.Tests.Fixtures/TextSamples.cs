using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Plain-text fixtures for the text reader and the encoding/BOM detector: short files in each of
/// the BOM-bearing encodings (UTF-8, UTF-16 LE/BE, UTF-32 LE) plus a BOM-less UTF-8 file, an empty
/// file, and two long files (LF and CRLF) that exceed a single display window so the head-first
/// streaming/windowing path is exercised.
///
/// Every entry is a <see cref="SampleFile.Raw"/> sample: BOM bytes and exact line endings are the
/// whole point, so nothing here may be normalised.
/// </summary>
internal sealed class TextSamples : ISampleSet
{
    public string SubDirectory => "text";

    private const string ShortBody =
        "The quick brown fox jumps over the lazy dog.\n" +
        "Pack my box with five dozen liquor jugs.\n" +
        "Sphinx of black quartz, judge my vow.\n" +
        "Café crème — naïve façade, jalapeño piñata. ☕\n" +   // non-ASCII to make the encoding matter
        "How vexingly quick daft zebras jump!\n";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        // BOM-less UTF-8 (LF).
        SampleFile.Raw("short_utf8_nobom.txt", Encoding.UTF8.GetBytes(ShortBody)),

        // UTF-8 with BOM (EF BB BF).
        SampleFile.Raw("short_utf8_bom.txt", WithBom(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ShortBody)),

        // UTF-16 LE with BOM (FF FE).
        SampleFile.Raw("short_utf16le_bom.txt", WithBom(new UnicodeEncoding(bigEndian: false, byteOrderMark: true), ShortBody)),

        // UTF-16 BE with BOM (FE FF).
        SampleFile.Raw("short_utf16be_bom.txt", WithBom(new UnicodeEncoding(bigEndian: true, byteOrderMark: true), ShortBody)),

        // UTF-32 LE with BOM (FF FE 00 00).
        SampleFile.Raw("short_utf32le_bom.txt", WithBom(new UTF32Encoding(bigEndian: false, byteOrderMark: true), ShortBody)),

        // Empty file (0 bytes) — degenerate streaming case.
        SampleFile.Raw("empty.txt", []),

        // Long, LF, BOM-less UTF-8.
        SampleFile.Raw("long_lf.txt", Encoding.UTF8.GetBytes(BuildLong("\n"))),

        // Long, CRLF, BOM-less UTF-8.
        SampleFile.Raw("long_crlf.txt", Encoding.UTF8.GetBytes(BuildLong("\r\n"))),
    ];

    private static byte[] WithBom(Encoding enc, string body)
    {
        var preamble = enc.GetPreamble();
        var text     = enc.GetBytes(body);
        var bytes    = new byte[preamble.Length + text.Length];
        preamble.CopyTo(bytes, 0);
        text.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    /// <summary>2,000 numbered lines joined by <paramref name="newline"/>, ending with one terminator.</summary>
    private static string BuildLong(string newline)
    {
        var sb = new StringBuilder(2000 * 40);
        for (int i = 1; i <= 2000; i++)
            sb.Append("Line ").Append(i).Append(" — lorem ipsum dolor sit amet consectetur.").Append(newline);
        return sb.ToString();
    }
}
