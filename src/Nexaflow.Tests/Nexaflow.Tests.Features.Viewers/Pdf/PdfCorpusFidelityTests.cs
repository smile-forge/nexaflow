using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
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
using PDFiumCore;
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Every page of a corpus of real PDFs drawn the way the reader draws it, and held pixel for pixel against the same page
/// drawn at the same size, in colour, by two other renderers: Windows' own (<c>Windows.Data.Pdf</c>) and PDFium, Chrome's.
/// <para>
/// PDF is a fixed format: a page has one right appearance, and two renderers that both get it right differ only where
/// their edges are antialiased. So each page is measured three ways — the share of pixels that are <em>identical</em>,
/// the share that are <em>near</em> (every channel within <see cref="NearLevel"/>: antialiasing noise), and the share
/// that are <em>off</em> (some channel past <see cref="OffLevel"/>: content one renderer has and the other has not) —
/// with the mean error, and the off share of the worst <see cref="Tile"/>-pixel tile, so one missing logo is not
/// averaged away across a page of matching text.
/// </para>
/// <para>
/// Either reference can be the one that is wrong — Windows draws some pages blank, or as noise where it cannot decode a
/// picture — so a page is judged by all three: it <em>matches</em> where ours agrees with either reference (no more than
/// <see cref="AgreeShare"/> of its pixels off), and the other is taken to be wrong; it is <em>against us</em> where the
/// references agree with each other and ours with neither, or only one drew it and ours does not agree with that one; and
/// it is an <em>edge</em> where no two of the three agree — a page every renderer draws its own way. Its measures are
/// those against the reference it is nearer.
/// </para>
/// <para>
/// Everything it writes goes beside the corpus in <c>pdf-fidelity\</c>, not into the repository:
/// <list type="bullet">
/// <item><c>reference\</c> and <c>reference-pdfium\</c> — Windows' and PDFium's render of every page, kept, so a later
/// run only redraws our side.</item>
/// <item><c>trees\</c> — every page's layout written down (<see cref="PdfLayoutSnapshot"/>), one gzipped file per
/// document: what a page's drawing is kept as once it is accepted, for a later run to compare against exactly.</item>
/// <item><c>review\</c> — our render and a difference map, against the nearer reference, of every page against us or at
/// an edge, and <c>index.html</c>: those against us worst first, then the edges.</item>
/// <item><c>pages.csv</c> and <c>summary.txt</c> — every page's measures, and their distribution.</item>
/// </list>
/// </para>
/// <para>
/// Opt-in, local and long: <c>NEXAFLOW_PDF_CORPUS</c> names the corpus and <c>NEXAFLOW_PDF_FIDELITY=1</c> asks for this
/// sweep. <c>NEXAFLOW_PDF_FIDELITY_DPI</c> sets the resolution (150 when unset); <c>NEXAFLOW_PDF_CORPUS_LIMIT</c> takes
/// only that many files. A run resumes what the same build began at the same resolution: documents it measured are
/// skipped, so stopping it costs nothing done. What another build measured is put aside under <c>runs\</c>, and the
/// references' renders are kept for every run. <c>NEXAFLOW_PDF_FIDELITY_PAGES=review</c> draws again only the pages the last run's
/// review showed, and reviews every one of them beside what it measured before — what a fix did to the pages it was for.
/// It fails only when our reading or drawing of a page throws.
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

    /// <summary>
    /// Two pictures with no more than this share of their pixels off show the same page: the pages that draw as they should
    /// stand under it, antialiasing and a pixel's rounding at an edge all that tells them apart.
    /// </summary>
    private const double AgreeShare = 0.01;

    /// <summary>A row of a run before references were judged together is reviewed with at least this share of off pixels.</summary>
    private const double ReviewShare = 0.002;

    /// <summary>The longest side a page is drawn at, whatever its size: a poster at 150 dpi is not a page anyone reads whole.</summary>
    private const int LongestSide = 2400;

    /// <summary>How many pages the review page shows.</summary>
    private const int Reviewed = 600;

    /// <summary>How long one page of ours may take to read before it is given up on.</summary>
    private static readonly TimeSpan PageBudget = TimeSpan.FromSeconds(60);

    /// <summary>What a page is judged, by <see cref="AgreeShare"/> — see the class's summary.</summary>
    private static class Verdict
    {
        public const string Matches = "matches";
        public const string Against = "against";
        public const string Edge = "edge";

        /// <param name="ours">Our page's share of off pixels against each reference, null where it did not draw the page.</param>
        /// <param name="between">The least share of off pixels between any two references that drew it, null where fewer did.</param>
        public static string Of(IReadOnlyList<double?> ours, double? between)
        {
            if (ours.Any(static o => o <= AgreeShare)) return Matches;
            if (ours.Count(static o => o is not null) == 1) return Against;   // one reference: it is all there is to go by
            return between <= AgreeShare ? Against : Edge;
        }
    }

    /// <summary>The renderers a page is held against, in the order their columns are written.</summary>
    private static readonly ReferenceRenderer[] References = [new WindowsReference(), new PdfiumReference()];

    private static readonly string Header =
        "file,page,width,height,identical,near,off,mae,worsttile,ours_ms,unsupported,status,verdict,nearer,references_off"
        + string.Concat(References.Select(static r => $",{r.Key}_off,{r.Key}_ms")) + ",notes";

    [TestMethod]
    public void EveryPage_IsDrawnAndHeldAgainstTheReferences()
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

        var run = new Run(corpus, work, dpi, revisit: Environment.GetEnvironmentVariable("NEXAFLOW_PDF_FIDELITY_PAGES") == "review");
        if (run.Revisiting is { } revisiting)
        {
            if (revisiting.Count == 0)
            {
                Assert.Inconclusive($"NEXAFLOW_PDF_FIDELITY_PAGES=review revisits the pages the last run's review showed, and {work} has no run with any.");
                return;
            }

            files = files.Where(f => revisiting.ContainsKey(run.Relative(f))).ToList();
        }

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

        public Run(string corpus, string work, double dpi, bool revisit)
        {
            _corpus = corpus;
            _work = work;
            _dpi = dpi;

            var pages = Path.Combine(work, "pages.csv");
            var stamp = Path.Combine(work, "run.txt");

            // A revisit is a run of its own over the pages the last run reviewed, those pages' measures kept to set beside the
            // new ones: read before that run is put aside, and stamped apart so a whole run never resumes from it.
            if (revisit)
            {
                Revisiting = [];
                if (File.Exists(pages))
                    foreach (var row in ReviewedBy(pages))
                    {
                        if (!Revisiting.TryGetValue(row.File, out var numbers)) Revisiting[row.File] = numbers = [];
                        numbers.Add(row.Page);
                        _before[(row.File, row.Page)] = row;
                    }
            }

            // Rows measure one build's drawing at one resolution, so a run resumes only what that build at that resolution
            // began; what any other left is put aside.
            var build = $"{typeof(PdfPageLayout).Assembly.ManifestModule.ModuleVersionId} {dpi.ToString(CultureInfo.InvariantCulture)} dpi"
                        + (revisit ? " review" : "");
            if (File.Exists(pages) && (revisit || !File.Exists(stamp) || File.ReadAllText(stamp).Trim() != build)) PutAside();
            File.WriteAllText(stamp, build);

            if (!File.Exists(pages)) File.WriteAllText(pages, Header + "\n");

            var done = Path.Combine(work, "done.txt");
            Done = File.Exists(done) ? File.ReadAllLines(done).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        }

        /// <summary>The pages a revisit draws again, by document, or null for a run over every page.</summary>
        public Dictionary<string, HashSet<int>>? Revisiting { get; }

        /// <summary>What the run before a revisit measured of the pages it draws again.</summary>
        private readonly Dictionary<(string File, int Page), Parsed> _before = new();

        /// <summary>The pages a run's review shows: those against us, worst first, then the edges, as many as it holds.</summary>
        private static IEnumerable<Parsed> ReviewedBy(string pages) =>
            Rows(pages).Where(static r => r.Off is not null && Shown(r)).OrderBy(static r => r.Verdict == Verdict.Edge)
                .ThenByDescending(static r => r.Off).Take(Reviewed);

        /// <summary>Whether the review shows a page: it is against us or at an edge — or, measured before references were judged
        /// together, it has enough off pixels against Windows.</summary>
        private static bool Shown(Parsed row) => row.Verdict is { } verdict ? verdict != Verdict.Matches : row.Off >= ReviewShare;

        /// <summary>Moves another run's rows, review and trees under <c>runs\</c>; the references' renders stay, the same for every run.</summary>
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

        /// <summary>Every page of one document, ours and the references', measured; its rows written once it is finished.</summary>
        public void Document(string file)
        {
            var relative = Relative(file);
            var rows = new List<string>();
            var trees = new StringBuilder();
            var opened = new ReferenceDocument?[References.Length];

            try
            {
                using var open = new CancellationTokenSource(PageBudget);
                using var scope = PdfDocumentScope.TryOpen(file, open.Token);
                if (scope is null)
                {
                    rows.Add(Row(relative, 0, default, null, 0, 0, "unopenable", null));
                    return;
                }

                // The scope holds its token for as long as it is open, and closes the file when it fires: the budget is
                // for opening only, so it is disarmed once the document is open. Each page has its own.
                open.CancelAfter(Timeout.InfiniteTimeSpan);
                var document = scope.Pages.ReadDocument(CancellationToken.None);
                for (var r = 0; r < References.Length; r++) opened[r] = References[r].Open(file);

                var chosen = Revisiting?[relative];
                for (var index = 0; index < document.Pages.Count; index++)
                    if (chosen is null || chosen.Contains(document.Pages[index].Number))
                        rows.Add(Page(relative, scope, document.Pages[index], index, opened, trees));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Escaped.Enqueue($"{relative}: {ex.GetType().Name}: {ex.Message}");
                rows.Add(Row(relative, 0, default, null, 0, 0, $"escaped: {Clean(ex.Message)}", null));
            }
            finally
            {
                foreach (var reference in opened) reference?.Dispose();
                Finish(relative, rows, trees);
            }
        }

        private string Page(string relative, PdfDocumentScope scope, PdfPageSlot slot, int index, ReferenceDocument?[] opened,
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
                return Row(relative, number, default, null, clock.ElapsedMilliseconds, 0, "timeout", null);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Escaped.Enqueue($"{relative} p{number}: {ex.GetType().Name}: {ex.Message}");
                return Row(relative, number, default, null, clock.ElapsedMilliseconds, 0, $"escaped: {Clean(ex.Message)}", null);
            }

            trees.Append("## page ").Append(number).Append('\n').Append(PdfLayoutSnapshot.Of(tree));

            var size = Pixels(page.Size);
            var dips = new Rect(0, 0, page.Size.Width * PdfPageLayout.DipsPerPoint, page.Size.Height * PdfPageLayout.DipsPerPoint);
            try
            {
                ours = WpfPainter.Painted(tree.Root, dips, PixelsPerPoint(page.Size) / PdfPageLayout.DipsPerPoint,
                    Brushes.Black, Brushes.White);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Escaped.Enqueue($"{relative} p{number} drawing: {ex.GetType().Name}: {ex.Message}");
                return Row(relative, number, size, null, clock.ElapsedMilliseconds, page.Unsupported.Count, $"escaped: {Clean(ex.Message)}", null);
            }

            var oursMs = clock.ElapsedMilliseconds;
            if (ours is null) return Row(relative, number, size, null, oursMs, page.Unsupported.Count, "ours-drew-nothing", null);

            var pictures = new BitmapSource?[References.Length];
            var held = new Held[References.Length];
            var unmeasured = new List<string>();
            for (var r = 0; r < References.Length; r++)
            {
                clock.Restart();
                pictures[r] = Reference(References[r], opened[r], relative, number, index, size, out var why);
                var ms = clock.ElapsedMilliseconds;
                held[r] = new Held(pictures[r] is { } picture ? Measures.Of(ours, picture) : null, ms);
                if (why is not null) unmeasured.Add($"{References[r].Name} {why}");
            }

            if (pictures.All(static p => p is null))
                return Row(relative, number, size, null, oursMs, page.Unsupported.Count, "no reference: " + string.Join("; ", unmeasured), null, held: held);

            double? between = null;
            for (var a = 0; a < pictures.Length; a++)
                for (var b = a + 1; b < pictures.Length; b++)
                    if (pictures[a] is { } first && pictures[b] is { } second)
                        between = Math.Min(between ?? 1, Measures.Of(first, second).Off);

            var nearer = Enumerable.Range(0, held.Length).Where(r => held[r].Measures is not null).MinBy(r => held[r].Measures!.Off);
            var verdict = Verdict.Of([.. held.Select(static h => h.Measures?.Off)], between);
            // A revisited page is reviewed whatever it now measures: how far it came is the point.
            if (verdict != Verdict.Matches || Revisiting is not null) KeepForReview(relative, number, ours, pictures[nearer]!);

            var status = page.Unsupported.Count == 0 ? "ok" : "ok " + string.Join(';', page.Unsupported.Select(static u => u.Kind).Distinct());
            return Row(relative, number, size, held[nearer].Measures, oursMs, page.Unsupported.Count, status, verdict,
                References[nearer].Key, between, held, string.Join("; ", unmeasured));
        }

        /// <summary>Our page against one reference: how it measured, null where the reference did not draw it, and how long the
        /// reference took.</summary>
        private sealed record Held(Measures? Measures, long Ms);

        /// <summary>The page's size in pixels at the sweep's resolution, its longest side held to <see cref="LongestSide"/>.</summary>
        private (int Width, int Height) Pixels(Size points)
        {
            var scale = PixelsPerPoint(points);
            return (Math.Max(1, (int)Math.Round(points.Width * scale)), Math.Max(1, (int)Math.Round(points.Height * scale)));
        }

        /// <summary>
        /// How many pixels a point is drawn at: the sweep's resolution, less where the page's longest side would pass
        /// <see cref="LongestSide"/>. Ours is drawn at exactly this, never at a scale taken back from a rounded width, which
        /// on a tall page falls short of Windows' by a pixel for every few hundred.
        /// </summary>
        private double PixelsPerPoint(Size points) =>
            _dpi / 72 * Math.Min(1, LongestSide / (Math.Max(points.Width, points.Height) / 72 * _dpi));

        /// <summary>
        /// The <paramref name="renderer"/>'s render of the page, from the kept copy when it is there at this size, drawn and kept
        /// when not. A render a pixel off the size asked for is the renderers rounding the page differently and is measured
        /// over the size they share; one further off is not this page at this size and is not measured.
        /// </summary>
        private BitmapSource? Reference(ReferenceRenderer renderer, ReferenceDocument? document, string relative, int number, int index,
            (int Width, int Height) size, out string? why)
        {
            why = null;
            var kept = Path.Combine(_work, renderer.Folder, relative, $"p{number}.png");
            if (File.Exists(kept))
            {
                var loaded = Load(File.ReadAllBytes(kept));
                if (Fits(loaded, size)) return loaded;
            }

            if (document is null)
            {
                why = "would not open the document";
                return null;
            }

            if (index >= document.PageCount)
            {
                why = $"has {document.PageCount} page(s)";
                return null;
            }

            try
            {
                var png = document.Render(index, size);
                var picture = Load(png);
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

        private void KeepForReview(string relative, int number, BitmapSource ours, BitmapSource reference)
        {
            var folder = Directory.CreateDirectory(Path.Combine(_work, "review", relative)).FullName;
            File.WriteAllBytes(Path.Combine(folder, $"p{number}-ours.png"), WpfPainter.Png(ours));
            File.WriteAllBytes(Path.Combine(folder, $"p{number}-diff.png"), WpfPainter.Png(Measures.Map(ours, reference)));
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

        private static string Row(string file, int page, (int Width, int Height) size, Measures? m, long oursMs, int unsupported,
            string status, string? verdict, string? nearer = null, double? between = null, IReadOnlyList<Held>? held = null,
            string notes = "") =>
            string.Join(',',
                Quote(file), page, size.Width, size.Height,
                m is null ? "" : Share(m.Identical), m is null ? "" : Share(m.Near), m is null ? "" : Share(m.Off),
                m is null ? "" : m.MeanError.ToString("0.###", CultureInfo.InvariantCulture),
                m is null ? "" : Share(m.WorstTile),
                oursMs, unsupported, Quote(status), verdict ?? "", nearer ?? "", between is { } b ? Share(b) : "",
                string.Join(',', Enumerable.Range(0, References.Length).Select(r =>
                    held is null ? "," : $"{(held[r].Measures is { } h ? Share(h.Off) : "")},{held[r].Ms}")),
                Quote(notes));

        private static string Share(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        private static string Clean(string message) => message.ReplaceLineEndings(" ").Trim();

        /// <summary>The run's pages, as a distribution: how many match a reference, how many are against us or at an edge, and how
        /// far from the nearer reference they stand.</summary>
        public string Summarise(TimeSpan elapsed, int documents, int corpusDocuments)
        {
            var rows = Rows(Path.Combine(_work, "pages.csv"));
            var measured = rows.Where(static r => r.Off is not null).ToList();
            var off = measured.Select(static r => r.Off!.Value).Order().ToList();
            string Distribution(IEnumerable<double> shares)
            {
                var sorted = shares.Order().ToList();
                return $"p50 {Percent(Percentile(sorted, 0.5))}  p90 {Percent(Percentile(sorted, 0.9))}  p99 {Percent(Percentile(sorted, 0.99))}";
            }

            var text = new StringBuilder();
            text.AppendLine($"PDF fidelity sweep — {_corpus}, {_dpi:0} dpi, against {string.Join(" and ", References.Select(static r => r.Name))}");
            text.AppendLine($"  a page matches where ours agrees with either reference (off <= {Percent(AgreeShare)}); it is against us where the");
            text.AppendLine("  references agree and ours with neither, or the one that drew it disagrees; an edge where no two of the three agree");
            text.AppendLine($"this run: {documents} document(s) in {elapsed.TotalHours:F1} h; all runs: {rows.Select(static r => r.File).Distinct().Count()} of {corpusDocuments} document(s), {rows.Count} page row(s)");
            text.AppendLine();
            text.AppendLine("Pages by outcome");
            foreach (var status in rows.GroupBy(static r => r.Status.Split(':')[0].Split(' ')[0]).OrderByDescending(static g => g.Count()))
                text.AppendLine($"  {status.Key,-20} {status.Count(),8}");

            if (off.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"Measured pages: {off.Count}");
                foreach (var verdict in new[] { Verdict.Matches, Verdict.Against, Verdict.Edge })
                {
                    var count = measured.Count(r => r.Verdict == verdict);
                    text.AppendLine($"  {verdict,-10} {count,8} ({Percent((double)count / off.Count)})");
                }

                text.AppendLine("  of the pages that match, the reference they match:");
                foreach (var reference in References)
                {
                    var only = measured.Count(r => r.Verdict == Verdict.Matches && r.OffBy.GetValueOrDefault(reference.Key) <= AgreeShare
                                                   && References.Where(o => o != reference).All(o => !(r.OffBy.GetValueOrDefault(o.Key) <= AgreeShare)));
                    text.AppendLine($"    {reference.Name} alone {only,8}");
                }
                text.AppendLine($"    every one  {measured.Count(r => r.Verdict == Verdict.Matches && References.All(o => r.OffBy.GetValueOrDefault(o.Key) <= AgreeShare)),8}");

                text.AppendLine();
                text.AppendLine($"  off pixels (some channel > {OffLevel}) from the nearer reference:");
                text.AppendLine($"    {Distribution(off)}  max {Percent(off[^1])}");
                foreach (var bound in new[] { 0.0, 0.001, 0.01, 0.05 })
                    text.AppendLine($"    pages with off <= {Percent(bound),7}: {off.Count(o => o <= bound),8} ({Percent((double)off.Count(o => o <= bound) / off.Count)})");
                foreach (var reference in References)
                    text.AppendLine($"  from {reference.Name} alone: {Distribution(measured.Select(r => r.OffBy.GetValueOrDefault(reference.Key)).OfType<double>())}");
                text.AppendLine($"  the references from each other: {Distribution(measured.Select(static r => r.Between).OfType<double>())}");
                text.AppendLine($"  identical pixels, mean over pages: {Percent(measured.Average(static r => r.Identical!.Value))}");
                text.AppendLine($"  near pixels (every channel <= {NearLevel}), mean: {Percent(measured.Average(static r => r.Near!.Value))}");
                text.AppendLine($"  mean channel error, mean over pages: {measured.Average(static r => r.MeanError!.Value):0.00} / 255");
                text.AppendLine($"  worst {Tile}px tile off share: {Distribution(measured.Select(static r => r.WorstTile!.Value))}");

                text.AppendLine();
                text.AppendLine("Pages against us by what the page asked for that is not carried (pages, of measured pages with it)");
                text.AppendLine($"  {"(nothing)",-20} {measured.Count(static r => r.Unsupported == 0 && r.Verdict == Verdict.Against),8} of {measured.Count(static r => r.Unsupported == 0)}");
                foreach (var kind in measured.Where(static r => r.Status.StartsWith("ok ", StringComparison.Ordinal))
                             .SelectMany(static r => r.Status[3..].Split(';').Select(k => (k, r))).GroupBy(static x => x.k))
                    text.AppendLine($"  {kind.Key,-20} {kind.Count(static x => x.r.Verdict == Verdict.Against),8} of {kind.Count()}");

                if (Revisiting is not null)
                {
                    var changes = measured.Select(Change).Where(static c => c is not null).Select(static c => c!.Value).ToList();
                    text.AppendLine();
                    text.AppendLine($"Against the run before, by share of off pixels ({changes.Count} pages measured both times; a row measured");
                    text.AppendLine("against Windows alone is set beside this run's off from Windows)");
                    text.AppendLine($"  better by more than 0.5 points: {changes.Count(static c => c > 0.005),6}");
                    text.AppendLine($"  within 0.5 points:              {changes.Count(static c => Math.Abs(c) <= 0.005),6}");
                    text.AppendLine($"  worse by more than 0.5 points:  {changes.Count(static c => c < -0.005),6}");
                }

                foreach (var (verdict, title) in new[] { (Verdict.Against, "Worst 50 pages against us"), (Verdict.Edge, "Worst 50 edges") })
                {
                    text.AppendLine();
                    text.AppendLine(title);
                    foreach (var row in measured.Where(r => r.Verdict == verdict).OrderByDescending(static r => r.Off).Take(50))
                        text.AppendLine($"  {Percent(row.Off!.Value),8} off  {row.File} p{row.Page}  {row.Status}"
                                        + string.Concat(References.Select(r => $"  {r.Name} {(row.OffBy.GetValueOrDefault(r.Key) is { } o ? Percent(o) : "-")}"))
                                        + $"  between {(row.Between is { } b ? Percent(b) : "-")}");
                }
            }

            text.AppendLine();
            text.AppendLine("Seconds spent: ours " + (rows.Sum(static r => r.OursMs) / 1000.0).ToString("0", CultureInfo.InvariantCulture)
                            + string.Concat(References.Select(r => $", {r.Name} " + (rows.Sum(x => x.MsBy.GetValueOrDefault(r.Key)) / 1000.0).ToString("0", CultureInfo.InvariantCulture))));

            WriteReview(measured);
            return text.ToString();
        }

        /// <summary>How wide a page is shown in the review, in CSS pixels.</summary>
        private const double ShownWidth = 460;

        private void WriteReview(List<Parsed> measured)
        {
            var revisit = Revisiting is not null;
            // Those against us first, then the edges, then — a revisit shows every page it drew again — those that match; worst
            // first within each, or in a revisit most improved first.
            static int Group(Parsed row) => row.Verdict switch { Verdict.Against => 0, Verdict.Edge => 1, Verdict.Matches => 2, _ => 0 };
            var shown = (revisit ? measured : measured.Where(Shown).Take(Reviewed)).OrderBy(Group)
                .ThenByDescending(r => revisit ? Change(r) ?? 0 : 0).ThenByDescending(static r => r.Off).ToList();
            var headings = new Dictionary<int, string> { [0] = "against us", [1] = "edges: no two renderers agree", [2] = "matching a reference" };

            var html = new StringBuilder();
            var pictures = string.Join(" · ", References.Select(static r => r.Name));
            html.AppendLine("<!doctype html><meta charset=utf-8><title>PDF fidelity review</title>");
            html.AppendLine("<style>body{font:13px sans-serif;background:#777}td{vertical-align:top;padding:6px}img{background:#fff;box-shadow:0 0 4px #222}.better{color:#cfc}.worse{color:#fcc}.nearer{outline:3px solid #8cf}</style>");
            html.AppendLine(revisit
                ? $"<h2>The {shown.Count} pages the last review showed, drawn again, most improved first in each group — {pictures} · ours · difference from the nearer reference, outlined (red off, orange past near)</h2><table>"
                : $"<h2>Pages against us, worst first, then the edges — {pictures} · ours · difference from the nearer reference, outlined (red off, orange past near)</h2><table>");
            var group = -1;
            foreach (var row in shown)
            {
                if (Group(row) != group)
                {
                    group = Group(row);
                    html.AppendLine($"<tr><th colspan={References.Length + 3} style=\"text-align:left;font-size:18px;padding-top:24px\">"
                                    + $"{shown.Count(r => Group(r) == group)} {headings[group]}</th></tr>");
                }
                var ours = Path.Combine(row.File, $"p{row.Page}-ours.png").Replace('\\', '/');
                var diff = Path.Combine(row.File, $"p{row.Page}-diff.png").Replace('\\', '/');
                var change = Change(row);
                var was = change is { } c
                    ? $"<br><span class={(c > 0 ? "better" : c < 0 ? "worse" : "same")}>{(c > 0 ? "better" : c < 0 ? "worse" : "the same")} by {Percent(Math.Abs(c))}</span>"
                    : "";
                // The pictures are shown at one scale, the page's width brought to 460 pixels, never each at one width: a picture
                // a pixel or two wider than another would otherwise be drawn smaller, and its page look shorter than it is.
                var zoom = (ShownWidth / Math.Max(1, row.Width)).ToString("0.####", CultureInfo.InvariantCulture);
                var img = $"loading=lazy style=\"zoom:{zoom}\"";
                var references = string.Concat(References.Select(r =>
                {
                    var source = Path.Combine("..", r.Folder, row.File, $"p{row.Page}.png").Replace('\\', '/');
                    var measure = row.OffBy.GetValueOrDefault(r.Key) is { } o ? $"{r.Name}: {Percent(o)} off" : $"{r.Name}: not drawn";
                    return $"<td>{WebUtility.HtmlEncode(measure)}<br><img {img} class=\"{(row.Nearer == r.Key ? "nearer" : "")}\" src=\"{source}\"></td>";
                }));
                html.AppendLine($"<tr><td>{WebUtility.HtmlEncode(row.Verdict ?? "")} {Percent(row.Off!.Value)} off{was}<br>{WebUtility.HtmlEncode(row.File)} p{row.Page}"
                                + $"<br>{WebUtility.HtmlEncode(row.Status)}<br>references {(row.Between is { } b ? Percent(b) : "-")} apart</td>"
                                + $"{references}<td>ours<br><img {img} src=\"{ours}\"></td><td>difference<br><img {img} src=\"{diff}\"></td></tr>");
            }

            html.AppendLine("</table>");
            Directory.CreateDirectory(Path.Combine(_work, "review"));
            File.WriteAllText(Path.Combine(_work, "review", "index.html"), html.ToString());
        }

        /// <summary>
        /// How much a revisited page's share of off pixels fell since the run before, or null where it was not measured then. A
        /// row the run before measured against Windows alone is set beside this run's off from Windows, so that a page now judged
        /// by both references does not seem to have improved by that alone.
        /// </summary>
        private double? Change(Parsed row)
        {
            if (!_before.TryGetValue((row.File, row.Page), out var before) || before.Off is not { } then) return null;
            var now = before.Verdict is null ? row.OffBy.GetValueOrDefault("windows") : row.Off;
            return now is { } value ? then - value : null;
        }

        private static double Percentile(List<double> sorted, double p) =>
            sorted.Count == 0 ? double.NaN : sorted[(int)Math.Min(sorted.Count - 1, p * sorted.Count)];

        private static string Percent(double share) =>
            double.IsNaN(share) ? "-" : (share * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

        /// <param name="Verdict">What the page was judged; null in a row of a run that held pages against Windows alone.</param>
        /// <param name="Nearer">The key of the reference its measures are against.</param>
        /// <param name="Between">The share of off pixels between the references.</param>
        /// <param name="OffBy">Its share of off pixels against each reference, by key, null where that one did not draw it.</param>
        /// <param name="MsBy">How long each reference took, by key.</param>
        private sealed record Parsed(string File, int Page, int Width, double? Identical, double? Near, double? Off, double? MeanError,
            double? WorstTile, long OursMs, int Unsupported, string Status, string? Verdict, string? Nearer, double? Between,
            IReadOnlyDictionary<string, double?> OffBy, IReadOnlyDictionary<string, long> MsBy);

        /// <summary>
        /// The rows of a <c>pages.csv</c>, each read by the names its header gives its columns — so a run's rows read whichever
        /// references it held pages against, and a run that held them against Windows alone reads as having measured its
        /// <c>off</c> against Windows.
        /// </summary>
        private static List<Parsed> Rows(string pages)
        {
            var lines = File.ReadLines(pages).GetEnumerator();
            if (!lines.MoveNext()) return [];
            var columns = Cells(lines.Current).Select(static (name, at) => (name, at)).ToDictionary(static c => c.name, static c => c.at);
            var keys = columns.Keys.Where(static c => c.EndsWith("_off", StringComparison.Ordinal) && c != "references_off")
                .Select(static c => c[..^4]).ToList();
            var judged = columns.ContainsKey("verdict");
            var rows = new List<Parsed>();
            while (lines.MoveNext())
            {
                var cells = Cells(lines.Current);
                if (cells.Count < columns.Count) continue;
                string Cell(string name) => columns.TryGetValue(name, out var at) ? cells[at] : "";
                static double? D(string s) => s.Length == 0 ? null : double.Parse(s, CultureInfo.InvariantCulture);
                static long L(string s) => s.Length == 0 ? 0 : long.Parse(s, CultureInfo.InvariantCulture);
                var off = D(Cell("off"));
                rows.Add(new Parsed(Cell("file"), int.Parse(Cell("page"), CultureInfo.InvariantCulture), int.Parse(Cell("width"), CultureInfo.InvariantCulture),
                    D(Cell("identical")), D(Cell("near")), off, D(Cell("mae")), D(Cell("worsttile")), L(Cell("ours_ms")),
                    int.Parse(Cell("unsupported"), CultureInfo.InvariantCulture), Cell("status"),
                    judged && Cell("verdict").Length > 0 ? Cell("verdict") : null, Cell("nearer") is { Length: > 0 } nearer ? nearer : null,
                    D(Cell("references_off")),
                    judged ? keys.ToDictionary(static k => k, k => D(Cell(k + "_off"))) : new Dictionary<string, double?> { ["windows"] = off },
                    judged ? keys.ToDictionary(static k => k, k => L(Cell(k + "_ms"))) : new Dictionary<string, long> { ["windows"] = L(Cell("windows_ms")) }));
            }

            return rows;
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

    /// <summary>A renderer pages are held against, its renders kept beside the corpus in a folder of its own.</summary>
    private abstract class ReferenceRenderer
    {
        /// <summary>What the summary and the review call it.</summary>
        public abstract string Name { get; }

        /// <summary>What its columns in <c>pages.csv</c> are named by.</summary>
        public abstract string Key { get; }

        /// <summary>The folder under <c>pdf-fidelity\</c> its renders are kept in.</summary>
        public abstract string Folder { get; }

        /// <summary>The document as this renderer reads it, or null where it will not open it.</summary>
        public abstract ReferenceDocument? Open(string file);
    }

    /// <summary>One document a <see cref="ReferenceRenderer"/> has open.</summary>
    private abstract class ReferenceDocument : IDisposable
    {
        public abstract int PageCount { get; }

        /// <summary>The page at <paramref name="index"/> drawn over white at <paramref name="size"/>, as a PNG — a pixel either
        /// way of it is the renderer rounding the page.</summary>
        public abstract byte[] Render(int index, (int Width, int Height) size);

        public virtual void Dispose() { }
    }

    /// <summary>Windows' own renderer, <c>Windows.Data.Pdf</c>.</summary>
    private sealed class WindowsReference : ReferenceRenderer
    {
        /// <summary>The display scale Windows draws a page at, as the last render that came back another size showed it.</summary>
        private static double _scale = 1;

        public override string Name => "Windows";

        public override string Key => "windows";

        public override string Folder => "reference";

        public override ReferenceDocument? Open(string file)
        {
            try
            {
                return new Document(Task.Run(async () => await WinPdf.PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(file)))
                    .GetAwaiter().GetResult());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        private static (uint Width, uint Height) Requested((int Width, int Height) size, double scale) =>
            ((uint)Math.Max(1, Math.Round(size.Width / scale)), (uint)Math.Max(1, Math.Round(size.Height / scale)));

        private sealed class Document(WinPdf.PdfDocument document) : ReferenceDocument
        {
            public override int PageCount => (int)document.PageCount;

            // Windows takes the size asked for in device-independent pixels and draws it at the scale of the display it reckons
            // it is drawing for — which changes when a monitor sleeps or wakes. Every render is checked, and one that comes back
            // another size gives the scale now, which this page and those after it are asked for by.
            public override byte[] Render(int index, (int Width, int Height) size)
            {
                var asked = Requested(size, Volatile.Read(ref _scale));
                var png = Rendered(index, asked);
                var picture = Load(png);
                if (Fits(picture, size)) return png;

                var scale = (double)picture.PixelWidth / asked.Width;
                Volatile.Write(ref _scale, scale);
                return Rendered(index, Requested(size, scale));
            }

            private byte[] Rendered(int index, (uint Width, uint Height) request) =>
                Task.Run(async () =>
                {
                    using var page = document.GetPage((uint)index);
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
        }
    }

    /// <summary>PDFium, Chrome's renderer, drawing a page as Chrome shows it: its annotations and form fields with it.</summary>
    private sealed class PdfiumReference : ReferenceRenderer
    {
        /// <summary>PDFium keeps state of its own across documents: one call into it at a time.</summary>
        private static readonly object Gate = new();

        /// <summary>Annotations drawn, as a viewer draws them (<c>FPDF_ANNOT</c>).</summary>
        private const int Annotations = 0x01;

        /// <summary>A bitmap of blue, green, red and alpha bytes (<c>FPDFBitmap_BGRA</c>).</summary>
        private const int Bgra = 4;

        private static bool _started;

        public override string Name => "PDFium";

        public override string Key => "pdfium";

        public override string Folder => "reference-pdfium";

        public override ReferenceDocument? Open(string file)
        {
            lock (Gate)
            {
                if (!_started)
                {
                    fpdfview.FPDF_InitLibrary();
                    _started = true;
                }

                var document = fpdfview.FPDF_LoadDocument(file, null);
                return document is null || document.__Instance == IntPtr.Zero ? null : new Document(document);
            }
        }

        private sealed class Document : ReferenceDocument
        {
            private readonly FpdfDocumentT _document;

            // Held for as long as the form environment it is given to, which keeps a pointer to it.
            private readonly FPDF_FORMFILLINFO _formInfo = new() { Version = 1 };
            private readonly FpdfFormHandleT? _form;

            public Document(FpdfDocumentT document)
            {
                _document = document;
                _form = fpdf_formfill.FPDFDOC_InitFormFillEnvironment(document, _formInfo);
                PageCount = fpdfview.FPDF_GetPageCount(document);
            }

            public override int PageCount { get; }

            private bool HasForm => _form is not null && _form.__Instance != IntPtr.Zero;

            public override byte[] Render(int index, (int Width, int Height) size)
            {
                BitmapSource picture;
                lock (Gate)
                {
                    var page = fpdfview.FPDF_LoadPage(_document, index);
                    if (page is null || page.__Instance == IntPtr.Zero) throw new InvalidOperationException("PDFium could not load the page.");
                    try
                    {
                        if (HasForm) fpdf_formfill.FORM_OnAfterLoadPage(page, _form);
                        var bitmap = fpdfview.FPDFBitmapCreateEx(size.Width, size.Height, Bgra, IntPtr.Zero, 0);
                        if (bitmap is null || bitmap.__Instance == IntPtr.Zero)
                            throw new InvalidOperationException($"PDFium could not make a {size.Width}x{size.Height} bitmap.");
                        try
                        {
                            fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, size.Width, size.Height, 0xFFFFFFFF);
                            fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, size.Width, size.Height, 0, Annotations);
                            if (HasForm) fpdf_formfill.FPDF_FFLDraw(_form, bitmap, page, 0, 0, size.Width, size.Height, 0, Annotations);
                            var stride = fpdfview.FPDFBitmapGetStride(bitmap);
                            var pixels = new byte[stride * size.Height];
                            Marshal.Copy(fpdfview.FPDFBitmapGetBuffer(bitmap), pixels, 0, pixels.Length);
                            picture = BitmapSource.Create(size.Width, size.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                        }
                        finally
                        {
                            fpdfview.FPDFBitmapDestroy(bitmap);
                        }

                        if (HasForm) fpdf_formfill.FORM_OnBeforeClosePage(page, _form);
                    }
                    finally
                    {
                        fpdfview.FPDF_ClosePage(page);
                    }
                }

                picture.Freeze();
                return WpfPainter.Png(picture);
            }

            public override void Dispose()
            {
                lock (Gate)
                {
                    if (HasForm) fpdf_formfill.FPDFDOC_ExitFormFillEnvironment(_form);
                    fpdfview.FPDF_CloseDocument(_document);
                }

                _formInfo.Dispose();
            }
        }
    }

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

    /// <summary>Whether a render is the page at <paramref name="size"/>, a pixel either way for the renderer's rounding.</summary>
    private static bool Fits(BitmapSource picture, (int Width, int Height) size) =>
        Math.Abs(picture.PixelWidth - size.Width) <= 1 && Math.Abs(picture.PixelHeight - size.Height) <= 1;
}
