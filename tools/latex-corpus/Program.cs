using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using WpfMath;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;

namespace Nexaflow.Tools.LatexCorpus;

/// <summary>
/// Runs a corpus of real LaTeX through the engine Nexaflow renders maths with, and puts what it
/// draws next to what the corpus ships. See README.md.
/// </summary>
internal static class Program
{
    private const double DefaultScale = 14.0;
    private const string ImageFolderName = "images";

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var verb = args.FirstOrDefault();
            var options = Options.Parse(args.Skip(1));
            return verb switch
            {
                "parse" => RunParse(options),
                "render" => RunRender(options),
                "compare" => RunCompare(options),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            latex-corpus <verb> --dataset <folder> [options]

              parse     Parse and render every formula; report what the engine could not take.
              render    Write our rendering of each formula as a PNG.
              compare   Render every formula and lay it beside the corpus image, as browsable pages.

            Options
              --dataset <folder>   The corpus (final_png_formulas.txt + generated_png_images/).
              --out <path>         Output folder (compare, render) or report file (parse).
              --limit <n>          Stop after n formulas. 0 = all; compare defaults to 2000.
              --skip <n>           Start n formulas in.
              --scale <n>          Formula scale to render at. Default 14.
              --flag <0..1>        Ink overlap at or below which a pair is marked. Default 0.35.
              --page-size <n>      Pairs per page. Default 400.
              --order <how>        "worst" (default) puts the most different first; "corpus" keeps
                                   the order the dataset lists them in.
            """);
        return 1;
    }

    // ── parse ────────────────────────────────────────────────────────────────────

    private static int RunParse(Options options)
    {
        var corpus = Corpus.Load(options.Dataset, options.Limit ?? 0, options.Skip);
        Console.WriteLine($"Parsing {corpus.Entries.Count:N0} formulas...");

        var parser = WpfTeXFormulaParser.Instance;
        var environment = WpfTeXEnvironment.Create();
        var failures = new Dictionary<string, Failure>();
        var watch = Stopwatch.StartNew();
        int rendered = 0, blank = 0;

        foreach (var entry in corpus.Entries)
        {
            try
            {
                var geometry = parser.Parse(entry.Formula).RenderToGeometry(environment, options.Scale);
                if (geometry.Bounds.IsEmpty) blank++;
                else rendered++;
            }
            catch (Exception ex)
            {
                Record(failures, ex.GetBaseException().Message, entry.Formula);
            }

            if ((entry.Index + 1) % 20000 == 0)
                Console.WriteLine($"  {entry.Index + 1:N0}... ({watch.Elapsed.TotalSeconds:N0}s)");
        }

        var report = new StringBuilder();
        var failed = failures.Values.Sum(f => f.Count);
        report.AppendLine($"{corpus.Entries.Count:N0} formulas, {watch.Elapsed.TotalSeconds:N0}s");
        report.AppendLine($"  {rendered:N0} rendered  {blank:N0} rendered blank  {failed:N0} rejected");
        report.AppendLine();
        report.AppendLine("Rejections by message, most common first:");
        foreach (var failure in failures.Values.OrderByDescending(f => f.Count))
        {
            report.AppendLine();
            report.AppendLine($"{failure.Count,8:N0}  {failure.Message}");
            foreach (var sample in failure.Samples)
                report.AppendLine($"          e.g. {Shorten(sample, 150)}");
        }

        Console.WriteLine(report.ToString());
        if (options.Out is { } path)
        {
            File.WriteAllText(path, report.ToString());
            Console.WriteLine($"Written to {path}");
        }

        return 0;
    }

    private sealed class Failure
    {
        public required string Message { get; init; }

        public int Count { get; set; }

        public List<string> Samples { get; } = [];
    }

    private static void Record(Dictionary<string, Failure> failures, string message, string formula)
    {
        if (!failures.TryGetValue(message, out var failure))
            failures[message] = failure = new Failure { Message = message };
        failure.Count++;
        if (failure.Samples.Count < 3)
            failure.Samples.Add(formula);
    }

    // ── render ───────────────────────────────────────────────────────────────────

    private static int RunRender(Options options)
    {
        var outputFolder = options.Out ?? throw new ArgumentException("render needs --out <folder>.");
        Directory.CreateDirectory(outputFolder);

        var corpus = Corpus.Load(options.Dataset, options.Limit ?? 0, options.Skip);
        var parser = WpfTeXFormulaParser.Instance;
        var environment = WpfTeXEnvironment.Create();
        int written = 0, failed = 0;

        foreach (var entry in corpus.Entries)
        {
            try
            {
                var formula = parser.Parse(entry.Formula);
                formula.SaveAsPng(Path.Combine(outputFolder, entry.Id + ".png"), environment, options.Scale);
                written++;
            }
            catch
            {
                failed++;
            }
        }

        Console.WriteLine($"{written:N0} written to {outputFolder}, {failed:N0} could not be rendered.");
        return 0;
    }

    // ── compare ──────────────────────────────────────────────────────────────────

    private static int RunCompare(Options options)
    {
        // Comparing renders as well as parses, so a run over the whole corpus is a long one: the
        // default is a sample, and --limit 0 asks for all of it.
        var corpus = Corpus.Load(options.Dataset, options.Limit ?? 2000, options.Skip);
        var folder = options.Out ?? Path.Combine(Environment.CurrentDirectory, "corpus-report");
        var images = Path.Combine(folder, ImageFolderName);
        Directory.CreateDirectory(images);

        var parser = WpfTeXFormulaParser.Instance;
        var environment = WpfTeXEnvironment.Create();
        var pairs = new List<Pair>();
        var watch = Stopwatch.StartNew();

        foreach (var entry in corpus.Entries)
        {
            var referencePath = entry.ImageName is null ? null : corpus.ImagePath(entry);
            if (referencePath is null || !File.Exists(referencePath))
                continue;

            var referenceName = entry.Id + "-ref.png";
            File.Copy(referencePath, Path.Combine(images, referenceName), overwrite: true);
            var reference = GrayImage.Load(referencePath).CropToInk();

            try
            {
                var bitmap = parser.Parse(entry.Formula).RenderToBitmap(environment, options.Scale);
                var ourName = entry.Id + "-ours.png";
                File.WriteAllBytes(Path.Combine(images, ourName), ToPng(bitmap));

                var overlap = GrayImage.InkOverlap(GrayImage.FromBitmap(bitmap).CropToInk(), reference);
                pairs.Add(new Pair(entry, referenceName, ourName, null, overlap, overlap <= options.Flag));
            }
            catch (Exception ex)
            {
                pairs.Add(new Pair(entry, referenceName, null, ex.GetBaseException().Message, 0, true));
            }

            if (pairs.Count % 20000 == 0)
                Console.WriteLine($"  {pairs.Count:N0}... ({watch.Elapsed.TotalSeconds:N0}s)");
        }

        // Most different first, so the first page is the one worth reading. The corpus order is
        // arbitrary anyway - it is not sorted by anything a reader would recognise.
        var ordered = options.WorstFirst
            ? pairs.OrderBy(p => p.Error is null).ThenBy(p => p.Overlap).ToList()
            : pairs;

        var index = Report.Write(folder, ordered, options.PageSize, ImageFolderName);

        var failed = pairs.Count(p => p.Error is not null);
        Console.WriteLine($"{pairs.Count:N0} pairs in {watch.Elapsed.TotalSeconds:N0}s");
        Console.WriteLine($"  {failed:N0} did not render, " +
                          $"{pairs.Count(p => p.Flagged) - failed:N0} marked as different");
        Console.WriteLine($"Open {index}");
        return 0;
    }

    private static byte[] ToPng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string Shorten(string text, int length) =>
        text.Length <= length ? text : text[..length] + " …";

    // ── options ──────────────────────────────────────────────────────────────────

    private sealed class Options
    {
        public string Dataset { get; private set; } = "";

        public string? Out { get; private set; }

        /// <summary>Null where the option was not given, so a verb can have its own default.</summary>
        public int? Limit { get; private set; }

        public int Skip { get; private set; }

        public double Scale { get; private set; } = DefaultScale;

        public double Flag { get; private set; } = 0.35;

        public int PageSize { get; private set; } = Report.DefaultPageSize;

        public bool WorstFirst { get; private set; } = true;

        public static Options Parse(IEnumerable<string> args)
        {
            var options = new Options();
            using var rest = args.GetEnumerator();
            while (rest.MoveNext())
            {
                var name = rest.Current;
                switch (name)
                {
                    case "--dataset": options.Dataset = Next(rest, name); continue;
                    case "--out": options.Out = Next(rest, name); continue;
                    case "--limit": options.Limit = int.Parse(Next(rest, name)); continue;
                    case "--skip": options.Skip = int.Parse(Next(rest, name)); continue;
                    case "--scale": options.Scale = double.Parse(Next(rest, name)); continue;
                    case "--flag": options.Flag = double.Parse(Next(rest, name)); continue;
                    case "--page-size": options.PageSize = int.Parse(Next(rest, name)); continue;
                    case "--order":
                        var order = Next(rest, name);
                        options.WorstFirst = order switch
                        {
                            "worst" => true,
                            "corpus" => false,
                            _ => throw new ArgumentException(
                                $"--order takes \"worst\" or \"corpus\", not \"{order}\"."),
                        };
                        continue;
                    default: throw new ArgumentException($"Unknown option \"{name}\".");
                }
            }

            if (options.Dataset.Length == 0)
                throw new ArgumentException("--dataset <folder> is required.");
            return options;
        }

        private static string Next(IEnumerator<string> args, string name) =>
            args.MoveNext() ? args.Current : throw new ArgumentException($"{name} needs a value.");
    }
}
