using Nexaflow.IO.Common;
using System.Linq;
using System.Threading;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular;

/// <summary>
/// End-to-end detection + streaming tests over the generated <c>tabular/</c> sample set
/// (see <see cref="TestSampleData"/>). The fixtures are materialised (and cached) on first
/// access, so these run on any machine with no external sample directory — replacing the old
/// tests that read a hand-curated, machine-local sample folder.
/// </summary>
[TestClass]
public class SampleFileDetectionTests
{
    private static string Sample(string name) => TestSampleData.Path("tabular", name);

    private static async Task<(FileSample sample, CsvShape top)> ProbeAsync(string path)
    {
        var sample = await LineSamplingReader.ReadAsync(VirtualFileSystem.Instance, path);
        var shapes = ShapeDetector.DetectAll(sample);
        return (sample, shapes[0]);
    }

    // ── comma_header.csv: comma, header, Integer/String/Decimal/Boolean columns ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    public async Task CommaHeader_DetectedAsCommaWithHeader_AndColumnTypes()
    {
        var (_, top) = await ProbeAsync(Sample("comma_header.csv"));
        Assert.AreEqual(',', top.Separator);
        Assert.AreEqual(4,   top.FieldCount);
        Assert.IsTrue(top.HasHeader);
        Assert.AreEqual(CsvDataType.Integer, top.ColumnTypes[0]);
        Assert.AreEqual(CsvDataType.String,  top.ColumnTypes[1]);
        Assert.AreEqual(CsvDataType.Decimal, top.ColumnTypes[2]);
        Assert.AreEqual(CsvDataType.Boolean, top.ColumnTypes[3]);
    }

    // ── tab_no_header.tsv: tab-separated, 2 string columns, no header ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    public async Task TabNoHeader_DetectedAsTabSeparated_NoHeader()
    {
        var (_, top) = await ProbeAsync(Sample("tab_no_header.tsv"));
        Assert.AreEqual('\t', top.Separator);
        Assert.AreEqual(2,    top.FieldCount);
        Assert.IsFalse(top.HasHeader);
    }

    // ── semicolon_dates.csv: semicolon, header, a dd.MM.yyyy HH:mm:ss DateTime column ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    public async Task SemicolonDates_DetectedAsSemicolonWithHeader_AndDateTimeColumn()
    {
        var (_, top) = await ProbeAsync(Sample("semicolon_dates.csv"));
        Assert.AreEqual(';', top.Separator);
        Assert.AreEqual(7,   top.FieldCount);
        Assert.IsTrue(top.HasHeader, "Header labels over typed columns should flag HasHeader.");
        Assert.AreEqual(CsvDataType.DateTime, top.ColumnTypes[4],
            "Column 5 (StartDateTime, dd.MM.yyyy HH:mm:ss) should classify as DateTime.");
    }

    // ── single_column.csv: one numeric column, no separator ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    public async Task SingleColumn_FallsBackToSingleColumnShape()
    {
        var (_, top) = await ProbeAsync(Sample("single_column.csv"));
        Assert.AreEqual(1, top.FieldCount);
        Assert.IsNull(top.Separator);
    }

    // ── comma_space_header.csv: comma+space, header, many columns ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    public async Task CommaSpaceHeader_DetectedAsCommaWithHeader_ManyColumns()
    {
        var (_, top) = await ProbeAsync(Sample("comma_space_header.csv"));
        Assert.AreEqual(',', top.Separator);
        Assert.IsTrue(top.HasHeader);
        Assert.IsTrue(top.FieldCount >= 10, $"expected ≥10 fields, got {top.FieldCount}");
    }

    // ── quoted_fields.csv: comma, double-quoted fields containing commas ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    public async Task QuotedFields_DetectedAsQuotedComma_ThreeColumns()
    {
        var (_, top) = await ProbeAsync(Sample("quoted_fields.csv"));
        Assert.AreEqual(',',  top.Separator);
        Assert.AreEqual('"',  top.Quote);
        Assert.AreEqual(3,    top.FieldCount);
        Assert.IsTrue(top.HasHeader);
    }

    // ── iso_dates.csv: comma, header, ISO date column + decimals ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    public async Task IsoDates_DetectedAsComma_WithDateColumn()
    {
        var (_, top) = await ProbeAsync(Sample("iso_dates.csv"));
        Assert.AreEqual(',', top.Separator);
        Assert.AreEqual(3,   top.FieldCount);
        Assert.AreEqual(CsvDataType.Date, top.ColumnTypes[0]);
    }

    // ── SmallFileLoader: load comma_header body and check first row content ──

    [TestMethod]
    [CoversNode("tabular-windowed-load")]
    public async Task CommaHeader_SmallLoader_ReturnsBodyRows()
    {
        var path = Sample("comma_header.csv");
        var (sample, top) = await ProbeAsync(path);

        var loader = await SmallFileLoader.LoadAsync(VirtualFileSystem.Instance, path, sample.Encoding, sample.BomByteCount, new DataShape(top, null));
        Assert.IsTrue(loader.KnownRowCount > 0);

        var window = loader.GetVisibleAsync(0, 5, _ => true, CancellationToken.None).Result;
        Assert.AreEqual(5, window.Count);
        Assert.AreEqual("1",      window[0].Cells[0]);
        Assert.AreEqual("Widget", window[0].Cells[1]);
    }

    // ── RowWindowReader: scan deep into the long file and confirm row alignment ──

    [TestMethod]
    [CoversNode("tabular-windowed-load")]
    public async Task CommaLong_WindowReader_ReadsDeepRange()
    {
        var path = Sample("comma_long.csv");
        var (sample, top) = await ProbeAsync(path);

        using var reader = new RowWindowReader(VirtualFileSystem.Instance, path, sample.Encoding, sample.BomByteCount, new DataShape(top, null));
        var window = reader.GetVisibleAsync(1000, 50, _ => true, CancellationToken.None).Result;
        Assert.AreEqual(50, window.Count);
        Assert.AreEqual(1000, window[0].AbsoluteIndex, "Window should start exactly at the requested row.");
    }

    // ── Every tabular sample should at minimum probe + tokenize without error ──

    [TestMethod]
    [CoversNode("tabular-detection")]
    [CoversNode("tabular-windowed-load")]
    public async Task AllSamples_ProbeAndLoadFirstWindow_DoNotThrow()
    {
        foreach (var path in TestSampleData.Files("tabular"))
        {
            var (sample, top) = await ProbeAsync(path);
            using var reader = new RowWindowReader(VirtualFileSystem.Instance, path, sample.Encoding, sample.BomByteCount, new DataShape(top, null));
            var rows = reader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            Assert.IsTrue(rows.Count >= 0, $"{System.IO.Path.GetFileName(path)}: window read failed");
        }
    }
}
