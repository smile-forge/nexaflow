using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class RowWindowReaderTests
{
    private static string WriteRows(int count, bool header)
    {
        var sb = new StringBuilder();
        if (header) sb.AppendLine("name,value");
        for (int i = 0; i < count; i++) sb.Append("name").Append(i).Append(',').Append(i).Append('\n');
        var path = Path.Combine(Path.GetTempPath(), $"window-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static CsvShape Shape(bool header) => new()
    {
        Separator   = ',',
        Quote       = null,
        HasHeader   = header,
        ColumnTypes = [CsvDataType.String, CsvDataType.Integer],
        FieldCount  = 2,
        Score       = 1f,
        Label       = "test",
        Description = "test",
    };

    [TestMethod]
    public void GetVisible_FromMiddle_ReturnsExactlyRequestedRows()
    {
        var path = WriteRows(1000, header: false);
        try
        {
            var reader = new RowWindowReader(path, Encoding.UTF8, 0, new DataShape(Shape(false), null));
            var rows = reader.GetVisibleAsync(400, 150, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(150, rows.Count);
            Assert.AreEqual(400, rows[0].AbsoluteIndex);
            Assert.AreEqual(549, rows[^1].AbsoluteIndex);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_FilterApplied_KeepsScanningUntil150Visible()
    {
        var path = WriteRows(1000, header: false);
        try
        {
            var reader = new RowWindowReader(path, Encoding.UTF8, 0, new DataShape(Shape(false), null));
            // Keep only even-indexed rows (value is the index, "0","1",…).
            bool Filter(string[] cells) => int.Parse(cells[1]) % 2 == 0;
            var rows = reader.GetVisibleAsync(0, 150, Filter, CancellationToken.None).Result;
            Assert.AreEqual(150, rows.Count);
            // Even-only filter starting at 0 → 0, 2, 4, … 298.
            Assert.AreEqual(0,   rows[0].AbsoluteIndex);
            Assert.AreEqual(298, rows[^1].AbsoluteIndex);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_HeaderRow_NotEmittedAndDoesNotShiftAbsoluteIndex()
    {
        var path = WriteRows(10, header: true);
        try
        {
            var reader = new RowWindowReader(path, Encoding.UTF8, 0, new DataShape(Shape(true), null));
            var rows = reader.GetVisibleAsync(0, 50, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(10, rows.Count);
            Assert.AreEqual(0,  rows[0].AbsoluteIndex);
            Assert.AreEqual("name0", rows[0].Cells[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_NearEnd_StopsCleanlyAtEof()
    {
        var path = WriteRows(100, header: false);
        try
        {
            var reader = new RowWindowReader(path, Encoding.UTF8, 0, new DataShape(Shape(false), null));
            var rows = reader.GetVisibleAsync(90, 150, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(10, rows.Count);
            Assert.AreEqual(99, rows[^1].AbsoluteIndex);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_AnchorReuseAfterFirstScan_KeepsRowIndicesAligned()
    {
        // Regression: anchors used to be written AFTER reading a row, recording the file
        // position of row N+1 against rowIdx N. The second scan would then label every
        // row one too early, and partial reads near EOF would under-count by 1 — which
        // triggers the EOF-clamp to keep shrinking KnownRowCount on every wheel tick and
        // ultimately blanks the viewport.
        var path = WriteRows(1000, header: false);
        try
        {
            var reader = new RowWindowReader(path, Encoding.UTF8, 0, new DataShape(Shape(false), null));

            // First scan from 0 writes anchors at 200, 400, 600, 800.
            var first = reader.GetVisibleAsync(0, 1000, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(1000,    first.Count);
            Assert.AreEqual(0,       first[0].AbsoluteIndex);
            Assert.AreEqual(999,     first[^1].AbsoluteIndex);
            Assert.AreEqual("name0", first[0].Cells[0]);
            Assert.AreEqual("name999", first[^1].Cells[0]);

            // Second scan from a row that *exactly* corresponds to a written anchor.
            // The first row returned must carry that row's content, not the next row's.
            var atAnchor = reader.GetVisibleAsync(600, 5, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(5,         atAnchor.Count);
            Assert.AreEqual(600,       atAnchor[0].AbsoluteIndex);
            Assert.AreEqual("name600", atAnchor[0].Cells[0],
                "Anchor-based scan returned the wrong row's content — anchor off-by-one.");
            Assert.AreEqual("name604", atAnchor[^1].Cells[0]);

            // And a partial read at EOF must return the actual last row.
            var atEof = reader.GetVisibleAsync(995, 100, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(5,         atEof.Count);
            Assert.AreEqual("name995", atEof[0].Cells[0]);
            Assert.AreEqual("name999", atEof[^1].Cells[0]);
            Assert.AreEqual(999,       atEof[^1].AbsoluteIndex);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_AlreadyCancelledToken_ReturnsPartial_DoesNotThrow()
    {
        var path = WriteRows(2000, header: false);
        try
        {
            var reader = new RowWindowReader(path, Encoding.UTF8, 0, new DataShape(Shape(false), null));
            using var cts = new CancellationTokenSource();
            cts.Cancel();  // already cancelled before the call
            // Must not throw OperationCanceledException.
            var rows = reader.GetVisibleAsync(0, 150, _ => true, cts.Token).Result;
            // Either zero or some early rows — never an exception.
            Assert.IsTrue(rows.Count <= 150);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_RepeatedCalls_ReuseAnchorsAndProduceConsistentResults()
    {
        var path = WriteRows(5000, header: false);
        try
        {
            var reader = new RowWindowReader(path, Encoding.UTF8, 0, new DataShape(Shape(false), null));
            var a = reader.GetVisibleAsync(2000, 150, _ => true, CancellationToken.None).Result;
            var b = reader.GetVisibleAsync(2000, 150, _ => true, CancellationToken.None).Result;
            CollectionAssert.AreEqual(a.Select(r => r.AbsoluteIndex).ToArray(),
                                      b.Select(r => r.AbsoluteIndex).ToArray());
        }
        finally { File.Delete(path); }
    }
}
