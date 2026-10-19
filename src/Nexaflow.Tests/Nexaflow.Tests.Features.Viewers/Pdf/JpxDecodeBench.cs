using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

using Nexaflow.Tests.Fixtures;

using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Media.Jpeg2000;
using UglyToad.PdfPig.Filters;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// What decoding a PDF's JPEG 2000 images costs in Nexaflow.Media's decoder — OpenJPEG.net, a managed port of
/// OpenJPEG — beside libopenjp2, the C library it was ported from: run by hand, over a folder of real PDFs, to see
/// how far apart they are, whether they decode alike, and which stages of the managed decode the time goes to.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in: set <c>NEXAFLOW_JPX_BENCH</c> to a folder, and each measurement writes one <c>jpx-bench-*.json</c> or
/// <c>jpx-profile-*.json</c> there, with a log of how far it has got; timing also needs <c>NEXAFLOW_OPENJP2</c>, the
/// <c>openjp2.dll</c> to measure against. <c>tools/bench/Run-JpxBench.ps1</c> builds OpenJPEG and Release, labels
/// the run with the commit and runs both, each in a process of its own. The images are those in the folders
/// <c>NEXAFLOW_JPX_CORPUS</c> names — PdfPig's own test documents when it names none — sampled by size
/// (<c>NEXAFLOW_JPX_PER_SIZE</c>). A timing is a comparison, not an absolute: runs are only compared on one
/// machine, in one configuration.
/// </para>
/// <para>
/// Every image is decoded by both libraries from the same bytes, and their samples are set side by side before
/// anything is timed, so a speed is never quoted for a decode that came out different. Each image is then timed
/// five ways: OpenJPEG.net's decode serially and with its own threading, the whole <c>JPXDecode</c> filter as the
/// reader calls it (decode, bit depth, palette, colour, packing), and libopenjp2 on one thread and on one per
/// processor. OpenJPEG.net hands every code block to the thread pool even when told not to thread; serially it
/// hands out one at a time, so its serial figure is one job at a time, not one thread.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class JpxDecodeBench
{
    /// <summary>How many times each decode is timed; the median is kept.</summary>
    private const int Runs = 5;

    /// <summary>
    /// A decode slower than this is timed once: its first run, which the pixel comparison has already warmed. Repeats
    /// buy nothing at that size, where a run is long beside the timer and the noise.
    /// </summary>
    private const double Slow = 1000;
    /// <summary>A timed run repeats a decode until it lasts this long, so a tiny image is not timer noise.</summary>
    private const double Batch = 5;

    /// <summary>How long each profile samples for, the corpus decoded as many times as that takes.</summary>
    private const double ProfileMs = 15_000;

    [TestMethod]
    public void HowManagedJpxDecodingComparesWithNative()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(JpxRun.Folder))) { Assert.Inconclusive($"set {JpxRun.Folder} to a folder to write the run to"); return; }
        if (JpxRun.NativeLibrary() is not { } library) return;

        if (JpxRun.Open("bench") is not { } sample) return;
        using var _ = sample;

        using var native = new LibOpenJpeg(library);
        var threads = Environment.ProcessorCount;
        var filter = new PdfJpxFilter();

        var rows = sample.Streams.Select((stream, at) =>
        {
            var row = Measured(stream, native, threads, filter);
            sample.Log($"[{at + 1}/{sample.Streams.Count}] {stream.Name} {row.Megapixels:0.###} MP {row.Pixels}"
                + (row.Timing is { } t ? $" one job at a time {t.ManagedSingleMs} vs {t.NativeSingleMs} ms" : ""));
            return row;
        }).ToList();
        var timed = rows.Where(row => row.Timing is not null).ToList();

        var run = sample.Described();
        run["processors"] = threads;
        run["libopenjp2"] = new Dictionary<string, object> { ["version"] = native.Version, ["path"] = library };
        ((Dictionary<string, object>)run["corpus"])["timed"] = timed.Count;
        ((Dictionary<string, object>)run["corpus"])["identical"] = rows.Count(row => row.Pixels == "identical");
        run["totals"] = Totals(timed);
        run["bySize"] = BySize(timed);
        run["images"] = rows.Select(row => row.Written()).ToList();

        sample.Write(run);
    }

    /// <summary>
    /// Where OpenJPEG.net's time goes, by stage, over the same sample <see cref="HowManagedJpxDecodingComparesWithNative"/>
    /// times: the reader's whole <c>JPXDecode</c> filter, then the decode handing out one job at a time. Run in a
    /// process of its own with <c>DOTNET_ReadyToRun=0</c>, so the framework is jitted and every frame a sample holds is
    /// named — a wait, a copy or an allocation is otherwise unnamed, and counted to the code that called it.
    /// </summary>
    [TestMethod]
    public void WhereManagedJpxDecodingSpendsItsTime()
    {
        if (JpxRun.Open("profile") is not { } sample) return;
        using var _ = sample;

        var filter = new PdfJpxFilter();
        var decoded = sample.Streams.Where(stream => Decodes(stream, filter)).ToList();
        sample.Log($"{decoded.Count} of {sample.Streams.Count} decode through the filter; sampling the reader's decode");

        var run = sample.Described();
        run["readyToRun"] = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0";
        ((Dictionary<string, object>)run["corpus"])["profiled"] = decoded.Count;
        run["appProfile"] = CpuStageSampler.Sample(ForAWhile(decoded,
            stream => filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, 0)), StageOf);
        sample.Log("sampling the decode, one job at a time");
        run["decodeProfile"] = CpuStageSampler.Sample(ForAWhile(decoded, stream => Managed(stream, singleThreaded: true)), StageOf);

        sample.Write(run);
    }

    /// <summary>Whether the reader's filter decodes this image at all — only what it decodes is profiled.</summary>
    private static bool Decodes(JpxStream stream, PdfJpxFilter filter)
    {
        try { filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, 0); return true; }
        catch (Exception) { return false; }
    }

    /// <summary>One image: decoded by both, compared, and — when both decoded it alike — timed every way.</summary>
    private static Row Measured(JpxStream stream, LibOpenJpeg native, int threads, PdfJpxFilter filter)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        JpxPlanes? mine = null, theirs = null;
        string? managedError = null, nativeError = null, filterError = null;

        try { mine = JpxPlanes.Of(Managed(stream, singleThreaded: true)); }
        catch (Exception failure) { managedError = $"{failure.GetType().Name}: {failure.Message}"; }

        try { theirs = native.Read(stream, 0); }
        catch (Exception failure) { nativeError = failure.Message; }

        // Only the shape outlives the comparison: a row holding every image's samples holds the whole corpus decoded.
        var shape = ImageShape.Of(mine ?? theirs);
        var pixels = mine is not null && theirs is not null ? mine.Against(theirs) : "not compared";
        var row = new Row(stream, shape, pixels, managedError, nativeError, null, null);
        if (pixels != "identical") return row;

        double? filterMs = null;
        try { filterMs = Time(() => filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, 0)); }
        catch (Exception failure) { filterError = $"{failure.GetType().Name}: {failure.Message}"; }

        return row with
        {
            FilterError = filterError,
            Timing = new Timing(
                ManagedSingleMs: Time(() => Managed(stream, singleThreaded: true)),
                ManagedMs: Time(() => Managed(stream, singleThreaded: false)),
                FilterMs: filterMs,
                NativeSingleMs: Time(() => native.Run(stream, 0)),
                NativeMs: Time(() => native.Run(stream, threads)),
                ManagedSingleKb: Allocated(() => Managed(stream, singleThreaded: true))),
        };
    }

    /// <summary>The managed decode alone, set up as <see cref="PdfJpxFilter"/> sets it up.</summary>
    private static Jpeg2000Image Managed(JpxStream stream, bool singleThreaded) =>
        Jpeg2000Decoder.Decode(stream.Data, new Jpeg2000DecodeOptions { ApplyPalette = !stream.IgnorePalette, Parallel = !singleThreaded });

    /// <summary>
    /// The median of a decode's timed runs, in milliseconds, after one run to warm it — or that one run, when it is
    /// <see cref="Slow"/>.
    /// </summary>
    private static double Time(Action decode)
    {
        var clock = Stopwatch.StartNew();
        decode();
        var once = clock.Elapsed.TotalMilliseconds;
        if (once > Slow) return Round(once);

        var batch = Math.Max(1, (int)Math.Ceiling(Batch / Math.Max(once, 0.001)));
        var times = new double[Runs];

        for (var at = 0; at < times.Length; at++)
        {
            clock.Restart();
            for (var repeat = 0; repeat < batch; repeat++) decode();
            times[at] = clock.Elapsed.TotalMilliseconds / batch;
        }

        Array.Sort(times);
        return Round(times[times.Length / 2]);
    }

    /// <summary>What one decode allocates on the thread that runs it, in KB.</summary>
    private static double Allocated(Action decode)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        decode();
        return Round((GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0);
    }

    /// <summary>The images decoded over and over, whole passes, until <see cref="ProfileMs"/> has gone by.</summary>
    private static Action ForAWhile(IReadOnlyList<JpxStream> streams, Action<JpxStream> decode) => () =>
    {
        var clock = Stopwatch.StartNew();
        do
        {
            foreach (var stream in streams) decode(stream);
        }
        while (clock.Elapsed.TotalMilliseconds < ProfileMs);
    };

    private static Dictionary<string, object> Totals(IReadOnlyList<Row> timed)
    {
        double Sum(Func<Timing, double?> of) => Round(timed.Sum(row => of(row.Timing!) ?? 0));

        var filtered = timed.Where(row => row.Timing!.FilterMs is not null).ToList();
        var managedSingle = Sum(t => t.ManagedSingleMs);
        var nativeSingle = Sum(t => t.NativeSingleMs);
        var managed = Sum(t => t.ManagedMs);
        var native = Sum(t => t.NativeMs);

        return new Dictionary<string, object>
        {
            ["images"] = timed.Count,
            ["megapixels"] = Round(timed.Sum(row => row.Megapixels)),
            ["managedSingleMs"] = managedSingle,
            ["managedMs"] = managed,
            ["nativeSingleMs"] = nativeSingle,
            ["nativeMs"] = native,
            ["singleThreadRatio"] = Ratio(managedSingle, nativeSingle),
            ["threadedRatio"] = Ratio(managed, native),
            ["managedSingleKb"] = Sum(t => t.ManagedSingleKb),
            // The filter is only timed where it decodes; its own pair keeps the comparison like for like.
            ["filterImages"] = filtered.Count,
            ["filterMs"] = Round(filtered.Sum(row => row.Timing!.FilterMs!.Value)),
            ["filterNativeMs"] = Round(filtered.Sum(row => row.Timing!.NativeMs)),
            ["filterRatio"] = Ratio(filtered.Sum(row => row.Timing!.FilterMs!.Value), filtered.Sum(row => row.Timing!.NativeMs)),
        };
    }

    /// <summary>The totals again, per size of image: a fixed cost and a per-pixel cost show apart.</summary>
    private static List<Dictionary<string, object>> BySize(IReadOnlyList<Row> timed)
    {
        var listed = new List<Dictionary<string, object>>();

        foreach (var group in timed.GroupBy(row => JpxRun.SizeOf(row.Megapixels)).OrderBy(g => g.Key))
        {
            var totals = Totals(group.ToList());
            totals["size"] = JpxRun.Sizes[group.Key].Name;
            listed.Add(totals);
        }

        return listed;
    }

    // Which stage of OpenJPEG.net a frame belongs to, by its type. Helpers every stage calls into are left
    // unnamed, so a sample in one is counted to the stage that called it.
    private static readonly Dictionary<string, string> StageOfType = Staged(
        ("tier-1: code blocks (MQ, bit-planes)", ["Tier1Coding", "MQCoder", "MQCState", "T1Luts", "T1CBLKDecodeProcessingJob"]),
        ("tier-1: HT code blocks", ["ht_dec", "T1HTLuts", "dec_mel", "frwd_struct", "rev_struct"]),
        ("tier-2: packets", ["Tier2Coding", "PacketIterator", "TagTree", "TagNode", "BIO", "PIComp", "PIResolution", "ProgOrdChang"]),
        ("inverse wavelet (DWT)", ["DWT", "dwt_local", "v4dwt_local"]),
        ("inverse colour transform (MCT)", ["MCT", "Invert", "MctData", "SimpleMccDecorrelationData"]),
        ("tile coder: buffers, copy-out", ["TileCoder", "TcdImage", "TcdTile", "TcdTilecomp", "TcdResolution", "TcdBand", "TcdPrecinct",
        "TcdCblkDec", "TcdSeg", "TcdSegDataChunk", "SparseArrayInt32", "TileCodingParams", "TileCompParams"]),
        ("codestream and JP2 boxes", ["J2K", "JP2", "JP2Box", "CIO", "CodingParameters", "CompressionInfo", "DecompressionParameters",
            "DecParams", "J2KTables", "CodestreamIndex", "MarkerInfo", "TileIndex", "TPIndex", "TlmInfo", "EventMgr",
            "JP2Color", "JP2pclr", "JP2cmap_comp", "JP2cdefInfo", "JP2Comps"]),
        ("image output: bit depth, palette, colour, packing", ["JPXImage", "ImageComp", "Scaler", "LinearInterpolator", "BitWriter"]),
        ("decoder API", ["Jpeg2000Decoder", "Jpeg2000Image", "Jpeg2000Component"]));

    private static readonly HashSet<string> Helpers = ["MyMath", "Constants", "Ptr", "ArPtr", "IntOrFloat", "IntOrUInt", "ShortOrIntOrFloatOrDoubleAr"];

    private const string Filter = "Nexaflow.Features.Pdf.Reading.PdfJpxFilter.";
    private const string Decoder = "Nexaflow.Media.Jpeg2000.";

    private static string? StageOf(string method)
    {
        if (method.StartsWith(Filter, StringComparison.Ordinal)) return "filter: JPXDecode wrapper";
        if (!method.StartsWith(Decoder, StringComparison.Ordinal)) return null;
        // TileCoder.RunThreads hands every code block to the thread pool, one at a time when told not to thread, through
        // a lock and two events per block — and its workers' side of that too. Its own stage, apart from tile coding.
        if (method.Contains("RunThreads", StringComparison.Ordinal)) return "job hand-off: thread pool, lock, events";

        var rest = method.AsSpan(Decoder.Length);
        foreach (var inner in (ReadOnlySpan<string>)["Internal.", "Util."])
            if (rest.StartsWith(inner, StringComparison.Ordinal)) rest = rest[inner.Length..];

        var end = rest.IndexOfAny('.', '+');
        var type = (end < 0 ? rest : rest[..end]).ToString();

        if (Helpers.Contains(type)) return null;
        return StageOfType.TryGetValue(type, out var stage) ? stage : $"other: {type}";
    }

    private static Dictionary<string, string> Staged(params (string Stage, string[] Types)[] stages) =>
        stages.SelectMany(s => s.Types.Select(type => (type, s.Stage))).ToDictionary(p => p.type, p => p.Stage, StringComparer.Ordinal);

    private static double Ratio(double managed, double native) => native <= 0 ? 0 : Math.Round(managed / native, 2);

    private static double Round(double value) => Math.Round(value, 3);

    private sealed record Timing(double ManagedSingleMs, double ManagedMs, double? FilterMs, double NativeSingleMs, double NativeMs, double ManagedSingleKb);

    private sealed record Row(JpxStream Stream, ImageShape? Shape, string Pixels, string? ManagedError, string? NativeError, string? FilterError, Timing? Timing)
    {
        public double Megapixels => Shape is null ? 0 : Shape.Width * (double)Shape.Height / 1_000_000;

        public Dictionary<string, object?> Written() => new()
        {
            ["name"] = Stream.Name,
            ["bytes"] = Stream.Data.Length,
            ["format"] = Stream.IsRawCodestream ? "j2k" : "jp2",
            ["width"] = Shape?.Width,
            ["height"] = Shape?.Height,
            ["components"] = Shape?.Components,
            ["precision"] = Shape?.Precision,
            ["pixels"] = Pixels,
            ["managedError"] = ManagedError,
            ["nativeError"] = NativeError,
            ["filterError"] = FilterError,
            ["timing"] = Timing,
            ["singleThreadRatio"] = Timing is null ? null : Ratio(Timing.ManagedSingleMs, Timing.NativeSingleMs),
            ["threadedRatio"] = Timing is null ? null : Ratio(Timing.ManagedMs, Timing.NativeMs),
        };
    }

    /// <summary>A decoded image's size and make-up, without its samples.</summary>
    private sealed record ImageShape(int Width, int Height, int Components, int Precision)
    {
        public static ImageShape? Of(JpxPlanes? planes) =>
            planes is null || planes.Planes.Count == 0
                ? null
                : new(planes.Planes[0].Width, planes.Planes[0].Height, planes.Planes.Count, planes.Planes.Max(p => p.Precision));
    }
}
