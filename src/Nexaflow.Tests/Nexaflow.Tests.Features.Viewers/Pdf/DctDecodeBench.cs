using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Media.Jpeg;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig.Filters;
using static Nexaflow.Tests.Features.Pdf.DecodeTiming;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// What decoding a PDF's JPEG images costs in Nexaflow.Media's decoder beside libjpeg-turbo, the C library it was
/// ported from, and beside Windows' own codec: run by hand, over a folder of real PDFs, to see how far apart they are
/// and whether they decode alike.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in: set <c>NEXAFLOW_DCT_BENCH</c> to a folder, and the run writes one <c>dct-bench-*.json</c> there, with a log
/// of how far it has got; it also needs <c>NEXAFLOW_TURBOJPEG</c>, the <c>turbojpeg.dll</c> to measure against.
/// <c>tools/bench/Run-DctBench.ps1</c> builds libjpeg-turbo and Release, labels the run with the commit and runs it.
/// The images are those in the folders <c>NEXAFLOW_DCT_CORPUS</c> names, sampled by size (<c>NEXAFLOW_DCT_PER_SIZE</c>).
/// A timing is a comparison, not an absolute: runs are only compared on one machine, in one configuration.
/// </para>
/// <para>
/// Every image is decoded by ours and by libjpeg-turbo from the same bytes, and their samples set side by side before
/// anything is timed, so a speed is never quoted for a decode that came out different. Each image is then timed four
/// ways — ours serially and with its own threading, the whole <c>DCTDecode</c> filter as the reader calls it, and
/// libjpeg-turbo with its SIMD code, which decodes on one thread — and a fifth where Windows' codec reads it: a grey or
/// RGB image, decoded and converted to bytes as a reader would ask WIC to.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class DctDecodeBench
{
    [TestMethod]
    public void HowManagedJpegDecodingComparesWithNative()
    {
        var codec = PdfImageCodec.Dct;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(codec.FolderVariable))) { Assert.Inconclusive($"set {codec.FolderVariable} to a folder to write the run to"); return; }
        if (PdfImageRun.NativeLibrary(codec) is not { } library) return;

        if (PdfImageRun.Open(codec, "bench") is not { } sample) return;
        using var _ = sample;

        using var native = new LibJpegTurbo(library);
        var filter = new PdfDctFilter();
        sample.Log("warming the decoders up");
        DecodeTiming.WarmUp(sample.Streams, stream =>
        {
            JpegDecoder.Decode(stream.Data, Serial);
            JpegDecoder.Decode(stream.Data);
            filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, 0);
            native.Decode(stream.Data);
            Wic(stream.Data, 3);
        });

        var (gen0, gen1, gen2, paused) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());

        var rows = sample.Streams.Select((stream, at) =>
        {
            var row = Measured(stream, native, filter);
            sample.Log($"[{at + 1}/{sample.Streams.Count}] {stream.Name} {row.Megapixels:0.###} MP {row.Pixels}"
                + (row.Timing is { } t ? $" serial {t.ManagedSingleMs} threaded {t.ManagedMs} libjpeg-turbo {t.NativeMs} WIC {t.WicMs?.ToString() ?? "-"} ms" : ""));
            return row;
        }).ToList();
        var timed = rows.Where(row => row.Timing is not null).ToList();

        var run = sample.Described();
        run["processors"] = Environment.ProcessorCount;
        run["libjpegTurbo"] = new Dictionary<string, object> { ["path"] = library };
        ((Dictionary<string, object>)run["corpus"])["timed"] = timed.Count;
        ((Dictionary<string, object>)run["corpus"])["identical"] = rows.Count(row => row.Pixels == "identical");
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

    private static readonly JpegDecodeOptions Serial = new() { Parallel = false };

    /// <summary>One image: decoded by ours and libjpeg-turbo, compared, and — when they decoded it alike — timed every way.</summary>
    private static Row Measured(PdfImageStream stream, LibJpegTurbo native, PdfDctFilter filter)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        JpegImage? mine = null;
        string? managedError = null, filterError = null;
        try { mine = JpegDecoder.Decode(stream.Data, Serial); }
        catch (JpegException failure) { managedError = failure.Message; }

        var theirs = native.Decode(stream.Data);
        var shape = mine is not null ? new ImageShape(mine.Width, mine.Height, mine.Components, mine.SourceColourSpace.ToString())
            : theirs.Samples is not null ? new ImageShape(theirs.Width, theirs.Height, theirs.Components, "") : null;
        var pixels = mine is null || theirs.Samples is null ? "not compared"
            : theirs.Samples.AsSpan().SequenceEqual(mine.Samples.Span) ? "identical" : "samples differ";
        var row = new Row(stream, shape, pixels, managedError, theirs.Error, null, null, null);
        if (pixels != "identical") return row;

        // Windows' codec, where it reads the image as the reader once asked it to: grey or RGB, converted to bytes.
        double? wicMs = null;
        string? wicPixels = null;
        if (mine!.Components is 1 or 3 && Wic(stream.Data, mine.Components) is { } wic)
        {
            wicPixels = wic.AsSpan().SequenceEqual(theirs.Samples) ? "identical" : "samples differ";
            wicMs = Time(() => Wic(stream.Data, mine.Components));
        }

        double? filterMs = null;
        DecodeMemory? filterMemory = null;
        try
        {
            filterMs = Time(() => filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, 0));
            filterMemory = Allocated(() => filter.Decode(stream.Data, stream.Dictionary, DefaultFilterProvider.Instance, 0));
        }
        catch (Exception failure) { filterError = $"{failure.GetType().Name}: {failure.Message}"; }

        return row with
        {
            FilterError = filterError,
            WicPixels = wicPixels,
            Timing = new Timing(
                ManagedSingleMs: Time(() => JpegDecoder.Decode(stream.Data, Serial)),
                ManagedMs: Time(() => JpegDecoder.Decode(stream.Data)),
                FilterMs: filterMs,
                NativeMs: Time(() => native.Decode(stream.Data)),
                WicMs: wicMs,
                ManagedSingleMemory: Allocated(() => JpegDecoder.Decode(stream.Data, Serial)),
                ManagedMemory: Allocated(() => JpegDecoder.Decode(stream.Data)),
                FilterMemory: filterMemory),
        };
    }

    /// <summary>
    /// A grey or RGB JPEG decoded by Windows' codec into interleaved bytes, its colour profile ignored — or null when
    /// it will not read it.
    /// </summary>
    private static byte[]? Wic(byte[] jpeg, int components)
    {
        try
        {
            using var stream = new MemoryStream(jpeg, writable: false);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            var converted = new FormatConvertedBitmap(frame, components == 1 ? PixelFormats.Gray8 : PixelFormats.Rgb24, null, 0);
            var stride = frame.PixelWidth * components;
            var samples = new byte[stride * frame.PixelHeight];
            converted.CopyPixels(samples, stride, 0);
            return samples;
        }
        catch (Exception failure) when (failure is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<string, object> Totals(IReadOnlyList<Row> timed)
    {
        double Sum(IEnumerable<Row> rows, Func<Timing, double?> of) => Round(rows.Sum(row => of(row.Timing!) ?? 0));

        var managedSingle = Sum(timed, t => t.ManagedSingleMs);
        var managed = Sum(timed, t => t.ManagedMs);
        var native = Sum(timed, t => t.NativeMs);
        // The filter and WIC are only timed where they decode; each has its own pairs, so a comparison is like for like.
        var filtered = timed.Where(row => row.Timing!.FilterMs is not null).ToList();
        var wic = timed.Where(row => row.Timing!.WicMs is not null).ToList();

        return new Dictionary<string, object>
        {
            ["images"] = timed.Count,
            ["megapixels"] = Round(timed.Sum(row => row.Megapixels)),
            ["managedSingleMs"] = managedSingle,
            ["managedMs"] = managed,
            ["nativeMs"] = native,
            ["singleThreadRatio"] = Ratio(managedSingle, native),
            ["threadedRatio"] = Ratio(managed, native),
            ["filterImages"] = filtered.Count,
            ["filterMs"] = Sum(filtered, t => t.FilterMs),
            ["filterNativeMs"] = Sum(filtered, t => t.NativeMs),
            ["filterRatio"] = Ratio(Sum(filtered, t => t.FilterMs), Sum(filtered, t => t.NativeMs)),
            ["wicImages"] = wic.Count,
            ["wicIdentical"] = wic.Count(row => row.WicPixels == "identical"),
            ["wicMs"] = Sum(wic, t => t.WicMs),
            ["wicManagedMs"] = Sum(wic, t => t.ManagedMs),
            ["wicNativeMs"] = Sum(wic, t => t.NativeMs),
            ["wicRatio"] = Ratio(Sum(wic, t => t.WicMs), Sum(wic, t => t.NativeMs)),
            // Ours as the reader decodes it, threaded, over the images WIC reads: below 1 is faster than WIC.
            ["threadedToWicRatio"] = Ratio(Sum(wic, t => t.ManagedMs), Sum(wic, t => t.WicMs)),
            ["managedSingleKb"] = Sum(timed, t => t.ManagedSingleMemory.AllocatedKb),
            ["managedSingleKbPerMp"] = PerMegapixel(timed, t => t.ManagedSingleMemory.AllocatedKb),
            ["managedKbPerMp"] = PerMegapixel(timed, t => t.ManagedMemory.AllocatedKb),
            ["filterKbPerMp"] = PerMegapixel(filtered, t => t.FilterMemory?.AllocatedKb),
            ["managedSingleGen2"] = Sum(timed, t => t.ManagedSingleMemory.Gen2),
            ["filterGen2"] = Sum(filtered, t => t.FilterMemory?.Gen2),
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

    private sealed record Timing(double ManagedSingleMs, double ManagedMs, double? FilterMs, double NativeMs, double? WicMs,
        DecodeMemory ManagedSingleMemory, DecodeMemory ManagedMemory, DecodeMemory? FilterMemory);

    private sealed record Row(PdfImageStream Stream, ImageShape? Shape, string Pixels, string? ManagedError, string? NativeError,
        string? FilterError, string? WicPixels, Timing? Timing)
    {
        public double Megapixels => Shape is null ? 0 : Shape.Width * (double)Shape.Height / 1_000_000;

        public Dictionary<string, object?> Written() => new()
        {
            ["name"] = Stream.Name,
            ["bytes"] = Stream.Data.Length,
            ["width"] = Shape?.Width,
            ["height"] = Shape?.Height,
            ["components"] = Shape?.Components,
            ["colourSpace"] = Shape?.ColourSpace,
            ["pixels"] = Pixels,
            ["wicPixels"] = WicPixels,
            ["managedError"] = ManagedError,
            ["nativeError"] = NativeError,
            ["filterError"] = FilterError,
            ["timing"] = Timing,
            ["singleThreadRatio"] = Timing is null ? null : Ratio(Timing.ManagedSingleMs, Timing.NativeMs),
            ["threadedRatio"] = Timing is null ? null : Ratio(Timing.ManagedMs, Timing.NativeMs),
        };
    }

    /// <summary>A decoded image's size and make-up, without its samples.</summary>
    private sealed record ImageShape(int Width, int Height, int Components, string ColourSpace);
}
