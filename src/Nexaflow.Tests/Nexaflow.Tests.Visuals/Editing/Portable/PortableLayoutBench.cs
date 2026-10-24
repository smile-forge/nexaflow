using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// Whether a layout tree free of WPF costs anything over the WPF one — measured, over every document the markdown bench lays.
///
/// <para>
/// Each document is laid by the engine as a surface lays it, and its tree taken apart into the calls that made it
/// (<see cref="LaidReplay"/>). Then the same tree is made again both ways, the same work in the same order, and painted, pressed
/// and caretted both ways: the WPF tree with WPF's marks and its own painter, and the renderer-free tree with
/// <see cref="WpfMedium"/>'s handles and <see cref="PortablePainter"/>. What one costs over the other is what the abstraction
/// costs. Painting both into pixels and comparing them is what says the two trees are the same drawing.
/// </para>
/// <para>
/// Opt in with <c>NEXAFLOW_PORTABLE_BENCH</c> pointing at a folder; each run writes one <c>portable-layout-*.md</c> there.
/// Run it in Release: a comparison only means something between two runs on one machine in one configuration.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class PortableLayoutBench
{
    private const string Folder = "NEXAFLOW_PORTABLE_BENCH";

    /// <summary>How many times each step is timed, each way, alternating; the median is kept.</summary>
    private const int Runs = 7;

    private const double Room = 900;

    /// <summary>How many presses a document is asked about, spread over all of it.</summary>
    private const int Presses = 200;

    /// <summary>How much of a document is turned into pixels to compare the two drawings.</summary>
    private const double Compared = 2400;

    private static readonly Rect Screen = new(0, 0, Room, 1000);

    [TestMethod]
    public void WhatALayoutFreeOfWpfCosts() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var docs = TestSampleData.Files("markdown").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                                 .Select(path => (Path.GetFileName(path), File.ReadAllText(path))).ToList();
        docs.Add(("MarkdownSupport.md", File.ReadAllText(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")))));
        docs.Add(("generated-lecture.md", MarkdownLayoutBench.LectureScript(MarkdownLayoutBench.LectureSections)));

        // A run over some of them, to measure one change again without waiting on the whole corpus.
        if (Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_ONLY") is { Length: > 0 } only)
            docs = [.. docs.Where(doc => System.Text.RegularExpressions.Regex.IsMatch(doc.Item1, only))];

        var style = StyleFormat.Dark;
        var medium = new WpfMedium();
        var ink = medium.Ink(style.Text);

        var rows = new List<Row>();
        foreach (var (name, text) in docs)
        {
            var clock = Stopwatch.StartNew();
            rows.Add(Document(name, text, style, medium, ink));
            Console.WriteLine($"{name}: {rows[^1].Pieces} pieces, measured in {clock.Elapsed.TotalSeconds:F1}s");
        }

        var report = new StringBuilder();
#if DEBUG
        report.AppendLine("**Debug build — not a measurement worth keeping.**").AppendLine();
#endif
        report.AppendLine($"# A layout tree free of WPF — {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.MachineName}");
        report.AppendLine();
        report.AppendLine($"{docs.Count} documents laid at {Room}, {Runs} runs each way alternating, medians. ms unless said; "
                          + "W = the WPF tree as it is, P = the tree free of WPF painted through the WPF medium; Δ = P − W.");
        report.AppendLine();
        report.AppendLine("| document | pieces | marks | engine open | compose W | compose P | Δ | KB W | KB P | first paint W | first paint P | Δ | repaint W | repaint P | screen W | screen P | press W µs | press P µs | carets W | carets P | pixels differing |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var row in rows)
            report.AppendLine($"| {row.Name} | {row.Pieces} | {row.Marks} | {row.Open:F2} | {row.Compose.W:F3} | {row.Compose.P:F3} | {Delta(row.Compose)} "
                              + $"| {row.Kb.W:F0} | {row.Kb.P:F0} | {row.Paint.W:F3} | {row.Paint.P:F3} | {Delta(row.Paint)} | {row.Repaint.W:F3} | {row.Repaint.P:F3} "
                              + $"| {row.Shown.W:F3} | {row.Shown.P:F3} | {row.Press.W:F2} | {row.Press.P:F2} | {row.Carets.W:F3} | {row.Carets.P:F3} | {row.Differing} |");

        (double W, double P) Sum(Func<Row, (double W, double P)> of) => (rows.Sum(row => of(row).W), rows.Sum(row => of(row).P));

        var compose = Sum(row => row.Compose);
        var kb = Sum(row => row.Kb);
        var paint = Sum(row => row.Paint);
        var repaint = Sum(row => row.Repaint);
        var shown = Sum(row => row.Shown);
        var press = Sum(row => row.Press);
        var carets = Sum(row => row.Carets);

        report.AppendLine($"| **all** | {rows.Sum(row => row.Pieces)} | {rows.Sum(row => row.Marks)} | {rows.Sum(row => row.Open):F1} | {compose.W:F2} | {compose.P:F2} | {Delta(compose)} "
                          + $"| {kb.W:F0} | {kb.P:F0} | {paint.W:F2} | {paint.P:F2} | {Delta(paint)} | {repaint.W:F2} | {repaint.P:F2} | {shown.W:F2} | {shown.P:F2} "
                          + $"| {press.W:F1} | {press.P:F1} | {carets.W:F2} | {carets.P:F2} | {rows.Sum(row => row.Differing)} |");

        report.AppendLine();
        report.AppendLine("Marks laid, by kind: " + string.Join(", ", rows.SelectMany(row => row.Kinds).GroupBy(kind => kind.Key)
                                                                         .Select(kind => $"{kind.Key} {kind.Sum(each => each.Value)}")));
        report.AppendLine();
        report.AppendLine("What the recipe could not take as it was laid: " +
                          (rows.SelectMany(row => row.Unfaithful).GroupBy(miss => miss.Key).Select(miss => $"{miss.Key} {miss.Sum(each => each.Value)}")
                               .DefaultIfEmpty("nothing") is var misses ? string.Join(", ", misses) : ""));
        report.AppendLine();
        report.AppendLine("Pixels differing between the replayed WPF tree and the engine's own tree: "
                          + string.Join(", ", rows.Where(row => row.Replayed > 0).Select(row => $"{row.Name} {row.Replayed}").DefaultIfEmpty("none")));

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"portable-layout-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(file, report.ToString());

        Console.WriteLine(file);
        Console.WriteLine(report);
    });

    private static string Delta((double W, double P) pair) =>
        pair.W <= 0 ? "" : $"{pair.P - pair.W:+0.000;-0.000} ({(pair.P - pair.W) / pair.W:+0%;-0%})";

    private sealed record Row(string Name, int Pieces, int Marks, double Open, (double W, double P) Compose, (double W, double P) Kb,
                              (double W, double P) Paint, (double W, double P) Repaint, (double W, double P) Shown,
                              (double W, double P) Press, (double W, double P) Carets, long Differing, long Replayed,
                              IReadOnlyDictionary<string, int> Kinds, IReadOnlyDictionary<string, int> Unfaithful);

    private static Row Document(string name, string text, StyleFormat style, WpfMedium medium, WpfInk ink)
    {
        var laid = Laying.Lay(null, text, Room, style, writing: true);
        var replay = LaidReplay.Of(laid.Tree, medium);

        var open = Median(() => Laying.Lay(null, text, Room, style, writing: true));

        // Building the tree, both ways — every word set, outline drawn and pen made again.
        var compose = Alternating(() => replay.Wpf(), () => replay.Portable());
        var kb = (Allocated(() => replay.Wpf()), Allocated(() => replay.Portable()));

        // Painting a tree for the first time, which records every kept picture; and painting it again, which hands them over.
        var paint = Alternating(replay.Wpf, replay.Portable,
                                tree => Painted(dc => LayoutPainter.Paint(dc, tree.Root, style.Text)),
                                tree => Painted(dc => PortablePainter.Paint(dc, tree.Root, ink)));

        var wpf = replay.Wpf();
        var portable = replay.Portable();
        Painted(dc => LayoutPainter.Paint(dc, wpf.Root, style.Text));
        Painted(dc => PortablePainter.Paint(dc, portable.Root, ink));

        var repaint = Alternating(() => Painted(dc => LayoutPainter.Paint(dc, wpf.Root, style.Text)),
                                  () => Painted(dc => PortablePainter.Paint(dc, portable.Root, ink)));

        var shown = Alternating(replay.Wpf, replay.Portable,
                                tree => Painted(dc => LayoutPainter.Paint(dc, tree.Root, style.Text, Screen)),
                                tree => Painted(dc => PortablePainter.Paint(dc, tree.Root, ink, Screen)));

        // Pressing all over it, and asking every run of words where its carets stand: the answers must agree.
        var points = Points(wpf.Root.Bounds);
        Assert.AreEqual(Pressed(wpf, points), Pressed(portable, points), $"{name}: the two trees answer a press differently");
        Assert.AreEqual(Caretted(wpf), Caretted(portable), 1e-6, $"{name}: the two trees put carets in different places");

        var press = Alternating(() => Pressed(wpf, points), () => Pressed(portable, points));
        var carets = Alternating(() => Caretted(wpf), () => Caretted(portable));

        // The same drawing: the replayed WPF tree against the tree free of WPF, and against the engine's own.
        var size = new Size(Room, Math.Min(Compared, Math.Max(1, wpf.Root.Bounds.Bottom)));
        var drawnWpf = Pixels(dc => LayoutPainter.Paint(dc, wpf.Root, style.Text), size);
        var drawnPortable = Pixels(dc => PortablePainter.Paint(dc, portable.Root, ink), size);
        var drawnLaid = Pixels(dc => LayoutPainter.Paint(dc, laid.Root, style.Text), size);

        return new Row(name, replay.Pieces, replay.Marks, open, compose, kb, paint, repaint, shown,
                       (press.W * 1000 / Presses, press.P * 1000 / Presses), carets,
                       Differing(drawnWpf, drawnPortable), Differing(drawnLaid, drawnWpf), replay.MarkKinds, replay.Unfaithful);
    }

    // ── Timing ─────────────────────────────────────────────────────────────────

    /// <summary>Two ways of doing one thing, timed by turns after a warm-up — so drift on the machine lands on both.</summary>
    private static (double W, double P) Alternating<TW, TP>(Func<TW> wpf, Func<TP> portable)
    {
        Warm(() => { wpf(); portable(); });

        var w = new double[Runs];
        var p = new double[Runs];
        Settle();

        // Which goes first is swapped every run, so whatever one run leaves the next to pay for is shared.
        for (var run = 0; run < Runs; run++)
        {
            if (run % 2 == 0) { w[run] = Timed(wpf); p[run] = Timed(portable); }
            else { p[run] = Timed(portable); w[run] = Timed(wpf); }
        }

        return (Middle(w), Middle(p));
    }

    /// <summary>The same, of something done to a thing made fresh for each run, untimed.</summary>
    private static (double W, double P) Alternating<TW, TP>(Func<TW> makeWpf, Func<TP> makePortable, Func<TW, double> wpf, Func<TP, double> portable)
    {
        Warm(() => { wpf(makeWpf()); portable(makePortable()); });

        var w = new double[Runs];
        var p = new double[Runs];

        for (var run = 0; run < Runs; run++)
        {
            var madeWpf = makeWpf();
            var madePortable = makePortable();

            GC.Collect(1, GCCollectionMode.Forced, blocking: true);
            if (run % 2 == 0) { w[run] = wpf(madeWpf); p[run] = portable(madePortable); }
            else { p[run] = portable(madePortable); w[run] = wpf(madeWpf); }
        }

        return (Middle(w), Middle(p));
    }

    /// <summary>
    /// Both ways run until the JIT has finished with them — thirty calls, or a third of a second of them, whichever comes first.
    /// Code run a few times is still the JIT's first draft, and the way written for this pilot is colder than the one the engine
    /// has been running all along.
    /// </summary>
    private static void Warm(Action both)
    {
        var clock = Stopwatch.StartNew();
        for (var warm = 0; warm < 30 && (warm < 2 || clock.ElapsedMilliseconds < 300); warm++) both();
    }

    /// <summary>
    /// One run, timed from a young generation swept clean — so neither way pays for collecting what the other left behind. A
    /// full collection each time would cost more than the run on a heap holding the corpus, and the young ones are what a run
    /// fills.
    /// </summary>
    private static double Timed<T>(Func<T> run)
    {
        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
        var clock = Stopwatch.StartNew();
        run();
        return clock.Elapsed.TotalMilliseconds;
    }

    private static double Median<T>(Func<T> run)
    {
        run();
        var times = new double[5];
        for (var at = 0; at < times.Length; at++) times[at] = Timed(run);
        return Middle(times);
    }

    private static double Middle(double[] times)
    {
        Array.Sort(times);
        return times[times.Length / 2];
    }

    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static double Allocated<T>(Func<T> run)
    {
        run();
        Settle();

        var before = GC.GetAllocatedBytesForCurrentThread();
        run();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0;
    }

    private static double Painted(Action<DrawingContext> paint)
    {
        var clock = Stopwatch.StartNew();
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen()) paint(dc);

        return clock.Elapsed.TotalMilliseconds;
    }

    // ── Asking it things ───────────────────────────────────────────────────────

    /// <summary>Points spread over a document's whole extent, the same every run.</summary>
    private static Point[] Points(Rect over)
    {
        var random = new Random(17);
        var points = new Point[Presses];

        for (var at = 0; at < points.Length; at++)
            points[at] = new Point(over.X + (random.NextDouble() * over.Width), over.Y + (random.NextDouble() * over.Height));

        return points;
    }

    /// <summary>
    /// For each point, the last piece in reading order whose box holds it and whose shape, where it says one, does — summed into
    /// one number both trees must agree on. The walk a press makes down a tree, without the rules a selection adds on top.
    /// </summary>
    private static long Pressed(LayoutTree tree, Point[] points)
    {
        long sum = 0;

        foreach (var point in points)
        {
            var hit = -1;

            foreach (var (at, where) in tree.Within(0))
            {
                if (!where.Contains(point)) continue;
                if (tree.RegionOf(at) is { } region && !region.FillContains(tree.FrameOf(at).Back(point))) continue;

                hit = at;
            }

            sum += hit + 1;
        }

        return sum;
    }

    private static long Pressed(PortableTree tree, Point[] points)
    {
        long sum = 0;

        foreach (var point in points)
        {
            var hit = -1;
            var at = new LayoutPoint(point.X, point.Y);

            foreach (var (piece, where) in tree.Within(0))
            {
                if (!where.Contains(at)) continue;
                if (tree.RegionOf(piece) is { } region && !region.Contains(tree.FrameOf(piece).Back(at))) continue;

                hit = piece;
            }

            sum += hit + 1;
        }

        return sum;
    }

    /// <summary>Where every run of words puts its caret at either end and in the middle, and which letter its middle is.</summary>
    private static double Caretted(LayoutTree tree)
    {
        var sum = 0.0;

        for (var at = 0; at < tree.Count; at++)
            if (tree.WordsOf(at) is { } words)
            {
                sum += words.Caret(0).X + words.Caret(words.Length / 2).X + words.Caret(words.Length).X;
                sum += words.IndexAt(words.Caret(words.Length / 2).X + 0.5);
                sum += words.Covers(0, words.Length) is { IsEmpty: false } covers ? covers.Width : 0;
            }

        return sum;
    }

    private static double Caretted(PortableTree tree)
    {
        var sum = 0.0;

        for (var at = 0; at < tree.Count; at++)
            if (tree.WordsOf(at) is { } words)
            {
                sum += words.Caret(0).X + words.Caret(words.Length / 2).X + words.Caret(words.Length).X;
                sum += words.IndexAt(words.Caret(words.Length / 2).X + 0.5);
                sum += words.Covers(0, words.Length) is { IsEmpty: false } covers ? covers.Width : 0;
            }

        return sum;
    }

    // ── Comparing the drawings ─────────────────────────────────────────────────

    private static byte[] Pixels(Action<DrawingContext> paint, Size size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(size));
            paint(dc);
        }

        var width = (int)Math.Ceiling(size.Width);
        var height = (int)Math.Ceiling(size.Height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static long Differing(byte[] a, byte[] b)
    {
        long differing = 0;
        for (var at = 0; at + 3 < Math.Min(a.Length, b.Length); at += 4)
            if (a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2] || a[at + 3] != b[at + 3]) differing++;

        return differing + (Math.Abs(a.Length - b.Length) / 4);
    }
}
