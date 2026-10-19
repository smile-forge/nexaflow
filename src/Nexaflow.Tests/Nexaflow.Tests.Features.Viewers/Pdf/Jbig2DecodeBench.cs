using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Media.Jbig2;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig.Filters;
using static Nexaflow.Tests.Features.Pdf.DecodeTiming;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// What decoding a PDF's JBIG2 images costs in Nexaflow.Media's decoder beside jbig2dec: run by hand, over a folder of
/// real PDFs, to see how far apart they are and whether they decode alike.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in: set <c>NEXAFLOW_JBIG2_BENCH</c> to a folder, and the run writes one <c>jbig2-bench-*.json</c> there, with a
/// log of how far it has got; it also needs <c>NEXAFLOW_JBIG2DEC</c>, the <c>jbig2.dll</c> to measure against.
/// <c>tools/bench/Run-Jbig2Bench.ps1</c> builds jbig2dec and Release, labels the run with the commit and runs it. The
/// images are those in the folders <c>NEXAFLOW_JBIG2_CORPUS</c> names, sampled by size (<c>NEXAFLOW_JBIG2_PER_SIZE</c>).
/// A timing is a comparison, not an absolute: runs are only compared on one machine, in one configuration.
/// </para>
/// <para>
/// Every image is decoded by ours and by jbig2dec from the same bytes and globals, and their bits set side by side
/// before anything is timed, so a speed is never quoted for a decode that came out different. Each image is then timed
/// three ways — ours with its globals read afresh, as jbig2dec is timed; the whole <c>JBIG2Decode</c> filter as the
/// reader calls it, its globals kept from one page to the next as the reader keeps them; and jbig2dec — all on one
/// thread, which is how JBIG2 decodes.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class Jbig2DecodeBench
{
    [TestMethod]
    public void HowManagedJbig2DecodingComparesWithNative()
    {
        var codec = PdfImageCodec.Jbig2;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(codec.FolderVariable))) { Assert.Inconclusive($"set {codec.FolderVariable} to a folder to write the run to"); return; }
        if (PdfImageRun.NativeLibrary(codec) is not { } library) return;

        if (PdfImageRun.Open(codec, "bench") is not { } sample) return;
        using var _ = sample;

        using var native = new Jbig2Dec(library);
        var filter = new PdfJbig2Filter();
        var globals = sample.Streams.Select(Jbig2ComparisonSweep.Globals).ToList();
        sample.Log("warming the decoders up");
        DecodeTiming.WarmUp([.. sample.Streams.Select((stream, at) => (stream, globals[at]))], item =>
        {
            Ours(item.stream.Data, item.Item2);
            filter.Decode(item.stream.Data, item.stream.Dictionary, DefaultFilterProvider.Instance, item.stream.FilterIndex);
            native.Decode(item.stream.Data, item.Item2);
        });

        var (gen0, gen1, gen2, paused) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());

        var rows = sample.Streams.Select((stream, at) =>
        {
            var row = Measured(stream, globals[at], native, filter);
            sample.Log($"[{at + 1}/{sample.Streams.Count}] {stream.Name} {row.Megapixels:0.###} MP {row.Pixels}"
                + (row.Timing is { } t ? $" ours {t.ManagedMs} the reader {t.FilterMs?.ToString() ?? "-"} jbig2dec {t.NativeMs} ms" : ""));
            return row;
        }).ToList();
        var timed = rows.Where(row => row.Timing is not null).ToList();

        var run = sample.Described();
        run["jbig2dec"] = new Dictionary<string, object> { ["path"] = library };
        ((Dictionary<string, object>)run["corpus"])["timed"] = timed.Count;
        ((Dictionary<string, object>)run["corpus"])["identical"] = rows.Count(row => row.Pixels == "identical");
        ((Dictionary<string, object>)run["corpus"])["withGlobals"] = globals.Count(g => g is not null);
        run["totals"] = Totals(timed);
        run["memory"] = new Dictionary<string, object>
        {
            ["gen0Collections"] = GC.CollectionCount(0) - gen0,
            ["gen1Collections"] = GC.CollectionCount(1) - gen1,
            ["gen2Collections"] = GC.CollectionCount(2) - gen2,
            ["gcPauseMs"] = Round((GC.GetTotalPauseDuration() - paused).TotalMilliseconds),
            ["peakWorkingSetMb"] = Round(System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0),
        };
        run["bySize"] = BySize(timed);
        run["images"] = rows.Select(row => row.Written()).ToList();

        sample.Write(run);
    }

    private static Jbig2Image Ours(byte[] data, byte[]? globals) =>
        Jbig2Decoder.Decode(data, globals is null ? null : Jbig2Globals.Read(globals));

    /// <summary>One image: decoded by ours and jbig2dec, compared, and — when they decoded it alike — timed every way.</summary>
    private static Row Measured(PdfImageStream stream, byte[]? globals, Jbig2Dec native, PdfJbig2Filter filter)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Jbig2Image? mine = null;
        string? managedError = null, filterError = null;
        try { mine = Ours(stream.Data, globals); }
        catch (Jbig2Exception failure) { managedError = failure.Message; }

        var theirs = native.Decode(stream.Data, globals);
        var (width, height) = mine is not null ? (mine.Width, mine.Height) : (theirs.Width, theirs.Height);
        var pixels = mine is null || theirs.Bits is null ? "not compared"
            : (theirs.Width, theirs.Height) == (mine.Width, mine.Height) && theirs.Bits.AsSpan().SequenceEqual(mine.Bits.Span) ? "identical" : "pixels differ";
        var row = new Row(stream, width, height, globals?.Length, pixels, managedError, theirs.Error, null, null);
        if (pixels != "identical") return row;

        double? filterMs = null;
        DecodeMemory? filterMemory = null;
        try
        {
            filterMs = Time(() => filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, stream.FilterIndex));
            filterMemory = Allocated(() => filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, stream.FilterIndex));
        }
        catch (Exception failure) { filterError = $"{failure.GetType().Name}: {failure.Message}"; }

        return row with
        {
            FilterError = filterError,
            Timing = new Timing(
                ManagedMs: Time(() => Ours(stream.Data, globals)),
                FilterMs: filterMs,
                NativeMs: Time(() => native.Decode(stream.Data, globals)),
                ManagedMemory: Allocated(() => Ours(stream.Data, globals)),
                FilterMemory: filterMemory),
        };
    }

    private static Dictionary<string, object> Totals(IReadOnlyList<Row> timed)
    {
        double Sum(IEnumerable<Row> rows, Func<Timing, double?> of) => Round(rows.Sum(row => of(row.Timing!) ?? 0));

        var managed = Sum(timed, t => t.ManagedMs);
        var native = Sum(timed, t => t.NativeMs);
        // The filter is only timed where it decodes; it has its own pairs, so a comparison is like for like.
        var filtered = timed.Where(row => row.Timing!.FilterMs is not null).ToList();

        return new Dictionary<string, object>
        {
            ["images"] = timed.Count,
            ["megapixels"] = Round(timed.Sum(row => row.Megapixels)),
            ["managedMs"] = managed,
            ["nativeMs"] = native,
            ["ratio"] = Ratio(managed, native),
            ["filterImages"] = filtered.Count,
            ["filterMs"] = Sum(filtered, t => t.FilterMs),
            ["filterNativeMs"] = Sum(filtered, t => t.NativeMs),
            ["filterRatio"] = Ratio(Sum(filtered, t => t.FilterMs), Sum(filtered, t => t.NativeMs)),
            ["managedKb"] = Sum(timed, t => t.ManagedMemory.AllocatedKb),
            ["managedKbPerMp"] = PerMegapixel(timed, t => t.ManagedMemory.AllocatedKb),
            ["filterKbPerMp"] = PerMegapixel(filtered, t => t.FilterMemory?.AllocatedKb),
            ["managedGen2"] = Sum(timed, t => t.ManagedMemory.Gen2),
        };
    }

    private static double PerMegapixel(IReadOnlyList<Row> rows, Func<Timing, double?> of)
    {
        var megapixels = rows.Sum(row => row.Megapixels);
        return megapixels <= 0 ? 0 : Round(rows.Sum(row => of(row.Timing!) ?? 0) / megapixels);
    }

    /// <summary>The totals again, per size of image: a fixed cost and a per-pixel cost show apart.</summary>
    private static List<Dictionary<string, object>> BySize(IReadOnlyList<Row> timed)
    {
        var listed = new List<Dictionary<string, object>>();
        foreach (var group in timed.GroupBy(row => PdfImageRun.SizeOf(row.Megapixels)).OrderBy(g => g.Key))
        {
            var totals = Totals(group.ToList());
            totals["size"] = PdfImageRun.Sizes[group.Key].Name;
            listed.Add(totals);
        }

        return listed;
    }

    private sealed record Timing(double ManagedMs, double? FilterMs, double NativeMs, DecodeMemory ManagedMemory, DecodeMemory? FilterMemory);

    private sealed record Row(PdfImageStream Stream, int Width, int Height, int? GlobalsBytes, string Pixels, string? ManagedError,
        string? NativeError, string? FilterError, Timing? Timing)
    {
        public double Megapixels => Width * (double)Height / 1_000_000;

        public Dictionary<string, object?> Written() => new()
        {
            ["name"] = Stream.Name,
            ["bytes"] = Stream.Data.Length,
            ["globalsBytes"] = GlobalsBytes,
            ["width"] = Width,
            ["height"] = Height,
            ["pixels"] = Pixels,
            ["managedError"] = ManagedError,
            ["nativeError"] = NativeError,
            ["filterError"] = FilterError,
            ["timing"] = Timing,
            ["ratio"] = Timing is null ? null : Ratio(Timing.ManagedMs, Timing.NativeMs),
        };
    }
}
