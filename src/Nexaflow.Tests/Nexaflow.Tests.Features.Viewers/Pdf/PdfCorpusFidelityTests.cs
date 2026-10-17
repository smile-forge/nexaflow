using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Every page of a corpus of real PDFs drawn the way the reader draws it, and held pixel for pixel against the same page
/// drawn by Windows' own PDF renderer at the same size, in colour.
/// <para>
/// PDF is a fixed format: a page has one right appearance, and two renderers that both get it right differ only where
/// their edges are antialiased. So each page is measured three ways — the share of pixels that are <em>identical</em>,
/// the share that are <em>near</em> (every channel within <see cref="NearLevel"/>: antialiasing noise), and the share
/// that are <em>off</em> (some channel past <see cref="OffLevel"/>: content one renderer has and the other has not) —
/// with the mean error, and the off share of the worst <see cref="Tile"/>-pixel tile, so one missing logo is not
/// averaged away across a page of matching text.
/// </para>
/// <para>
/// Everything it writes goes beside the corpus in <c>pdf-fidelity\</c>, not into the repository:
/// <list type="bullet">
/// <item><c>reference\</c> — Windows' render of every page, kept, so a later run only redraws our side.</item>
/// <item><c>trees\</c> — every page's layout written down (<see cref="PdfLayoutSnapshot"/>), one gzipped file per
/// document: what a page's drawing is kept as once it is accepted, for a later run to compare against exactly.</item>
/// <item><c>review\</c> — our render and a difference map of every page with off pixels, and <c>index.html</c>, worst
/// first.</item>
/// <item><c>pages.csv</c> and <c>summary.txt</c> — every page's measures, and their distribution.</item>
/// </list>
/// </para>
/// <para>
/// Opt-in, local and long: <c>NEXAFLOW_PDF_CORPUS</c> names the corpus and <c>NEXAFLOW_PDF_FIDELITY=1</c> asks for this
/// sweep. <c>NEXAFLOW_PDF_FIDELITY_DPI</c> sets the resolution (150 when unset); <c>NEXAFLOW_PDF_CORPUS_LIMIT</c> takes
/// only that many files. A run resumes what the same build began at the same resolution: documents it measured are
/// skipped, so stopping it costs nothing done. What another build measured is put aside under <c>runs\</c>, and Windows'
/// renders are kept for every run. It fails only when our reading or drawing of a page throws.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("opt-in fidelity sweep over an external corpus of real PDFs")]
public class PdfCorpusFidelityTests
{
    /// <summary>Every channel within this of the reference: the pixel is the same but for antialiasing.</summary>
    private const int NearLevel = 16;

    /// <summary>Some channel past this from the reference: the pixel shows something the other renderer's does not.</summary>
    private const int OffLevel = 64;

    /// <summary>The side of the square a page's worst-placed difference is measured over.</summary>
    private const int Tile = 32;

    /// <summary>A page with at least this share of off pixels keeps our render and a difference map for review.</summary>
    private const double ReviewShare = 0.002;

    /// <summary>The longest side a page is drawn at, whatever its size: a poster at 150 dpi is not a page anyone reads whole.</summary>
    private const int LongestSide = 2400;

    /// <summary>How many pages the review page shows.</summary>
    private const int Reviewed = 600;

    /// <summary>How long one page of ours may take to read before it is given up on.</summary>
    private static readonly TimeSpan PageBudget = TimeSpan.FromSeconds(60);

    private const string Header = "file,page,width,height,identical,near,off,mae,worsttile,ours_ms,windows_ms,unsupported,status";

