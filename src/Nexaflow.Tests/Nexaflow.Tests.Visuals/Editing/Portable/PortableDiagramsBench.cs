using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;

using Nexaflow.Markdown.Layout.Mermaid.Architecture;
using Nexaflow.Markdown.Layout.Mermaid.Block;
using Nexaflow.Markdown.Layout.Mermaid.C4;
using Nexaflow.Markdown.Layout.Mermaid.Class;
using Nexaflow.Markdown.Layout.Mermaid.Cynefin;
using Nexaflow.Markdown.Layout.Mermaid.Er;
using Nexaflow.Markdown.Layout.Mermaid.Flowchart;
using Nexaflow.Markdown.Layout.Mermaid.Gantt;
using Nexaflow.Markdown.Layout.Mermaid.Git;
using Nexaflow.Markdown.Layout.Mermaid.Ishikawa;
using Nexaflow.Markdown.Layout.Mermaid.Journey;
using Nexaflow.Markdown.Layout.Mermaid.Kanban;
using Nexaflow.Markdown.Layout.Mermaid.Mindmap;
using Nexaflow.Markdown.Layout.Mermaid.Pie;
using Nexaflow.Markdown.Layout.Mermaid.Quadrant;
using Nexaflow.Markdown.Layout.Mermaid.Radar;
using Nexaflow.Markdown.Layout.Mermaid.Requirement;
using Nexaflow.Markdown.Layout.Mermaid.Sankey;
using Nexaflow.Markdown.Layout.Mermaid.Sequence;
using Nexaflow.Markdown.Layout.Mermaid.State;
using Nexaflow.Markdown.Layout.Mermaid.Swimlane;
using Nexaflow.Markdown.Layout.Mermaid.Timeline;
using Nexaflow.Markdown.Layout.Mermaid.Venn;
using Nexaflow.Markdown.Layout.Mermaid.Xy;
using Nexaflow.Markdown.Layout.Nomnoml;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown;
using Nexaflow.Tests.Visuals.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Block;
using Nexaflow.Visuals.Text.Markdown.Mermaid.C4;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Class;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Cynefin;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Er;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Git;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Ishikawa;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Journey;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Kanban;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Mindmap;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Quadrant;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Requirement;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sankey;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;
using Nexaflow.Visuals.Text.Markdown.Mermaid.State;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Swimlane;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Timeline;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;
using Nexaflow.Visuals.Text.Markdown.Nomnoml;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// Every diagram the sample documents, the help's own page and the diagrams' own builder tests write, laid by its WPF builder and by its
/// renderer-free port side by side — wide, narrow, and only looked at — written down exactly, painted, and timed, a diagram at a time.
///
/// <para>
/// A diagram with no port yet is counted and left out, so the run says how far the port has come as well as whether what is ported
/// draws the same. Opt in with <c>NEXAFLOW_PORTABLE_BENCH</c>, naming a folder to write the run to.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class PortableDiagramsBench
{
    private const string Folder = "NEXAFLOW_PORTABLE_BENCH";

    private const int Runs = 5;

    private const double Compared = 2400;

    /// <summary>The ways each block is laid: wide and written in, narrow enough to make it fit, and wide but only looked at.</summary>
    private static readonly (double Room, bool ReadOnly, string Name)[] Ways = [(900, false, "wide"), (300, false, "narrow"), (900, true, "read")];

    [TestMethod]
    public void TheDiagramsFreeOfWpfAgainstTheOnesThereAre() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var style = StyleFormat.Dark with { Standing = ContentStanding.Block };
        var builders = new Builders(style);
        var subjects = Subjects();

        var report = new StringBuilder()
            .AppendLine($"# The diagrams free of WPF — {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.MachineName}").AppendLine()
            .AppendLine("| diagram | blocks | same pieces | pixels differing | W median ms | P median ms | Δ |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|");

        var differences = new StringBuilder();
        var waiting = new List<string>();
        var (wpfAll, freeAll) = (new StringBuilder(), new StringBuilder());

        foreach (var kind in subjects.GroupBy(subject => subject.Builder).OrderBy(kind => kind.Key.Name))
        {
            if (!Builders.Ported(kind.Key)) { waiting.Add($"{kind.Key.Name} ({kind.Count()})"); continue; }

            var group = kind.ToList();
            var (same, differing, laid) = (0, 0L, 0);

            foreach (var subject in group)
                foreach (var (room, readOnly, way) in Ways)
                {
                    var (w, p) = (builders.Wpf(subject, room, readOnly), builders.Portable(subject, room, readOnly));
                    var (wpf, free) = (new StringBuilder(), new StringBuilder());

                    PortableTexBench.Digest(w, wpf, advances: false);
                    PortableTexBench.Digest(p, free);
                    laid++;

                    var name = $"{kind.Key.Name} {way} {Named(subject.Source)}";
                    wpfAll.Append("== ").AppendLine(name).Append(wpf);
                    freeAll.Append("== ").AppendLine(name).Append(free);

                    if (wpf.ToString() == free.ToString()) same++;
                    else differences.Append("- pieces differ: ").Append(name).Append(" — ").AppendLine(FirstDifference(wpf.ToString(), free.ToString()));

                    var size = new Size(Math.Max(1, Math.Ceiling(w.Size.Width)), Math.Max(1, Math.Min(Compared, Math.Ceiling(w.Size.Height))));
                    var pixels = Differing(PortableTexBench.Pixels(dc => LayoutPainter.Paint(dc, w.Root, style.Text), size),
                                           PortableTexBench.Pixels(dc => PortablePainter.Paint(dc, p.Root, builders.Ink), size));
                    if (pixels > 0) differences.AppendLine($"- {pixels} pixels differ: {name}");
                    differing += pixels;
                }

            for (var warm = 0; warm < 2; warm++) foreach (var s in group) { builders.Wpf(s, 900, false); builders.Portable(s, 900, false); }

            var (wt, pt) = (new double[Runs], new double[Runs]);
            for (var run = 0; run < Runs; run++)
            {
                double W() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in group) builders.Wpf(s, 900, false); return clock.Elapsed.TotalMilliseconds; }
                double P() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in group) builders.Portable(s, 900, false); return clock.Elapsed.TotalMilliseconds; }

                if (run % 2 == 0) { wt[run] = W(); pt[run] = P(); }
                else { pt[run] = P(); wt[run] = W(); }
            }

            Array.Sort(wt);
            Array.Sort(pt);

            var (wm, pm) = (wt[Runs / 2], pt[Runs / 2]);
            report.AppendLine($"| {kind.Key.Name} | {laid} | {same} | {differing} | {wm:F2} | {pm:F2} | {(pm - wm) / wm:+0%;-0%} |");
        }

        report.AppendLine().Append(differences.Length == 0 ? "Every block laid the same pieces and painted the same pixels.\n" : differences.ToString());
        if (waiting.Count > 0) report.AppendLine().AppendLine("Not ported yet: " + string.Join(", ", waiting));

        Directory.CreateDirectory(folder!);
        File.WriteAllText(Path.Combine(folder!, "diagrams-digest-builder-wpf.txt"), wpfAll.ToString());
        File.WriteAllText(Path.Combine(folder!, "diagrams-digest-builder-free.txt"), freeAll.ToString());
        File.WriteAllText(Path.Combine(folder!, $"portable-diagrams-{DateTime.Now:yyyyMMdd-HHmmss}.md"), report.ToString());
        Console.WriteLine(report);
    });

    /// <summary>
    /// One builder laying every block of one diagram over and over for twenty seconds, alone in its process — <c>NEXAFLOW_PORTABLE_PROFILE</c>
    /// names which, <c>W</c> or <c>P</c>, and <c>NEXAFLOW_PORTABLE_KIND</c> the diagram, by its WPF builder's name.
    /// </summary>
    [TestMethod]
    public void OneDiagramBuilderForAProfilerToWatch() => UiThread.Run(() =>
    {
        var which = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_PROFILE");
        var kind = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_KIND");
        if (which is not ("W" or "P") || string.IsNullOrWhiteSpace(kind))
        {
            Assert.Inconclusive("set NEXAFLOW_PORTABLE_PROFILE to W or P, and NEXAFLOW_PORTABLE_KIND to a diagram builder's name");
            return;
        }

        var builders = new Builders(StyleFormat.Dark with { Standing = ContentStanding.Block });
        var subjects = Subjects().Where(subject => subject.Builder.Name == kind).ToList();
        if (subjects.Count == 0) { Assert.Inconclusive($"no block is laid by {kind}"); return; }

        // Laid once by the WPF builder whichever is watched, so the renderer-free one has what the WPF one was handed to replay.
        foreach (var subject in subjects) builders.Wpf(subject, 900, false);

        var clock = Stopwatch.StartNew();
        var passes = 0;

        while (clock.Elapsed.TotalSeconds < 20)
        {
            foreach (var subject in subjects)
                if (which == "W") builders.Wpf(subject, 900, false);
                else builders.Portable(subject, 900, false);

            passes++;
        }

        var said = $"{which} {kind}: {passes} passes over {subjects.Count} blocks in {clock.Elapsed.TotalSeconds:F1}s, {clock.Elapsed.TotalMilliseconds / passes:F2} ms a pass";
        Console.WriteLine(said);

        if (Environment.GetEnvironmentVariable(Folder) is { Length: > 0 } folder)
            File.AppendAllLines(Path.Combine(folder, "portable-diagrams-throughput.txt"), [said]);
    });

    private static string Named(string source) =>
        (source.Length > 50 ? source[..50] : source).ReplaceLineEndings(" ");

    /// <summary>The first line two digests differ on, as each writes it.</summary>
    private static string FirstDifference(string wpf, string free)
    {
        var (w, p) = (wpf.Split('\n'), free.Split('\n'));
        for (var at = 0; at < Math.Max(w.Length, p.Length); at++)
        {
            var (a, b) = (at < w.Length ? w[at].TrimEnd() : "(nothing)", at < p.Length ? p[at].TrimEnd() : "(nothing)");
            if (a != b) return $"line {at + 1}: W `{a}` P `{b}`";
        }

        return "same lines";
    }

    private sealed record Subject(Type Builder, string Source, ContentReading Reading);

    /// <summary>
    /// Every diagram written in the sample documents and the help's own page, and every block each diagram's builder tests draw, each once,
    /// read as a document reads it while it is being written.
    /// </summary>
    private static List<Subject> Subjects()
    {
        var texts = TestSampleData.Files("markdown").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(File.ReadAllText).ToList();
        texts.Add(File.ReadAllText(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md"))));

        var seen = new HashSet<(string, string)>();
        var subjects = new List<Subject>();
        var engine = new ContentEngine();

        // A block fenced as Mermaid's holds the diagram its header names, written in that diagram's own language.
        void Add(string language, string source)
        {
            if (ContentLanguages.For(language)?.Builder is not { } builder) return;
            if (!seen.Add((language.ToLowerInvariant(), source))) return;

            var reading = engine.Read(language, source, writing: true);
            if (typeof(MermaidBuilder).IsAssignableFrom(builder)) { subjects.Add(new Subject(builder, source, reading)); return; }


            foreach (var part in reading.Root.SelfAndDescendants())
                if (ContentNested.Language(part) is { } inner && !string.Equals(inner, language, StringComparison.OrdinalIgnoreCase))
                    Add(inner, ContentNested.Own((part.Part(Roles.Body) ?? part).Node));
        }

        foreach (var text in texts)
            foreach (var part in engine.Read(null, text, writing: true).Root.SelfAndDescendants())
                if (ContentNested.Language(part) is { } language && part.Part(Roles.Body) is { } body)
                    Add(language, ContentNested.Own(body.Node));

        var drawn = typeof(MermaidBuilderContract).GetProperty("Drawn", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var contract in typeof(MermaidBuilderContract).Assembly.GetTypes()
                     .Where(type => !type.IsAbstract && typeof(MermaidBuilderContract).IsAssignableFrom(type)).OrderBy(type => type.Name))
        {
            var tests = (MermaidBuilderContract)Activator.CreateInstance(contract)!;
            foreach (var (_, source) in (IEnumerable<(string What, string Source)>)drawn.GetValue(tests)!) Add(tests.Language, source);
        }

        return subjects;
    }

    /// <summary>The two builders of each diagram, each made the way it is made for a showing — the WPF one as the engine makes it, the other in the medium.</summary>
    private sealed class Builders(StyleFormat style)
    {
        private readonly WpfMedium _medium = new();
        private PortableStyle? _style;

        private readonly ContentEngine _engine = new();
        private readonly Dictionary<(ContentNode Node, int Start, double Room, ContentStanding Standing), ContentInset?> _laid = [];
        private readonly Dictionary<(ContentNode Node, int Start, double Room, PortableStanding Standing), PortableInset?> _made = [];
        private Nesting? _nesting;

        /// <summary>Another language written in a diagram, laid by the engine the first time a WPF builder asks, and kept as it was laid.</summary>
        private Nesting Nesting => _nesting ??= new Nesting((holder, room, asked, shown, writing) =>
        {
            var key = (holder.Node, holder.Start, room, asked.Standing);
            if (_laid.TryGetValue(key, out var laid)) return laid;

            return _laid[key] = _engine.Nested(holder, room, asked, shown, writing);
        });

        /// <summary>The same inset, as the WPF builder was handed it, replayed in the medium for the renderer-free one.</summary>
        private PortableInset? Replayed(ContentPart holder, double room, PortableStanding standing)
        {
            var key = (holder.Node, holder.Start, room, standing);
            if (_made.TryGetValue(key, out var made)) return made;

            if (!_laid.TryGetValue((holder.Node, holder.Start, room, (ContentStanding)(int)standing), out var laid))
                throw new InvalidOperationException($"nothing was laid for {holder.Kind} at {holder.Start}");

            return _made[key] = laid is null ? null : PortableProseBench.Made(laid, _medium);
        }

        private delegate PortableContentBuilder Made(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                                                     PortableNesting nesting, Func<ContentPart, LayoutPicture?> pictures);

        /// <summary>Each diagram's renderer-free builder, by its WPF builder's name.</summary>
        private static readonly Dictionary<string, Made> Ports = new()
        {
            [nameof(FlowchartBuilder)] = (reading, state, style, isReadOnly, medium, nesting, pictures) => new PortableFlowchartBuilder(reading, state, style, isReadOnly, medium, nesting, pictures),
                        [nameof(SwimlaneBuilder)] = (reading, state, style, isReadOnly, medium, nesting, pictures) => new PortableSwimlaneBuilder(reading, state, style, isReadOnly, medium, nesting, pictures),
                        [nameof(ClassBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableClassBuilder(reading, state, style, isReadOnly, medium, nesting),
                        [nameof(NomnomlBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableNomnomlBuilder(reading, state, style, isReadOnly, medium, nesting),
                        [nameof(SequenceBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableSequenceBuilder(reading, state, style, isReadOnly, medium, nesting),
                        [nameof(C4SequenceBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableC4SequenceBuilder(reading, state, style, isReadOnly, medium, nesting),

                        [nameof(PieBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortablePieBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(ArchitectureBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableArchitectureBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(BlockBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableBlockBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(C4Builder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableC4Builder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(CynefinBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableCynefinBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(ErBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableErBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(GanttBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableGanttBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(GitBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableGitBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(IshikawaBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableIshikawaBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(JourneyBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableJourneyBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(KanbanBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableKanbanBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(MindmapBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableMindmapBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(QuadrantBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableQuadrantBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(RadarBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableRadarBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(RequirementBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableRequirementBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(SankeyBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableSankeyBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(StateBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableStateBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(TimelineBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableTimelineBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(VennBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableVennBuilder(reading, state, style, isReadOnly, medium, nesting),
            [nameof(XyBuilder)] = (reading, state, style, isReadOnly, medium, nesting, _) => new PortableXyBuilder(reading, state, style, isReadOnly, medium, nesting),
        };

        public static bool Ported(Type builder) => Ports.ContainsKey(builder.Name);

        public WpfInk Ink => _medium.Ink(style.Text);

        public Laid Wpf(Subject subject, double room, bool readOnly) =>
            ((ContentBuilder)Activator.CreateInstance(subject.Builder, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
                                                      [subject.Reading, EditState.For(subject.Source), style, readOnly, Nesting], null)!)
            .Lay(room);

        public PortableLaid Portable(Subject subject, double room, bool readOnly)
        {
            _style ??= PortableProseBench.Styled(style, _medium);
            return Ports[subject.Builder.Name](subject.Reading, EditState.For(subject.Source), _style, readOnly, _medium, Replayed, Pictured).Lay(room);
        }

        /// <summary>The picture a node was found to name, as the medium holds it.</summary>
        private LayoutPicture? Pictured(ContentPart part) => WithDiagramPictures.Of(part) is { } picture ? _medium.Picture(picture) : null;
    }

    private static long Differing(byte[] a, byte[] b)
    {
        long differing = 0;
        for (var at = 0; at + 3 < Math.Min(a.Length, b.Length); at += 4)
            if (a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2] || a[at + 3] != b[at + 3]) differing++;

        return differing + (Math.Abs(a.Length - b.Length) / 4);
    }
}
