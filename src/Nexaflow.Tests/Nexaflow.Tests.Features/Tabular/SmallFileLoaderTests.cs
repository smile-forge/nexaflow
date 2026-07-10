using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
[CoversNode("tabular-windowed-load")]
public class SmallFileLoaderTests
{
    private static string WriteTemp(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"small-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private static CsvShape SimpleCsv(bool hasHeader, int fields) => new()
    {
        Separator   = ',',
        Quote       = null,
        HasHeader   = hasHeader,
        ColumnTypes = Enumerable.Repeat(CsvDataType.String, fields).ToList(),
        FieldCount  = fields,
        Score       = 1f,
        Label       = "test",
        Description = "test",
    };

    [TestMethod]
    public void Load_WithHeader_SkipsHeaderRow()
    {
        var path = WriteTemp("name,age\nalice,30\nbob,42\n");
        try
        {
            var loader = new SmallFileLoader(path, Encoding.UTF8, 0, new DataShape(SimpleCsv(hasHeader: true, fields: 2), null));
            Assert.AreEqual(2, loader.KnownRowCount);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_FilterMatchesAll_ReturnsAllRows()
    {
        var path = WriteTemp("a,1\nb,2\nc,3\n");
        try
        {
            var loader = new SmallFileLoader(path, Encoding.UTF8, 0, new DataShape(SimpleCsv(hasHeader: false, fields: 2), null));
            var rows = loader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("a", rows[0].Cells[0]);
            Assert.AreEqual("3", rows[2].Cells[1]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_Filter_DropsNonMatchingRows()
    {
        var path = WriteTemp("a,1\nb,2\nc,3\n");
        try
        {
            var loader = new SmallFileLoader(path, Encoding.UTF8, 0, new DataShape(SimpleCsv(hasHeader: false, fields: 2), null));
            var rows = loader.GetVisibleAsync(0, 10, cells => cells[1] != "2", CancellationToken.None).Result;
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("a", rows[0].Cells[0]);
            Assert.AreEqual("c", rows[1].Cells[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void GetVisible_FromRowOffset_StartsAtThatIndex()
    {
        var path = WriteTemp("a,1\nb,2\nc,3\nd,4\n");
        try
        {
            var loader = new SmallFileLoader(path, Encoding.UTF8, 0, new DataShape(SimpleCsv(hasHeader: false, fields: 2), null));
            var rows = loader.GetVisibleAsync(2, 10, _ => true, CancellationToken.None).Result;
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("c", rows[0].Cells[0]);
            Assert.AreEqual(2,   rows[0].AbsoluteIndex);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Sort_RewritesUnderlyingRowOrder()
    {
        var path = WriteTemp("c,3\na,1\nb,2\n");
        try
        {
            var loader = new SmallFileLoader(path, Encoding.UTF8, 0, new DataShape(SimpleCsv(hasHeader: false, fields: 2), null));
            loader.Sort((x, y) => string.Compare(x[0], y[0], StringComparison.Ordinal));
            var rows = loader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            Assert.AreEqual("a", rows[0].Cells[0]);
            Assert.AreEqual("b", rows[1].Cells[0]);
            Assert.AreEqual("c", rows[2].Cells[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Load_MergeTransform_AppliesAtHydration()
    {
        var path = WriteTemp("first,last,age\nada,lovelace,36\nlinus,torvalds,55\n");
        try
        {
            var data = new DataShape(SimpleCsv(hasHeader: true, fields: 3), null);
            data.AddTransform(new MergeColumnsTransform([0, 1], " "));
            var loader = new SmallFileLoader(path, Encoding.UTF8, 0, data);
            var rows = loader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            Assert.AreEqual("ada lovelace", rows[0].Cells[0]);
            Assert.AreEqual("36",           rows[0].Cells[1]);
        }
        finally { File.Delete(path); }
    }
}
