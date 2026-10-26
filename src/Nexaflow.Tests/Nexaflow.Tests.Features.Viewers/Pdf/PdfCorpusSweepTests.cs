using Nexaflow.IO.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Every page of a corpus of real PDFs, read as objects — what the native renderer will draw from — and tallied:
/// what failed to open, what threw out of a page instead of being kept as damage, what pages asked for that the
/// reading does not carry, and how long pages took.
/// <para>
/// Opt-in and local: <c>NEXAFLOW_PDF_CORPUS</c> points at a folder of PDFs (searched recursively), and the report goes
/// beside it in <c>pdf-sweep\</c>, not into the repository. <c>NEXAFLOW_PDF_CORPUS_LIMIT</c> reads only the first that
/// many files, in path order, for a quick pass.
/// </para>
/// <para>
/// The one thing that fails it is an exception escaping a page read: damaged content is the interpreter's to keep
/// as <see cref="PdfUnsupportedKind.DamagedContent"/>, so an escape is a bug in the reading, never in the document.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("opt-in sweep over an external corpus of real PDFs")]
public class PdfCorpusSweepTests
{
    /// <summary>How long one file may take before it is given up on and counted as a timeout.</summary>
    private static readonly TimeSpan FileBudget = TimeSpan.FromSeconds(90);

    [TestMethod]
    public void EveryPageOfTheCorpusReads_WithNothingEscapingTheInterpreter()
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_PDF_CORPUS") is not { Length: > 0 } corpus)
        {
            Assert.Inconclusive("NEXAFLOW_PDF_CORPUS names no corpus of PDFs on this machine.");
            return;
        }

        var files = Directory.EnumerateFiles(corpus, "*.pdf", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        if (int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_PDF_CORPUS_LIMIT"), out var limit) && limit > 0)
            files = files.Take(limit).ToList();

        var clock = Stopwatch.StartNew();
        var results = new ConcurrentBag<FileResult>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file => results.Add(Read(file)));

        var report = Report(results.OrderBy(r => r.Path, StringComparer.Ordinal).ToList(), corpus, clock.Elapsed);
        var folder = Directory.CreateDirectory(Path.Combine(corpus, "pdf-sweep")).FullName;
        File.WriteAllText(Path.Combine(folder, "report.txt"), report);

        var escaped = results.Where(r => r.Escaped.Count > 0).ToList();
        if (escaped.Count > 0)
            Assert.Fail($"{escaped.Sum(r => r.Escaped.Count)} page read(s) in {escaped.Count} file(s) threw out of the "
                        + $"interpreter. Report: {Path.Combine(folder, "report.txt")}\n"
                        + string.Join('\n', escaped.Take(10).Select(r => $"  {r.Path}: {r.Escaped[0]}")));
    }

