using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class CommentLineTests
{
    private static string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tabular-comments-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    [TestMethod]
    public void LeadingHashLines_AreCollected_WhenTailHasNoHash()
    {
        var path = WriteTemp("# header comment 1\n# header comment 2\nname,age\nalice,30\nbob,42\ncarol,25\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            Assert.AreEqual(2,                       sample.LeadingCommentLines.Count);
            Assert.AreEqual("# header comment 1",    sample.LeadingCommentLines[0]);
            Assert.AreEqual("name,age",              sample.HeadLines[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void LeadingHash_NotStripped_WhenTailAlsoHasHash()
    {
        // The "#" char appears at both ends — it's part of the data, not a comment marker.
        var path = WriteTemp("#1,a\n#2,b\n#3,c\n#4,d\n#5,e\n#6,f\n#7,g\n#8,h\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            Assert.AreEqual(0, sample.LeadingCommentLines.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SmallFileLoader_SkipsLeadingCommentLines()
    {
        var path = WriteTemp("# comment\nname,age\nalice,30\nbob,42\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            var shapes = ShapeDetector.DetectAll(sample);
            var shape  = shapes[0];
            var raw    = shape.HasHeader && sample.HeadLines.Count > 0
                ? SmallFileLoader.TokenizeRow(sample.HeadLines[0], shape) : null;
            var data   = new DataShape(shape, raw, sample.LeadingCommentLines.Count);
            var loader = new SmallFileLoader(path, sample.Encoding, sample.BomByteCount, data);

            // Comment + header are both skipped; only data rows remain.
            Assert.AreEqual(2, loader.KnownRowCount);
            var window = loader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            Assert.AreEqual("alice", window[0].Cells[0]);
            Assert.AreEqual("bob",   window[1].Cells[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SmallFileLoader_NoLeadingComments_StartsAtRowZero()
    {
        // Regression: ensure the very first data row after the header is included in the
        // streamed window, not eaten by an off-by-one in the skip logic.
        var path = WriteTemp("name,age\nalice,30\nbob,42\ncarol,25\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            var shape  = ShapeDetector.DetectAll(sample)[0];
            var raw    = shape.HasHeader
                ? SmallFileLoader.TokenizeRow(sample.HeadLines[0], shape) : null;
            var data   = new DataShape(shape, raw, sample.LeadingCommentLines.Count);
            using var reader = new RowWindowReader(path, sample.Encoding, sample.BomByteCount, data);

            var rows = reader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(3,       rows.Count);
            Assert.AreEqual("alice", rows[0].Cells[0],
                "First data row was eaten by an off-by-one in the comment/header skip path.");
            Assert.AreEqual(0,       rows[0].AbsoluteIndex);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void RowWindowReader_SkipsCommentsAndHeader_AtRow0()
    {
        var path = WriteTemp("# comment line one\n# comment line two\nname,age\nalice,30\nbob,42\ncarol,25\n");
        try
        {
            var sample = LineSamplingReader.Read(path);
            var shapes = ShapeDetector.DetectAll(sample);
            var shape  = shapes[0];
            var raw    = shape.HasHeader && sample.HeadLines.Count > 0
                ? SmallFileLoader.TokenizeRow(sample.HeadLines[0], shape) : null;
            var data   = new DataShape(shape, raw, sample.LeadingCommentLines.Count);

            using var reader = new RowWindowReader(path, sample.Encoding, sample.BomByteCount, data);
            var rows = reader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(3,         rows.Count);
            Assert.AreEqual("alice",   rows[0].Cells[0]);
            Assert.AreEqual(0,         rows[0].AbsoluteIndex);  // data row 0, not file row 0
        }
        finally { File.Delete(path); }
    }
}
