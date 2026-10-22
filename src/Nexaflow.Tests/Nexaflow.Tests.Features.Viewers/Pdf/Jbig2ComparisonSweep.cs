using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

using Nexaflow.Media.Jbig2;
using Nexaflow.Tests.Fixtures;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Whether Nexaflow.Media's JBIG2 decoder decodes every JBIG2 image in the corpus to the bits jbig2dec — the decoder
/// Ghostscript and MuPDF are built on — decodes it to: run by hand, the check behind "it matches jbig2dec".
/// </summary>
/// <remarks>
/// Every image is decoded by both from the same bytes, with the same globals, to its JBIG2 page — before the reader
/// lays it out at the PDF's size and inverts it, which jbig2dec knows nothing of. An image they disagree on — other
/// bits, another size, or one decoding what the other refuses — is kept as a file beside the run's
/// <c>jbig2-compare-*.json</c>, its globals beside it. <c>tools/bench/Run-Jbig2Bench.ps1 -Compare</c> runs it.
/// </remarks>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class Jbig2ComparisonSweep
{
    private static readonly TimeSpan Stuck = TimeSpan.FromMinutes(1);

    [TestMethod]
    public void EveryJbig2Image_DecodesAsJbig2decDoes()
    {
        var codec = PdfImageCodec.Jbig2;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(codec.FolderVariable))) { Assert.Inconclusive($"set {codec.FolderVariable} to a folder to write the run to"); return; }
        if (PdfImageRun.NativeLibrary(codec) is not { } library) return;
        if (PdfImageRun.Open(codec, "compare", everyImage: true) is not { } run) return;
        using var _ = run;
        using var native = new Jbig2Dec(library);

        var outcomes = new ConcurrentDictionary<string, int>();
        var disagreements = new ConcurrentBag<Dictionary<string, object>>();
        var decoding = new ConcurrentDictionary<int, (string Name, Stopwatch Clock)>();
        long images = 0;

        using var watchdog = new Timer(_ =>
        {
            foreach (var (name, clock) in decoding.Values)
                if (clock.Elapsed > Stuck) run.Log($"still decoding {name} after {clock.Elapsed:mm\\:ss}");
        }, null, Stuck, Stuck);

        var unread = run.Visit(Environment.ProcessorCount, (entry, stream) =>
        {
            decoding[Environment.CurrentManagedThreadId] = (entry.Name, Stopwatch.StartNew());
            var globals = Globals(stream);
            var (outcome, detail) = Compared(stream.Data, globals, native);
            decoding.TryRemove(Environment.CurrentManagedThreadId, out var _);

            outcomes.AddOrUpdate(outcome, 1, (_, count) => count + 1);
            if (!Agrees(outcome))
            {
                var file = run.Keep(stream);
                if (globals is not null) File.WriteAllBytes(Path.ChangeExtension(file, ".glob"), globals);
                disagreements.Add(new() { ["name"] = entry.Name, ["pdf"] = entry.Pdf, ["outcome"] = outcome, ["detail"] = detail, ["file"] = file, ["bytes"] = stream.Data.Length });
            }

            var done = Interlocked.Increment(ref images);
            if (done % 1000 == 0) run.Log($"{done}/{run.Entries.Count} images, {disagreements.Count} disagree");
        });

        var report = run.Described();
        report["jbig2dec"] = new Dictionary<string, object> { ["path"] = library };
        report["unread"] = unread;
        report["outcomes"] = outcomes.OrderByDescending(o => o.Value).ToDictionary(o => o.Key, o => (object)o.Value);
        report["disagreements"] = disagreements.OrderBy(d => (string)d["name"], StringComparer.Ordinal).ToList();
        run.Write(report);
    }

    private const string Identical = "identical";
    private const string IdenticalDamaged = "identical, damaged and warned of";
    private const string BothRefuse = "both refuse";

    private static bool Agrees(string outcome) => outcome is Identical or IdenticalDamaged or BothRefuse;

    /// <summary>The bytes of the <c>JBIG2Globals</c> stream an image's decode parameters name, or null when they name none.</summary>
    internal static byte[]? Globals(PdfImageStream stream)
    {
        var parameters = DecodeParameterResolver.GetFilterParameters(stream.Dictionary, stream.FilterIndex);
        return parameters.TryGet(NameToken.Jbig2Globals, out StreamToken globals) && !globals.Data.IsEmpty
            ? globals.Decode(DefaultFilterProvider.Instance).ToArray()
            : null;
    }

    /// <summary>What happened to one image in each decoder, and, when they disagree, how.</summary>
    internal static (string Outcome, string Detail) Compared(byte[] data, byte[]? globals, Jbig2Dec native)
    {
        Jbig2Image? ours = null;
        string? refusal = null;
        try { ours = Jbig2Decoder.Decode(data, globals is null ? null : Jbig2Globals.Read(globals)); }
        catch (Jbig2Exception failure) { refusal = failure.Message; }

        var theirs = native.Decode(data, globals);
        if (theirs.Bits is null && ours is null) return (BothRefuse, $"{refusal} | {theirs.Error}");
        if (ours is null) return ("only jbig2dec decodes it", refusal!);
        if (theirs.Bits is null) return ("only ours decodes it", $"{theirs.Error} | ours warned {ours.Warnings.Count} times");

        if ((theirs.Width, theirs.Height) != (ours.Width, ours.Height))
            return ("sizes differ", $"ours {ours.Width}x{ours.Height}, theirs {theirs.Width}x{theirs.Height}");
        if (!theirs.Bits.AsSpan().SequenceEqual(ours.Bits.Span))
            return ("pixels differ", Difference(ours, theirs.Bits));
        return (theirs.Warnings.Count > 0 || ours.Warnings.Count > 0 ? IdenticalDamaged : Identical, "");
    }

    /// <summary>How many pixels two decodes of one page differ in, and where the first is.</summary>
    private static string Difference(Jbig2Image ours, byte[] theirs)
    {
        var bits = ours.Bits.Span;
        long count = 0;
        var first = -1;
        for (var at = 0; at < bits.Length; at++)
        {
            var differ = bits[at] ^ theirs[at];
            if (differ == 0) continue;
            if (first < 0) first = at;
            count += System.Numerics.BitOperations.PopCount((uint)differ);
        }
        return $"{ours.Width}x{ours.Height}: {count} pixels differ, the first in row {first / ours.Stride}; ours warned: {string.Join("; ", ours.Warnings.Take(3))}";
    }
}
