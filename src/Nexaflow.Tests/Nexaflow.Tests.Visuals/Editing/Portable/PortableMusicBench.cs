using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Layout.Scores;
using Nexaflow.Markdown.Scores;
using Nexaflow.Markdown.Scores.Abc;
using Nexaflow.Markdown.Scores.LilyPond;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.LilyPond;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// Every tune the sample documents hold, and a sample of a real-world corpus, laid as a document lays them — timed, and written
/// down exactly, so the engraver can be moved and its scores held to what they were.
///
/// <para>
/// What is written down is the whole of each laid tree, every outline by its bounds, its inks and a hash of its exact path, so
/// two runs that write the same file engraved the same tunes the same way to the last curve. Opt in with
/// <c>NEXAFLOW_PORTABLE_BENCH</c>; <c>NEXAFLOW_BENCH_LABEL</c> names the run, <c>NEXAFLOW_ABC_CORPUS</c> where the corpus is.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class PortableMusicBench
{
    private const string Folder = "NEXAFLOW_PORTABLE_BENCH";

    private const int Runs = 9;

    internal const double Room = 900;

    private const double Compared = 2400;

    /// <summary>How many of the corpus's tunes are taken — the first this many, in the order the folders list them.</summary>
    private const int Sampled = 400;

    private const string Corpus = @"D:\Datasets\abcmusic\zenoob";

    private static readonly string[] Pages = ["music-abc.md", "music-lilypond.md"];

    [TestMethod]
    public void EveryTuneAsItIsLaid() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var label = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_LABEL") is { Length: > 0 } named ? named : "run";
        var tunes = Tunes();
        var style = StyleFormat.Dark with { Standing = ContentStanding.Block };

        var digest = new StringBuilder();
        var pieces = 0;
        foreach (var (dialect, source) in tunes)
        {
            var laid = Lay(dialect, source, style);
            pieces += laid.Tree.Count;
            digest.Append("== ").Append(dialect).Append(' ').AppendLine(source.Length > 60 ? source[..60].ReplaceLineEndings(" ") : source.ReplaceLineEndings(" "));
            PortableTexBench.Digest(laid, digest);
        }

        Directory.CreateDirectory(folder!);
        File.WriteAllText(Path.Combine(folder!, $"music-digest-{label}.txt"), digest.ToString());

        foreach (var page in Pages)
        {
            var text = File.ReadAllText(TestSampleData.Files("markdown").First(path => Path.GetFileName(path) == page));
            var laid = Laying.Lay(null, text, Room, StyleFormat.Dark, writing: true);
            File.WriteAllBytes(Path.Combine(folder!, $"music-page-{label}-{page}.bin"),
                               PortableTexBench.Pixels(dc => LayoutPainter.Paint(dc, laid.Root, style.Text),
                                                       new Size(Room, Math.Min(Compared, laid.Size.Height))));
        }

        for (var warm = 0; warm < 2; warm++) LayAll(tunes, style);

        var times = new double[Runs];
        for (var run = 0; run < Runs; run++)
        {
            GC.Collect(1, GCCollectionMode.Forced, blocking: true);
            var clock = Stopwatch.StartNew();
            LayAll(tunes, style);
            times[run] = clock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        var said = $"{DateTime.Now:yyyy-MM-dd HH:mm} {label}: {tunes.Count} tunes, {pieces} pieces — laid in {times[Runs / 2]:F1} ms "
                   + $"(fastest {times[0]:F1}, slowest {times[^1]:F1})";

        File.AppendAllLines(Path.Combine(folder!, "music-timing.txt"), [said]);
        Console.WriteLine(said);
    });

    /// <summary>
    /// The WPF music builder and the one free of WPF, on the same readings of the same tunes through the same engraver: what each
    /// lays, compared piece for piece and pixel for pixel, and what laying them all costs each, timed by turns.
    /// </summary>
    [TestMethod]
    public void TheMusicBuilderFreeOfWpfAgainstTheOneThereIs() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var style = StyleFormat.Dark with { Standing = ContentStanding.Block };
        var builders = new Builders(style);
        var subjects = Subjects();

        // The same layout, both ways — and the WPF builder handed a reading directly laying what the engine laid.
        var (engine, wpf, free) = (new StringBuilder(), new StringBuilder(), new StringBuilder());
        long differing = 0;
        var tunesDiffering = new List<string>();

        foreach (var subject in subjects)
        {
            var (w, p) = (builders.Wpf(subject), builders.Portable(subject));
            var name = subject.Source.Length > 60 ? subject.Source[..60].ReplaceLineEndings(" ") : subject.Source.ReplaceLineEndings(" ");

            engine.Append("== ").Append(subject.Dialect).Append(' ').AppendLine(name);
            PortableTexBench.Digest(Lay(subject.Dialect, subject.Source, style), engine);
            wpf.Append("== ").AppendLine(name);
            PortableTexBench.Digest(w, wpf, advances: false);
            free.Append("== ").AppendLine(name);
            PortableTexBench.Digest(p, free);

            var size = new Size(Math.Max(1, Math.Ceiling(w.Size.Width)), Math.Max(1, Math.Min(Compared, Math.Ceiling(w.Size.Height))));
            var pixels = Differing(PortableTexBench.Pixels(dc => LayoutPainter.Paint(dc, w.Root, style.Text), size),
                                   PortableTexBench.Pixels(dc => PortablePainter.Paint(dc, p.Root, builders.Ink), size));
            if (pixels > 0) tunesDiffering.Add($"{name} ({pixels})");
            differing += pixels;
        }

        Directory.CreateDirectory(folder!);
        File.WriteAllText(Path.Combine(folder!, "music-digest-builder-wpf.txt"), wpf.ToString());
        File.WriteAllText(Path.Combine(folder!, "music-digest-builder-free.txt"), free.ToString());

        var baseline = Path.Combine(folder!, "music-digest-baseline.txt");
        var report = new StringBuilder()
            .AppendLine($"# The music builder free of WPF — {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.MachineName}").AppendLine()
            .AppendLine($"{subjects.Count} tunes.").AppendLine()
            .AppendLine($"- the engine lays what the baseline laid: {(File.Exists(baseline) ? (File.ReadAllText(baseline) == engine.ToString()).ToString() : "no baseline")}")
            .AppendLine($"- the two builders lay the same pieces: {wpf.ToString() == free.ToString()}")
            .AppendLine($"- pixels differing: {differing}, in {tunesDiffering.Count} tunes")
            .AppendLine();

        foreach (var which in tunesDiffering.Take(10)) report.AppendLine($"  - {which}");

        for (var warm = 0; warm < 2; warm++) foreach (var s in subjects) { builders.Wpf(s); builders.Portable(s); }

        var (wt, pt) = (new double[Runs], new double[Runs]);
        for (var run = 0; run < Runs; run++)
        {
            double W() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in subjects) builders.Wpf(s); return clock.Elapsed.TotalMilliseconds; }
            double P() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in subjects) builders.Portable(s); return clock.Elapsed.TotalMilliseconds; }

            if (run % 2 == 0) { wt[run] = W(); pt[run] = P(); }
            else { pt[run] = P(); wt[run] = W(); }
        }

        Array.Sort(wt);
        Array.Sort(pt);

        report.AppendLine().AppendLine("| | median ms | fastest ms |").AppendLine("|---|---:|---:|")
              .AppendLine($"| W | {wt[Runs / 2]:F1} | {wt[0]:F1} |")
              .AppendLine($"| P | {pt[Runs / 2]:F1} | {pt[0]:F1} |")
              .AppendLine($"| Δ | {pt[Runs / 2] - wt[Runs / 2]:+0.0;-0.0} ({(pt[Runs / 2] - wt[Runs / 2]) / wt[Runs / 2]:+0%;-0%}) | {pt[0] - wt[0]:+0.0;-0.0} |");

        File.WriteAllText(Path.Combine(folder!, $"portable-music-{DateTime.Now:yyyyMMdd-HHmmss}.md"), report.ToString());
        Console.WriteLine(report);
    });

    /// <summary>
    /// One builder engraving every tune over and over for twenty seconds, alone in its process — <c>NEXAFLOW_PORTABLE_PROFILE</c>
    /// names which, <c>W</c> or <c>P</c>.
    /// </summary>
    [TestMethod]
    public void OneMusicBuilderForAProfilerToWatch() => UiThread.Run(() =>
    {
        var which = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_PROFILE");
        if (which is not ("W" or "P")) { Assert.Inconclusive("set NEXAFLOW_PORTABLE_PROFILE to W or P"); return; }

        var builders = new Builders(StyleFormat.Dark with { Standing = ContentStanding.Block });
        var subjects = Subjects();

        var clock = Stopwatch.StartNew();
        var passes = 0;

        while (clock.Elapsed.TotalSeconds < 20)
        {
            foreach (var subject in subjects)
                if (which == "W") builders.Wpf(subject);
                else builders.Portable(subject);

            passes++;
        }

        var said = $"{which}: {passes} passes over {subjects.Count} tunes in {clock.Elapsed.TotalSeconds:F1}s, {clock.Elapsed.TotalMilliseconds / passes:F1} ms a pass";
        Console.WriteLine(said);

        if (Environment.GetEnvironmentVariable(Folder) is { Length: > 0 } folder)
            File.AppendAllLines(Path.Combine(folder, "portable-music-throughput.txt"), [said]);
    });

    private sealed record Subject(string Dialect, string Source, ContentReading Reading);

    private static List<Subject> Subjects() =>
        [.. Tunes().Select(tune => new Subject(tune.Dialect, tune.Source, new ContentEngine().Read(tune.Dialect, tune.Source, writing: true)))];

    /// <summary>The two builders, each made the way it is made for a showing — the WPF one as the engine makes it, the other in the medium.</summary>
    private sealed class Builders(StyleFormat style)
    {
        private readonly WpfMedium _medium = new();
        private PortableStyle? _style;
        private PortableScoreFonts? _fonts;

        public WpfInk Ink => _medium.Ink(style.Text);

        public Laid Wpf(Subject subject) =>
            (subject.Dialect == "abc"
                ? new AbcBuilder(subject.Reading, EditState.For(subject.Source), style, isReadOnly: false, Laying.NestingNothing)
                : (ContentBuilder)new LilyPondBuilder(subject.Reading, EditState.For(subject.Source), style, isReadOnly: false, Laying.NestingNothing))
            .Lay(Room);

        public PortableLaid Portable(Subject subject)
        {
            _style ??= PortableProseBench.Styled(style, _medium);
            _fonts ??= PortableScoreFonts.Of(_medium);

            Engraver engraver = subject.Dialect == "abc" ? new AbcEngraver(subject.Reading) : new LilyPondEngraver(subject.Reading);
            return new PortableMusicBuilder(subject.Reading, engraver, _style, _medium, _fonts).Lay(Room);
        }
    }

    private static long Differing(byte[] a, byte[] b)
    {
        long differing = 0;
        for (var at = 0; at + 3 < Math.Min(a.Length, b.Length); at += 4)
            if (a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2] || a[at + 3] != b[at + 3]) differing++;

        return differing + (Math.Abs(a.Length - b.Length) / 4);
    }

    private static void LayAll(List<(string Dialect, string Source)> tunes, StyleFormat style)
    {
        foreach (var (dialect, source) in tunes) Lay(dialect, source, style);
    }

    /// <summary>A tune laid as a document lays one: as a block among blocks, at the page's room.</summary>
    internal static Laid Lay(string dialect, string source, StyleFormat style) =>
        Laying.Lay(dialect, source, Room, style, writing: true);

    /// <summary>
    /// Every tune written in the sample documents and the help's own page, each once, and the first of the corpus's — each with the
    /// dialect it is written in.
    /// </summary>
    internal static List<(string Dialect, string Source)> Tunes()
    {
        var texts = TestSampleData.Files("markdown").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(File.ReadAllText).ToList();
        texts.Add(File.ReadAllText(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md"))));

        var seen = new HashSet<(string, string)>();
        var tunes = new List<(string, string)>();

        foreach (var text in texts)
            foreach (var part in new ContentEngine().Read(null, text, writing: true).Root.SelfAndDescendants())
            {
                var dialect = ContentNested.Language(part)?.ToLowerInvariant();
                if (dialect is not ("abc" or "lilypond") || part.Part(Roles.Body) is not { } body) continue;

                var source = ContentNested.Own(body.Node);
                if (seen.Add((dialect, source))) tunes.Add((dialect, source));
            }

        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_CORPUS") is { Length: > 0 } named ? named : Corpus;
        if (Directory.Exists(corpus))
            foreach (var file in Directory.EnumerateFiles(corpus, "*.abc", SearchOption.AllDirectories).Take(Sampled))
                tunes.Add(("abc", File.ReadAllText(file)));

        return tunes;
    }
}
