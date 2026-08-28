using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Rendering;

using Size = System.Windows.Size;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// The whole feature swept over a corpus of real formulas, rather than the dozen or so we thought to
/// write down.
///
/// <para>
/// Opt-in: point <c>NEXAFLOW_LATEX_CORPUS</c> at a file of one formula per line and it runs; otherwise it
/// is inconclusive. It is not part of any suite's inner loop — a quarter of a million formulas rendered
/// twice takes hours — but it is the only thing that can say a construct nobody remembered still works.
/// </para>
/// <para>
/// Three questions per formula, and the third is the point of the exercise:
/// </para>
/// <list type="number">
///   <item>does it still typeset at all?</item>
///   <item>do the spans obey their invariants — nothing claiming source outside the text, no piece
///     repeating a name its own ancestor carries, no piece naming source outside its parent's?</item>
///   <item>and does painting it out of the tree give the same pixels as asking the typesetter to draw
///     it?</item>
/// </list>
///
/// Set <c>NEXAFLOW_LATEX_CORPUS_STRIDE</c> to sample every Nth line, and
/// <c>NEXAFLOW_LATEX_CORPUS_LIMIT</c> to stop early. A report is written beside the corpus file.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("opt-in sweep over an external corpus; not a unit of the product")]
public class LatexCorpusSweepTests
{
    private const double Scale = 16;

    [TestMethod]
    public void TheTreeSurvivesRealFormulas() => UiThread.Run(() =>
    {
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS");
        if (string.IsNullOrWhiteSpace(corpus) || !File.Exists(corpus))
            Assert.Inconclusive($"set NEXAFLOW_LATEX_CORPUS to a file of formulas (got: {corpus ?? "nothing"})");

        var stride = int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS_STRIDE"), out var s) ? Math.Max(s, 1) : 1;
        var limit = int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS_LIMIT"), out var l) ? l : int.MaxValue;
        var report = Path.Combine(Path.GetDirectoryName(corpus)!, "latex-sweep-report.txt");

        var counts = new Sweep();
        var clock = Stopwatch.StartNew();
        var log = new StringBuilder();
        var line = 0;

        using (var writer = new StreamWriter(report, append: false))
        {
            writer.AutoFlush = true;
            writer.WriteLine($"corpus: {corpus}  stride: {stride}  limit: {limit}  scale: {Scale}");

            foreach (var raw in File.ReadLines(corpus))
            {
                if (line++ % stride != 0) continue;
                if (counts.Seen >= limit) break;

                counts.Seen++;
                var latex = raw.Trim();
                if (latex.Length == 0) continue;

                foreach (var complaint in Check(latex, counts))
                {
                    counts.Faults++;
                    if (counts.Faults <= 200) writer.WriteLine($"line {line}: {complaint}\n    {latex}");
                }

                if (counts.Seen % 2000 == 0)
                    writer.WriteLine($"… {counts.Seen} seen, {counts.Typeset} typeset, {counts.Faults} faults, "
                                     + $"{clock.Elapsed.TotalMinutes:F1} min");
            }

            writer.WriteLine(counts.Summary(clock.Elapsed));
        }

        log.AppendLine(counts.Summary(clock.Elapsed));
        Assert.AreEqual(0, counts.Faults, $"{log}see {report}");
    });

    private sealed class Sweep
    {
        public int Seen, Typeset, Faults, Rejected, Repeated, Escaped, Repainted, RepairedLevels, RepairedLeaves, Recovered, Huge;

        public string Summary(TimeSpan taken) =>
            $"seen {Seen}, typeset {Typeset}, faults {Faults} "
            + $"(spans out of range {Rejected}, repeated names {Repeated}, names outside the parent {Escaped}, "
            + $"pictures that moved {Repainted}) in {taken.TotalMinutes:F1} min"
            + Environment.NewLine
            + $"  disowned {RepairedLevels} level(s) whose children still name their own source, "
            + $"and {RepairedLeaves} leaf/leaves with nothing beneath them to fall back on"
            + Environment.NewLine
            + $"  picture not compared for {Recovered} recovered formula(s) — the plain parser cannot draw "
            + $"those, so no reference exists — nor for {Huge} too large to rasterise";
    }

