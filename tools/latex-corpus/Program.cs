using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Windows.Media.Imaging;
using WpfMath;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;

namespace Nexaflow.Tools.LatexCorpus;

/// <summary>
/// Runs a corpus of real LaTeX through the engine Nexaflow renders maths with, and says where it
/// disagrees with the renderings the corpus ships. See README.md for what the numbers mean.
/// </summary>
internal static class Program
{
    private const double DefaultScale = 20.0;

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
              compare   Render, compare against the corpus images, and write an HTML report.

            Options
              --dataset <folder>   The corpus (final_png_formulas.txt + generated_png_images/).
              --out <path>         Report file, or output folder for `render`.
              --limit <n>          Stop after n formulas (0 = all). Default 2000 for compare, 0 otherwise.
              --skip <n>           Start n formulas in.
              --scale <n>          Formula scale to render at. Default 20.
              --flag <0..1>        Ink overlap at or below which a row is flagged. Default 0.35.
              --top <n>            How many rows the report shows (0 = no limit). Default 300.
              --csv <path>         Write every comparison, unfiltered, as CSV.
              --all-rows           Put every compared formula in the report, not just the worst.
              --embed              Put the images in the HTML itself rather than a folder beside it.
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

    /// <param name="OursFile">
    /// Our rendering, already written into the report's image folder - a full corpus run compares a
    /// quarter of a million formulas, and holding their images until the report is written is several
    /// gigabytes of nothing useful.
    /// </param>
    private sealed record Comparison(
        CorpusEntry Entry,
        double Overlap,
        double AspectRatio,
        string? Error,
        string? OursFile,
        string? ReferenceFile)
    {
        public bool Rendered => this.Error is null;
    }

    private static int RunCompare(Options options)
    {
        // Comparing renders as well as parses, so a run over the whole corpus is a long one: the
        // default is a sample, and --limit 0 asks for all of it.
        var corpus = Corpus.Load(options.Dataset, options.Limit ?? 2000, options.Skip);
        var parser = WpfTeXFormulaParser.Instance;
        var environment = WpfTeXEnvironment.Create();
        var results = new List<Comparison>();
        var watch = Stopwatch.StartNew();
        var reportPath = options.Out ?? Path.Combine(Environment.CurrentDirectory, "latex-corpus.html");
        var assetFolder = Path.ChangeExtension(reportPath, null) + "-images";
        Directory.CreateDirectory(assetFolder);

        foreach (var entry in corpus.Entries)
        {
            var referencePath = entry.ImageName is null ? null : corpus.ImagePath(entry);
            if (referencePath is null || !File.Exists(referencePath))
                continue;

            var reference = GrayImage.Load(referencePath).CropToInk();
            Comparison result;
            try
            {
                var formula = parser.Parse(entry.Formula);
                var bitmap = formula.RenderToBitmap(environment, options.Scale);
                var ours = GrayImage.FromBitmap(bitmap).CropToInk();
                var overlap = GrayImage.InkOverlap(ours, reference);
                var aspect = AspectOf(ours) / Math.Max(AspectOf(reference), 1e-6);
                result = new Comparison(entry, overlap, aspect, null, null, null);

                if (options.AllRows || overlap <= options.Flag)
                    result = result with
                    {
                        OursFile = Keep(assetFolder, entry.Id + "-ours.png", ToPng(bitmap)),
                        ReferenceFile = Keep(assetFolder, entry.Id + "-ref.png", referencePath),
                    };
            }
            catch (Exception ex)
            {
                result = new Comparison(entry, 0, 0, ex.GetBaseException().Message, null,
                    Keep(assetFolder, entry.Id + "-ref.png", referencePath));
            }

            results.Add(result);
            if (results.Count % 20000 == 0)
                Console.WriteLine($"  {results.Count:N0}... ({watch.Elapsed.TotalSeconds:N0}s)");
        }

        var comparable = results.Where(r => r.Rendered).ToList();
        var flagged = results.Where(r => !r.Rendered || r.Overlap <= options.Flag).ToList();
        Console.WriteLine($"{results.Count:N0} compared in {watch.Elapsed.TotalSeconds:N0}s");
        Console.WriteLine($"  {results.Count - comparable.Count:N0} did not render");
        if (comparable.Count > 0)
        {
            var sorted = comparable.Select(r => r.Overlap).Order().ToList();
            Console.WriteLine($"  ink overlap: median {sorted[sorted.Count / 2]:F3}, " +
                              $"mean {sorted.Average():F3}, " +
                              $"worst {sorted[0]:F3}");
        }

        Console.WriteLine($"  {flagged.Count:N0} flagged at or below {options.Flag:F2}");

        if (options.Csv is { } csvPath)
        {
            WriteCsv(csvPath, results);
            Console.WriteLine($"Every row written to {csvPath}");
        }

        var rows = (options.AllRows ? results : flagged)
            .OrderBy(r => r.Rendered)
            .ThenBy(r => r.Overlap)
            .ToList();
        if (options.Top > 0)
            rows = rows.Take(options.Top).ToList();
        WriteReport(reportPath, assetFolder, rows, results.Count, comparable.Count, options);
        Console.WriteLine($"Report ({rows.Count:N0} rows) written to {reportPath}");
        return 0;
    }

