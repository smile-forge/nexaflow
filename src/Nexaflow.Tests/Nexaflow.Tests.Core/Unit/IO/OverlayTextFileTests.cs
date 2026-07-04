using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Core.Unit.IO;

/// <summary>
/// Covers <see cref="OverlayTextFile"/> — the windowed-read + change-block-overlay + streaming-merge
/// engine. Asserts byte-exact merges, that only edited bytes reach the add-buffer (the "change file
/// holds only the edited pages" guarantee), correct line-count/offset math across insert/delete, BOM
/// preservation, overlay-aware windowed reads, and edit-aware find/replace.
/// </summary>
[TestClass]
public class OverlayTextFileTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"nexa-overlay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<(OverlayTextFile file, string original, string addPath)> Open(
        string content, Encoding enc, int linesPerPage = 4)
    {
        var path    = Path.Combine(_dir, $"src_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content, enc);
        var addPath = path + ".nexedit.tmp";
        var file    = await OverlayTextFile.OpenAsync(path, enc, linesPerPage, addPath, CancellationToken.None);
        return (file, path, addPath);
    }

    private static async Task<byte[]> Save(OverlayTextFile file)
    {
        using var ms = new MemoryStream();
        await file.SaveAsync(ms, CancellationToken.None);
        return ms.ToArray();
    }

    private static byte[] Expected(Encoding enc, string content)
    {
        var pre  = enc.GetPreamble();
        var body = enc.GetBytes(content);
        return pre.Length == 0 ? body : [.. pre, .. body];
    }

    [TestMethod]
    public async Task NoEdits_SaveReproducesOriginalByteForByte()
    {
        var enc = new UTF8Encoding(true); // with BOM
        var (file, path, _) = await Open("alpha\nbeta\ngamma\n", enc);
        using (file)
        {
            CollectionAssert.AreEqual(File.ReadAllBytes(path), await Save(file));
            Assert.IsFalse(file.IsDirty);
        }
    }

    [TestMethod]
    public async Task SingleAsciiEdit_SplicesAndBuffersOnlyInsertedBytes()
    {
        var enc = new UTF8Encoding(false);
        var (file, _, addPath) = await Open("line0\nline1\nline2\n", enc);
        using (file)
        {
            // Replace "line1" (5 bytes at offset 6) with "EDITED" — ASCII so byte == char offsets.
            file.ApplyEdit(curByteOffset: 6, bytesRemoved: 5, insertedText: "EDITED");

            Assert.IsTrue(file.IsDirty);
            CollectionAssert.AreEqual(Expected(enc, "line0\nEDITED\nline2\n"), await Save(file));
            Assert.AreEqual(6, new FileInfo(addPath).Length, "add-buffer holds only the inserted bytes");
        }
    }

    [TestMethod]
    public async Task TwoFarApartEdits_BulkCopiesUnchangedAndBuffersOnlyEdits()
    {
        var enc = new UTF8Encoding(false);
        var sb  = new StringBuilder();
        const int lines = 40_000;                       // ~1 MB
        for (int i = 0; i < lines; i++) sb.Append("row ").Append(i.ToString("D6")).Append('\n');
        var (file, _, addPath) = await Open(sb.ToString(), enc, linesPerPage: 1000);
        using (file)
        {
            await file.ReplaceLinesAsync(1, 1, "ROW ONE EDITED\n");
            await file.ReplaceLinesAsync(lines - 2, lines - 2, "ROW LAST EDITED\n");

            var outBytes = await Save(file);
            var text = enc.GetString(outBytes);
            StringAssert.Contains(text, "\nROW ONE EDITED\n");
            StringAssert.Contains(text, "\nROW LAST EDITED\n");
            StringAssert.Contains(text, "\nrow 020000\n");                 // a middle line is untouched
            Assert.IsFalse(text.Contains("row 000001\n"), "line 1 was replaced");

            Assert.IsTrue(new FileInfo(addPath).Length < 100,
                "add-buffer holds only the two edited lines, not the file");
        }
    }

    [TestMethod]
    public async Task InsertAddingLines_GrowsTotalLines()
    {
        var enc = new UTF8Encoding(false);
        var (file, _, _) = await Open("a\nb\nc\nd\n", enc); // lines: a,b,c,d,"" = 5
        using (file)
        {
            Assert.AreEqual(5, file.TotalLines);
            await file.ReplaceLinesAsync(1, 1, "X\nY\n");   // replace "b" with two lines
            Assert.AreEqual(6, file.TotalLines);
            CollectionAssert.AreEqual(Expected(enc, "a\nX\nY\nc\nd\n"), await Save(file));
        }
    }

    [TestMethod]
    public async Task DeleteRemovingLines_ShrinksTotalLines()
    {
        var enc = new UTF8Encoding(false);
        var (file, _, _) = await Open("a\nb\nc\nd\n", enc);
        using (file)
        {
            await file.ReplaceLinesAsync(1, 2, "");        // remove "b\nc\n"
            Assert.AreEqual(3, file.TotalLines);           // a, d, ""
            CollectionAssert.AreEqual(Expected(enc, "a\nd\n"), await Save(file));
        }
    }

    [TestMethod]
    public async Task EditAtEof_NoTrailingByteLoss()
    {
        var enc = new UTF8Encoding(false);
        var (file, _, _) = await Open("a\nb", enc);        // 2 lines, no trailing newline
        using (file)
        {
            await file.ReplaceLinesAsync(1, 1, "bb");      // replace last line "b" -> "bb"
            CollectionAssert.AreEqual(Expected(enc, "a\nbb"), await Save(file));
        }
    }

    [TestMethod]
    public async Task ReadWindow_IsOverlayAwareAfterEdit()
    {
        var enc = new UTF8Encoding(false);
        var sb  = new StringBuilder();
        for (int i = 0; i < 200; i++) sb.Append("orig ").Append(i.ToString("D3")).Append('\n');
        var (file, _, _) = await Open(sb.ToString(), enc, linesPerPage: 16);
        using (file)
        {
            await file.ReplaceLinesAsync(100, 100, "CHANGED-100\n");

            var win = await file.ReadWindowAsync(98, 5, CancellationToken.None);
            StringAssert.Contains(win.Text, "orig 099\n");
            StringAssert.Contains(win.Text, "CHANGED-100\n");
            StringAssert.Contains(win.Text, "orig 101\n");
            Assert.IsFalse(win.Text.Contains("orig 100"), "line 100 now shows the edit");

            var far = await file.ReadWindowAsync(10, 3, CancellationToken.None);
            StringAssert.Contains(far.Text, "orig 010\n");
        }
    }

    [TestMethod]
    public async Task Find_ReflectsPendingEdits()
    {
        var enc = new UTF8Encoding(false);
        var (file, _, _) = await Open("apple\nbanana\ncherry\n", enc);
        using (file)
        {
            // Remove the only "banana", add a "berry".
            await file.ReplaceLinesAsync(1, 1, "berry\n");

            var berry  = await file.FindAsync("berry",  isRegex: false, caseSensitive: false, 0, file.TotalLines, 100, CancellationToken.None);
            var banana = await file.FindAsync("banana", isRegex: false, caseSensitive: false, 0, file.TotalLines, 100, CancellationToken.None);

            Assert.AreEqual(1, berry.Count);
            Assert.AreEqual(1, berry[0].Line);
            Assert.AreEqual(0, banana.Count, "the edited-away match is gone");
        }
    }

    [TestMethod]
    public async Task ReplaceAll_WholeFileAndPageScoped()
    {
        var enc = new UTF8Encoding(false);

        var (whole, _, _) = await Open("a x\nb x\nc x\n", enc);
        using (whole)
        {
            int n = await whole.ReplaceAsync("x", "Y", isRegex: false, caseSensitive: false, 0, whole.TotalLines, CancellationToken.None);
            Assert.AreEqual(3, n);
            CollectionAssert.AreEqual(Expected(enc, "a Y\nb Y\nc Y\n"), await Save(whole));
        }

        var (scoped, _, _) = await Open("a x\nb x\nc x\n", enc);
        using (scoped)
        {
            int n = await scoped.ReplaceAsync("x", "Y", isRegex: false, caseSensitive: false, 1, 1, CancellationToken.None);
            Assert.AreEqual(1, n);
            CollectionAssert.AreEqual(Expected(enc, "a x\nb Y\nc x\n"), await Save(scoped));
        }
    }

    [TestMethod]
    public async Task BomPreserved_AfterEdit()
    {
        var enc = new UTF8Encoding(true); // UTF-8 BOM
        var (file, _, _) = await Open("héllo\nwörld\n", enc);
        using (file)
        {
            await file.ReplaceLinesAsync(1, 1, "MÜNCHEN\n");
            var outBytes = await Save(file);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, outBytes.Take(3).ToArray());
            CollectionAssert.AreEqual(Expected(enc, "héllo\nMÜNCHEN\n"), outBytes);
        }
    }

    [TestMethod]
    public async Task Utf16_EditRoundTrips()
    {
        var enc = Encoding.Unicode; // UTF-16 LE + BOM
        var (file, _, _) = await Open("alpha\nbeta\ngamma\n", enc);
        using (file)
        {
            await file.ReplaceLinesAsync(0, 0, "ALPHA-EDITED\n");
            CollectionAssert.AreEqual(Expected(enc, "ALPHA-EDITED\nbeta\ngamma\n"), await Save(file));
        }
    }

    [TestMethod]
    public async Task DisposeDeletesAddBuffer()
    {
        var enc = new UTF8Encoding(false);
        var (file, _, addPath) = await Open("a\nb\n", enc);
        file.ApplyEdit(0, 1, "A");
        Assert.IsTrue(File.Exists(addPath));
        file.Dispose();
        Assert.IsFalse(File.Exists(addPath), "add-buffer temp is removed on dispose");
    }
}
