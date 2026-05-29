using System.IO;
using System.Text;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class EncodingDetectorTests
{
    [TestMethod]
    public void Detect_Utf8Bom_ReturnsUtf8WithBom()
    {
        var bytes = (byte[])[ 0xEF, 0xBB, 0xBF, (byte)'a', (byte)'b' ];
        using var ms = new MemoryStream(bytes);
        var probe = EncodingDetector.Detect(ms);
        Assert.IsTrue(probe.HadBom);
        Assert.AreEqual(3, probe.BomByteCount);
        Assert.AreEqual(Encoding.UTF8, probe.Encoding);
    }

    [TestMethod]
    public void Detect_Utf16LeBom_ReturnsUnicodeWithBom()
    {
        var bytes = (byte[])[ 0xFF, 0xFE, (byte)'a', 0x00 ];
        using var ms = new MemoryStream(bytes);
        var probe = EncodingDetector.Detect(ms);
        Assert.IsTrue(probe.HadBom);
        Assert.AreEqual(2, probe.BomByteCount);
        Assert.AreEqual(Encoding.Unicode, probe.Encoding);
    }

    [TestMethod]
    public void Detect_Utf16BeBom_ReturnsBigEndianUnicodeWithBom()
    {
        var bytes = (byte[])[ 0xFE, 0xFF, 0x00, (byte)'a' ];
        using var ms = new MemoryStream(bytes);
        var probe = EncodingDetector.Detect(ms);
        Assert.IsTrue(probe.HadBom);
        Assert.AreEqual(2, probe.BomByteCount);
        Assert.AreEqual(Encoding.BigEndianUnicode, probe.Encoding);
    }

    [TestMethod]
    public void Detect_PlainAscii_NoBom_ReturnsUtf8()
    {
        var bytes = Encoding.ASCII.GetBytes("name,age\nalice,30\nbob,42\n");
        using var ms = new MemoryStream(bytes);
        var probe = EncodingDetector.Detect(ms);
        Assert.IsFalse(probe.HadBom);
        Assert.AreEqual(0, probe.BomByteCount);
        Assert.AreEqual(Encoding.UTF8, probe.Encoding);
    }

    [TestMethod]
    public void Detect_HighBytesInvalidUtf8_FallsBackToLatin1()
    {
        // 0xC3 alone (without a valid continuation) is invalid UTF-8.
        var bytes = (byte[])[ (byte)'a', 0xC3, 0x28, (byte)'b' ];
        using var ms = new MemoryStream(bytes);
        var probe = EncodingDetector.Detect(ms);
        Assert.IsFalse(probe.HadBom);
        Assert.AreEqual(Encoding.Latin1, probe.Encoding);
    }
}
