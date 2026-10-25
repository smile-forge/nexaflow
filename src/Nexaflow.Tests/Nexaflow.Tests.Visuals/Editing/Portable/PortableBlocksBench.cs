using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Barcode;
using Nexaflow.Markdown.Layout.Chemistry;
using Nexaflow.Markdown.Layout.Code;
using Nexaflow.Markdown.Layout.Matrix;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Layout.WordCloud;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Barcode;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.WordCloud;
using Nexaflow.Visuals.Text.Markdown.WordCloud.Stages;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// Every block the sample documents and the help's own page hold that one of the smaller builders lays — code, the 2D codes, barcodes,
/// word clouds, molecules — and content nothing could read, laid by the WPF builder and by its renderer-free port side by side: written
/// down exactly, painted, and timed, a kind of block at a time.
///
/// <para>
/// What is written down is the whole of each laid tree, every outline by its bounds, its inks and a hash of its exact path, so two
/// builders that write the same digest laid the same pieces to the last curve. Opt in with <c>NEXAFLOW_PORTABLE_BENCH</c>, naming a
/// folder to write the run to.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class PortableBlocksBench
{
    private const string Folder = "NEXAFLOW_PORTABLE_BENCH";

    private const int Runs = 9;

    private const double Room = 900;

    private const double Compared = 2400;

    /// <summary>What a block nothing could read is shown as, and why — the unread builder's whole subject.</summary>
    private static readonly (string Source, string Why)[] Unreadable =
    [
        ("\\frac{a}{", "The block could not be read: the source ended inside a group."),
        ("graph TD\n  A --> B\n  B -->", "The block could not be read: a link has nowhere to go."),
        ("", "The block could not be read: there was nothing in it."),
    ];

    [TestMethod]
    public void TheBuildersFreeOfWpfAgainstTheOnesThereAre() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var style = StyleFormat.Dark with { Standing = ContentStanding.Block };
        var builders = new Builders(style);
        var subjects = Subjects();

        var report = new StringBuilder()
            .AppendLine($"# The smaller builders free of WPF — {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.MachineName}").AppendLine()
            .AppendLine("| builder | blocks | same pieces | pixels differing | W median ms | P median ms | Δ |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|");

        var differences = new StringBuilder();
        var (wpfAll, freeAll) = (new StringBuilder(), new StringBuilder());

        foreach (var kind in subjects.GroupBy(subject => subject.Builder))
        {
            var group = kind.ToList();
            var (same, differing) = (0, 0L);

            foreach (var subject in group)
            {
                var (w, p) = (builders.Wpf(subject), builders.Portable(subject));
                var (wpf, free) = (new StringBuilder(), new StringBuilder());

                PortableTexBench.Digest(w, wpf, advances: false);
                PortableTexBench.Digest(p, free);

                var name = $"{kind.Key.Name} {Named(subject.Source)}";
                wpfAll.Append("== ").AppendLine(name).Append(wpf);
                freeAll.Append("== ").AppendLine(name).Append(free);

                if (wpf.ToString() == free.ToString()) same++;
                else differences.Append("- pieces differ: ").AppendLine(name);

                var size = new Size(Math.Max(1, Math.Ceiling(w.Size.Width)), Math.Max(1, Math.Min(Compared, Math.Ceiling(w.Size.Height))));
                var pixels = Differing(PortableTexBench.Pixels(dc => LayoutPainter.Paint(dc, w.Root, style.Text), size),
                                       PortableTexBench.Pixels(dc => PortablePainter.Paint(dc, p.Root, builders.Ink), size));
                if (pixels > 0) differences.AppendLine($"- {pixels} pixels differ: {name}");
                differing += pixels;
            }

            for (var warm = 0; warm < 2; warm++) foreach (var s in group) { builders.Wpf(s); builders.Portable(s); }

            var (wt, pt) = (new double[Runs], new double[Runs]);
            for (var run = 0; run < Runs; run++)
            {
                double W() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in group) builders.Wpf(s); return clock.Elapsed.TotalMilliseconds; }
                double P() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in group) builders.Portable(s); return clock.Elapsed.TotalMilliseconds; }

                if (run % 2 == 0) { wt[run] = W(); pt[run] = P(); }
                else { pt[run] = P(); wt[run] = W(); }
            }

            Array.Sort(wt);
            Array.Sort(pt);

            var (wm, pm) = (wt[Runs / 2], pt[Runs / 2]);
            report.AppendLine($"| {kind.Key.Name} | {group.Count} | {same} | {differing} | {wm:F2} | {pm:F2} | {(pm - wm) / wm:+0%;-0%} |");
        }

        report.AppendLine().Append(differences.Length == 0 ? "Every block laid the same pieces and painted the same pixels.\n" : differences.ToString());

        Directory.CreateDirectory(folder!);
        File.WriteAllText(Path.Combine(folder!, "blocks-digest-builder-wpf.txt"), wpfAll.ToString());
        File.WriteAllText(Path.Combine(folder!, "blocks-digest-builder-free.txt"), freeAll.ToString());
        File.WriteAllText(Path.Combine(folder!, $"portable-blocks-{DateTime.Now:yyyyMMdd-HHmmss}.md"), report.ToString());
        Console.WriteLine(report);
    });

    /// <summary>
    /// One builder laying every block of its kind over and over for twenty seconds, alone in its process — <c>NEXAFLOW_PORTABLE_PROFILE</c>
    /// names which, <c>W</c> or <c>P</c>, and <c>NEXAFLOW_PORTABLE_KIND</c> the kind of block, by its WPF builder's name.
    /// </summary>
    [TestMethod]
    public void OneBlockBuilderForAProfilerToWatch() => UiThread.Run(() =>
    {
        var which = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_PROFILE");
        var kind = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_KIND");
        if (which is not ("W" or "P") || string.IsNullOrWhiteSpace(kind))
        {
            Assert.Inconclusive("set NEXAFLOW_PORTABLE_PROFILE to W or P, and NEXAFLOW_PORTABLE_KIND to a builder's name");
            return;
        }

        var builders = new Builders(StyleFormat.Dark with { Standing = ContentStanding.Block });
        var subjects = Subjects().Where(subject => subject.Builder.Name == kind).ToList();
        if (subjects.Count == 0) { Assert.Inconclusive($"no block is laid by {kind}"); return; }

        var clock = Stopwatch.StartNew();
        var passes = 0;

        while (clock.Elapsed.TotalSeconds < 20)
        {
            foreach (var subject in subjects)
                if (which == "W") builders.Wpf(subject);
                else builders.Portable(subject);

            passes++;
        }

        var said = $"{which} {kind}: {passes} passes over {subjects.Count} blocks in {clock.Elapsed.TotalSeconds:F1}s, {clock.Elapsed.TotalMilliseconds / passes:F2} ms a pass";
        Console.WriteLine(said);

        if (Environment.GetEnvironmentVariable(Folder) is { Length: > 0 } folder)
            File.AppendAllLines(Path.Combine(folder, "portable-blocks-throughput.txt"), [said]);
    });

    private static string Named(string source) =>
        (source.Length > 50 ? source[..50] : source).ReplaceLineEndings(" ");

    private sealed record Subject(Type Builder, string Source, ContentReading Reading);

    /// <summary>The kinds of block measured here, by the WPF builder that lays each.</summary>
    private static readonly HashSet<Type> Measured =
        [typeof(CodeBuilder), typeof(MatrixBuilder), typeof(BarcodeBuilder), typeof(WordCloudBuilder), typeof(SmilesBuilder)];

    /// <summary>
    /// Every block written in the sample documents and the help's own page that one of the measured builders lays, each once, read as a
    /// document reads it while it is being written — and the unreadable ones, read as nothing could read them.
    /// </summary>
    private static List<Subject> Subjects()
    {
        var texts = TestSampleData.Files("markdown").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(File.ReadAllText).ToList();
        texts.Add(File.ReadAllText(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md"))));

        var seen = new HashSet<(string, string)>();
        var subjects = new List<Subject>();
        var engine = new ContentEngine();

        foreach (var text in texts)
            foreach (var part in engine.Read(null, text, writing: true).Root.SelfAndDescendants())
            {
                if (ContentNested.Language(part) is not { } language || part.Part(Roles.Body) is not { } body) continue;
                if (ContentLanguages.For(language)?.Builder is not { } builder || !Measured.Contains(builder)) continue;

                var source = ContentNested.Own(body.Node);
                if (seen.Add((language.ToLowerInvariant(), source)))
                    subjects.Add(new Subject(builder, source, engine.Read(language, source, writing: true)));
            }

        foreach (var (source, why) in Unreadable)
            subjects.Add(new Subject(typeof(UnreadBuilder), source, ContentReading.Of(ContentNode.Shown(source, why), 0, source)));

        return subjects;
    }

    /// <summary>The two builders of each kind, each made the way it is made for a showing — the WPF one as the engine makes it, the other in the medium.</summary>
    private sealed class Builders(StyleFormat style)
    {
        private readonly WpfMedium _medium = new();
        private PortableStyle? _style;

        private static readonly PortableNesting NestingNothing = static (_, _, _) => null;

        public WpfInk Ink => _medium.Ink(style.Text);

        public Laid Wpf(Subject subject) =>
            ((ContentBuilder)Activator.CreateInstance(subject.Builder, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
                                                      [subject.Reading, EditState.For(subject.Source), style, false, Laying.NestingNothing], null)!)
            .Lay(Room);

        public PortableLaid Portable(Subject subject)
        {
            _style ??= PortableProseBench.Styled(style, _medium);

            var (reading, state) = (subject.Reading, EditState.For(subject.Source));

            PortableContentBuilder builder = subject.Builder.Name switch
            {
                nameof(CodeBuilder) => new PortableCodeBuilder(reading, state, _style, false, _medium, NestingNothing),
                nameof(MatrixBuilder) => new PortableMatrixBuilder(reading, state, _style, false, _medium, NestingNothing),
                nameof(BarcodeBuilder) => new PortableBarcodeBuilder(reading, state, _style, false, _medium, NestingNothing),
                nameof(WordCloudBuilder) => new PortableWordCloudBuilder(reading, state, _style, false, _medium, NestingNothing, Pictured),
                nameof(SmilesBuilder) => new PortableSmilesBuilder(reading, state, _style, false, _medium, NestingNothing),
                nameof(UnreadBuilder) => new PortableUnreadBuilder(reading, state, _style, false, _medium, NestingNothing),
                _ => throw new InvalidOperationException($"no renderer-free builder for {subject.Builder.Name}"),
            };

            return builder.Lay(Room);
        }

        /// <summary>The picture a cloud's <c>mask:</c> was found to name, as the medium holds it.</summary>
        private LayoutPicture? Pictured(ContentPart part) =>
            part.Node.HeldAs(WithPictures.Picture) is ImageSource picture ? _medium.Picture(picture) : null;
    }

    private static long Differing(byte[] a, byte[] b)
    {
        long differing = 0;
        for (var at = 0; at + 3 < Math.Min(a.Length, b.Length); at += 4)
            if (a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2] || a[at + 3] != b[at + 3]) differing++;

        return differing + (Math.Abs(a.Length - b.Length) / 4);
    }
}
