using Nexaflow.IO.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
/// Pages from a corpus of real PDFs, laid and drawn the way the reader draws them, beside the same pages drawn by
/// Windows' own PDF renderer — the only reference here that was never ours. Two rasterisers never agree on a pixel, so
/// the comparison only ranks: it sorts the pages for somebody to look at, worst first, on a review page written beside
/// the corpus.
/// <para>
/// Opt-in and local, like <see cref="PdfCorpusSweepTests"/>: <c>NEXAFLOW_PDF_CORPUS</c> names the corpus, and
/// <c>NEXAFLOW_PDF_DRAW_SAMPLE</c> how many of its files (spread evenly through it) have a page drawn — 200 when unset.
/// Pages are laid on the thread pool, as the reader lays them. It fails only when laying or drawing a page throws.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("opt-in comparison against an external corpus of real PDFs")]
public class PdfCorpusDrawingTests
{
    /// <summary>How wide both drawings are compared at, in pixels: enough to see a line out of place, small enough to be quick.</summary>
    private const int CompareWidth = 400;

    /// <summary>How wide the drawings kept for review are.</summary>
    private const int ReviewWidth = 900;

    [TestMethod]
    public void SamplePages_DrawWithoutThrowing_AndAreRankedAgainstWindows()
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_PDF_CORPUS") is not { Length: > 0 } corpus)
        {
            Assert.Inconclusive("NEXAFLOW_PDF_CORPUS names no corpus of PDFs on this machine.");
            return;
        }

