using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

using Nexaflow.Media.Jpeg2000;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Whether Nexaflow.Media's JPEG 2000 decoder decodes every image in the corpus to the samples libopenjp2 — the C
/// library it was ported from — decodes it to: run by hand, the check behind "it matches the C library".
/// </summary>
/// <remarks>
/// Every image is decoded by both, serially, from the same bytes, and their components set side by side before any
/// colour work; the managed decoder decodes it again with its own threading, as the reader does, and that is held to
/// its serial decode. An image they disagree on — different samples, or one decoding what the other refuses — is kept as a
/// file beside the run's <c>jpx-compare-*.json</c>, so <c>opj_dump</c> can say what kind of codestream it is. Images
/// go out one a thread on threads of the sweep's own, not the thread pool, which the managed decoder hands its code
/// blocks to; a watchdog logs any image still decoding after a minute. <c>tools/bench/Run-JpxBench.ps1 -Compare</c>
/// runs it.
/// </remarks>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class JpxComparisonSweep
{
    private static readonly TimeSpan Stuck = TimeSpan.FromMinutes(1);

    [TestMethod]
    public void EveryJpegTwoThousandImage_DecodesAsLibopenjp2Does()
    {
        var codec = PdfImageCodec.Jpx;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(codec.FolderVariable))) { Assert.Inconclusive($"set {codec.FolderVariable} to a folder to write the run to"); return; }
        if (PdfImageRun.NativeLibrary(codec) is not { } library) return;
        if (PdfImageRun.Open(codec, "compare", everyImage: true) is not { } run) return;
        using var _ = run;
        using var native = new LibOpenJpeg(library);

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
            var (outcome, detail) = Compared(stream, native);
            decoding.TryRemove(Environment.CurrentManagedThreadId, out var _);

            outcomes.AddOrUpdate(outcome, 1, (_, count) => count + 1);
            if (outcome is not ("identical" or "both refuse"))
                disagreements.Add(new() { ["name"] = entry.Name, ["pdf"] = entry.Pdf, ["outcome"] = outcome, ["detail"] = detail, ["file"] = run.Keep(stream), ["bytes"] = stream.Data.Length });

            var done = Interlocked.Increment(ref images);
            var pixels = Interlocked.Add(ref kilopixels, (long)(entry.DeclaredMegapixels * 1000));
            if (done % 500 == 0)
            {
                var share = run.Megapixels <= 0 ? 0 : pixels / 1000.0 / run.Megapixels;
                var left = share <= 0 ? TimeSpan.Zero : run.Elapsed * ((1 - share) / share);
                run.Log($"{done}/{run.Entries.Count} images, {share:P0} of the pixels, {disagreements.Count} disagree; about {left:hh\\:mm} left");
            }
        });

        var report = run.Described();
        report["libopenjp2"] = new Dictionary<string, object> { ["version"] = native.Version, ["path"] = library };
        report["unread"] = unread;
        report["outcomes"] = outcomes.OrderByDescending(o => o.Value).ToDictionary(o => o.Key, o => (object)o.Value);
        report["disagreements"] = disagreements.OrderBy(d => (string)d["name"], StringComparer.Ordinal).ToList();
        run.Write(report);
    }

    /// <summary>What happened to one image in each decoder, and, when they disagree, how.</summary>
    private static (string Outcome, string Detail) Compared(PdfImageStream stream, LibOpenJpeg native)
    {
        JpxPlanes? mine = null, theirs = null;
        string? myFailure = null, theirFailure = null;

        try { mine = JpxPlanes.Of(Jpeg2000Decoder.Decode(stream.Data, new Jpeg2000DecodeOptions { ApplyPalette = !stream.IgnorePalette, Parallel = false })); }
        catch (Exception failure) { myFailure = $"{failure.GetType().Name}: {failure.Message}"; }

        // The reader decodes with the decoder's own threading, so the threaded decode is held to the serial one.
        if (mine is not null)
        {
            string threaded;
            try { threaded = JpxPlanes.Of(Jpeg2000Decoder.Decode(stream.Data, new Jpeg2000DecodeOptions { ApplyPalette = !stream.IgnorePalette })).Against(mine); }
            catch (Exception failure) { threaded = $"{failure.GetType().Name}: {failure.Message}"; }
            if (threaded != "identical") return ("threaded decode differs from serial", threaded);
        }

        try { theirs = native.Read(stream, 0); }
        catch (Exception failure) { theirFailure = failure.Message; }

        return (mine, theirs) switch
        {
            ({ } m, { } t) when m.Against(t) is var against => against == "identical" ? ("identical", "") : ("samples differ", against),
            (null, { }) => ("only libopenjp2 decodes it", myFailure!),
            ({ }, null) => ("only the managed decoder decodes it", theirFailure!),
            _ => ("both refuse", $"{myFailure} | {theirFailure}"),
        };
    }
}

