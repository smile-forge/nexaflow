using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.IO.Common;
using Nexaflow.Media.Photoshop;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;
using Nexaflow.Features.Images.Editing.Selection;
using System.Buffers;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Every Photoshop file of a corpus <c>NEXAFLOW_PSD_CORPUS</c> names — files other applications wrote, deliberately not in the
/// repository — read, drawn and written back:
/// <list type="bullet">
/// <item>each reads, or is refused for a reason Nexaflow gives (a colour mode it does not edit, a side past its largest);</item>
/// <item>where nothing in it is left undrawn and its merged image is real, Nexaflow's composite matches Photoshop's;</item>
/// <item>written back and read again, every layer is as it was — kind, name, opacity, blend, visibility, locks, clip, mask and
/// every pixel — every block Nexaflow keeps is still there, and the composite is the same, pixel for pixel.</item>
/// </list>
/// Inconclusive until the variable is set. A report of every file is written beside the test's output.
/// </summary>
[TestClass]
[CoversNode("images-psd")]
public class PhotoshopCorpusTests
{
    /// <summary>The most an 8-bit premultiplied channel may differ from Photoshop's, on average, and the share of pixels
    /// that may differ by more than 16 levels.</summary>
    private const double MeanLimit = 1.0, OffShareLimit = 0.01;

    public TestContext TestContext { get; set; } = null!;

    private sealed class NoPictures : IPictureLayerImages
    {
        public PictureLayerImage? ImageOf(PictureLayer layer, double scale) => null;
    }

    [TestMethod]
    [Timeout(4 * 60 * 60 * 1000)]
    public async Task EveryFileOfTheCorpus_ReadsDrawsAsPhotoshopDid_AndWritesBackAsItWas()
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_PSD_CORPUS") is not { Length: > 0 } root || !Directory.Exists(root))
        {
            Assert.Inconclusive("Set NEXAFLOW_PSD_CORPUS to a folder of .psd and .psb files.");
            return;
        }
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".psb", StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(f => new FileInfo(f).Length).ToArray();
        var work = TestTemp.CreateSubdirectory("nexaflow-psd-corpus-").FullName;
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        using var services = new ImageEditingServices(vfs, new Nexaflow.Features.Images.Editing.Recipe.ImageRecipeStore(TimeSpan.FromHours(1)),
                                                      new RestoredPictures(Path.Combine(work, "restored"), RestoredPictures.DefaultBudget),
                                                      new CutOutMasks(Path.Combine(work, "cutouts"), CutOutMasks.DefaultBudget),
                                                      new ErasedPictures(Path.Combine(work, "erased"), ErasedPictures.DefaultBudget));

