using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Chemistry;
using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Markdown.Music.LilyPond;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.Prose.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What laying a markdown document costs, step by step — run by hand, over the sample corpus and the largest document
/// in the repository, to see where the time goes and whether a change moved it.
///
/// <para>
/// Opt-in: set <c>NEXAFLOW_MARKDOWN_BENCH</c> to a folder and each run writes one <c>markdown-bench-*.json</c> there.
/// <c>tools/bench/Run-MarkdownBench.ps1</c> builds Release, labels the run with the commit and runs it. A timing is a
/// comparison, not an absolute: runs are only compared on one machine, in one configuration.
/// </para>
/// <para>
/// Per document it times <b>open</b> — a document laid for the first time — and <b>edit</b>, the same document laid again
/// after one character is typed in the middle of it, which is what a keystroke costs and what reusing unchanged layout
/// can win back. Then each step on its own: Markdig finding the blocks, each stage the document is read by, the builder
/// (and within it, what the nested languages cost and what is markdown's own), and painting what was laid. Across the
/// corpus it totals every nested language, every stage of each language's own pipeline, and every kind of block.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class MarkdownLayoutBench
{
    private const string Folder = "NEXAFLOW_MARKDOWN_BENCH";

    /// <summary>How many times each step is timed; the median is kept.</summary>
    private const int Runs = 5;

    /// <summary>The width every document is laid in — a comfortable reading column.</summary>
    private const double Room = 900;

    [TestMethod]
    public void WhereLayingADocumentSpendsItsTime() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var docs = TestSampleData.Files("markdown").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        docs.Add(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")));

        var style = StyleFormat.Dark;
        var options = new DiagramRenderOptions { Palette = style, ReadOnly = false };

        var rows = new List<Dictionary<string, object>>();
        var languages = new Dictionary<string, (int Count, double Ms)>();
        var stages = new Dictionary<string, (int Count, double Ms)>();
        var kinds = new Dictionary<string, (int Count, double Ms)>();

        foreach (var path in docs)
        {
            var text = File.ReadAllText(path);
            rows.Add(Document(Path.GetFileName(path), text, style, options, languages, stages, kinds));
        }

        var run = new Dictionary<string, object>
        {
            ["label"] = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_LABEL") ?? "",
            ["commit"] = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_COMMIT") ?? "",
#if DEBUG
            ["configuration"] = "Debug",
#else
            ["configuration"] = "Release",
#endif
            ["machine"] = Environment.MachineName,
            ["when"] = DateTimeOffset.Now.ToString("o"),
            ["runs"] = Runs,
            ["room"] = Room,
            ["documents"] = rows,
            ["totals"] = Totals(rows),
            ["languages"] = Listed(languages),
            ["languageStages"] = Listed(stages),
            ["blockKinds"] = Listed(kinds),
        };

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"markdown-bench-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine(file);
    });

    /// <summary>One document, step by step.</summary>
    private static Dictionary<string, object> Document(string name, string text, StyleFormat style, DiagramRenderOptions options,
                                                       Dictionary<string, (int Count, double Ms)> languages,
                                                       Dictionary<string, (int Count, double Ms)> stages,
                                                       Dictionary<string, (int Count, double Ms)> kinds)
    {
        var open = Median(() => MarkdownContent.Of(style, options).Lay(EditState.For(text), Room, false));

        // A keystroke: the same content, laid again with one more character in the middle — a different one each time,
        // so nothing laid before is the answer.
        var content = MarkdownContent.Of(style, options);
        content.Lay(EditState.For(text), Room, false);
        var middle = Middle(text);
        var typed = 0;
        var edit = Median(() => content.Lay(EditState.For(text.Insert(middle, new string('x', ++typed))), Room, false));

        ContentNode read = null!;
        var tRead = Median(() => read = MarkdownParser.Read(text));

        IAstStage[] pipeline =
        [
            new WithBlocks(),
            new WithDefinitions(),
            new Nexaflow.Visuals.Text.Markdown.Stages.WithNested(style, options),
            new Nexaflow.Visuals.Text.Markdown.Stages.WithImages(options.Pictures),
            new Nexaflow.Visuals.Text.Markdown.Stages.WithLinks(options.Links),

            // Timed against itself: from the second run every block is the one it was, so every block is compared whole.
            new Nexaflow.Visuals.Text.Markdown.Stages.WithUnchanged(),
        ];

        var tree = read;
        var staged = new Dictionary<string, object>();

        foreach (var stage in pipeline)
        {
            var input = tree;
            staged[stage.Name] = Round(Median(() => stage.Run(input)));

            // What is built from below is built from nothing: a reading saying which blocks are unchanged would hand the
            // second build what the first one laid.
            if (stage is not Nexaflow.Visuals.Text.Markdown.Stages.WithUnchanged) tree = stage.Run(input);
        }

        // What reading every block beside the document's definitions costs: the same stage with none to read beside.
        var bare = read.With([.. read.Children.Where(child => !(child.IsDerived && child.Kind == MarkdownKinds.Definitions))]);
        var tBare = Median(() => new WithBlocks().Run(bare));

        var reading = ContentReading.Of(tree);
        Laid laid = null!;
        var build = Median(() => laid = new MarkdownBuilder(reading, EditState.For(text), style, false).Lay(Room));

        var nested = 0.0;
        var count = 0;

        foreach (var part in reading.Root.SelfAndDescendants())
        {
            if (ContentNesting.Of(part) is not { } nesting || part.Part(Roles.Body) is not { } body) continue;

            count++;
            var t = Median(() => nesting.At(body, Room, null, false));
            nested += t;
            Add(languages, nesting.Named.ToLowerInvariant(), t);

            var (start, length) = ContentNesting.Own(body);
            Language(nesting.Named.ToLowerInvariant(), text.Substring(start, length), style, options, stages);
        }

        foreach (var block in reading.Root.Children.Where(part => part.Role != Roles.Trivia && !part.Derived))
        {
            var source = block.Print();
            Add(kinds, block.Kind, Median(() => MarkdownBuilder.Lay(source, style, Room, isReadOnly: false)));
        }

        // Painting what was just laid, as a keystroke does; and painting the same tree again, as a caret blink or a
        // change of selection does — the two differ by whatever painting keeps from one time to the next.
        var paint = Timed(() => new MarkdownBuilder(reading, EditState.For(text), style, false).Lay(Room),
                          fresh => Painted(fresh, style));
        var repaint = Median(() => Painted(laid, style));

        // Painting what a keystroke laid, where the page was painted before it: what was kept of the blocks nobody typed in
        // is drawn as it was, and only the block typed in is painted.
        var painter = MarkdownContent.Of(style, options);
        Painted(painter.Lay(EditState.For(text), Room, false), style);
        var retyped = 0;
        var editPaint = Timed(() => painter.Lay(EditState.For(text.Insert(middle, new string('x', ++retyped))), Room, false),
                              typedIn => Painted(typedIn, style));

        return new Dictionary<string, object>
        {
            ["name"] = name,
            ["kb"] = Math.Round(text.Length / 1024.0, 1),
            ["lines"] = text.Count(c => c == '\n'),
            ["blocks"] = read.Children.Count(child => child.Role != Roles.Trivia && !child.IsDerived),
            ["nestedCount"] = count,
            ["pieces"] = laid.Root.SelfAndDescendants().Count(),
            ["open"] = Round(open),
            ["edit"] = Round(edit),
            ["read"] = Round(tRead),
            ["stages"] = staged,
            ["definitions"] = Round(Math.Max(0, (double)staged[pipeline[0].Name] - tBare)),
            ["build"] = Round(build),
            ["nested"] = Round(nested),
            ["buildOwn"] = Round(Math.Max(0, build - nested)),
            ["paint"] = Round(paint),
            ["repaint"] = Round(repaint),
            ["editPaint"] = Round(editPaint),
        };
    }

    /// <summary>A nested language's own reading, stage by stage, and — for a diagram — what its builder costs beside it.</summary>
    private static void Language(string language, string source, StyleFormat style, DiagramRenderOptions options,
                                 Dictionary<string, (int Count, double Ms)> stages)
    {
        try
        {
            ContentNode tree;

            switch (language)
            {
                case "mermaid":
                {
                    tree = Time(stages, "mermaid: parse", () => MermaidParser.Parse(source));
                    var block = MermaidBlock.Of(tree);
                    IAstStage[] after = [.. MermaidDiagrams.Grammar(block.Diagram)?.Stages(block) ?? [], new WithFolds(),
                                         .. MermaidBuilders.After(style, options).Stages];
                    tree = Staged(stages, "mermaid", tree, after);

                    if (MermaidBuilders.For(block.Diagram) is { } make)
                    {
                        var reading = ContentReading.Of(tree);
                        Time(stages, "mermaid: build", () => make(reading, EditState.For(source), style, false).Lay(Room));
                    }

                    return;
                }

                case "latex":
                    Staged(stages, "latex", Time(stages, "latex: parse", () => TexParser.Parse(source)), TexPipeline.Of().Stages);
                    return;

                case "abc":
                    Staged(stages, "abc", Time(stages, "abc: parse", () => AbcParser.Parse(source)), AbcPipeline.Of().Stages);
                    return;

                case "lilypond":
                    Staged(stages, "lilypond", Time(stages, "lilypond: parse", () => LilyPondParser.Parse(source)), LilyPondPipeline.Of().Stages);
                    return;

                case "smiles":
                    Staged(stages, "smiles", Time(stages, "smiles: parse", () => SmilesParser.Parse(source)), SmilesPipeline.Of().Stages);
                    return;
            }
        }
        catch (Exception ex)
        {
            Add(stages, $"{language}: threw {ex.GetType().Name}", 0);
        }
    }

    private static ContentNode Staged(Dictionary<string, (int Count, double Ms)> into, string language, ContentNode tree,
                                      IEnumerable<IAstStage> pipeline)
    {
        foreach (var stage in pipeline)
        {
            var input = tree;
            tree = Time(into, $"{language}: {stage.Name}", () => stage.Run(input));
        }

        return tree;
    }

    /// <summary>The middle of the document, moved to the start of a word so what is typed lands in text rather than in markup.</summary>
    private static int Middle(string text)
    {
        var at = text.Length / 2;
        while (at > 0 && !char.IsWhiteSpace(text[at - 1])) at--;
        return at;
    }

    private static Dictionary<string, object> Totals(List<Dictionary<string, object>> rows)
    {
        double Sum(string key) => Round(rows.Sum(row => Convert.ToDouble(row[key])));

        return new Dictionary<string, object>
        {
            ["open"] = Sum("open"),
            ["edit"] = Sum("edit"),
            ["read"] = Sum("read"),
            ["stages"] = Round(rows.Sum(row => ((Dictionary<string, object>)row["stages"]).Values.Sum(Convert.ToDouble))),
            ["build"] = Sum("build"),
            ["nested"] = Sum("nested"),
            ["buildOwn"] = Sum("buildOwn"),
            ["paint"] = Sum("paint"),
            ["repaint"] = Sum("repaint"),
            ["editPaint"] = Sum("editPaint"),
        };
    }

    private static List<Dictionary<string, object>> Listed(Dictionary<string, (int Count, double Ms)> totals) =>
        [.. totals.OrderByDescending(entry => entry.Value.Ms).Select(entry => new Dictionary<string, object>
        {
            ["name"] = entry.Key,
            ["count"] = entry.Value.Count,
            ["ms"] = Round(entry.Value.Ms),
        })];

    private static T Time<T>(Dictionary<string, (int Count, double Ms)> into, string name, Func<T> run)
    {
        var result = run();
        Add(into, name, Median(() => result = run()));
        return result;
    }

    private static void Add(Dictionary<string, (int Count, double Ms)> into, string name, double ms)
    {
        var (count, sum) = into.GetValueOrDefault(name);
        into[name] = (count + 1, sum + ms);
    }

    private static double Round(double ms) => Math.Round(ms, 3);

    /// <summary>Paints a laid tree as the element does, and says how long that took.</summary>
    private static double Painted(Laid laid, StyleFormat style)
    {
        var clock = Stopwatch.StartNew();
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen()) LayoutPainter.Paint(dc, laid.Root, style.Text);

        return clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>The median of <see cref="Runs"/> timings of <paramref name="timed"/>, each on something <paramref name="make"/> made afresh, untimed.</summary>
    private static double Timed<T>(Func<T> make, Func<T, double> timed)
    {
        timed(make());
        var times = new double[Runs];

        for (var at = 0; at < Runs; at++) times[at] = timed(make());

        Array.Sort(times);
        return times[Runs / 2];
    }

    /// <summary>The median of <see cref="Runs"/> timings, after one run to warm up.</summary>
    private static double Median(Action run)
    {
        run();
        var times = new double[Runs];

        for (var at = 0; at < Runs; at++)
        {
            var clock = Stopwatch.StartNew();
            run();
            times[at] = clock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        return times[Runs / 2];
    }

    private static double Median<T>(Func<T> run) => Median(() => { run(); });
}