    /// <summary>Every comparison, in the order the corpus lists them, so nothing is filtered out.</summary>
    private static void WriteCsv(string path, IReadOnlyList<Comparison> results)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("index,id,overlap,aspect,error,formula");
        foreach (var r in results)
        {
            writer.WriteLine(string.Join(",",
                r.Entry.Index,
                r.Entry.Id,
                r.Rendered ? r.Overlap.ToString("F4") : "",
                r.Rendered ? r.AspectRatio.ToString("F4") : "",
                Csv(r.Error ?? ""),
                Csv(r.Entry.Formula)));
        }
    }

    private static string Csv(string field) => "\"" + field.Replace("\"", "\"\"") + "\"";

    private static double AspectOf(GrayImage image) =>
        image.IsEmpty ? 0 : (double)image.Width / image.Height;

    /// <summary>Puts one image in the report's folder and returns the name to reference it by.</summary>
    private static string Keep(string folder, string name, byte[] png)
    {
        File.WriteAllBytes(Path.Combine(folder, name), png);
        return name;
    }

    private static string Keep(string folder, string name, string sourcePath)
    {
        File.Copy(sourcePath, Path.Combine(folder, name), overwrite: true);
        return name;
    }

    private static byte[] ToPng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // ── the report ───────────────────────────────────────────────────────────────

    // Kept out of an interpolated string so the CSS and the script can use braces normally.
    private const string ReportStyle = """
        <style>
          :root { color-scheme: light dark; --line: #8884; --flag: #d33; --ok: #2a7; }
          body { font: 14px/1.5 system-ui, sans-serif; margin: 1.5rem; }
          h1 { font-size: 1.3rem; margin: 0 0 .3rem; }
          .summary { margin: 0 0 1rem; max-width: 60rem; }
          .bar { position: sticky; top: 0; z-index: 2; background: Canvas; padding: .6rem 0;
                 border-bottom: 1px solid var(--line); display: flex; gap: .8rem; align-items: center;
                 flex-wrap: wrap; }
          button { font: inherit; padding: .3rem .7rem; }
          table { border-collapse: collapse; width: 100%; }
          th, td { border-bottom: 1px solid var(--line); padding: .45rem .6rem; vertical-align: top; }
          th { text-align: left; font-weight: 600; }
          td.score { font-variant-numeric: tabular-nums; white-space: nowrap; }
          .flag { color: var(--flag); font-weight: 600; }
          img { height: 34px; width: auto; max-width: 100%; background: #fff; padding: 2px;
                border-radius: 3px; }
          code { font: 12px/1.4 ui-monospace, monospace; white-space: pre-wrap; word-break: break-word;
                 display: block; max-width: 44rem; opacity: .85; }
          .err { color: var(--flag); font-size: 12px; }
          tr.picked { background: color-mix(in srgb, var(--flag) 12%, transparent); }
          textarea { width: 100%; height: 12rem; font: 12px ui-monospace, monospace; margin-top: .5rem; }
        </style>
        """;

    private const string ReportScript = """
        <script>
          const rows = () => [...document.querySelectorAll('tbody tr')];
          const picked = () => rows().filter(r => r.querySelector('input').checked);
          function refresh() {
            rows().forEach(r => r.classList.toggle('picked', r.querySelector('input').checked));
            const list = picked();
            document.getElementById('count').textContent = list.length + ' picked';
            document.getElementById('picked').value = list
              .map(r => '- `' + r.dataset.formula + '`  (' + r.dataset.id + ', overlap ' + r.dataset.score + ')')
              .join('\n');
          }
          document.addEventListener('change', e => { if (e.target.matches('tbody input')) refresh(); });
          document.getElementById('all').onclick = () => {
            rows().forEach(r => r.querySelector('input').checked = true); refresh();
          };
          document.getElementById('none').onclick = () => {
            rows().forEach(r => r.querySelector('input').checked = false); refresh();
          };
          document.getElementById('copy').onclick = async () => {
            const text = document.getElementById('picked').value;
            try { await navigator.clipboard.writeText(text); document.getElementById('copy').textContent = 'Copied'; }
            catch { document.getElementById('picked').select(); }
            setTimeout(() => document.getElementById('copy').textContent = 'Copy picked', 1200);
          };
          refresh();
        </script>
        """;

    private static void WriteReport(
        string path,
        string assetFolder,
        IReadOnlyList<Comparison> rows,
        int compared,
        int renderedCount,
        Options options)
    {

        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<title>LaTeX corpus comparison</title>");
        html.AppendLine(ReportStyle);
        html.AppendLine("<h1>LaTeX corpus comparison</h1>");
        html.Append($"""
            <p class="summary">
              {compared:N0} formulas compared, {renderedCount:N0} of them rendered. Showing
              {rows.Count:N0} {(options.AllRows ? "rows" : $"flagged at or below an ink overlap of {options.Flag:F2}")},
              worst first. <strong>Ink overlap</strong> puts both renderings at the same height and asks how
              much of their ink coincides - it ranks disagreement, it does not decide correctness. Tick the
              ones that are really wrong and copy them out.
            </p>
            <div class="bar">
              <span id="count">0 picked</span>
              <button id="all" type="button">Pick all</button>
              <button id="none" type="button">Pick none</button>
              <button id="copy" type="button">Copy picked</button>
            </div>
            <details><summary>Picked formulas</summary><textarea id="picked" readonly></textarea></details>
            <table>
              <thead><tr><th></th><th>Overlap</th><th>Reference</th><th>Nexaflow</th><th>Formula</th></tr></thead>
              <tbody>

            """);

        foreach (var row in rows)
        {
            var score = row.Rendered
                ? $"""<span class="{(row.Overlap <= options.Flag ? "flag" : "")}">{row.Overlap:F3}</span>"""
                : """<span class="flag">did not render</span>""";
            var aspect = row.Rendered ? $"<br><small>×{row.AspectRatio:F2} wide</small>" : "";
            var scoreText = row.Rendered ? row.Overlap.ToString("F3") : "did not render";
            html.Append($"""
                    <tr data-id="{row.Entry.Id}" data-score="{scoreText}" data-formula="{Escape(row.Entry.Formula)}">
                      <td><input type="checkbox"></td>
                      <td class="score">{score}{aspect}</td>
                      <td>{Image(row.ReferenceFile, assetFolder, options.Embed)}</td>
                      <td>{Image(row.OursFile, assetFolder, options.Embed)}{Error(row.Error)}</td>
                      <td><code>{Escape(row.Entry.Formula)}</code></td>
                    </tr>

                """);
        }

        html.AppendLine("  </tbody>");
        html.AppendLine("</table>");
        html.AppendLine(ReportScript);
        File.WriteAllText(path, html.ToString(), new UTF8Encoding(false));
    }

    // Thousands of rows of base64 make a file no browser enjoys, so by default the images stay in the
    // folder beside the report and load as they are scrolled to. --embed makes it self-contained.
    private static string Image(string? file, string folder, bool embed)
    {
        if (file is null) return "";
        if (!embed)
            return $"""<img loading="lazy" src="{Path.GetFileName(folder)}/{file}" alt="">""";

        var png = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder, file)));
        return $"""<img loading="lazy" src="data:image/png;base64,{png}" alt="">""";
    }

    private static string Error(string? message) =>
        message is null ? "" : $"""<div class="err">{Escape(message)}</div>""";

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

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

        public int Top { get; private set; } = 300;

        public bool AllRows { get; private set; }

        public string? Csv { get; private set; }

        /// <summary>Put the images in the file itself, instead of a folder beside it.</summary>
        public bool Embed { get; private set; }

        public static Options Parse(IEnumerable<string> args)
        {
            var options = new Options();
            using var rest = args.GetEnumerator();
            while (rest.MoveNext())
            {
                var name = rest.Current;
                switch (name)
                {
                    case "--all-rows": options.AllRows = true; continue;
                    case "--embed": options.Embed = true; continue;
                    case "--dataset": options.Dataset = Next(rest, name); continue;
                    case "--out": options.Out = Next(rest, name); continue;
                    case "--csv": options.Csv = Next(rest, name); continue;
                    case "--limit": options.Limit = int.Parse(Next(rest, name)); continue;
                    case "--skip": options.Skip = int.Parse(Next(rest, name)); continue;
                    case "--scale": options.Scale = double.Parse(Next(rest, name)); continue;
                    case "--flag": options.Flag = double.Parse(Next(rest, name)); continue;
                    case "--top": options.Top = int.Parse(Next(rest, name)); continue;
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
