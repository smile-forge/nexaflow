using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Tests.Visuals.Editing.Portable;

/// <summary>
/// The markdown prose builder against its port onto the tree free of WPF — the same reading, the same nested content, laid both
/// ways and compared piece for piece and pixel for pixel, then timed.
///
/// <para>
/// What a document holds in another language — its formulas, its diagrams — is laid once, before anything is timed, and handed to
/// both builders as it was laid: to the WPF one as the engine laid it, to the port as that same layout made again in the medium.
/// So what is timed is the prose builder's own work, both ways, and nothing either way pays for laying a diagram.
/// </para>
/// <para>
/// Opt in with <c>NEXAFLOW_PORTABLE_BENCH</c> pointing at a folder; each run writes one <c>portable-prose-*.md</c> there.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Bench")]
[DoNotParallelize]
[NoCoverage("a measurement to read, not a check")]
public class PortableProseBench
{
    private const string Folder = "NEXAFLOW_PORTABLE_BENCH";

    private const int Runs = 15;

    private const double Room = 900;

    private const double Compared = 2400;

    /// <summary>The documents timed; every other is only compared.</summary>
    private static readonly HashSet<string> Timed = new(StringComparer.OrdinalIgnoreCase) { "mixed-content.md", "MarkdownSupport.md" };