        var sample = int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_PDF_DRAW_SAMPLE"), out var n) && n > 0 ? n : 200;
        var files = Directory.EnumerateFiles(corpus, "*.pdf", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        var stride = Math.Max(1, files.Count / sample);
        var chosen = files.Where((_, i) => i % stride == 0).Take(sample).ToList();

        var folder = Directory.CreateDirectory(Path.Combine(corpus, "pdf-sweep", "drawn")).FullName;
        var rows = new ConcurrentBag<Row>();
        var failures = new ConcurrentBag<string>();

        Parallel.ForEach(chosen, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            try
            {
                if (Draw(file, folder) is { } row) rows.Add(row);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add($"{file}: {ex.GetType().Name}: {ex.Message} @ {string.Join(" < ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(static l => l.Trim()))}");
            }
        });

        var ranked = rows.OrderBy(r => r.Score).ToList();
        File.WriteAllText(Path.Combine(folder, "index.html"), Review(ranked, failures.ToList()));

        if (!failures.IsEmpty)
            Assert.Fail($"{failures.Count} page(s) threw while laid or drawn. Review: {Path.Combine(folder, "index.html")}\n"
                        + string.Join('\n', failures.Take(10)));
    }

    private sealed record Row(string File, int Page, double Score, string Ours, string Theirs, int Unsupported);

    private static Row? Draw(string file, string folder)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var scope = PdfDocumentScope.TryOpen(File.OpenRead(file), budget.Token);
        if (scope is null) return null;

        var document = scope.Pages.ReadDocument(budget.Token);
        if (document.Pages.Count == 0) return null;

        var number = 1;
        var page = scope.Pages.ReadPage(number, budget.Token);
        var paper = Brushes.White;
        var tree = PdfPageLayout.Lay(page, paper);

        var size = new Size(page.Size.Width * PdfPageLayout.DipsPerPoint, page.Size.Height * PdfPageLayout.DipsPerPoint);
        if (size.Width <= 0 || size.Height <= 0) return null;

        var ours = LayoutPictures.Painted(tree.Root, new Rect(size), ReviewWidth / size.Width, Brushes.Black, Brushes.White);
        if (ours is null) return null;

        var theirs = Windows(file, number - 1, ReviewWidth);

        var stem = Path.GetFileNameWithoutExtension(file);
        var oursName = $"{stem}-p{number}-ours.png";
        File.WriteAllBytes(Path.Combine(folder, oursName), LayoutPictures.Png(ours));

        string theirsName = string.Empty;
        var score = double.NaN;
        if (theirs is not null)
        {
            theirsName = $"{stem}-p{number}-windows.png";
            File.WriteAllBytes(Path.Combine(folder, theirsName), LayoutPictures.Png(theirs));
            score = Likeness(ours, theirs);
        }

        return new Row(file, number, score, oursName, theirsName, page.Unsupported.Count);
    }

    /// <summary>Page <paramref name="index"/> drawn by Windows.Data.Pdf, <paramref name="width"/> pixels wide.</summary>
    private static BitmapSource? Windows(string file, int index, int width)
    {
        try
        {
            return Task.Run(async () =>
            {
                var storage = await StorageFile.GetFileFromPathAsync(file);
                var document = await WinPdf.PdfDocument.LoadFromFileAsync(storage);
                if (index >= document.PageCount) return null;
                using var page = document.GetPage((uint)index);
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new WinPdf.PdfPageRenderOptions { DestinationWidth = (uint)width });

                var bytes = new byte[stream.Size];
                using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(bytes);
                }

                // A decoded frame stays tied to the decoder's thread; its pixels, copied out, belong to nobody.
                var decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
                var stride = frame.PixelWidth * 4;
                var pixels = new byte[stride * frame.PixelHeight];
                frame.CopyPixels(pixels, stride, 0);
                var copy = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
                copy.Freeze();
                return copy;
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Windows declines some files the reader opens (and the other way round); such a page is drawn but not ranked.
            return null;
        }
    }

    /// <summary>
    /// How alike two drawings of a page are, 0..1: one less the mean difference of their greys, both brought to a common
    /// width. Coarse on purpose — it ranks, it does not judge.
    /// </summary>
    private static double Likeness(BitmapSource a, BitmapSource b)
    {
        var height = (int)Math.Round(CompareWidth * (double)a.PixelHeight / a.PixelWidth);
        var x = Grey(a, CompareWidth, height);
        var y = Grey(b, CompareWidth, height);
        double total = 0;
        for (var i = 0; i < x.Length; i++) total += Math.Abs(x[i] - y[i]);
        return 1 - total / x.Length / 255;
    }

    private static byte[] Grey(BitmapSource source, int width, int height)
    {
        var scaled = new TransformedBitmap(source, new ScaleTransform((double)width / source.PixelWidth, (double)height / source.PixelHeight));
        var grey = new FormatConvertedBitmap(scaled, PixelFormats.Gray8, null, 0);
        var pixels = new byte[grey.PixelWidth * grey.PixelHeight];
        grey.CopyPixels(pixels, grey.PixelWidth, 0);
        if (pixels.Length == width * height) return pixels;
        Array.Resize(ref pixels, width * height);
        return pixels;
    }

    private static string Review(List<Row> rows, List<string> failures)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><meta charset=utf-8><title>PDF drawing review</title>");
        html.AppendLine("<style>body{font:13px sans-serif;background:#888}table{border-collapse:collapse}td{vertical-align:top;padding:6px}img{width:440px;background:#fff;box-shadow:0 0 4px #333}</style>");
        html.AppendLine($"<h2>{rows.Count} page(s), worst first; {failures.Count} failure(s)</h2>");
        foreach (var failure in failures) html.AppendLine($"<div>{WebUtility.HtmlEncode(failure)}</div>");
        html.AppendLine("<table><tr><th>score</th><th>ours</th><th>Windows</th></tr>");
        foreach (var row in rows)
            html.AppendLine($"<tr><td>{row.Score:F3}<br>{WebUtility.HtmlEncode(Path.GetFileName(row.File))} p{row.Page}<br>{row.Unsupported} unsupported</td>"
                            + $"<td><img src=\"{row.Ours}\"></td><td>{(row.Theirs.Length > 0 ? $"<img src=\"{row.Theirs}\">" : "—")}</td></tr>");
        html.AppendLine("</table>");
        return html.ToString();
    }
}