    /// <summary>Everything wrong with one formula, or nothing.</summary>
    private static IEnumerable<string> Check(string latex, Sweep counts)
    {
        LatexLayout? layout = null;
        string? threw = null;
        try { layout = LatexLayout.Build(latex, Scale); }
        catch (Exception e) { threw = $"Build threw {e.GetType().Name}: {e.Message}"; }
        if (threw is not null) { yield return threw; yield break; }

        // A formula this typesetter cannot read is the corpus's business, not ours.
        if (layout is null) yield break;
        counts.Typeset++;

        var named = layout.Tree.Root.SelfAndDescendants().Where(n => n.SourceLength > 0).ToList();

        foreach (var node in named)
        {
            if (node.SourceStart < 0 || node.SourceEnd() > latex.Length)
            {
                counts.Rejected++;
                yield return $"{node.Kind} names {node.SourceStart}+{node.SourceLength} of {latex.Length}";
                break;
            }
        }

        if (named.FirstOrDefault(n => n.Ancestors()
                .Any(a => a.SourceStart == n.SourceStart && a.SourceLength == n.SourceLength)) is { } repeat)
        {
            counts.Repeated++;
            yield return $"{repeat.Kind} repeats the name its ancestor carries: {repeat.SourceStart}+{repeat.SourceLength}";
        }

        if (named.FirstOrDefault(n => n.Parent is { } p
                                      && p.SourceLength > 0
                                      && (n.SourceStart < p.SourceStart || n.SourceEnd() > p.SourceEnd())) is { } escapee)
        {
            counts.Escaped++;
            yield return $"{escapee.Kind} names {escapee.SourceStart}+{escapee.SourceLength}, "
                         + $"outside its parent's {escapee.Parent!.SourceStart}+{escapee.Parent.SourceLength}";
        }

        // Not a fault of ours — the tree repaired it — but counted, because it is a term that quietly lost
        // the ability to be selected on its own, and the tail of them should not grow unnoticed.
        var (levels, leaves) = Repairs(latex);
        counts.RepairedLevels += levels;
        counts.RepairedLeaves += leaves;

        // Nothing to compare a recovered formula against. The oracle below is the typesetter's own
        // renderer, reached through the parser that throws rather than recovers — so for input only
        // recovery can read, there is no reference picture in existence. Counted rather than quietly
        // passed over, so the number of formulas the picture was never checked for stays visible.
        if (layout.Tree.Diagnostics.Count > 0) { counts.Recovered++; yield break; }

        // Anything enormous is skipped rather than rasterised: a formula wider than a wall says nothing
        // about correctness and costs a second of the sweep.
        if (layout.Size.Width > 4000 || layout.Size.Height > 4000) { counts.Huge++; yield break; }

        string tree = "", typesetter = "";
        string? painting = null;
        try
        {
            tree = Draw(layout.Size, dc => layout.Paint(dc, Brushes.Black));
            typesetter = Draw(layout.Size, dc => Typeset(dc, latex, layout.PaintOffset));
        }
        catch (Exception e) { painting = $"painting threw {e.GetType().Name}: {e.Message}"; }
        if (painting is not null) { yield return painting; yield break; }

        if (tree != typesetter)
        {
            counts.Repainted++;
            yield return "the picture painted from the tree differs from the typesetter's";
        }
    }

    /// <summary>How many names the capture had to take off this formula's pieces, and of what kind.</summary>
    private static (int Levels, int Leaves) Repairs(string latex)
    {
        var capture = new LatexLayoutCapture(Scale, latex);
        try
        {
            WpfTeXFormulaParser.Instance.Parse(latex)
                .RenderTo(capture, WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale), 0, 0);
            capture.FinishRendering();
        }
        catch { return (0, 0); }

        return (capture.Disowned.Count(d => d.StartsWith("level")),
                capture.Disowned.Count(d => d.StartsWith("leaf")));
    }

    private static void Typeset(DrawingContext dc, string latex, Vector paintOffset)
    {
        var formula = WpfTeXFormulaParser.Instance.Parse(latex);
        var environment = WpfTeXEnvironment.Create(
            style: TexStyle.Display,
            scale: Scale,
            systemTextFontName: "Arial",
            foreground: Brushes.Black);

        dc.PushTransform(new TranslateTransform(paintOffset.X, paintOffset.Y));
        formula.RenderTo(dc, environment, Scale, 0, 0);
        dc.Pop();
    }

    private static string Draw(Size size, Action<DrawingContext> paint)
    {
        var width = (int)Math.Ceiling(Math.Max(size.Width, 1));
        var height = (int)Math.Ceiling(Math.Max(size.Height, 1));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            paint(dc);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
}