    [TestMethod]
    public void EveryPage_IsDrawnAndHeldAgainstWindows()
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_PDF_CORPUS") is not { Length: > 0 } corpus ||
            Environment.GetEnvironmentVariable("NEXAFLOW_PDF_FIDELITY") != "1")
        {
            Assert.Inconclusive("Set NEXAFLOW_PDF_CORPUS to a corpus of PDFs and NEXAFLOW_PDF_FIDELITY=1 to run this sweep.");
            return;
        }

        var dpi = double.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_PDF_FIDELITY_DPI"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var asked) && asked > 0 ? asked : 150;

        var work = Directory.CreateDirectory(Path.Combine(corpus, "pdf-fidelity")).FullName;
        var files = Directory.EnumerateFiles(corpus, "*.pdf", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(work, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToList();
        if (int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_PDF_CORPUS_LIMIT"), out var limit) && limit > 0)
            files = files.Take(limit).ToList();

        var run = new Run(corpus, work, dpi);
        var todo = files.Where(f => !run.Done.Contains(run.Relative(f)))
            // The largest documents first, so the run does not end on one long document while every other core is idle.
            .OrderByDescending(static f => new FileInfo(f).Length).ToList();

        var clock = Stopwatch.StartNew();
        // Handed out one document at a time as workers come free: split into fixed ranges up front, the largest documents
        // would share a range and one worker.
        Parallel.ForEach(Partitioner.Create(todo, EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, run.Document);

        var summary = run.Summarise(clock.Elapsed, todo.Count, files.Count);
        File.WriteAllText(Path.Combine(work, "summary.txt"), summary);

        if (!run.Escaped.IsEmpty)
            Assert.Fail($"{run.Escaped.Count} page(s) threw while we read or drew them. {Path.Combine(work, "summary.txt")}\n"
                        + string.Join('\n', run.Escaped.Take(10)));
    }

    /// <summary>One sweep's state: where it writes, what it has done, what has gone wrong.</summary>
    private sealed class Run
    {
        private readonly string _corpus;
        private readonly string _work;
        private readonly double _dpi;
        private readonly object _writing = new();

        public Run(string corpus, string work, double dpi)
        {
            _corpus = corpus;
            _work = work;
            _dpi = dpi;

            // Rows measure one build's drawing at one resolution, so a run resumes only what that build at that resolution
            // began; what any other left is put aside.
            var pages = Path.Combine(work, "pages.csv");
            var stamp = Path.Combine(work, "run.txt");
            var build = $"{typeof(PdfPageLayout).Assembly.ManifestModule.ModuleVersionId} {dpi.ToString(CultureInfo.InvariantCulture)} dpi";
            if (File.Exists(pages) && (!File.Exists(stamp) || File.ReadAllText(stamp).Trim() != build)) PutAside();
            File.WriteAllText(stamp, build);

            if (!File.Exists(pages)) File.WriteAllText(pages, Header + "\n");

            var done = Path.Combine(work, "done.txt");
            Done = File.Exists(done) ? File.ReadAllLines(done).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        }

        /// <summary>Moves another run's rows, review and trees under <c>runs\</c>; Windows' renders stay, the same for every run.</summary>
        private void PutAside()
        {
            var aside = Directory.CreateDirectory(Path.Combine(_work, "runs",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))).FullName;
            foreach (var name in new[] { "pages.csv", "done.txt", "summary.txt", "run.txt" })
                if (File.Exists(Path.Combine(_work, name))) File.Move(Path.Combine(_work, name), Path.Combine(aside, name));
            foreach (var name in new[] { "review", "trees" })
                if (Directory.Exists(Path.Combine(_work, name))) Directory.Move(Path.Combine(_work, name), Path.Combine(aside, name));
        }

        /// <summary>The documents a previous run finished, by their path inside the corpus.</summary>
        public HashSet<string> Done { get; }

        public ConcurrentQueue<string> Escaped { get; } = new();

        public string Relative(string file) => Path.GetRelativePath(_corpus, file);

        /// <summary>Every page of one document, ours and Windows', measured; its rows written once it is finished.</summary>
        public void Document(string file)
        {
            var relative = Relative(file);
            var rows = new List<string>();
            var trees = new StringBuilder();
            WinPdf.PdfDocument? windows = null;

            try
            {
                using var open = new CancellationTokenSource(PageBudget);
                using var scope = PdfDocumentScope.TryOpen(file, open.Token);
                if (scope is null)
                {
                    rows.Add(Row(relative, 0, default, null, 0, 0, 0, "unopenable"));
                    return;
                }

                // The scope holds its token for as long as it is open, and closes the file when it fires: the budget is
                // for opening only, so it is disarmed once the document is open. Each page has its own.
                open.CancelAfter(Timeout.InfiniteTimeSpan);
                var document = scope.Pages.ReadDocument(CancellationToken.None);
                windows = Windows(file);

                for (var index = 0; index < document.Pages.Count; index++)
                    rows.Add(Page(relative, scope, document.Pages[index], index, windows, trees));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Escaped.Enqueue($"{relative}: {ex.GetType().Name}: {ex.Message}");
                rows.Add(Row(relative, 0, default, null, 0, 0, 0, $"escaped: {Clean(ex.Message)}"));
            }
            finally
            {
                Finish(relative, rows, trees);
            }
        }

        private string Page(string relative, PdfDocumentScope scope, PdfPageSlot slot, int index, WinPdf.PdfDocument? windows,
            StringBuilder trees)
        {
            var number = slot.Number;
            PdfPageNode page;
            LayoutTree tree;
            BitmapSource? ours;
            var clock = Stopwatch.StartNew();
            try
            {
                using var budget = new CancellationTokenSource(PageBudget);
                page = scope.Pages.ReadPage(number, budget.Token);
                tree = PdfPageLayout.Lay(page, Brushes.White, budget.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return Row(relative, number, default, null, clock.ElapsedMilliseconds, 0, 0, "timeout");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Escaped.Enqueue($"{relative} p{number}: {ex.GetType().Name}: {ex.Message}");
                return Row(relative, number, default, null, clock.ElapsedMilliseconds, 0, 0, $"escaped: {Clean(ex.Message)}");
            }

            trees.Append("## page ").Append(number).Append('\n').Append(PdfLayoutSnapshot.Of(tree));

            var size = Pixels(page.Size);
            var dips = new Rect(0, 0, page.Size.Width * PdfPageLayout.DipsPerPoint, page.Size.Height * PdfPageLayout.DipsPerPoint);
            try
            {
                ours = LayoutPictures.Painted(tree.Root, dips, size.Width / dips.Width, Brushes.Black, Brushes.White);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Escaped.Enqueue($"{relative} p{number} drawing: {ex.GetType().Name}: {ex.Message}");
                return Row(relative, number, size, null, clock.ElapsedMilliseconds, 0, page.Unsupported.Count, $"escaped: {Clean(ex.Message)}");
            }

            var oursMs = clock.ElapsedMilliseconds;
            if (ours is null) return Row(relative, number, size, null, oursMs, 0, page.Unsupported.Count, "ours-drew-nothing");

            clock.Restart();
            var reference = Reference(relative, number, index, windows, size, out var why);
            var windowsMs = clock.ElapsedMilliseconds;
            if (reference is null) return Row(relative, number, size, null, oursMs, windowsMs, page.Unsupported.Count, $"windows: {why}");

            var measure = Measures.Of(ours, reference);
            if (measure.Off >= ReviewShare) KeepForReview(relative, number, ours, reference);

            return Row(relative, number, size, measure, oursMs, windowsMs, page.Unsupported.Count,
                page.Unsupported.Count == 0 ? "ok" : "ok " + string.Join(';', page.Unsupported.Select(static u => u.Kind).Distinct()));
        }

        /// <summary>The page's size in pixels at the sweep's resolution, its longest side held to <see cref="LongestSide"/>.</summary>
        private (int Width, int Height) Pixels(Size points)
        {
            var width = points.Width / 72 * _dpi;
            var height = points.Height / 72 * _dpi;
            var shrink = Math.Min(1, LongestSide / Math.Max(width, height));
            return (Math.Max(1, (int)Math.Round(width * shrink)), Math.Max(1, (int)Math.Round(height * shrink)));
        }

        private static WinPdf.PdfDocument? Windows(string file)
        {
            try
            {
                return Task.Run(async () => await WinPdf.PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(file)))
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        /// <summary>
        /// Windows' render of the page, from the kept copy when it is there at this size, drawn and kept when not. A render
        /// a pixel off the size asked for is the two renderers rounding the page differently and is measured over the size
        /// they share; one further off is not this page at this size and is not measured.
        /// </summary>
        private BitmapSource? Reference(string relative, int number, int index, WinPdf.PdfDocument? windows,
            (int Width, int Height) size, out string? why)
        {
            why = null;
            var kept = Path.Combine(_work, "reference", relative, $"p{number}.png");
            if (File.Exists(kept))
            {
                var loaded = Load(File.ReadAllBytes(kept));
                if (Fits(loaded, size)) return loaded;
            }

            if (windows is null)
            {
                why = "would not open the document";
                return null;
            }

            if (index >= windows.PageCount)
            {
                why = $"has {windows.PageCount} page(s)";
                return null;
            }

            try
            {
                // Windows takes the size asked for in device-independent pixels and draws it at the scale of the display it
                // reckons it is drawing for — which changes when a monitor sleeps or wakes. Every render is checked, and one that
                // comes back another size gives the scale now, which this page and those after it are asked for by.
                var scale = Volatile.Read(ref _windowsScale);
                var asked = Requested(size, scale);
                var png = Rendered(windows, index, asked);
                var picture = Load(png);
                if (!Fits(picture, size))
                {
                    scale = (double)picture.PixelWidth / asked.Width;
                    Volatile.Write(ref _windowsScale, scale);
                    png = Rendered(windows, index, Requested(size, scale));
                    picture = Load(png);
                }

                if (!Fits(picture, size))
                {
                    why = $"drew {picture.PixelWidth}x{picture.PixelHeight} for {size.Width}x{size.Height}";
                    return null;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                File.WriteAllBytes(kept, png);
                return picture;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                why = Clean(ex.Message);
                return null;
            }
        }

        /// <summary>The display scale Windows draws a page at, as the last render that came back another size showed it.</summary>
        private static double _windowsScale = 1;

        private static (uint Width, uint Height) Requested((int Width, int Height) size, double scale) =>
            ((uint)Math.Max(1, Math.Round(size.Width / scale)), (uint)Math.Max(1, Math.Round(size.Height / scale)));

        private static bool Fits(BitmapSource picture, (int Width, int Height) size) =>
            Math.Abs(picture.PixelWidth - size.Width) <= 1 && Math.Abs(picture.PixelHeight - size.Height) <= 1;

        /// <summary>One page drawn by Windows at <paramref name="request"/>, over white, as PNG bytes.</summary>
        private static byte[] Rendered(WinPdf.PdfDocument windows, int index, (uint Width, uint Height) request) =>
            Task.Run(async () =>
            {
                using var page = windows.GetPage((uint)index);
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new WinPdf.PdfPageRenderOptions
                {
                    DestinationWidth = request.Width,
                    DestinationHeight = request.Height,
                    BackgroundColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255),
                });
                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                return bytes;
            }).GetAwaiter().GetResult();

        /// <summary>A PNG's pixels as premultiplied BGRA belonging to no thread.</summary>
        private static BitmapSource Load(byte[] png)
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            var frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
            var stride = frame.PixelWidth * 4;
            var pixels = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);
            var copy = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            copy.Freeze();
            return copy;
        }

        private void KeepForReview(string relative, int number, BitmapSource ours, BitmapSource reference)
        {
            var folder = Directory.CreateDirectory(Path.Combine(_work, "review", relative)).FullName;
            File.WriteAllBytes(Path.Combine(folder, $"p{number}-ours.png"), LayoutPictures.Png(ours));
            File.WriteAllBytes(Path.Combine(folder, $"p{number}-diff.png"), LayoutPictures.Png(Measures.Map(ours, reference)));
        }

        private void Finish(string relative, List<string> rows, StringBuilder trees)
        {
            if (trees.Length > 0)
            {
                var path = Path.Combine(_work, "trees", relative + ".txt.gz");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = File.Create(path);
                using var zip = new GZipStream(file, CompressionLevel.Optimal);
                var bytes = Encoding.UTF8.GetBytes(trees.ToString());
                zip.Write(bytes);
            }

            lock (_writing)
            {
                File.AppendAllLines(Path.Combine(_work, "pages.csv"), rows);
                File.AppendAllLines(Path.Combine(_work, "done.txt"), [relative]);
            }
        }

        private static string Row(string file, int page, (int Width, int Height) size, Measures? m, long oursMs, long windowsMs,
            int unsupported, string status) =>
            string.Join(',',
                Quote(file), page, size.Width, size.Height,
                m is null ? "" : Share(m.Identical), m is null ? "" : Share(m.Near), m is null ? "" : Share(m.Off),
                m is null ? "" : m.MeanError.ToString("0.###", CultureInfo.InvariantCulture),
                m is null ? "" : Share(m.WorstTile),
                oursMs, windowsMs, unsupported, Quote(status));

        private static string Share(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        private static string Clean(string message) => message.ReplaceLineEndings(" ").Trim();

        /// <summary>The run's pages, as a distribution: how many draw as Windows draws them, and how far the rest are.</summary>
        public string Summarise(TimeSpan elapsed, int documents, int corpusDocuments)
        {
            var rows = File.ReadLines(Path.Combine(_work, "pages.csv")).Skip(1).Select(RowOf).Where(static r => r is not null)
                .Select(static r => r!).ToList();
            var measured = rows.Where(static r => r.Off is not null).ToList();
            var off = measured.Select(static r => r.Off!.Value).Order().ToList();

            var text = new StringBuilder();
            text.AppendLine($"PDF fidelity sweep — {_corpus}, {_dpi:0} dpi, against Windows.Data.Pdf");
            text.AppendLine($"this run: {documents} document(s) in {elapsed.TotalHours:F1} h; all runs: {rows.Select(static r => r.File).Distinct().Count()} of {corpusDocuments} document(s), {rows.Count} page row(s)");
            text.AppendLine();
            text.AppendLine("Pages by outcome");
            foreach (var status in rows.GroupBy(static r => r.Status.Split(':')[0].Split(' ')[0]).OrderByDescending(static g => g.Count()))
                text.AppendLine($"  {status.Key,-20} {status.Count(),8}");

            if (off.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"Measured pages: {off.Count}");
                text.AppendLine($"  off pixels (some channel > {OffLevel} from Windows):");
                text.AppendLine($"    p50 {Percent(Percentile(off, 0.5))}  p90 {Percent(Percentile(off, 0.9))}  p99 {Percent(Percentile(off, 0.99))}  max {Percent(off[^1])}");
                foreach (var bound in new[] { 0.0, 0.001, 0.01, 0.05 })
                    text.AppendLine($"    pages with off <= {Percent(bound),7}: {off.Count(o => o <= bound),8} ({Percent((double)off.Count(o => o <= bound) / off.Count)})");
                text.AppendLine($"  identical pixels, mean over pages: {Percent(measured.Average(static r => r.Identical!.Value))}");
                text.AppendLine($"  near pixels (every channel <= {NearLevel}), mean: {Percent(measured.Average(static r => r.Near!.Value))}");
                text.AppendLine($"  mean channel error, mean over pages: {measured.Average(static r => r.MeanError!.Value):0.00} / 255");
                text.AppendLine($"  worst {Tile}px tile off share: p50 {Percent(Percentile(measured.Select(static r => r.WorstTile!.Value).Order().ToList(), 0.5))}  p99 {Percent(Percentile(measured.Select(static r => r.WorstTile!.Value).Order().ToList(), 0.99))}");

                text.AppendLine();
                text.AppendLine("Off share by what the page asked for that is not carried (pages, p50 off)");
                text.AppendLine($"  {"(nothing)",-20} {measured.Count(static r => r.Unsupported == 0),8}  {Percent(Percentile(measured.Where(static r => r.Unsupported == 0).Select(static r => r.Off!.Value).Order().ToList(), 0.5))}");
                foreach (var kind in measured.Where(static r => r.Status.StartsWith("ok ", StringComparison.Ordinal))
                             .SelectMany(static r => r.Status[3..].Split(';').Select(k => (k, r))).GroupBy(static x => x.k))
                    text.AppendLine($"  {kind.Key,-20} {kind.Count(),8}  {Percent(Percentile(kind.Select(static x => x.r.Off!.Value).Order().ToList(), 0.5))}");

                text.AppendLine();
                text.AppendLine("Worst 50 pages");
                foreach (var row in measured.OrderByDescending(static r => r.Off).Take(50))
                    text.AppendLine($"  {Percent(row.Off!.Value),8} off  {row.File} p{row.Page}  {row.Status}");
            }

            text.AppendLine();
            text.AppendLine("Seconds spent: ours " + (rows.Sum(static r => r.OursMs) / 1000.0).ToString("0", CultureInfo.InvariantCulture)
                            + ", Windows " + (rows.Sum(static r => r.WindowsMs) / 1000.0).ToString("0", CultureInfo.InvariantCulture));

            WriteReview(measured);
            return text.ToString();
        }

        private void WriteReview(List<Parsed> measured)
        {
            var html = new StringBuilder();
            html.AppendLine("<!doctype html><meta charset=utf-8><title>PDF fidelity review</title>");
            html.AppendLine("<style>body{font:13px sans-serif;background:#777}td{vertical-align:top;padding:6px}img{width:460px;background:#fff;box-shadow:0 0 4px #222}</style>");
            html.AppendLine($"<h2>Worst {Reviewed} pages by off pixels — Windows · ours · difference (red off, orange past near)</h2><table>");
            foreach (var row in measured.Where(static r => r.Off >= ReviewShare).OrderByDescending(static r => r.Off).Take(Reviewed))
            {
                var windows = Path.Combine("..", "reference", row.File, $"p{row.Page}.png").Replace('\\', '/');
                var ours = Path.Combine(row.File, $"p{row.Page}-ours.png").Replace('\\', '/');
                var diff = Path.Combine(row.File, $"p{row.Page}-diff.png").Replace('\\', '/');
                html.AppendLine($"<tr><td>{Percent(row.Off!.Value)} off<br>{WebUtility.HtmlEncode(row.File)} p{row.Page}<br>{WebUtility.HtmlEncode(row.Status)}</td>"
                                + $"<td><img loading=lazy src=\"{windows}\"></td><td><img loading=lazy src=\"{ours}\"></td><td><img loading=lazy src=\"{diff}\"></td></tr>");
            }

            html.AppendLine("</table>");
            Directory.CreateDirectory(Path.Combine(_work, "review"));
            File.WriteAllText(Path.Combine(_work, "review", "index.html"), html.ToString());
        }

        private static double Percentile(List<double> sorted, double p) =>
            sorted.Count == 0 ? double.NaN : sorted[(int)Math.Min(sorted.Count - 1, p * sorted.Count)];

        private static string Percent(double share) =>
            double.IsNaN(share) ? "-" : (share * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

        private sealed record Parsed(string File, int Page, double? Identical, double? Near, double? Off, double? MeanError,
            double? WorstTile, long OursMs, long WindowsMs, int Unsupported, string Status);

        private static Parsed? RowOf(string line)
        {
            var cells = Cells(line);
            if (cells.Count < 13) return null;
            static double? D(string s) => s.Length == 0 ? null : double.Parse(s, CultureInfo.InvariantCulture);
            return new Parsed(cells[0], int.Parse(cells[1], CultureInfo.InvariantCulture), D(cells[4]), D(cells[5]), D(cells[6]),
                D(cells[7]), D(cells[8]), long.Parse(cells[9], CultureInfo.InvariantCulture),
                long.Parse(cells[10], CultureInfo.InvariantCulture), int.Parse(cells[11], CultureInfo.InvariantCulture), cells[12]);
        }

        private static List<string> Cells(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }

            cells.Add(cell.ToString());
            return cells;
        }
    }

    /// <summary>How one page of ours stands against the reference, pixel by pixel.</summary>
    private sealed record Measures(double Identical, double Near, double Off, double MeanError, double WorstTile)
    {
        public static Measures Of(BitmapSource ours, BitmapSource reference)
        {
            var (width, height, a, b) = Pair(ours, reference);
            long identical = 0, near = 0, off = 0, error = 0;
            var tilesAcross = (width + Tile - 1) / Tile;
            var tiles = new int[tilesAcross * ((height + Tile - 1) / Tile)];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    var worst = 0;
                    for (var c = 0; c < 3; c++)
                    {
                        var d = Math.Abs(a[i + c] - b[i + c]);
                        error += d;
                        if (d > worst) worst = d;
                    }

                    if (worst == 0) identical++;
                    if (worst <= NearLevel) near++;
                    if (worst > OffLevel)
                    {
                        off++;
                        tiles[(y / Tile) * tilesAcross + x / Tile]++;
                    }
                }
            }

            double count = (double)width * height;
            return new Measures(identical / count, near / count, off / count, error / (count * 3),
                tiles.Length == 0 ? 0 : tiles.Max() / (double)(Tile * Tile));
        }

        /// <summary>
        /// Where the two differ, over a faded copy of the reference: red where a pixel is off, orange where it is past
        /// near, the reference's own grey, washed out, where they agree.
        /// </summary>
        public static BitmapSource Map(BitmapSource ours, BitmapSource reference)
        {
            var (width, height, a, b) = Pair(ours, reference);
            var map = new byte[width * height * 4];
            for (var i = 0; i < map.Length; i += 4)
            {
                var worst = 0;
                for (var c = 0; c < 3; c++) worst = Math.Max(worst, Math.Abs(a[i + c] - b[i + c]));

                var grey = (byte)(200 + (b[i] * 11 + b[i + 1] * 59 + b[i + 2] * 30) / 100 * 55 / 255);
                (map[i], map[i + 1], map[i + 2]) = worst > OffLevel ? ((byte)0, (byte)0, (byte)255)
                    : worst > NearLevel ? ((byte)0, (byte)160, (byte)255)
                    : (grey, grey, grey);
                map[i + 3] = 255;
            }

            var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, map, width * 4);
            picture.Freeze();
            return picture;
        }

        /// <summary>Both pictures' pixels as BGRA over white, over the size they share (ours can round a pixel larger).</summary>
        private static (int Width, int Height, byte[] A, byte[] B) Pair(BitmapSource ours, BitmapSource reference)
        {
            var width = Math.Min(ours.PixelWidth, reference.PixelWidth);
            var height = Math.Min(ours.PixelHeight, reference.PixelHeight);
            return (width, height, OverWhite(ours, width, height), OverWhite(reference, width, height));
        }

        private static byte[] OverWhite(BitmapSource bitmap, int width, int height)
        {
            var source = bitmap.Format == PixelFormats.Pbgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            var pixels = new byte[width * height * 4];
            source.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var clear = 255 - pixels[i + 3];
                pixels[i] = (byte)(pixels[i] + clear);
                pixels[i + 1] = (byte)(pixels[i + 1] + clear);
                pixels[i + 2] = (byte)(pixels[i + 2] + clear);
                pixels[i + 3] = 255;
            }

            return pixels;
        }
    }
}
