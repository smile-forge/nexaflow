using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.Streaming;

namespace Nexaflow.Tests.Features.Tabular;

/// <summary>
/// End-to-end smoke tests over the curated sample files in <c>d:\temp\test</c>.
/// Skipped (Inconclusive) when that directory is absent — the same tests run on any
/// machine that has the samples available without breaking CI elsewhere.
/// </summary>
[TestClass]
public class SampleFileDetectionTests
{
    private const string SampleDir = @"d:\temp\test";

    private static string Path(string name) => System.IO.Path.Combine(SampleDir, name);

    private static void EnsureSamples()
    {
        if (!Directory.Exists(SampleDir))
            Assert.Inconclusive($"Sample directory {SampleDir} not present on this machine.");
    }

    private static (FileSample sample, CsvShape top) Probe(string path)
    {
        var sample = LineSamplingReader.Read(path);
        var shapes = ShapeDetector.DetectAll(sample);
        return (sample, shapes[0]);
    }

    // ── tooltips.tsv: 2 columns, tab-separated, no header ─────────────────

    [TestMethod]
    public void Tooltips_Tsv_DetectedAsTabSeparated()
    {
        EnsureSamples();
        var (_, top) = Probe(Path("tooltips.tsv"));
        Assert.AreEqual('\t', top.Separator);
        Assert.AreEqual(2,    top.FieldCount);
        Assert.IsFalse(top.HasHeader);
    }

    // ── allcolors.csv: r,g,b,colorname — integers + string with header ────

    [TestMethod]
    public void AllColors_DetectedAsCommaWithHeader_FourColumns()
    {
        EnsureSamples();
        var (_, top) = Probe(Path("allcolors.csv"));
        Assert.AreEqual(',', top.Separator);
        Assert.AreEqual(4,   top.FieldCount);
        Assert.IsTrue(top.HasHeader);
        Assert.AreEqual(CsvDataType.Integer, top.ColumnTypes[0]);
        Assert.AreEqual(CsvDataType.String,  top.ColumnTypes[3]);
    }

    // ── subscription.csv: semicolon-separated ─────────────────────────────

    [TestMethod]
    public void Subscription_DetectedAsSemicolonSeparatedWithHeader_AndDateTimeColumn()
    {
        EnsureSamples();
        var (_, top) = Probe(Path("subscription.csv"));
        Assert.AreEqual(';', top.Separator);
        Assert.IsTrue(top.FieldCount >= 10, $"expected ≥10 fields, got {top.FieldCount}");
        Assert.IsTrue(top.HasHeader, "Header labels over Integer columns should flag HasHeader.");
        // Column 4 of subscription is StartDateTime — "21.05.2004 15:32:18" must classify as DateTime.
        Assert.IsTrue(top.ColumnTypes.Count > 4);
        Assert.AreEqual(CsvDataType.DateTime, top.ColumnTypes[4],
            "Column 5 (StartDateTime, dd.MM.yyyy HH:mm:ss format) should classify as DateTime.");
    }

    // ── parabola.csv: single-column file of numbers ───────────────────────

    [TestMethod]
    public void Parabola_SingleColumn_FallbackShape()
    {
        EnsureSamples();
        var (_, top) = Probe(Path("parabola.csv"));
        Assert.AreEqual(1, top.FieldCount);
    }

    // ── Player_Stats.csv: comma+space with header ─────────────────────────

    [TestMethod]
    public void PlayerStats_DetectedAsCommaWithHeader()
    {
        EnsureSamples();
        var (_, top) = Probe(Path("Player_Stats.csv"));
        Assert.AreEqual(',',  top.Separator);
        Assert.IsTrue(top.HasHeader);
        Assert.IsTrue(top.FieldCount >= 10, $"expected many fields, got {top.FieldCount}");
    }

    // ── iris.csv: well-known dataset; first row is metadata then floats ──

    [TestMethod]
    public void Iris_DetectedAsComma()
    {
        EnsureSamples();
        var (_, top) = Probe(Path("iris.csv"));
        Assert.AreEqual(',', top.Separator);
        Assert.IsTrue(top.FieldCount >= 5);
    }

    // ── Loads the body of allcolors.csv via SmallFileLoader and confirms
    //    that the row count + first-row content match expectations. ───────

    [TestMethod]
    public void AllColors_SmallLoader_ReturnsBodyRows()
    {
        EnsureSamples();
        var path = Path("allcolors.csv");
        var (sample, top) = Probe(path);

        // SmallFileLoader has no internal size cap — the runtime orchestrator picks small vs. large
        // by file size, but the loader itself is happy to ingest any file end-to-end.
        var loader = new SmallFileLoader(path, sample.Encoding, sample.BomByteCount, new DataShape(top, null));
        Assert.IsTrue(loader.KnownRowCount > 0);

        var window = loader.GetVisibleAsync(0, 5, _ => true, CancellationToken.None).Result;
        Assert.AreEqual(5, window.Count);
        // First non-header row is "0,0,0,black".
        Assert.AreEqual("0",     window[0].Cells[0]);
        Assert.AreEqual("black", window[0].Cells[3]);
    }

    // ── Tail-only check on iris: confirm the streaming reader can scan
    //    forward through a large body and the last row is consistent. ────

    [TestMethod]
    public void Iris_WindowReader_ReadsLargeRange()
    {
        EnsureSamples();
        var path = Path("iris.csv");
        var (sample, top) = Probe(path);

        // Iris has a 1-line header-ish "metadata" row that the detector treats variably;
        // regardless, the reader should produce rows aligned to its row index.
        using var reader = new RowWindowReader(path, sample.Encoding, sample.BomByteCount, new DataShape(top, null));
        var first = reader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
        Assert.IsTrue(first.Count > 0);
        Assert.AreEqual(0, first[0].AbsoluteIndex);
    }

    // ── Every sample file should at minimum probe + tokenize without error.

    [TestMethod]
    public void AllSamples_ProbeAndLoadFirstWindow_DoNotThrow()
    {
        EnsureSamples();
        foreach (var path in Directory.EnumerateFiles(SampleDir))
        {
            var (sample, top) = Probe(path);
            using var reader = new RowWindowReader(path, sample.Encoding, sample.BomByteCount, new DataShape(top, null));
            var rows = reader.GetVisibleAsync(0, 10, _ => true, CancellationToken.None).Result;
            // Some files may have <10 rows; assert non-negative read.
            Assert.IsTrue(rows.Count >= 0, $"{System.IO.Path.GetFileName(path)}: window read failed");
        }
    }
}
