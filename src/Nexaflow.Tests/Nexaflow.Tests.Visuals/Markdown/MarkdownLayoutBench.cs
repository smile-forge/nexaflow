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
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Prose;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Windows;
using Nexaflow.Markdown.Editing;

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
        var inputs = ContentInputs.None;

        var rows = new List<Dictionary<string, object>>();
        var languages = new Dictionary<string, (int Count, double Ms, double Kb)>();
        var stages = new Dictionary<string, (int Count, double Ms, double Kb)>();
        var kinds = new Dictionary<string, (int Count, double Ms, double Kb)>();

        foreach (var path in docs)
        {
            var text = File.ReadAllText(path);
            rows.Add(Document(Path.GetFileName(path), text, style, inputs, languages, stages, kinds));
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
            ["pace"] = Pace(),
            ["when"] = DateTimeOffset.Now.ToString("o"),
            ["runs"] = Runs,
            ["room"] = Room,
            ["documents"] = rows,
            ["totals"] = Totals(rows),
            ["languages"] = Listed(languages),
            ["languageStages"] = Listed(stages),
            ["blockKinds"] = Listed(kinds),
            ["memory"] = Memory(File.ReadAllText(Profiled(docs)), style, inputs),
        };

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"markdown-bench-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine(file);
    });

    /// <summary>One document, step by step.</summary>
    private static Dictionary<string, object> Document(string name, string text, StyleFormat style, ContentInputs inputs,
                                                       Dictionary<string, (int Count, double Ms, double Kb)> languages,
                                                       Dictionary<string, (int Count, double Ms, double Kb)> stages,
                                                       Dictionary<string, (int Count, double Ms, double Kb)> kinds)
    {
        var (open, openKb) = Cost(() => new ContentEngine(inputs).Lay(null, EditState.For(text), style, Room, false));

        // A keystroke: the same content, laid again with one more character in the middle — a different one each time,
        // so nothing laid before is the answer.
        var content = new ContentEngine(inputs);
        content.Lay(null, EditState.For(text), style, Room, false);
        var middle = Middle(text);
        var typed = 0;
        var (edit, editKb) = Cost(() => content.Lay(null, EditState.For(text.Insert(middle, new string('x', ++typed))), style, Room, false));

        ContentNode read = null!;
        var tRead = Median(() => read = MarkdownParser.Parsing()(text).Tree);

        IAstStage[] pipeline =
        [
            new Nexaflow.Visuals.Text.Markdown.Stages.WithImages(inputs.Pictures),
            new Nexaflow.Visuals.Text.Markdown.Stages.WithLinks(inputs.Links),

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

        var reading = ContentReading.Of(tree);

        // Laying out is the engine's, parse and all: what building costs is what is left of laying it out once reading it and
        // working it over are taken away.
        Laid laid = null!;
        var whole = Median(() => laid = Laying.Lay(null, text, Room, style, writing: true, inputs: inputs));
        var build = Math.Max(0, whole - tRead - staged.Values.Sum(Convert.ToDouble));

        var nested = 0.0;
        var count = 0;

        foreach (var part in reading.Root.SelfAndDescendants())
        {
            if (ContentLanguages.Held(part) is null || part.Part(Roles.Body) is not { } body) continue;

            count++;
            var named = ContentNested.Language(part)!.ToLowerInvariant();
            var (start, length) = ContentNested.Own(body);
            var own = text.Substring(start, length);

            var (laying, laidKb) = Cost(() => Laying.Lay(named, own, Room, style, writing: true, inputs: inputs));
            nested += laying;
            Add(languages, named, laying, laidKb);

            Language(named, own, style, inputs, stages);
        }

        foreach (var block in reading.Root.Children.Where(part => part.Role != Roles.Trivia && !part.Derived))
        {
            var source = block.Print();
            var (blockMs, blockKb) = Cost(() => Laying.Lay(null, source, Room, style, writing: true));
            Add(kinds, block.Kind, blockMs, blockKb);
        }

        // Painting what was just laid, as a keystroke does; and painting the same tree again, as a caret blink or a
        // change of selection does — the two differ by whatever painting keeps from one time to the next.
        var (paint, paintKb) = Timed(() => Laying.Lay(null, text, Room, style, writing: true, inputs: inputs),
                          fresh => Painted(fresh, style));
        var repaint = Median(() => Painted(laid, style));

        // The same, as a window showing a screen of it paints it: only what is near the screen.
        var (paintShown, paintShownKb) = Timed(() => Laying.Lay(null, text, Room, style, writing: true, inputs: inputs),
                                               fresh => Painted(fresh, style, Screen));

        // Painting what a keystroke laid, where the page was painted before it: what was kept of the blocks nobody typed in
        // is drawn as it was, and only the block typed in is painted.
        var painter = new ContentEngine(inputs);
        Painted(painter.Lay(null, EditState.For(text), style, Room, false), style);
        var retyped = 0;
        var (editPaint, editPaintKb) = Timed(() => painter.Lay(null, EditState.For(text.Insert(middle, new string('x', ++retyped))), style, Room, false),
                                             typedIn => Painted(typedIn, style));

        // What a document open for writing holds on to once it is on the page: its tree, the pictures its blocks keep,
        // and the reading kept so the next keystroke knows which blocks it has not touched.
        var retained = Retained(() =>
        {
            var held = new ContentEngine(inputs);
            var shown = held.Lay(null, EditState.For(text), style, Room, false);
            Painted(shown, style);

            return (held, shown);
        });

        var retainedShown = Retained(() =>
        {
            var held = new ContentEngine(inputs);
            var shown = held.Lay(null, EditState.For(text), style, Room, false);
            Painted(shown, style, Screen);

            return (held, shown);
        });

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
            
            ["build"] = Round(build),
            ["nested"] = Round(nested),
            ["buildOwn"] = Round(Math.Max(0, build - nested)),
            ["paint"] = Round(paint),
            ["repaint"] = Round(repaint),
            ["editPaint"] = Round(editPaint),
            ["openKb"] = Math.Round(openKb, 1),
            ["editKb"] = Math.Round(editKb, 1),
            ["paintKb"] = Math.Round(paintKb, 1),
            ["editPaintKb"] = Math.Round(editPaintKb, 1),
            ["retainedKb"] = Math.Round(retained, 1),
            ["paintShown"] = Round(paintShown),
            ["paintShownKb"] = Math.Round(paintShownKb, 1),
            ["retainedShownKb"] = Math.Round(retainedShown, 1),
        };
    }

    /// <summary>A nested language's own parse and each of its stages, and what laying it out costs whole.</summary>
    private static void Language(string language, string source, StyleFormat style, ContentInputs inputs,
                                 Dictionary<string, (int Count, double Ms, double Kb)> stages)
    {
        try
        {
            if (ContentLanguages.For(language) is not { } read) return;

            var parse = read.Parser();
            var tree = Time(stages, $"{language}: parse", () => parse(source).Tree);

            var showing = new ContentShowing(language, style, false, null, 0, inputs) { Reads = ContentLanguages.Reads };

            Staged(stages, language, tree, read.Stages(tree, showing).OfType<IAstStage>());
            Time(stages, $"{language}: lay", () => Laying.Lay(language, source, Room, style, inputs: inputs));
        }
        catch (Exception ex)
        {
            Add(stages, $"{language}: threw {ex.GetType().Name}", 0, 0);
        }
    }

    private static ContentNode Staged(Dictionary<string, (int Count, double Ms, double Kb)> into, string language, ContentNode tree,
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
            ["openKb"] = Sum("openKb"),
            ["editKb"] = Sum("editKb"),
            ["paintKb"] = Sum("paintKb"),
            ["editPaintKb"] = Sum("editPaintKb"),
            ["retainedKb"] = Sum("retainedKb"),
            ["paintShown"] = Sum("paintShown"),
            ["paintShownKb"] = Sum("paintShownKb"),
            ["retainedShownKb"] = Sum("retainedShownKb"),
        };
    }

    private static List<Dictionary<string, object>> Listed(Dictionary<string, (int Count, double Ms, double Kb)> totals) =>
        [.. totals.OrderByDescending(entry => entry.Value.Ms).Select(entry => new Dictionary<string, object>
        {
            ["name"] = entry.Key,
            ["count"] = entry.Value.Count,
            ["ms"] = Round(entry.Value.Ms),
            ["kb"] = Round(entry.Value.Kb),
        })];

    /// <summary>
    /// <paramref name="run"/>, with what it costs told to <paramref name="into"/> under <paramref name="name"/>.
    ///
    /// <para>
    /// Both what it takes and what it allocates, because the two answer different questions and only one of them is
    /// trustworthy on a machine doing anything else: a time is worth comparing only with one taken minutes earlier,
    /// where an allocation is the same on any machine on any day.
    /// </para>
    /// </summary>
    private static T Time<T>(Dictionary<string, (int Count, double Ms, double Kb)> into, string name, Func<T> run)
    {
        var result = run();
        var (ms, kb) = Cost(() => result = run());

        Add(into, name, ms, kb);
        return result;
    }

    private static void Add(Dictionary<string, (int Count, double Ms, double Kb)> into, string name, double ms, double kb)
    {
        var (count, ran, allocated) = into.GetValueOrDefault(name);
        into[name] = (count + 1, ran + ms, allocated + kb);
    }

    private static double Round(double ms) => Math.Round(ms, 3);

    /// <summary>Paints a laid tree as the element does, and says how long that took — all of it, or what is near <paramref name="showing"/>.</summary>
    private static double Painted(Laid laid, StyleFormat style, Rect? showing = null)
    {
        var clock = Stopwatch.StartNew();
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen()) LayoutPainter.Paint(dc, laid.Root, style.Text, showing);

        return clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>A screen's worth of the document, at the top — what a window opening it shows.</summary>
    private static readonly Rect Screen = new(0, 0, Room, 1000);

    /// <summary>
    /// The medians of <see cref="Runs"/> runs of <paramref name="timed"/>, each on something <paramref name="make"/> made afresh,
    /// untimed: how long it said it took, and how much it allocated.
    /// </summary>
    private static (double Ms, double Kb) Timed<T>(Func<T> make, Func<T, double> timed)
    {
        timed(make());
        var times = new double[Runs];
        var bytes = new double[Runs];

        for (var at = 0; at < Runs; at++)
        {
            var made = make();
            var allocated = GC.GetAllocatedBytesForCurrentThread();

            times[at] = timed(made);
            bytes[at] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0;
        }

        Array.Sort(times);
        Array.Sort(bytes);
        return (times[Runs / 2], bytes[Runs / 2]);
    }

    /// <summary>The median of <see cref="Runs"/> timings, after one run to warm up.</summary>
    private static double Median(Action run) => Cost(run).Ms;

    /// <summary>
    /// The medians of <see cref="Runs"/> runs, after one to warm up: how long each took, and how much it allocated — which is
    /// what the collector is left to clear up after every keystroke, whatever it cost at the time.
    /// </summary>
    private static (double Ms, double Kb) Cost(Action run)
    {
        run();
        var times = new double[Runs];
        var bytes = new double[Runs];

        for (var at = 0; at < Runs; at++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();

            run();

            times[at] = clock.Elapsed.TotalMilliseconds;
            bytes[at] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0;
        }

        Array.Sort(times);
        Array.Sort(bytes);
        return (times[Runs / 2], bytes[Runs / 2]);
    }

    /// <summary>How much more the heap holds, collected, while what <paramref name="make"/> made is still held — in KB.</summary>
    private static double Retained<T>(Func<T> make)
    {
        make();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var held = make();
        var after = GC.GetTotalMemory(forceFullCollection: true);

        GC.KeepAlive(held);
        return Math.Max(0, after - before) / 1024.0;
    }

    private static double Median<T>(Func<T> run) => Cost(() => { run(); }).Ms;

    /// <summary>
    /// Where the memory of the largest document goes: what each layer of it holds once laid and painted — the syntax tree, its
    /// reading, the laid tree, the pictures kept of its blocks, and all of it as an open document holds it — and what opening
    /// it and typing in it allocate, by type, sampled by the runtime about every hundred KB.
    /// </summary>
    private static Dictionary<string, object> Memory(string text, StyleFormat style, ContentInputs inputs)
    {
        void Paint(Laid laid)
        {
            var visual = new DrawingVisual();
            using var dc = visual.RenderOpen();
            LayoutPainter.Paint(dc, laid.Root, style.Text);
        }

        // Every cache warmed first, so what is held is what the document holds.
        Paint(new ContentEngine(inputs).Lay(null, EditState.For(text), style, Room, false));

        static long Heap() => GC.GetTotalMemory(forceFullCollection: true);

        var nothing = Heap();
        var tree = Laying.Read(null, text).Root.Node;
        var read = Heap();
        var reading = ContentReading.Of(tree, 0, text);
        var positioned = Heap();
        var laid = Laying.Lay(null, text, Room, style, writing: true, inputs: inputs);
        var built = Heap();
        Paint(laid);
        var painted = Heap();

        GC.KeepAlive(tree);
        GC.KeepAlive(reading);
        GC.KeepAlive(laid);

        var held = new Dictionary<string, object>
        {
            ["syntaxKb"] = Math.Round((read - nothing) / 1024.0, 1),
            ["readingKb"] = Math.Round((positioned - read) / 1024.0, 1),
            ["laidKb"] = Math.Round((built - positioned) / 1024.0, 1),
            ["picturesKb"] = Math.Round((painted - built) / 1024.0, 1),
        };

        using var allocations = new Allocations();
        Thread.Sleep(500);

        var middle = Middle(text);
        var typing = new ContentEngine(inputs);
        typing.Lay(null, EditState.For(text), style, Room, false);
        var typed = 0;

        return new Dictionary<string, object>
        {
            ["held"] = held,
            ["openByType"] = allocations.Sampled(() => new ContentEngine(inputs).Lay(null, EditState.For(text), style, Room, false), 5),
            ["editByType"] = allocations.Sampled(() => typing.Lay(null, EditState.For(text.Insert(middle, new string('x', ++typed))), style, Room, false), 10),
        };
    }

    /// <summary>
    /// What is allocated, by type — the runtime's own sampling, which names the type of whatever was being allocated each time
    /// about a hundred KB more had been, heard in this process.
    /// </summary>
    private sealed class Allocations : EventListener
    {
        private readonly Dictionary<string, long> _byType = [];
        private volatile bool _hearing;

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
        }

        protected override void OnEventWritten(EventWrittenEventArgs heard)
        {
            if (!_hearing || heard.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || heard.Payload is null) return;

            var names = heard.PayloadNames!;
            var type = names.IndexOf("TypeName") is var t and >= 0 ? heard.Payload[t] as string ?? "?" : "?";
            var amount = names.IndexOf("AllocationAmount64") is var a and >= 0 ? Convert.ToInt64(heard.Payload[a]) : 100_000;

            lock (_byType) _byType[type] = _byType.GetValueOrDefault(type) + amount;
        }

        /// <summary>What <paramref name="run"/> allocates each time, by type, the most first — as KB and as a share.</summary>
        public List<Dictionary<string, object>> Sampled(Action run, int times)
        {
            lock (_byType) _byType.Clear();

            _hearing = true;
            for (var at = 0; at < times; at++) run();

            // The runtime hands its events over on a thread of its own, a little after they happen.
            Thread.Sleep(1500);
            _hearing = false;

            List<KeyValuePair<string, long>> heard;
            lock (_byType) heard = [.. _byType.OrderByDescending(type => type.Value)];

            var total = Math.Max(1, heard.Sum(type => type.Value));

            return [.. heard.Take(25).Select(type => new Dictionary<string, object>
            {
                ["name"] = type.Key,
                ["kb"] = Math.Round(type.Value / (double)times / 1024.0, 1),
                ["share"] = Math.Round(type.Value * 100.0 / total, 1),
            })];
        }
    }

    /// <summary>
    /// How fast this machine is going right now, as a number every run carries.
    ///
    /// <para>
    /// A time is only worth comparing with one taken on the same machine in the same state, and no label can promise the
    /// state: something updating in the background puts every figure in a run up by more than a tenth, and nothing in
    /// the run itself says so. This is a fixed amount of arithmetic that allocates nothing, so two runs whose pace
    /// agrees are worth comparing and one whose pace is high was measured on a machine that was busy.
    /// </para>
    /// </summary>
    private static double Pace()
    {
        var times = new double[3];

        for (var at = 0; at < times.Length; at++)
        {
            var clock = Stopwatch.StartNew();

            var sum = 0L;
            for (var spin = 0; spin < 30_000_000; spin++) sum += spin & 15;

            times[at] = sum == 0 ? 0 : clock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        return Round(times[1]);
    }

    /// <summary>
    /// The document the type profile is taken from: the one holding a bit of every language.
    ///
    /// <para>
    /// Which matters more than it sounds. A profile is a list of what was allocated by type, and taken from a document
    /// of prose alone it names the types prose allocates and no others — so it says nothing about what laying a formula
    /// or a cloud costs, which is most of what a corpus of documents costs. Reading one of those as though it spoke for
    /// everything is how an afternoon gets spent in the wrong file.
    /// </para>
    /// </summary>
    private static string Profiled(IReadOnlyList<string> docs) =>
        docs.FirstOrDefault(path => Path.GetFileName(path) == "mixed-content.md") ?? docs[^1];
}
