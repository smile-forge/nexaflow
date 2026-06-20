using System.IO;
using System.Linq;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Smoke coverage for the non-tabular generated sample sets (text / json / logs / binary):
/// confirms each set materialises on disk and that the byte-exact fixtures are correct —
/// in particular that the BOM-bearing text files are detected as the right encoding by
/// <see cref="EncodingDetector"/>.
/// </summary>
[TestClass]
public class GeneratedSampleFilesTests
{
    [TestMethod]
    [DataRow("text")]
    [DataRow("json")]
    [DataRow("logs")]
    [DataRow("binary")]
    public void Set_MaterialisesEveryFile(string subDir)
    {
        var files = TestSampleData.Files(subDir);
        Assert.IsTrue(files.Count > 0, $"no files registered for '{subDir}'");
        foreach (var path in files)
            Assert.IsTrue(File.Exists(path), $"missing generated sample: {path}");
    }

    [TestMethod]
    [DataRow("short_utf8_nobom.txt", "utf-8",  false, 0)]
    [DataRow("short_utf8_bom.txt",   "utf-8",  true,  3)]
    [DataRow("short_utf16le_bom.txt","utf-16", true,  2)]   // Encoding.Unicode
    [DataRow("short_utf16be_bom.txt","utf-16BE", true, 2)]  // Encoding.BigEndianUnicode
    [DataRow("short_utf32le_bom.txt","utf-32", true,  4)]
    public void Text_BomFixtures_DetectExpectedEncoding(
        string name, string expectedWebName, bool expectBom, int expectBomBytes)
    {
        var path = TestSampleData.Path("text", name);
        using var fs = File.OpenRead(path);
        var probe = EncodingDetector.Detect(fs);

        Assert.AreEqual(expectedWebName, probe.Encoding.WebName);
        Assert.AreEqual(expectBom,       probe.HadBom);
        Assert.AreEqual(expectBomBytes,  probe.BomByteCount);
    }

    [TestMethod]
    public void Text_EmptyFixture_IsZeroBytes()
    {
        Assert.AreEqual(0, new FileInfo(TestSampleData.Path("text", "empty.txt")).Length);
    }

    [TestMethod]
    public void Text_LongFixtures_HaveDistinctLineEndings()
    {
        var lf   = File.ReadAllText(TestSampleData.Path("text", "long_lf.txt"));
        var crlf = File.ReadAllText(TestSampleData.Path("text", "long_crlf.txt"));

        Assert.IsFalse(lf.Contains('\r'),  "long_lf.txt should use bare LF.");
        Assert.IsTrue(crlf.Contains("\r\n"), "long_crlf.txt should use CRLF.");
        Assert.IsTrue(lf.Split('\n').Length > 1000, "long file should be many lines for streaming.");
    }

    [TestMethod]
    public void Binary_Fixtures_HaveExpectedContent()
    {
        Assert.AreEqual(4096, new FileInfo(TestSampleData.Path("binary", "random.bin")).Length);

        var zeros = File.ReadAllBytes(TestSampleData.Path("binary", "zeros.bin"));
        Assert.AreEqual(1024, zeros.Length);
        Assert.IsTrue(zeros.All(b => b == 0), "zeros.bin must be all-zero.");

        var png = File.ReadAllBytes(TestSampleData.Path("binary", "png_header.bin"));
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        CollectionAssert.AreEqual(signature, png.Take(8).ToArray(), "png_header.bin must start with the PNG signature.");
    }
}
