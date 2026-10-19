using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

using Nexaflow.Media.Jpeg;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Whether Nexaflow.Media's JPEG decoder decodes every JPEG in the corpus to the samples libjpeg-turbo — the C library
/// it was ported from — decodes it to: run by hand, the check behind "it matches libjpeg-turbo".
/// </summary>
/// <remarks>
/// Every image is decoded by both from the same bytes with libjpeg's default output, the PDF's colour rules left out,
/// since libjpeg-turbo knows nothing of them; ours decodes it serially and again with its own threading, as the
/// reader does, and that is held to the serial decode. An image they disagree on — different samples, or one
/// decoding what the other refuses — is kept as a file beside the run's <c>dct-compare-*.json</c>. Two outcomes are
/// agreements in all but name, and counted apart: an image whose two components no TurboJPEG pixel format holds, and
/// one libjpeg-turbo fails on after writing its output, where ours keeps that output with a warning.
/// <c>tools/bench/Run-DctBench.ps1 -Compare</c> runs it.
/// </remarks>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class DctComparisonSweep
{
    private static readonly TimeSpan Stuck = TimeSpan.FromMinutes(1);

    [TestMethod]
    public void EveryJpegImage_DecodesAsLibjpegTurboDoes()
    {
        var codec = PdfImageCodec.Dct;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(codec.FolderVariable))) { Assert.Inconclusive($"set {codec.FolderVariable} to a folder to write the run to"); return; }
        if (PdfImageRun.NativeLibrary(codec) is not { } library) return;
        if (PdfImageRun.Open(codec, "compare", everyImage: true) is not { } run) return;
        using var _ = run;
        using var native = new LibJpegTurbo(library);

        var outcomes = new ConcurrentDictionary<string, int>();
        var disagreements = new ConcurrentBag<Dictionary<string, object>>();
        var decoding = new ConcurrentDictionary<int, (string Name, Stopwatch Clock)>();
        long images = 0, kilopixels = 0;

        using var watchdog = new Timer(_ =>
        {
            foreach (var (name, clock) in decoding.Values)
                if (clock.Elapsed > Stuck) run.Log($"still decoding {name} after {clock.Elapsed:mm\\:ss}");
        }, null, Stuck, Stuck);

        var unread = run.Visit(Environment.ProcessorCount, (entry, stream) =>
        {
            decoding[Environment.CurrentManagedThreadId] = (entry.Name, Stopwatch.StartNew());
            var (outcome, detail) = Compared(stream.Data, native);
            decoding.TryRemove(Environment.CurrentManagedThreadId, out var _);

            outcomes.AddOrUpdate(outcome, 1, (_, count) => count + 1);
            if (!Agrees(outcome))
                disagreements.Add(new() { ["name"] = entry.Name, ["pdf"] = entry.Pdf, ["outcome"] = outcome, ["detail"] = detail, ["file"] = run.Keep(stream), ["bytes"] = stream.Data.Length });

            var done = Interlocked.Increment(ref images);
            var pixels = Interlocked.Add(ref kilopixels, (long)(entry.DeclaredMegapixels * 1000));
            if (done % 2000 == 0)
            {
                var share = run.Megapixels <= 0 ? 0 : pixels / 1000.0 / run.Megapixels;
                var left = share <= 0 ? TimeSpan.Zero : run.Elapsed * ((1 - share) / share);
                run.Log($"{done}/{run.Entries.Count} images, {share:P0} of the pixels, {disagreements.Count} disagree; about {left:hh\\:mm} left");
            }
        });

        var report = run.Described();
        report["libjpegTurbo"] = new Dictionary<string, object> { ["path"] = library };
        report["unread"] = unread;
        report["outcomes"] = outcomes.OrderByDescending(o => o.Value).ToDictionary(o => o.Key, o => (object)o.Value);
        report["disagreements"] = disagreements.OrderBy(d => (string)d["name"], StringComparer.Ordinal).ToList();
        run.Write(report);
    }

    private const string Identical = "identical";
    private const string IdenticalDamaged = "identical, damaged and warned of";
    private const string BothRefuse = "both refuse";
    private const string NoPixelFormat = "only ours decodes: no TurboJPEG pixel format holds its components";
    private const string WrittenThenFailed = "identical to what libjpeg-turbo wrote before failing";

    private static bool Agrees(string outcome) => outcome is Identical or IdenticalDamaged or BothRefuse or NoPixelFormat or WrittenThenFailed;

    /// <summary>What happened to one image in each decoder, and, when they disagree, how.</summary>
    internal static (string Outcome, string Detail) Compared(byte[] jpeg, LibJpegTurbo native)
    {
        JpegImage? serial = null;
        string? ours = null;
        try { serial = JpegDecoder.Decode(jpeg, new JpegDecodeOptions { Parallel = false }); }
        catch (JpegException failure) { ours = failure.Message; }

        // The reader decodes with the decoder's own threading, so the threaded decode is held to the serial one.
        if (serial is not null)
        {
            try
            {
                var threaded = JpegDecoder.Decode(jpeg);
                if (!threaded.Samples.Span.SequenceEqual(serial.Samples.Span)) return ("threaded decode differs from serial", Difference(threaded.Samples.Span, serial.Samples.Span, serial));
            }
            catch (JpegException failure) { return ("threaded decode differs from serial", failure.Message); }
        }

        var theirs = native.Decode(jpeg);
        if (theirs.Samples is null && serial is null) return (BothRefuse, $"{ours} | {theirs.Error}");
        if (serial is null) return ("only libjpeg-turbo decodes it", ours!);
        if (theirs.Samples is null)
        {
            if (theirs.Width == 0 && serial.Components is not (1 or 3 or 4)) return (NoPixelFormat, $"{serial.Components} components | {theirs.Error}");
            if (theirs.Written is { } written && written.AsSpan().SequenceEqual(serial.Samples.Span)) return (WrittenThenFailed, theirs.Error!);
            return ("only ours decodes it", $"{theirs.Error} | ours {serial.Components} components, {serial.Warnings.Count} warnings");
        }

        if (theirs.Samples.Length != serial.Samples.Length)
            return ("shapes differ", $"ours {serial.Width}x{serial.Height}x{serial.Components}, theirs {theirs.Width}x{theirs.Height}x{theirs.Components}");
        if (!theirs.Samples.AsSpan().SequenceEqual(serial.Samples.Span))
            return ("samples differ", Difference(serial.Samples.Span, theirs.Samples, serial));
        return (theirs.Warned || serial.Warnings.Count > 0 ? IdenticalDamaged : Identical, "");
    }

    /// <summary>Where two decodes of one image part, how many samples differ and by how much at most.</summary>
    private static string Difference(ReadOnlySpan<byte> mine, ReadOnlySpan<byte> theirs, JpegImage image)
    {
        int first = -1, count = 0, most = 0;
        for (var at = 0; at < Math.Min(mine.Length, theirs.Length); at++)
        {
            if (mine[at] == theirs[at]) continue;
            if (first < 0) first = at;
            count++;
            most = Math.Max(most, Math.Abs(mine[at] - theirs[at]));
        }

        var pixel = Math.Max(0, first) / image.Components;
        return $"{image.Width}x{image.Height}x{image.Components} {image.SourceColourSpace}: {count} samples differ, by up to {most}, the first at ({pixel % image.Width},{pixel / image.Width})";
    }
}
