using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class LineSamplingReaderTests
{
    private static string WriteTemp(string contents, Encoding? enc = null, byte[]? bomBytes = null)
    {
        enc ??= new UTF8Encoding(false);
        var path = Path.Combine(Path.GetTempPath(), $"tabular-sampling-{Guid.NewGuid():N}.txt");
        using var fs = File.OpenWrite(path);
        if (bomBytes is not null) fs.Write(bomBytes, 0, bomBytes.Length);
        var bytes = enc.GetBytes(contents);
        fs.Write(bytes, 0, bytes.Length);
        return path;
    }

    [TestMethod]
    public void Read_TenLineFile_ReturnsFirst4AndLast4()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"row{i}"));
        var path  = WriteTemp(lines + "\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            CollectionAssert.AreEqual(new[] { "row1", "row2", "row3", "row4" }, sample.HeadLines.ToArray());
            CollectionAssert.AreEqual(new[] { "row7", "row8", "row9", "row10" }, sample.TailLines.ToArray());
            Assert.AreEqual(LineEndingKind.Lf, sample.LineEnding);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Read_CrlfFile_DetectsCrlfLineEnding()
    {
        var path = WriteTemp("a\r\nb\r\nc\r\nd\r\ne\r\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            Assert.AreEqual(LineEndingKind.Crlf, sample.LineEnding);
            Assert.AreEqual("a", sample.HeadLines[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Read_Utf8BomFile_ReportsBomAndCorrectEncoding()
    {
        var path = WriteTemp("name\nx\ny\n", new UTF8Encoding(false), bomBytes: [0xEF, 0xBB, 0xBF]);
        try
        {
            var sample = LineSamplingReader.Read(path);
            Assert.IsTrue(sample.HadBom);
            Assert.AreEqual(3, sample.BomByteCount);
            Assert.AreEqual(Encoding.UTF8, sample.Encoding);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Read_ShortFile_HeadAndTailMayOverlap()
    {
        var path = WriteTemp("only\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            Assert.IsTrue(sample.HeadLines.Count >= 1);
            Assert.AreEqual("only", sample.HeadLines[0]);
        }
        finally { File.Delete(path); }
    }
}
