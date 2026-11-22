using System.IO;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

[TestClass]
[CoversNode("iocommon-encoding")]
public class EncodingDetectorTests
{
[TestMethod]
public void TextIsToldFromBytes_WideTextIncluded()
{
    Assert.IsTrue(EncodingDetector.HoldsText(System.Text.Encoding.UTF8.GetBytes("@echo off")));
    Assert.IsTrue(EncodingDetector.HoldsText(System.Text.Encoding.Unicode.GetBytes("wide text, full of NUL bytes")));
    Assert.IsTrue(EncodingDetector.HoldsText(System.Array.Empty<byte>()), "an empty file is an empty text");
    Assert.IsFalse(EncodingDetector.HoldsText(new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0xFF, 0, 0 }));
}

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
