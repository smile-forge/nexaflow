using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Maths;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// Every formula the sample documents hold, laid as a document lays it — timed, and written down exactly, so the typesetter can
/// be moved and its layout held to what it was.
///
/// <para>
/// What is written down is the whole of each laid tree: every piece's kind, part, anchor, box and paint, and every mark's every
/// coordinate — every glyph's index, size and origin. Two runs that write the same file laid the same formulas the same way, to
/// the last glyph. Opt in with <c>NEXAFLOW_PORTABLE_BENCH</c>; <c>NEXAFLOW_BENCH_LABEL</c> names the run.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class PortableTexBench
{
    private const string Folder = "NEXAFLOW_PORTABLE_BENCH";

    private const int Runs = 9;

    private const double Room = 900;

    private const double Compared = 2400;

    /// <summary>The documents whose pages are photographed whole.</summary>
    private static readonly string[] Pages =
        ["latex-math-amssymb.md", "latex-math-fonts.md", "latex-math-structures.md", "latex-math-symbols.md"];

    [TestMethod]
    public void EveryFormulaAsItIsLaid() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var label = Environment.GetEnvironmentVariable("NEXAFLOW_BENCH_LABEL") is { Length: > 0 } named ? named : "run";
        var formulas = Formulas();
        var style = StyleFormat.Dark;

        // Written down exactly.
        var digest = new StringBuilder();
        var pieces = 0;
        foreach (var (source, standing) in formulas)
        {
            var laid = Lay(source, standing, style);
            pieces += laid.Tree.Count;
            digest.Append("== ").Append(standing).Append(' ').AppendLine(source.ReplaceLineEndings(" "));
            Digest(laid, digest);
        }

        Directory.CreateDirectory(folder!);
        File.WriteAllText(Path.Combine(folder!, $"tex-digest-{label}.txt"), digest.ToString());

        // Each page photographed.
        foreach (var page in Pages)
        {
            var text = File.ReadAllText(TestSampleData.Files("markdown").First(path => Path.GetFileName(path) == page));
            var laid = Laying.Lay(null, text, Room, style, writing: true);
            File.WriteAllBytes(Path.Combine(folder!, $"tex-page-{label}-{page}.bin"),
                               Pixels(dc => LayoutPainter.Paint(dc, laid.Root, style.Text), new Size(Room, Math.Min(Compared, laid.Size.Height))));
        }

        // Timed: the whole corpus of formulas laid once, the median of several.
        for (var warm = 0; warm < 3; warm++) LayAll(formulas, style);

        var times = new double[Runs];
        for (var run = 0; run < Runs; run++)
        {
            GC.Collect(1, GCCollectionMode.Forced, blocking: true);
            var clock = Stopwatch.StartNew();
            LayAll(formulas, style);
            times[run] = clock.Elapsed.TotalMilliseconds;
        }

        Array.Sort(times);
        var said = $"{DateTime.Now:yyyy-MM-dd HH:mm} {label}: {formulas.Count} formulas, {pieces} pieces — laid in {times[Runs / 2]:F1} ms "
                   + $"(fastest {times[0]:F1}, slowest {times[^1]:F1})";

        File.AppendAllLines(Path.Combine(folder!, "tex-timing.txt"), [said]);
        Console.WriteLine(said);
    });

    /// <summary>
    /// The WPF builder and the one free of WPF on the same readings of the same formulas: what each lays, compared piece for piece and
    /// pixel for pixel, and what laying them all costs each, timed by turns.
    /// </summary>
    [TestMethod]
    public void TheLatexBuilderFreeOfWpfAgainstTheOneThereIs() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var style = StyleFormat.Dark;
        var medium = new WpfMedium();
        var portable = PortableProseBench.Styled(style, medium);
        var fonts = PortableTexFonts.Of(medium, "Arial");
        var ink = medium.Ink(style.Text);

        var subjects = Formulas().Select(formula => (formula.Source, formula.Standing,
                                                     Reading: new ContentEngine().Read("latex", formula.Source, writing: true))).ToList();

        Laid Wpf((string Source, ContentStanding Standing, ContentReading Reading) subject) =>
            new LatexBuilder(subject.Reading, EditState.For(subject.Source), style with { Standing = subject.Standing }, isReadOnly: false,
                             Laying.NestingNothing).Lay(subject.Standing == ContentStanding.Inline ? double.PositiveInfinity : Room);

        PortableLaid Portable((string Source, ContentStanding Standing, ContentReading Reading) subject) =>
            new PortableLatexBuilder(subject.Reading, EditState.For(subject.Source), portable, (PortableStanding)(int)subject.Standing,
                                     isReadOnly: false, medium, fonts)
                .Lay(subject.Standing == ContentStanding.Inline ? double.PositiveInfinity : Room);

        // The same layout, both ways — and the WPF builder handed a reading directly laying what the engine laid.
        var (engine, wpf, free) = (new StringBuilder(), new StringBuilder(), new StringBuilder());
        long differing = 0;
        var formulasDiffering = new List<string>();

        foreach (var subject in subjects)
        {
            var (w, p) = (Wpf(subject), Portable(subject));

            engine.Append("== ").Append(subject.Standing).Append(' ').AppendLine(subject.Source.ReplaceLineEndings(" "));
            Digest(Lay(subject.Source, subject.Standing, style), engine);

            wpf.Append("== ").AppendLine(subject.Source.ReplaceLineEndings(" "));
            Digest(w, wpf, advances: false);
            free.Append("== ").AppendLine(subject.Source.ReplaceLineEndings(" "));
            Digest(p, free);

            var size = new Size(Math.Max(1, Math.Ceiling(w.Size.Width)), Math.Max(1, Math.Ceiling(w.Size.Height)));
            var pixels = Differing(Pixels(dc => LayoutPainter.Paint(dc, w.Root, style.Text), size), Pixels(dc => PortablePainter.Paint(dc, p.Root, ink), size));
            if (pixels > 0) formulasDiffering.Add($"{subject.Source} ({pixels})");
            differing += pixels;
        }

        Directory.CreateDirectory(folder!);
        File.WriteAllText(Path.Combine(folder!, "tex-digest-builder-wpf.txt"), wpf.ToString());
        File.WriteAllText(Path.Combine(folder!, "tex-digest-builder-free.txt"), free.ToString());

        var baseline = Path.Combine(folder!, "tex-digest-baseline.txt");
        var report = new StringBuilder()
            .AppendLine($"# The LaTeX builder free of WPF — {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.MachineName}").AppendLine()
            .AppendLine($"{subjects.Count} formulas.").AppendLine()
            .AppendLine($"- the WPF builder, handed the reading, lays what the engine lays: {Same(engine, subjects, style)}")
            .AppendLine($"- the engine lays what the baseline laid: {(File.Exists(baseline) ? (File.ReadAllText(baseline) == engine.ToString()).ToString() : "no baseline")}")
            .AppendLine($"- the two builders lay the same pieces: {wpf.ToString() == free.ToString()}")
            .AppendLine($"- pixels differing: {differing}, in {formulasDiffering.Count} formulas")
            .AppendLine();

        foreach (var which in formulasDiffering.Take(10)) report.AppendLine($"  - {which}");

        // Timed: every formula laid once each way, by turns.
        for (var warm = 0; warm < 3; warm++) { foreach (var s in subjects) { Wpf(s); Portable(s); } }

        var (wt, pt) = (new double[Runs * 2], new double[Runs * 2]);
        for (var run = 0; run < Runs * 2; run++)
        {
            double W() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in subjects) Wpf(s); return clock.Elapsed.TotalMilliseconds; }
            double P() { GC.Collect(1, GCCollectionMode.Forced, blocking: true); var clock = Stopwatch.StartNew(); foreach (var s in subjects) Portable(s); return clock.Elapsed.TotalMilliseconds; }

            if (run % 2 == 0) { wt[run] = W(); pt[run] = P(); }
            else { pt[run] = P(); wt[run] = W(); }
        }

        Array.Sort(wt);
        Array.Sort(pt);

        double Allocated(Action lay) { lay(); GC.Collect(); var before = GC.GetAllocatedBytesForCurrentThread(); lay(); return (GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0; }
        var kb = (Allocated(() => { foreach (var s in subjects) Wpf(s); }), Allocated(() => { foreach (var s in subjects) Portable(s); }));

        report.AppendLine().AppendLine("| | median ms | fastest ms | KB |").AppendLine("|---|---:|---:|---:|")
              .AppendLine($"| W | {wt[Runs]:F2} | {wt[0]:F2} | {kb.Item1:F0} |")
              .AppendLine($"| P | {pt[Runs]:F2} | {pt[0]:F2} | {kb.Item2:F0} |")
              .AppendLine($"| Δ | {pt[Runs] - wt[Runs]:+0.00;-0.00} ({(pt[Runs] - wt[Runs]) / wt[Runs]:+0%;-0%}) | {pt[0] - wt[0]:+0.00;-0.00} | {kb.Item2 - kb.Item1:+0;-0} |");

        var file = Path.Combine(folder!, $"portable-tex-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(file, report.ToString());
        Console.WriteLine(report);
    });

    /// <summary>
    /// One builder laying every formula over and over for twenty seconds, alone in its process — <c>NEXAFLOW_PORTABLE_PROFILE</c> names
    /// which, <c>W</c> or <c>P</c> — so nothing the other leaves behind is paid for, and a profiler has one thing to watch.
    /// </summary>
    [TestMethod]
    public void OneLatexBuilderForAProfilerToWatch() => UiThread.Run(() =>
    {
        var which = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_PROFILE");
        if (which is not ("W" or "P")) { Assert.Inconclusive("set NEXAFLOW_PORTABLE_PROFILE to W or P"); return; }

        var style = StyleFormat.Dark;
        var medium = new WpfMedium();
        var portable = PortableProseBench.Styled(style, medium);
        var fonts = PortableTexFonts.Of(medium, "Arial");

        var subjects = Formulas().Select(formula => (formula.Source, formula.Standing,
                                                     Reading: new ContentEngine().Read("latex", formula.Source, writing: true))).ToList();

        var clock = Stopwatch.StartNew();
        var passes = 0;

        while (clock.Elapsed.TotalSeconds < 20)
        {
            foreach (var (source, standing, reading) in subjects)
            {
                var room = standing == ContentStanding.Inline ? double.PositiveInfinity : Room;

                if (which == "W")
                    new LatexBuilder(reading, EditState.For(source), style with { Standing = standing }, isReadOnly: false, Laying.NestingNothing).Lay(room);
                else
                    new PortableLatexBuilder(reading, EditState.For(source), portable, (PortableStanding)(int)standing, isReadOnly: false, medium, fonts).Lay(room);
            }

            passes++;
        }

        var said = $"{which}: {passes} passes over {subjects.Count} formulas in {clock.Elapsed.TotalSeconds:F1}s, {clock.Elapsed.TotalMilliseconds / passes:F2} ms a pass";
        Console.WriteLine(said);

        if (Environment.GetEnvironmentVariable(Folder) is { Length: > 0 } folder)
            File.AppendAllLines(Path.Combine(folder, "portable-tex-throughput.txt"), [said]);
    });

    /// <summary>Whether the engine lays each formula as the WPF builder does when handed the reading directly.</summary>
    private static bool Same(StringBuilder engine, List<(string Source, ContentStanding Standing, ContentReading Reading)> subjects, StyleFormat style)
    {
        var direct = new StringBuilder();
        foreach (var subject in subjects)
        {
            direct.Append("== ").Append(subject.Standing).Append(' ').AppendLine(subject.Source.ReplaceLineEndings(" "));
            Digest(new LatexBuilder(subject.Reading, EditState.For(subject.Source), style with { Standing = subject.Standing }, isReadOnly: false,
                                    Laying.NestingNothing).Lay(subject.Standing == ContentStanding.Inline ? double.PositiveInfinity : Room), direct);
        }

        return direct.ToString() == engine.ToString();
    }

    private static long Differing(byte[] a, byte[] b)
    {
        long differing = 0;
        for (var at = 0; at + 3 < Math.Min(a.Length, b.Length); at += 4)
            if (a[at] != b[at] || a[at + 1] != b[at + 1] || a[at + 2] != b[at + 2] || a[at + 3] != b[at + 3]) differing++;

        return differing + (Math.Abs(a.Length - b.Length) / 4);
    }

    /// <summary>A tree free of WPF laid in the WPF medium, written down as <see cref="Digest(Laid, StringBuilder, bool)"/> writes one, advances left out.</summary>
    internal static void Digest(PortableLaid laid, StringBuilder into)
    {
        var tree = laid.Tree;
        into.Append("size ").Append(N(laid.Width)).Append(',').Append(N(laid.Height)).Append(" trouble ").Append(laid.Trouble.Count).AppendLine();

        for (var at = 0; at < tree.Count; at++)
        {
            ref readonly var piece = ref tree.Piece(at);
            var part = tree.PartOf(at);
            var box = piece.Box.IsEmpty ? Rect.Empty : new Rect(piece.Box.X, piece.Box.Y, piece.Box.Width, piece.Box.Height);

            into.Append(at).Append(' ').Append(tree.KindOf(at))
                .Append(" part ").Append(part is null ? "-" : $"{part.Start}+{part.Length}")
                .Append(" at ").Append(N(piece.Offset.X)).Append(',').Append(N(piece.Offset.Y))
                .Append(" box ").Append(N(box))
                .Append(" parent ").Append(piece.Parent).Append(" extent ").Append(piece.Extent).Append(" stops ").Append((Stops)(int)piece.Stops);

            if (tree.PaintOf(at) is { } paint)
            {
                into.Append(" turn ").Append(string.Join(";", (paint.Turn ?? []).Select(turn => ((WpfTurn)turn).Transform.Value.ToString(CultureInfo.InvariantCulture))));
                if (paint.Snap is WpfGuides { Set: var snap })
                    into.Append(" snap ").Append(string.Join(";", snap.GuidelinesX.Select(N))).Append('|').Append(string.Join(";", snap.GuidelinesY.Select(N)));
                into.Append(" kept ").Append(paint.Kept is not null).Append(" scale ").Append(N(paint.Scale));
            }

            if (tree.RegionOf(at) is WpfShape { Geometry: var region }) into.Append(" region ").Append(N(region.Bounds));

            into.AppendLine();

            foreach (var mark in tree.MarksOf(at))
            {
                into.Append("  ");
                switch (mark)
                {
                    case PortableGeometryMark { Shape: WpfShape { Geometry: var outline } } shape:
                        into.Append("outline ").Append(N(outline.Bounds)).Append(' ').Append(Ink(((WpfInk?)shape.Fill)?.Brush)).Append(' ')
                            .Append(Ink(((WpfInk?)shape.Stroke?.Ink)?.Brush)).Append(' ').Append(N(shape.Stroke?.Thickness ?? 0))
                            .Append(" dashes - path ").Append(PathHash(outline));
                        break;
                    case PortableTextMark text:
                        into.Append("text ").Append(text.Glyphs.Text).Append(" at ").Append(N(text.At.X)).Append(',').Append(N(text.At.Y))
                            .Append(' ').Append(N(text.Glyphs.Width)).Append(' ').Append(Ink(((WpfInk?)text.Ink)?.Brush));
                        break;
                    case PortableGlyphMark { Run: GlyphRun run } glyphs:
                        into.Append("glyphs ").Append(Path.GetFileName(run.GlyphTypeface.FontUri.ToString())).Append(' ').Append(N(run.FontRenderingEmSize))
                            .Append(" at ").Append(N(run.BaselineOrigin.X)).Append(',').Append(N(run.BaselineOrigin.Y))
                            .Append(" [").Append(string.Join(",", run.GlyphIndices)).Append("] ").Append(Ink(((WpfInk?)glyphs.Ink)?.Brush));
                        break;
                    case PortableRuleMark rule:
                        into.Append("rule ").Append(N(new Rect(rule.Bounds.X, rule.Bounds.Y, rule.Bounds.Width, rule.Bounds.Height))).Append(' ')
                            .Append(Ink(((WpfInk?)rule.Ink)?.Brush));
                        break;
                    case PortableLineMark line:
                        into.Append("line ").Append(N(line.From.X)).Append(',').Append(N(line.From.Y)).Append(' ').Append(N(line.To.X)).Append(',')
                            .Append(N(line.To.Y)).Append(' ').Append(Ink(((WpfInk?)line.Ink)?.Brush));
                        break;
                    case PortableWashMark wash:
                        into.Append("wash ").Append(N(new Rect(wash.Bounds.X, wash.Bounds.Y, wash.Bounds.Width, wash.Bounds.Height))).Append(' ')
                            .Append(Ink(((WpfInk)wash.Fill).Brush));
                        break;
                    default:
                        var covers = mark.Covers;
                        into.Append(mark.GetType().Name.Replace("Portable", "")).Append(' ')
                            .Append(N(covers.IsEmpty ? Rect.Empty : new Rect(covers.X, covers.Y, covers.Width, covers.Height)));
                        break;
                }

                into.AppendLine();
            }
        }
    }

    private static void LayAll(List<(string Source, ContentStanding Standing)> formulas, StyleFormat style)
    {
        foreach (var (source, standing) in formulas) Lay(source, standing, style);
    }

    /// <summary>A formula laid as a document lays one: in a line of words, or as a block among blocks at the page's room.</summary>
    internal static Laid Lay(string source, ContentStanding standing, StyleFormat style) =>
        Laying.Lay("latex", source, standing == ContentStanding.Inline ? double.PositiveInfinity : Room, style with { Standing = standing },
                   writing: true);

    /// <summary>
    /// Every formula written in the sample documents, the help's own page and the generated lecture — each once, as it stands
    /// where it was written: in a line of words, or as a block.
    /// </summary>
    internal static List<(string Source, ContentStanding Standing)> Formulas()
    {
        var texts = TestSampleData.Files("markdown").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(File.ReadAllText).ToList();
        texts.Add(File.ReadAllText(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md"))));
        texts.Add(MarkdownLayoutBench.LectureScript(MarkdownLayoutBench.LectureSections));

        var seen = new HashSet<(string, ContentStanding)>();
        var formulas = new List<(string, ContentStanding)>();

        foreach (var text in texts)
            foreach (var part in new ContentEngine().Read(null, text, writing: true).Root.SelfAndDescendants())
            {
                if (!string.Equals(ContentNested.Language(part), "latex", StringComparison.OrdinalIgnoreCase)) continue;
                if (part.Part(Roles.Body) is not { } body) continue;

                var standing = part.Kind == MarkdownKinds.Formula ? ContentStanding.Inline : ContentStanding.Block;
                var source = ContentNested.Own(body.Node);

                if (seen.Add((source, standing))) formulas.Add((source, standing));
            }

        return formulas;
    }

    // ── Written down ───────────────────────────────────────────────────────────

    private static string N(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    private static string N(Rect rect) => rect.IsEmpty ? "empty" : $"{N(rect.X)},{N(rect.Y)},{N(rect.Width)},{N(rect.Height)}";

    private static string Ink(Brush? brush) => brush is SolidColorBrush { Color: var colour } ? colour.ToString() : brush is null ? "-" : brush.GetType().Name;

    /// <summary>A laid tree, written down to the last coordinate of the last glyph.</summary>
    internal static void Digest(Laid laid, StringBuilder into, bool advances = true)
    {
        var tree = laid.Tree;
        into.Append("size ").Append(N(laid.Size.Width)).Append(',').Append(N(laid.Size.Height)).Append(" trouble ").Append(laid.Trouble.Count).AppendLine();

        for (var at = 0; at < tree.Count; at++)
        {
            ref readonly var piece = ref tree.Piece(at);
            var part = tree.PartOf(at);

            into.Append(at).Append(' ').Append(tree.KindOf(at))
                .Append(" part ").Append(part is null ? "-" : $"{part.Start}+{part.Length}")
                .Append(" at ").Append(N(piece.Offset.X)).Append(',').Append(N(piece.Offset.Y))
                .Append(" box ").Append(N(piece.Box))
                .Append(" parent ").Append(piece.Parent).Append(" extent ").Append(piece.Extent).Append(" stops ").Append(piece.Stops);

            if (tree.PaintOf(at) is { } paint)
            {
                into.Append(" turn ").Append(string.Join(";", (paint.Turn ?? []).Select(turn => turn.Value.ToString(CultureInfo.InvariantCulture))));
                if (paint.Snap is { } snap)
                    into.Append(" snap ").Append(string.Join(";", snap.GuidelinesX.Select(N))).Append('|').Append(string.Join(";", snap.GuidelinesY.Select(N)));
                into.Append(" kept ").Append(paint.Kept is not null).Append(" scale ").Append(N(paint.Scale));
            }

            if (tree.RegionOf(at) is { } region) into.Append(" region ").Append(N(region.Bounds));

            into.AppendLine();

            foreach (var mark in tree.MarksOf(at))
            {
                into.Append("  ");
                switch (mark)
                {
                    case GlyphMark glyphs:
                        var run = glyphs.Run;
                        into.Append("glyphs ").Append(Path.GetFileName(run.GlyphTypeface.FontUri.ToString())).Append(' ').Append(N(run.FontRenderingEmSize))
                            .Append(" at ").Append(N(run.BaselineOrigin.X)).Append(',').Append(N(run.BaselineOrigin.Y))
                            .Append(" [").Append(string.Join(",", run.GlyphIndices)).Append(']')
                            .Append(advances ? $" [{string.Join(",", run.AdvanceWidths.Select(N))}]" : "")
                            .Append(' ').Append(Ink(glyphs.Foreground));
                        break;
                    case TextMark text:
                        into.Append("text ").Append(text.Glyphs.Text).Append(" at ").Append(N(text.At.X)).Append(',').Append(N(text.At.Y))
                            .Append(' ').Append(N(text.Glyphs.Width)).Append(' ').Append(Ink(text.Foreground));
                        break;
                    case RuleMark rule:
                        into.Append("rule ").Append(N(rule.Bounds)).Append(' ').Append(Ink(rule.Foreground));
                        break;
                    case LineMark line:
                        into.Append("line ").Append(N(line.From.X)).Append(',').Append(N(line.From.Y)).Append(' ').Append(N(line.To.X)).Append(',')
                            .Append(N(line.To.Y)).Append(' ').Append(Ink(line.Foreground));
                        break;
                    case WashMark wash:
                        into.Append("wash ").Append(N(wash.Bounds)).Append(' ').Append(Ink(wash.Fill));
                        break;
                    case GeometryMark shape:
                        into.Append("outline ").Append(N(shape.Shape.Bounds)).Append(' ').Append(Ink(shape.Fill)).Append(' ').Append(Ink(shape.Stroke))
                            .Append(' ').Append(N(shape.Thickness)).Append(" dashes ").Append(shape.Dashes is null ? "-" : string.Join(";", shape.Dashes.Select(N)))
                            .Append(" path ").Append(PathHash(shape.Shape));
                        break;
                    default:
                        into.Append(mark.GetType().Name).Append(' ').Append(N(mark.Covers));
                        break;
                }

                into.AppendLine();
            }
        }
    }

    /// <summary>An outline's exact path, as a short stable hash: the same figures and points to the last digit give the same.</summary>
    internal static string PathHash(Geometry shape)
    {
        var path = PathGeometry.CreateFromGeometry(shape).ToString(CultureInfo.InvariantCulture);
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(path)))[..12];
    }

    internal static byte[] Pixels(Action<DrawingContext> paint, Size size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(size));
            paint(dc);
        }

        var (width, height) = ((int)Math.Ceiling(size.Width), (int)Math.Max(1, Math.Ceiling(size.Height)));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }
}