    private static FileResult Read(string path)
    {
        var result = new FileResult(path);
        using var budget = new CancellationTokenSource(FileBudget);
        try
        {
            using var scope = PdfDocumentScope.TryOpen(File.OpenRead(path), budget.Token);
            if (scope is null)
            {
                result.Unopenable = true;
                return result;
            }

            var reader = scope.Pages;
            var document = reader.ReadDocument(budget.Token);
            result.Pages = document.Pages.Count;
            result.Fields = document.Fields.Count;

            foreach (var slot in document.Pages)
            {
                var clock = Stopwatch.StartNew();
                try
                {
                    var page = reader.ReadPage(slot.Number, budget.Token);
                    result.PageTimes.Add(clock.Elapsed.TotalMilliseconds);
                    result.Nodes += Count(page.Content);
                    result.TextLength += page.Text.Length;
                    foreach (var what in page.Unsupported) result.Unsupported.Add(what);
                    if (page.Size != slot.Size) result.SlotMismatches++;
                }
                // The budget is spent by closing the document's stream under PdfPig, and what PdfPig makes of a read
                // failing then varies — an encrypted document reports every failure as its encryption. Whatever a read
                // throws once the budget has run out is the budget's, not the reading's.
                catch (Exception) when (budget.IsCancellationRequested)
                {
                    result.TimedOut = true;
                    break;
                }
                catch (Exception ex)
                {
                    result.Escaped.Add($"page {slot.Number}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (budget.IsCancellationRequested)
        {
            _ = ex;
            result.TimedOut = true;
        }
        catch (Exception ex)
        {
            result.Escaped.Add($"document: {ex.GetType().Name}: {ex.Message}");
        }

        return result;
    }

    private static int Count(PdfGroupNode group)
    {
        var count = 1;
        foreach (var child in group.Children)
            count += child is PdfGroupNode nested ? Count(nested) : 1;
        return count;
    }

    private static string Report(List<FileResult> results, string corpus, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        var opened = results.Where(r => !r.Unopenable).ToList();
        var times = opened.SelectMany(r => r.PageTimes).Order().ToList();

        text.AppendLine($"PDF corpus sweep — {corpus}");
        text.AppendLine($"{results.Count} file(s) in {elapsed.TotalMinutes:F1} min");
        text.AppendLine($"  opened      {opened.Count}");
        text.AppendLine($"  unopenable  {results.Count(r => r.Unopenable)}");
        text.AppendLine($"  timed out   {results.Count(r => r.TimedOut)}");
        text.AppendLine($"  escaped     {results.Count(r => r.Escaped.Count > 0)} file(s), {results.Sum(r => r.Escaped.Count)} read(s)");
        text.AppendLine($"  pages read  {times.Count} of {opened.Sum(r => r.Pages)}; slot size differed on {opened.Sum(r => r.SlotMismatches)}");
        text.AppendLine($"  nodes       {opened.Sum(r => (long)r.Nodes)}; text {opened.Sum(r => (long)r.TextLength)} chars; fields {opened.Sum(r => r.Fields)}");
        if (times.Count > 0)
            text.AppendLine($"  page ms     p50 {Percentile(times, 0.5):F1}  p95 {Percentile(times, 0.95):F1}  p99 {Percentile(times, 0.99):F1}  max {times[^1]:F0}");

        text.AppendLine();
        text.AppendLine("Unsupported — kind: files (pages' reports), commonest details");
        foreach (var kind in opened.SelectMany(r => r.Unsupported.Select(u => (r.Path, u))).GroupBy(x => x.u.Kind)
                     .OrderByDescending(g => g.Select(x => x.Path).Distinct().Count()))
        {
            text.AppendLine($"  {kind.Key}: {kind.Select(x => x.Path).Distinct().Count()} ({kind.Count()})");
            foreach (var detail in kind.GroupBy(x => Shorten(x.u.Detail)).OrderByDescending(g => g.Count()).Take(8))
                text.AppendLine($"      {detail.Count(),6}  {detail.Key}  e.g. {Path.GetFileName(detail.First().Path)}");
        }

        text.AppendLine();
        text.AppendLine("Escaped");
        foreach (var r in results.Where(r => r.Escaped.Count > 0).Take(200))
            text.AppendLine($"  {r.Path}: {r.Escaped[0]}{(r.Escaped.Count > 1 ? $" (+{r.Escaped.Count - 1})" : "")}");

        text.AppendLine();
        text.AppendLine("Slowest files (total page ms)");
        foreach (var r in opened.OrderByDescending(r => r.PageTimes.Sum()).Take(25))
            text.AppendLine($"  {r.PageTimes.Sum(),9:F0}  {r.Pages,5} p  {r.Path}");

        text.AppendLine();
        text.AppendLine("Timed out");
        foreach (var r in results.Where(r => r.TimedOut).Take(100)) text.AppendLine($"  {r.Path}");

        return text.ToString();
    }

    private static double Percentile(List<double> sorted, double p) => sorted[(int)Math.Min(sorted.Count - 1, p * sorted.Count)];

    private static string Shorten(string detail) => detail.Length <= 100 ? detail : detail[..100] + "…";

    private sealed class FileResult(string path)
    {
        public string Path { get; } = path;
        public bool Unopenable { get; set; }
        public bool TimedOut { get; set; }
        public int Pages { get; set; }
        public int Fields { get; set; }
        public int Nodes { get; set; }
        public int TextLength { get; set; }
        public int SlotMismatches { get; set; }
        public List<double> PageTimes { get; } = [];
        public List<PdfUnsupported> Unsupported { get; } = [];
        public List<string> Escaped { get; } = [];
    }
}
