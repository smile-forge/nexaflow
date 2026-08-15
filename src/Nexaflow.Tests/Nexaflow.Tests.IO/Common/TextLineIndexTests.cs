using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// Covers <see cref="TextLineIndex"/> — the encoding-correct sparse page index. Validates exact line
/// counting and byte offsets across UTF-8/16/32 + single-byte, BOM handling, CRLF, and multibyte chars
/// straddling read boundaries. Offset correctness is checked by seeking to a page checkpoint and
/// decoding the line there back to its expected text.
/// </summary>
[TestClass]
[CoversNode("text-viewer-windowing")]
public class TextLineIndexTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nexa-lineindex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Write(string name, string content, Encoding enc)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content, enc); // emits the encoding's preamble (BOM) when it has one
        return path;
    }

    private static async Task<TextLineIndex> BuildAsync(string path, Encoding enc, int linesPerPage)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await TextLineIndex.BuildAsync(fs, enc, linesPerPage, CancellationToken.None);
    }

    // Reads the single line that begins at the given page checkpoint, via the index's byte offset.
    private static string ReadLineAtPage(string path, TextLineIndex idx, long page)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(idx.PageStartByte(page), SeekOrigin.Begin);
        using var sr = new StreamReader(fs, idx.Encoding, detectEncodingFromByteOrderMarks: false);
        return sr.ReadLine() ?? string.Empty;
    }

    [TestMethod]
    public async Task Ascii_NoTrailingNewline_CountsLinesAndOffsets()
    {
        var enc  = new UTF8Encoding(false);
        var path = Write("a.txt", "line0\nline1\nline2", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 1);

        Assert.AreEqual(3, idx.TotalLines);
        Assert.AreEqual(0, idx.BomByteCount);
        Assert.AreEqual(new FileInfo(path).Length, idx.TotalBytes);
        Assert.AreEqual("line0", ReadLineAtPage(path, idx, 0));
        Assert.AreEqual("line1", ReadLineAtPage(path, idx, 1));
        Assert.AreEqual("line2", ReadLineAtPage(path, idx, 2));
    }

    [TestMethod]
    public async Task TrailingNewline_YieldsFinalEmptyLine()
    {
        var enc  = new UTF8Encoding(false);
        var path = Write("b.txt", "a\nb\n", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 4);

        Assert.AreEqual(3, idx.TotalLines, "trailing \\n adds an empty final line");
    }

    [TestMethod]
    public async Task EmptyFile_IsOneLine()
    {
        var enc  = new UTF8Encoding(false);
        var path = Write("empty.txt", "", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 4);

        Assert.AreEqual(1, idx.TotalLines);
        Assert.AreEqual(0, idx.TotalBytes);
    }

    [TestMethod]
    public async Task Utf8Bom_SkipsBomAndReadsLineZero()
    {
        var enc  = new UTF8Encoding(true);
        var path = Write("bom.txt", "héllo\nwörld", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 1);

        Assert.AreEqual(3, idx.BomByteCount);
        Assert.AreEqual(2, idx.TotalLines);
        Assert.AreEqual("héllo", ReadLineAtPage(path, idx, 0), "line 0 offset is past the BOM");
        Assert.AreEqual("wörld", ReadLineAtPage(path, idx, 1));
    }

    [TestMethod]
    public async Task Utf16Le_DetectsNewlinesNotRawBytes()
    {
        // The whole point: a raw 0x0A scan mis-fires on UTF-16. Decoded-char counting is correct.
        var enc  = Encoding.Unicode; // UTF-16 LE + BOM
        var path = Write("u16le.txt", "alpha\nbeta\ngamma\ndelta", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 2);

        Assert.AreEqual(2, idx.BomByteCount);
        Assert.AreEqual(4, idx.TotalLines);
        Assert.AreEqual("alpha", ReadLineAtPage(path, idx, 0));
        Assert.AreEqual("gamma", ReadLineAtPage(path, idx, 1)); // page 1 == line 2
    }

    [TestMethod]
    public async Task Utf16Be_CountsAndOffsets()
    {
        var enc  = Encoding.BigEndianUnicode;
        var path = Write("u16be.txt", "one\ntwo\nthree", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 1);

        Assert.AreEqual(2, idx.BomByteCount);
        Assert.AreEqual(3, idx.TotalLines);
        Assert.AreEqual("three", ReadLineAtPage(path, idx, 2));
    }

    [TestMethod]
    public async Task Utf32Le_CountsAndOffsets()
    {
        var enc  = Encoding.UTF32;
        var path = Write("u32.txt", "x\ny\nz", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 1);

        Assert.AreEqual(4, idx.BomByteCount);
        Assert.AreEqual(3, idx.TotalLines);
        Assert.AreEqual("y", ReadLineAtPage(path, idx, 1));
    }

    [TestMethod]
    public async Task Latin1_HighBytes()
    {
        var enc  = Encoding.Latin1;
        var path = Write("latin.txt", "café\nrésumé", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 1);

        Assert.AreEqual(0, idx.BomByteCount);
        Assert.AreEqual(2, idx.TotalLines);
        Assert.AreEqual("café", ReadLineAtPage(path, idx, 0));
        Assert.AreEqual("résumé", ReadLineAtPage(path, idx, 1));
    }

    [TestMethod]
    public async Task Crlf_KeepsCarriageReturnInLine()
    {
        var enc  = new UTF8Encoding(false);
        var path = Write("crlf.txt", "a\r\nb\r\nc", enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 1);

        Assert.AreEqual(3, idx.TotalLines);
        // \r stays in the byte stream (only \n splits): line 0's bytes are [offset0, offset1) = "a\r\n".
        var all = File.ReadAllBytes(path);
        long o0 = idx.PageStartByte(0), o1 = idx.PageStartByte(1);
        var line0 = Encoding.UTF8.GetString(all, (int)o0, (int)(o1 - o0));
        Assert.AreEqual("a\r\n", line0);
        Assert.AreEqual('b', (char)all[o1]); // line 1 begins right after the first \n
    }

    [TestMethod]
    public async Task MultibyteAcrossReadBoundary_StaysCorrect()
    {
        // Build > 64 KB of UTF-8 with a 3-byte char per line so a code point straddles the 64 KB read
        // buffer; verify exact line count and a sampled mid-file line.
        var enc = new UTF8Encoding(false);
        var sb  = new StringBuilder();
        const int lineCount = 6000;
        for (int i = 0; i < lineCount; i++)
            sb.Append("€ euro line ").Append(i).Append('\n'); // '€' = 0xE2 0x82 0xAC
        var path = Write("multibyte.txt", sb.ToString(), enc);

        var idx = await BuildAsync(path, enc, linesPerPage: 500);

        Assert.AreEqual(lineCount + 1, idx.TotalLines); // trailing newline → final empty line
        Assert.AreEqual(new FileInfo(path).Length, idx.TotalBytes);
        Assert.AreEqual("€ euro line 2500", ReadLineAtPage(path, idx, 5)); // page 5 == line 2500
        Assert.AreEqual("€ euro line 4000", ReadLineAtPage(path, idx, 8)); // page 8 == line 4000
    }
}