        // Each file logged as it is done — its size and how long each step took — so a long run shows where it is.
        var log = Environment.GetEnvironmentVariable("NEXAFLOW_PSD_CORPUS_LOG") is { Length: > 0 } named ? named : Path.Combine(work, "photoshop-corpus.log");
        File.WriteAllText(log, $"{files.Length} file(s) under {root}{Environment.NewLine}");
        var gate = new Lock();
        int done = 0;
        var results = new ConcurrentBag<Result>();
        // A quarter as many at a time as there are cores: a file decodes and compresses its layers across the cores already,
        // and most are small. The largest first, so none is left running alone at the end.
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 4) }, async (file, ct) =>
        {
            var result = await CheckAsync(file, root, work, vfs, services);
            results.Add(result);
            lock (gate)
                File.AppendAllText(log, $"{++done}/{files.Length}\t{new FileInfo(file).Length / 1e6:0.0} MB\t{result.Line}{Environment.NewLine}");
        });

        var ordered = results.OrderBy(r => r.File, StringComparer.OrdinalIgnoreCase).ToList();
        // Beside the log, which outlives the test's own folder where it was named.
        var report = Path.ChangeExtension(log, ".tsv");
        File.WriteAllLines(report, [Result.Header, .. ordered.Select(r => r.Line)]);
        var summary = string.Join(", ", ordered.GroupBy(r => r.Outcome).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
        TestContext.WriteLine($"{files.Length} file(s): {summary}. Report: {report}");
        var failed = ordered.Where(r => r.Outcome == Outcome.Failed).ToList();
        Assert.IsEmpty(failed, $"{failed.Count} of {files.Length} file(s) failed ({summary}); report {report}:\n"
                               + string.Join("\n", failed.Take(40).Select(r => $"  {r.File}: {r.Note}")));
    }

    private enum Outcome { Matched, Undrawn, Refused, Failed }

    private sealed record Result(string File, Outcome Outcome, string Colour, int Layers, double MeanDiff, double OffShare, long Milliseconds, string Steps, string Note)
    {
        public const string Header = "file\toutcome\tcolour\tlayers\tmean diff\tshare off by >16\tms\tsteps (ms)\tnote";
        public string Line => $"{File}\t{Outcome}\t{Colour}\t{Layers}\t{MeanDiff:0.000}\t{OffShare:0.0000}\t{Milliseconds}\t{Steps}\t{Note}";
    }

    /// <summary>How long each step of checking a file took, in the order taken.</summary>
    private sealed class Laps
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(string Step, long Milliseconds)> _steps = [];
        private long _last;

        public long Elapsed => _clock.ElapsedMilliseconds;

        public void Mark(string step)
        {
            long now = _clock.ElapsedMilliseconds;
            _steps.Add((step, now - _last));
            _last = now;
        }

        public override string ToString() => string.Join(" ", _steps.Select(s => $"{s.Step} {s.Milliseconds}"));
    }

    private static async Task<Result> CheckAsync(string file, string root, string work, VirtualFileSystem vfs, ImageEditingServices services)
    {
        var name = Path.GetRelativePath(root, file);
        var laps = new Laps();
        LayeredPictureRead read;
        PhotoshopDocument document;
        try
        {
            using var stream = File.OpenRead(file);
            document = PhotoshopReader.Read(stream);
            read = PhotoshopFormat.ReadFrom(stream, Path.Combine(work, "sources")).Read;
            laps.Mark("read");
        }
        // A colour the Artist does not edit, or a file cut short: refused, saying why.
        catch (InvalidDataException ex) when (ex.Message.Contains("can't be edited", StringComparison.Ordinal) || ex.Message.Contains("ends before", StringComparison.Ordinal))
        {
            return new Result(name, Outcome.Refused, "", 0, 0, 0, laps.Elapsed, laps.ToString(), ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return new Result(name, Outcome.Refused, "", 0, 0, 0, laps.Elapsed, laps.ToString(), ex.Message.Split('\n')[0]);
        }
        catch (Exception ex)
        {
            return new Result(name, Outcome.Failed, "", 0, 0, 0, laps.Elapsed, laps.ToString(), $"read: {ex.GetType().Name}: {ex.Message}");
        }

        var picture = read.Picture;
        string colour = $"{picture.Colour.Model}/{picture.Colour.Bits}";
        int layers = picture.All.Count();
        try
        {
            using var composite = Composite(picture, services);
            laps.Mark("draw");
            double mean = 0, off = 0;
            var outcome = Outcome.Undrawn;
            var note = new StringBuilder();
            string? mergedProblem = null;
            if (!read.HoldsUndrawable && PhotoshopFormat.HasRealMergedImage(document) && MergedOf(file, services, out mergedProblem) is { } merged)
            {
                using var _ = merged;
                laps.Mark("merged");
                // A merged image saved with no transparency of its own is the picture flattened onto white.
                using var shown = document.MergedHasTransparency ? null : OverWhite(composite);
                (mean, off) = Difference(shown ?? composite, merged.Pixels);
                outcome = mean <= MeanLimit && off <= OffShareLimit ? Outcome.Matched : Outcome.Failed;
                if (outcome == Outcome.Failed) note.Append($"composite differs from Photoshop's: mean {mean:0.00}, {off:P1} off by >16; ");
                laps.Mark("compare");
            }
            else
            {
    note.Append(mergedProblem is not null ? $"merged image does not read: {mergedProblem}; "
                            : string.Join(",", picture.All.SelectMany(l => l.Kept.Undrawn).Distinct()) is { Length: > 0 } undrawn ? $"undrawn: {undrawn}; " : "no real merged image; ");
            }

            var again = Path.Combine(work, Guid.NewGuid().ToString("N") + Path.GetExtension(file));
            using (var renderer = await LayeredRenderer.PrepareAsync(picture, _ => MadeByTools.None, services))
                await services.LayeredFormatFor(again)!.WriteAsync(picture, again, renderer);
            laps.Mark("write");
            LayeredPicture reread;
            using (var stream = File.OpenRead(again)) reread = PhotoshopFormat.ReadFrom(stream, Path.Combine(work, "sources")).Read.Picture;
            laps.Mark("re-read");
            var changed = Changes(picture, reread);
            laps.Mark("check");
            if (changed is { } change)
            {
                outcome = Outcome.Failed;
                note.Append($"written back: {change}; ");
            }
            else
            {
                using var recomposite = Composite(reread, services);
                var (againMean, againOff) = Difference(composite, recomposite);
                if (againMean > 0 || againOff > 0)
                {
                    outcome = Outcome.Failed;
                    note.Append($"written back, drawn differently: mean {againMean:0.000}; ");
                }
            }
            laps.Mark("redraw");
            File.Delete(again);
            return new Result(name, outcome, colour, layers, mean, off, laps.Elapsed, laps.ToString(), note.ToString().TrimEnd(' ', ';'));
        }
        catch (Exception ex)
        {
            return new Result(name, Outcome.Failed, colour, layers, 0, 0, laps.Elapsed, laps.ToString(), $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The file's merged image as the viewer shows it, or why it does not read — a file cut short, whose layers may
    /// still be whole.</summary>
    private static DecodedImage? MergedOf(string file, ImageEditingServices services, out string? problem)
    {
        problem = null;
        try
        {
            return services.Decoders.Decode(File.ReadAllBytes(file), null);
        }
        catch (ImageDecodeException ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    private static SKImage Composite(LayeredPicture picture, ImageEditingServices services)
    {
        using var compositor = new LayerCompositor(services.Markup, cache: false);
        return compositor.Render(picture, 1, new NoPictures());
    }

    /// <summary>A picture flattened onto white, as Photoshop saves a merged image that holds no transparency.</summary>
    private static SKImage OverWhite(SKImage picture)
    {
        using var surface = SKSurface.Create(new SKImageInfo(picture.Width, picture.Height, SKColorType.RgbaF16, SKAlphaType.Premul, picture.ColorSpace))!;
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(picture, 0, 0);
        return surface.Snapshot();
    }

    /// <summary>Two pictures as 8-bit premultiplied sRGB: the mean difference of a channel, and the share of pixels any
    /// channel of which differs by more than 16 levels.</summary>
    private static (double Mean, double OffShare) Difference(SKImage a, SKImage b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return (255, 1);
        var info = new SKImageInfo(a.Width, a.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var pa = new SKBitmap(info);
        using var pb = new SKBitmap(info);
        a.ReadPixels(info, pa.GetPixels(), pa.RowBytes, 0, 0);
        b.ReadPixels(info, pb.GetPixels(), pb.RowBytes, 0, 0);
        var x = pa.GetPixelSpan();
        var y = pb.GetPixelSpan();
        long sum = 0, off = 0;
        for (int i = 0; i < x.Length; i += 4)
        {
            int most = 0;
            for (int c = 0; c < 4; c++)
            {
                int d = Math.Abs(x[i + c] - y[i + c]);
                sum += d;
                most = Math.Max(most, d);
            }
            if (most > 16) off++;
        }
        long pixels = x.Length / 4;
        return (sum / (4.0 * pixels), off / (double)pixels);
    }

    /// <summary>The first way <paramref name="after"/> — a picture written and read back — differs from <paramref name="before"/>;
    /// null where it does not.</summary>
    private static string? Changes(LayeredPicture before, LayeredPicture after)
    {
        if ((before.Width, before.Height, before.Colour, before.Background) != (after.Width, after.Height, after.Colour, after.Background))
            return "the picture's size, colour or background";
        if (Math.Abs(before.Dpi - after.Dpi) > 0.01) return $"its resolution, {before.Dpi} to {after.Dpi}";
        var a = before.All.ToList();
        var b = after.All.ToList();
        if (a.Count != b.Count) return $"{a.Count} layers became {b.Count}";
        // The layers compared side by side; the first that changed is the one told.
        var changes = new string?[a.Count];
        Parallel.For(0, a.Count, i => changes[i] = LayerChange(i, a[i], b[i]));
        return changes.FirstOrDefault(c => c is not null);
    }

    /// <summary>What changed of one layer, written and read back, or <c>null</c>.</summary>
    private static string? LayerChange(int i, Layer x, Layer y)
    {
        string where = $"layer {i} '{x.Name}'";
        if (x.GetType() != y.GetType()) return $"{where}: a {x.GetType().Name} became a {y.GetType().Name}";
        if ((x.Id, x.Name, x.Visible, x.Blend, x.Locked, x.AlphaLocked, x.ClipsToBelow) != (y.Id, y.Name, y.Visible, y.Blend, y.Locked, y.AlphaLocked, y.ClipsToBelow))
            return $"{where}: its identity, name, visibility, blend, locks or clip";
        if (Math.Abs(x.Opacity - y.Opacity) > 0.5 / 255) return $"{where}: its opacity, {x.Opacity:0.000} to {y.Opacity:0.000}";
        if (x is GroupLayer gx && gx.Isolated != ((GroupLayer)y).Isolated) return $"{where}: isolated or pass-through";
        if ((x.Mask is null) != (y.Mask is null)) return $"{where}: its mask";
        if (x.Mask is { } mx && (mx.Enabled != y.Mask!.Enabled || !SameCoverage(mx.Coverage, y.Mask.Coverage)))
            return $"{where}: its mask's coverage";
        if (x is PixelLayer { Pixels: var px } && y is PixelLayer { Pixels: var py })
        {
            if (px.Bounds != py.Bounds) return $"{where}: where it is painted, {px.Bounds} to {py.Bounds}";
            if (!SamePixels(px, py)) return $"{where}: its pixels";
        }
        var kx = x.Kept.Get<PhotoshopLayerKept>();
        var ky = y.Kept.Get<PhotoshopLayerKept>();
        if (kx is not null)
        {
            if (ky is null) return $"{where}: what it kept";
            var keys = ky.Blocks.Concat(ky.PixelBlocks).Select(k => k.Key).ToHashSet();
            if (kx.Blocks.Concat(kx.PixelBlocks).FirstOrDefault(k => !keys.Contains(k.Key)) is { } lost) return $"{where}: its '{lost.Key}' block";
        }
        return null;
    }

    /// <summary>Whether two layers' pixels, painted over the same rectangle, are alike — compared a band of rows at a time, in
    /// two buffers lent for the comparison.</summary>
    private static bool SamePixels(TiledPixels x, TiledPixels y)
    {
        var painted = x.Bounds;
        int bandBytes = painted.Width * TileFormat.TileSize * x.Format.PixelBytes;
        byte[] first = ArrayPool<byte>.Shared.Rent(bandBytes), second = ArrayPool<byte>.Shared.Rent(bandBytes);
        try
        {
            for (int top = painted.Top; top < painted.Bottom; top += TileFormat.TileSize)
            {
                var band = new SKRectI(painted.Left, top, painted.Right, Math.Min(painted.Bottom, top + TileFormat.TileSize));
                int bytes = band.Width * band.Height * x.Format.PixelBytes;
                x.ReadInto(band, first.AsSpan(0, bytes));
                y.ReadInto(band, second.AsSpan(0, bytes));
                if (!first.AsSpan(0, bytes).SequenceEqual(second.AsSpan(0, bytes))) return false;
            }
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(first);
            ArrayPool<byte>.Shared.Return(second);
        }
    }

    /// <summary>Whether two masks cover alike — compared a band of rows at a time.</summary>
    private static bool SameCoverage(TiledCoverage x, TiledCoverage y)
    {
        for (int top = 0; top < x.Height; top += TileFormat.TileSize)
        {
            var band = new SKRectI(0, top, x.Width, Math.Min(x.Height, top + TileFormat.TileSize));
            if (!x.ReadValues(band).AsSpan().SequenceEqual(y.ReadValues(band))) return false;
        }
        return true;
    }
}