    [TestMethod]
    public void TheProseBuilderFreeOfWpfAgainstTheOneThereIs() => UiThread.Run(() =>
    {
        var folder = Environment.GetEnvironmentVariable(Folder);
        if (string.IsNullOrWhiteSpace(folder)) { Assert.Inconclusive($"set {Folder} to a folder to write the run to"); return; }

        var docs = TestSampleData.Files("markdown").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                                 .Select(path => (Name: Path.GetFileName(path), Text: File.ReadAllText(path))).ToList();
        docs.Add(("MarkdownSupport.md", File.ReadAllText(Path.GetFullPath(Path.Combine(TestSampleData.Root, "..", "docs", "MarkdownSupport.md")))));

        var style = StyleFormat.Dark;
        var medium = new WpfMedium();
        var portable = Styled(style, medium);
        var ink = medium.Ink(style.Text);

        var report = new StringBuilder();
#if DEBUG
        report.AppendLine("**Debug build — not a measurement worth keeping.**").AppendLine();
#endif
        report.AppendLine($"# The prose builder free of WPF — {DateTime.Now:yyyy-MM-dd HH:mm}, {Environment.MachineName}").AppendLine();
        report.AppendLine($"Laid at {Room}, nested content laid once beforehand and handed to both. W = MarkdownBuilder as it is; "
                          + $"P = PortableMarkdownBuilder through the WPF medium. Timings: {Runs} runs each way, alternating, medians, ms.").AppendLine();
        report.AppendLine("## The same layout?").AppendLine();
        report.AppendLine("Pieces of the document's own are every piece outside what another language laid; what another language laid is "
                          + "made again for P from W's layout (as the replay pilot does), so differences inside it are that copy's, not the port's.").AppendLine();
        report.AppendLine("| document | pieces W | pieces P | pieces differing | of them the document's own | pixels differing |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");

        var timings = new List<string>();

        foreach (var (name, text) in docs)
        {
            var subject = new Subject(text, style, portable, medium);
            var (w, p) = (subject.Wpf(), subject.Portable());

            var size = new Size(Room, Math.Min(Compared, Math.Max(1, w.Size.Height)));
            var differing = Differing(Pixels(dc => LayoutPainter.Paint(dc, w.Root, style.Text), size),
                                      Pixels(dc => PortablePainter.Paint(dc, p.Root, ink), size));

            report.AppendLine($"| {name} | {w.Tree.Count} | {p.Tree.Count} | {Pieces(w.Tree, p.Tree, own: false)} | {Pieces(w.Tree, p.Tree, own: true)} | {differing} |");

            if (Timed.Contains(name))
            {
                timings.Add(Time(name, subject, style, ink));
                timings.Add(Time(name + " (nothing nested)", new Subject(text, style, portable, medium, nests: false), style, ink));
            }
        }

        report.AppendLine().AppendLine("## What laying it costs").AppendLine();
        report.AppendLine("| document | pieces | pieces differing | pixels differing | build W | build P | Δ | KB W | KB P | first paint W | first paint P | repaint W | repaint P |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var row in timings) report.AppendLine(row);

        Directory.CreateDirectory(folder!);
        var file = Path.Combine(folder!, $"portable-prose-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(file, report.ToString());

        Console.WriteLine(file);
        Console.WriteLine(report);
    });

    /// <summary>
    /// One builder, over and over on one document, for a profiler to watch: <c>NEXAFLOW_PORTABLE_PROFILE</c> names which — <c>W</c> or
    /// <c>P</c> — and <c>NEXAFLOW_PORTABLE_DOCUMENT</c> which sample, mixed-content.md where it names none.
    /// </summary>
    [TestMethod]
    public void OneBuilderForAProfilerToWatch() => UiThread.Run(() =>
    {
        var which = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_PROFILE");
        if (which is not ("W" or "P")) { Assert.Inconclusive("set NEXAFLOW_PORTABLE_PROFILE to W or P"); return; }

        var name = Environment.GetEnvironmentVariable("NEXAFLOW_PORTABLE_DOCUMENT") is { Length: > 0 } named ? named : "mixed-content.md";
        var text = File.ReadAllText(TestSampleData.Files("markdown").First(path => Path.GetFileName(path) == name));

        var style = StyleFormat.Dark;
        var medium = new WpfMedium();
        var subject = new Subject(text, style, Styled(style, medium), medium);

        var clock = Stopwatch.StartNew();
        var laid = 0;

        while (clock.Elapsed.TotalSeconds < 20)
        {
            if (which == "W") subject.Wpf();
            else subject.Portable();

            laid++;
        }

        var said = $"{which}: {laid} layouts of {name} in {clock.Elapsed.TotalSeconds:F1}s, {clock.Elapsed.TotalMilliseconds / laid:F2} ms each";
        Console.WriteLine(said);

        if (Environment.GetEnvironmentVariable(Folder) is { Length: > 0 } folder)
            File.AppendAllLines(Path.Combine(folder, "portable-prose-throughput.txt"), [said]);
    });

    /// <summary>One document read once, with its nested content laid once and kept both ways.</summary>
    private sealed class Subject
    {
        private readonly ContentReading _reading;
        private readonly EditState _state;
        private readonly StyleFormat _style;
        private readonly PortableStyle _portable;
        private readonly WpfMedium _medium;
        private readonly Dictionary<(ContentNode Node, int Start, double Room, ContentStanding Standing), ContentInset?> _laid = [];
        private readonly Dictionary<(ContentNode Node, int Start, double Room, PortableStanding Standing), PortableInset?> _made = [];
        private readonly Dictionary<LinkLook, PortableLinkLook> _looks = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<ImageSource, LayoutPicture> _pictures = new(ReferenceEqualityComparer.Instance);
        private readonly Nesting _nesting;

        /// <param name="nests">
        /// Whether what is written in another language is laid at all — where not, both builders are told nothing read it, and show it
        /// as written: the document's own work alone.
        /// </param>
        public Subject(string text, StyleFormat style, PortableStyle portable, WpfMedium medium, bool nests = true)
        {
            var engine = new ContentEngine();

            _reading = engine.Read(null, text, writing: true);
            _state = EditState.For(text);
            _style = style;
            _portable = portable;
            _medium = medium;

            // Laid by the engine the first time a builder asks, and handed back as it was laid from then on.
            _nesting = new Nesting((holder, room, asked, shown, writing) =>
            {
                var key = (holder.Node, holder.Start, room, asked.Standing);
                if (_laid.TryGetValue(key, out var laid)) return laid;

                return _laid[key] = nests ? engine.Nested(holder, room, asked, shown, writing) : null;
            });

            Wpf();

            foreach (var ((node, start, room, standing), inset) in _laid)
                _made[(node, start, room, (PortableStanding)(int)standing)] = inset is null ? null : Made(inset, medium);
        }

        /// <summary>The WPF builder's layout — laid afresh, nothing kept from a layout before.</summary>
        public Laid Wpf()
        {
            var laid = new MarkdownBuilder(_reading, _state, _style, isReadOnly: false, _nesting).Lay(Room);
            Forget();
            return laid;
        }

        public PortableLaid Portable() =>
            new PortableMarkdownBuilder(_reading, _state, _portable, isReadOnly: false,
                                        (holder, room, standing) => _made.TryGetValue((holder.Node, holder.Start, room, standing), out var made)
                                            ? made
                                            : throw new InvalidOperationException($"nothing was laid for {holder.Kind} at {holder.Start}"),
                                        _medium, Linked, Pictured).Lay(Room);

        /// <summary>What a reading keeps of the blocks a builder laid, let go — so the next layout of it is laid, not set down again.</summary>
        private void Forget()
        {
            if (LaidBlocks.Of(_reading.Root) is not { } laid) return;

            for (var at = 0; at < _reading.Root.Children.Count; at++)
                if (laid.For(at) is { } block) block.Last = null;
        }

        private PortableLinkLook? Linked(ContentPart part)
        {
            if (WithLinks.Of(part) is not { } look) return null;
            if (_looks.TryGetValue(look, out var made)) return made;

            return _looks[look] = new PortableLinkLook(look.Ink is { } inked ? _medium.Ink(inked) : null, look.Underline, look.Before,
                                                       look.After, look.Says) { MarkFont = look.MarkFont?.Source };
        }

        private LayoutPicture? Pictured(ContentPart part)
        {
            if (WithImages.Of(part) is not { } picture) return null;
            if (_pictures.TryGetValue(picture, out var made)) return made;

            return _pictures[picture] = _medium.Picture(picture);
        }

        private static PortableInset Made(ContentInset inset, WpfMedium medium) =>
            new(new PortableLaid(LaidReplay.Of(inset.Laid.Tree, medium).Portable(), inset.Width, inset.Height,
                                 [.. inset.Laid.Trouble.Select(said => new PortableDiagnostic(said.Start, said.Length, said.Message) { Part = said.Part })]));
    }

    /// <summary>The style in the medium's handles — the same brushes and the same typefaces, so what is measured and drawn is the same.</summary>
    internal static PortableStyle Styled(StyleFormat style, WpfMedium medium)
    {
        var families = new Dictionary<string, FontFamily>
        {
            [style.TextFont.Source] = style.TextFont,
            [style.MonoFont.Source] = style.MonoFont,
        };

        LayoutFace Faced(string family, bool bold, bool italic)
        {
            if (!families.TryGetValue(family, out var named)) families[family] = named = new FontFamily(family);

            return medium.Face(style.Face(named, bold ? FontWeights.Bold : FontWeights.Normal, italic ? FontStyles.Italic : FontStyles.Normal));
        }

        return new PortableStyle(Faced)
        {
            Text = medium.Ink(style.Text),
            TextMuted = medium.Ink(style.TextMuted),
            Accent = medium.Ink(style.Accent),
            Heading = medium.Ink(style.Heading),
            DefTerm = medium.Ink(style.DefTerm),
            Citation = medium.Ink(style.Citation),
            Marked = medium.Ink(style.Marked),
            Success = medium.Ink(style.Success),
            Warning = medium.Ink(style.Warning),
            Danger = medium.Ink(style.Danger),
            Important = medium.Ink(style.Important),
            CodeBg = medium.Ink(style.CodeBg),
            CodeBorder = medium.Ink(style.CodeBorder),
            QuoteBg = medium.Ink(style.QuoteBg),
            Hr = medium.Ink(style.Hr),
            TableBorder = medium.Ink(style.TableBorder),
            TableHeaderBg = medium.Ink(style.TableHeaderBg),
            TableAltRowBg = medium.Ink(style.TableAltRowBg),
            FigureBg = medium.Ink(style.FigureBg),
            FigureBorder = medium.Ink(style.FigureBorder),
            FooterBg = medium.Ink(style.FooterBg),
            TextFont = style.TextFont.Source,
            MonoFont = style.MonoFont.Source,
            TextSize = style.TextSize,
            Expansion = style.Expansion?.Expansion,
            Says = Str.Get,
        };
    }

    // ── Comparing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// How many pieces differ in kind, in where they stand on the page, or in being there at all — of every piece, or only of the
    /// document's own: what is set down for another language's layout is a piece holding nothing of its own, and its subtree that
    /// language's.
    /// </summary>
    private static int Pieces(LayoutTree wpf, PortableTree portable, bool own)
    {
        var differing = Math.Abs(wpf.Count - portable.Count);

        for (var at = 0; at < Math.Min(wpf.Count, portable.Count); at++)
        {
            if (own && wpf.PartOf(at) is null && wpf.KindOf(at) == MarkdownPieces.Block && wpf.Piece(at).Extent > 1)
            {
                at += wpf.Piece(at).Extent - 1;
                continue;
            }
            var (w, p) = (wpf.WhereIs(at), portable.WhereIs(at));
            var same = wpf.KindOf(at) == portable.KindOf(at)
                       && w.IsEmpty == p.IsEmpty
                       && (w.IsEmpty || (Near(w.X, p.X) && Near(w.Y, p.Y) && Near(w.Width, p.Width) && Near(w.Height, p.Height)));

            if (!same) differing++;
        }

        return differing;

        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
    }

    private static byte[] Pixels(Action<DrawingContext> paint, Size size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(size));
            paint(dc);
        }

        var (width, height) = ((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
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

    // ── Timing ─────────────────────────────────────────────────────────────────

    private static string Time(string name, Subject subject, StyleFormat style, WpfInk ink)
    {
        var build = Alternating(() => subject.Wpf(), () => subject.Portable());
        var kb = (Allocated(() => subject.Wpf()), Allocated(() => subject.Portable()));

        var paint = Alternating(subject.Wpf, subject.Portable,
                                laid => Painted(dc => LayoutPainter.Paint(dc, laid.Root, style.Text)),
                                laid => Painted(dc => PortablePainter.Paint(dc, laid.Root, ink)));

        var (w, p) = (subject.Wpf(), subject.Portable());
        var size = new Size(Room, Math.Min(Compared, Math.Max(1, w.Size.Height)));
        var pixels = Differing(Pixels(dc => LayoutPainter.Paint(dc, w.Root, style.Text), size), Pixels(dc => PortablePainter.Paint(dc, p.Root, ink), size));
        var pieces = Pieces(w.Tree, p.Tree, own: false);

        Painted(dc => LayoutPainter.Paint(dc, w.Root, style.Text));
        Painted(dc => PortablePainter.Paint(dc, p.Root, ink));
        var repaint = Alternating(() => Painted(dc => LayoutPainter.Paint(dc, w.Root, style.Text)),
                                  () => Painted(dc => PortablePainter.Paint(dc, p.Root, ink)));

        var delta = $"{build.P - build.W:+0.00;-0.00} ({(build.P - build.W) / build.W:+0%;-0%})";

        return $"| {name} | {w.Tree.Count} | {pieces} | {pixels} | {build.W:F2} | {build.P:F2} | {delta} | {kb.Item1:F0} | {kb.Item2:F0} "
               + $"| {paint.W:F2} | {paint.P:F2} | {repaint.W:F3} | {repaint.P:F3} |";
    }

    private static void Warm(Action both)
    {
        var clock = Stopwatch.StartNew();
        for (var warm = 0; warm < 30 && (warm < 2 || clock.ElapsedMilliseconds < 300); warm++) both();
    }

    /// <summary>Two ways of doing one thing, timed by turns, which goes first swapped every run.</summary>
    private static (double W, double P) Alternating<TW, TP>(Func<TW> wpf, Func<TP> portable)
    {
        Warm(() => { wpf(); portable(); });

        var w = new double[Runs];
        var p = new double[Runs];

        for (var run = 0; run < Runs; run++)
        {
            if (run % 2 == 0) { w[run] = Clocked(wpf); p[run] = Clocked(portable); }
            else { p[run] = Clocked(portable); w[run] = Clocked(wpf); }
        }

        return (Middle(w), Middle(p));
    }

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

    private static double Clocked<T>(Func<T> run)
    {
        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
        var clock = Stopwatch.StartNew();
        run();
        return clock.Elapsed.TotalMilliseconds;
    }

    private static double Middle(double[] times)
    {
        Array.Sort(times);
        return times[times.Length / 2];
    }

    private static double Allocated<T>(Func<T> run)
    {
        run();
        GC.Collect();
        GC.WaitForPendingFinalizers();

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
}
