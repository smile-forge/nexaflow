using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// Ten thousand real tunes, engraved, and held against the picture the corpus ships beside each one.
///
/// <para>
/// The only reference here that was never ours. Everything else this suite asks — does it read back, does
/// every piece name real source, does a stage leave the tune alone — is a question we set ourselves, and a
/// reading held against itself proves nothing about whether the page is right.
/// </para>
/// <para>
/// <strong>But a picture is <em>a</em> truth, not ground truth.</strong> Two engravers never agree on a
/// pixel — different fonts, different spacing curves, different ideas about where a line should break —
/// so the comparison has to be fuzzy, and a fuzzy comparison can only <em>rank</em>. The score sorts the
/// corpus worst-first onto a page somebody reads once. It is never asked for a verdict, and no threshold
/// here should be read as one.
/// </para>
/// <para>
/// <strong>Ours is drawn at their width, and line breaking is then part of what is measured.</strong> The
/// reference was drawn at a width nobody recorded — but the picture is evidence of it, so its ink is
/// measured and our page is given that many pixels plus a couple of air. That is the honest comparison and
/// it is also the useful one: a score has to be width-aware to sit in a window at all, so handing it a
/// width and asking what it does with it is a test of the thing rather than a way around it.
/// </para>
/// <para>
/// <strong>And at their size, which is measured rather than guessed.</strong> Every reference is fitted to
/// an 800×600 box — 2,397 of 2,400 sampled touch an edge exactly — so a thumbnail was scaled by whatever it
/// took to fit, and its pixels are not a page's pixels. The corpus is therefore drawn at as many sizes as
/// it has tunes: four taken at random want our notation at 0.56, 0.81, 0.62 and 0.46 of its natural size.
/// No single number can stand in for that, and a mean is the one answer guaranteed to fit none of them.
/// </para>
/// <para>
/// So each reference is asked how big its own staff is — <see cref="GrayImage.StaffSpace"/>, measured off
/// the empty stave at the right end of its systems — and ours is drawn at exactly that. The zoom is a real
/// render scale, so the tune is engraved into the room that notation leaves and breaks its lines where
/// theirs did, rather than being a shrunk picture of our own line breaks.
/// </para>
/// <para>
/// That leaves the comparison with nothing declared and nothing searched for: same width, same staff size,
/// and every difference left is an engraving difference. The shape number becomes something to read rather
/// than aim at — a score is not a picture and its aspect ratio is not a property anybody wants, but at a
/// shared width and size a page half the other's shape has twice its systems, which separates a low score
/// meaning <em>we drew it differently</em> from one meaning <em>we broke it differently</em>.
/// </para>
/// <para>
/// Opt-in and local. <c>NEXAFLOW_ABC_CORPUS</c> points at the corpus; everything written goes beside it
/// rather than into the repository. <c>NEXAFLOW_ABC_SWEEP</c> sets how many tunes to take
/// (<c>all</c> for the lot); a few hundred is enough to move a mean and quick enough to run while working.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("opt-in sweep over an external corpus of engraved tunes")]
public class AbcPictureSweepTests
{
    private const string DefaultCorpus = @"D:\Datasets\abcmusic\zenoob";

    /// <summary>How many tunes a run takes when nothing says otherwise.</summary>
    private const int DefaultSample = 400;

    /// <summary>
    /// The zoom to draw at when the reference will not say how big its own staff is — a handful of pages
    /// with no clear stave anywhere on them. Overridable with <c>NEXAFLOW_ABC_ZOOM</c>, which also pins
    /// every tune to one size, which is occasionally what you want to look at and never what you want to
    /// measure.
    /// </summary>
    private static readonly double? Pinned =
        double.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_ABC_ZOOM"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var asked) && asked > 0
            ? asked
            : null;

    /// <summary>What to draw at when nothing has been measured and nothing pinned.</summary>
    private const double Guessed = 0.65;

    /// <summary>
    /// The size to draw this tune at: whatever makes our staff the size of the staff in its own reference.
    /// </summary>
    private static double ZoomFor(GrayImage reference) =>
        Pinned ?? (reference.StaffSpace() is { } space and > 0 ? space / ScoreMetrics.S : Guessed);

    /// <summary>
    /// The air our page is given beyond the reference's ink. A couple of pixels: enough that the last note
    /// of a line is not pressed against the edge, few enough that it cannot buy an extra bar.
    /// </summary>
    private const int Margin = 4;

    /// <summary>
    /// A page taller than this is one nobody would print and one nothing should rasterise. It is a guard
    /// rather than a limit: a tune given a sane width should never reach it, and one that does is a bug
    /// that a memory profile would find long after the machine had stopped responding.
    /// </summary>
    private const double Absurd = 20000;

    /// <summary>
    /// The height both pictures are brought to before they are compared. Sixteen is right for a formula,
    /// which is one line; a page of five systems resampled that far is a grey smear with no systems in it.
    /// </summary>
    private static readonly int Detail =
        int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_ABC_DETAIL"), out var asked) && asked > 0
            ? asked
            : 96;

    /// <summary>
    /// What one tune came to: how well our page overlapped its own reference, how alike the two pages'
    /// shapes are, and — the number that says whether the first one means anything — how well it
    /// overlapped somebody <em>else's</em> reference.
    /// </summary>
    private sealed record Scored(string Name, double Overlap, double Shape, double Width, double Control,
                                 int Notes, double Stretch, double Zoom, string? Trouble);

    [TestMethod]
    public void HowCloseIsThisToWhatAnEngraverDrew()
    {
        if (Corpus() is not { } corpus) { Assert.Inconclusive(Missing); return; }

        var wanted = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_SWEEP");
        var take = string.Equals(wanted, "all", StringComparison.OrdinalIgnoreCase)
            ? corpus.Count
            : int.TryParse(wanted, out var count) ? count : DefaultSample;

        var tunes = corpus.Take(Math.Min(take, corpus.Count)).ToList();
        var scored = new ConcurrentBag<Scored>();
        var clock = Stopwatch.StartNew();

        // Each tune is also scored against the picture of a different one. Without that a mean is
        // uninterpretable: 0.58 is a good score if unrelated pages score 0.2 and no score at all if they
        // score 0.55, and nothing about the number itself says which.
        var shuffled = tunes.Skip(1).Append(tunes[0]).ToList();
        var pairs = tunes.Select((tune, at) => (Tune: tune, Other: shuffled[at].Image)).ToList();

        UiThread.Across(pairs, pair => scored.Add(Score(pair.Tune, pair.Other)));

        var all = scored.OrderBy(s => s.Overlap).ToList();
        var drawn = all.Where(s => s.Trouble is null).ToList();

        Report(corpus.Root, all, drawn, clock.Elapsed);

        Assert.IsTrue(drawn.Count > 0, "nothing was engraved at all");

        // The one thing a ranking may still assert: that it ranks. A page that matches its own picture no
        // better than it matches somebody else's is not being measured at all, and a run like that should
        // not read as a quiet pass.
        var separation = drawn.Average(s => s.Overlap) - drawn.Average(s => s.Control);
        Assert.IsTrue(separation > 0.05,
            $"mean overlap {drawn.Average(s => s.Overlap):F4} against its own picture and "
            + $"{drawn.Average(s => s.Control):F4} against a stranger's — the sweep is measuring nothing");
    }

    /// <summary>
    /// One tune drawn our way with the engraver's own picture underneath it, saved beside the corpus.
    ///
    /// <para>
    /// The sweep gives a number over ten thousand tunes and cannot say what is wrong with any of them. This
    /// is the other half of that loop: pick a tune, look at the two pages together at the size and width the
    /// sweep used, and see what the number was complaining about. Both are cropped to their ink and stacked
    /// on the same width, so a difference in where a note sits is a difference you can see.
    /// </para>
    /// <para>
    /// <c>NEXAFLOW_ABC_SHOW</c> names the tunes, comma separated; with none it takes one from each end of
    /// the corpus and one from the middle.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ShowOneAgainstTheEngraversOwnPicture()
    {
        if (Corpus() is not { } corpus) { Assert.Inconclusive(Missing); return; }

        var asked = (Environment.GetEnvironmentVariable("NEXAFLOW_ABC_SHOW") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var chosen = asked.Count > 0
            ? corpus.Items.Where(t => asked.Contains(Path.GetFileNameWithoutExtension(t.Abc))).ToList()
            : [corpus.Items[0], corpus.Items[corpus.Count / 2], corpus.Items[^1]];

        Assert.IsTrue(chosen.Count > 0, "none of those tunes are in the corpus");

        var into = Path.Combine(corpus.Root, "sweep");
        Directory.CreateDirectory(into);

        foreach (var tune in chosen)
            UiThread.Run(() =>
            {
                var name = Path.GetFileNameWithoutExtension(tune.Abc);
                var theirs = new BitmapImage(new Uri(tune.Image));
                var reference = GrayImage.Load(tune.Image).CropToInk();
                var width = reference.Width + Margin;
                var zoom = ZoomFor(reference);

                var element = new AbcScore(Text(tune.Abc), MarkdownPalette.Light, 0, zoom, pageWidth: 1.0);
                element.Measure(new Size(width, double.PositiveInfinity));
                element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

                var file = Path.Combine(into, $"{name}-at-{zoom:F2}.png");
                Save(Stack(element, element.DesiredSize, theirs), file);
                Console.WriteLine(file);
            });
    }

    /// <summary>Ours over theirs, on one white page, each labelled.</summary>
    private static BitmapSource Stack(FrameworkElement ours, Size size, BitmapSource theirs)
    {
        const double Gap = 14;
        const double Label = 16;

        var width = Math.Max(size.Width, theirs.PixelWidth);
        var height = Label + size.Height + Gap + Label + theirs.PixelHeight;

        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            Caption(dc, "ours", 0);
            dc.PushTransform(new TranslateTransform(0, Label));
            dc.DrawRectangle(new VisualBrush(ours), null, new Rect(0, 0, size.Width, size.Height));
            dc.Pop();

            var below = Label + size.Height + Gap;
            Caption(dc, "the engraver’s", below);
            dc.DrawImage(theirs, new Rect(0, below + Label, theirs.PixelWidth, theirs.PixelHeight));
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        return bitmap;

        static void Caption(DrawingContext dc, string what, double y) =>
            dc.DrawText(
                new FormattedText(what, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                  new Typeface("Segoe UI"), 11, Brushes.Gray, 1.0),
                new Point(2, y + 1));
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ── Scoring one tune ────────────────────────────────────────────────────

    private static Scored Score((string Abc, string Image) tune, string other)
    {
        var name = Path.GetFileNameWithoutExtension(tune.Abc);

        try
        {
            // Cropped because a margin is a fact about the picture rather than about the engraving, and
            // because it is the ink that says how wide the page it came from was.
            var theirs = GrayImage.Load(tune.Image).CropToInk();
            if (theirs.IsEmpty) return new Scored(name, 0, 0, 0, 0, 0, 0, 0, "the reference is empty");

            var abc = Text(tune.Abc);
            var notes = Notes(abc);
            var width = theirs.Width + Margin;
            var zoom = ZoomFor(theirs);

            var element = new AbcScore(abc, MarkdownPalette.Light, 0, zoom, pageWidth: 1.0);
            element.Measure(new Size(width, double.PositiveInfinity));

            var size = element.DesiredSize;
            if (size.Width < 1 || size.Height < 1)
                return new Scored(name, 0, 0, width, 0, notes, 0, zoom, "it engraved to nothing");
            if (size.Height > Absurd)
                return new Scored(name, 0, 0, width, 0, notes, 0, zoom,
                                  $"it engraved {size.Height:F0}px tall in {width}px of width");

            element.Arrange(new Rect(new Point(0, 0), size));

            var ours = Raster(element, size).CropToInk();
            if (ours.IsEmpty) return new Scored(name, 0, 0, width, 0, notes, 0, zoom, "it drew no ink");

            var overlap = GrayImage.InkOverlap(ours, theirs, Detail);

            // The same page against a picture of a different tune. Whatever two pages of music share just
            // by both being pages of music, this is it — and the gap between the two numbers is the whole
            // of what the sweep can actually see.
            var control = GrayImage.InkOverlap(ours, GrayImage.Load(other).CropToInk(), Detail);

            var us = (double)ours.Width / ours.Height;
            var them = (double)theirs.Width / theirs.Height;

            // Which way it is off, not just how far. Below one, our page is the taller of the two, which
            // at a shared width means more systems on it — so the notation is still too big.
            return new Scored(name, overlap, Closeness(us, them), width, control, notes, us / them, zoom, null);
        }
        catch (Exception ex)
        {
            return new Scored(name, 0, 0, 0, 0, 0, 0, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// How many notes a tune holds — the cheapest honest measure of how much there is to get wrong.
    /// <para>
    /// A two-bar jig and a five-system hymn are different problems, and a mean over both says which we are
    /// bad at only by accident. Counted off the tree rather than guessed from the file's length, which
    /// counts lyrics and comments as music.
    /// </para>
    /// </summary>
    private static int Notes(string abc) =>
        AbcParser.Parse(abc).SelfAndDescendants()
            .Count(n => n.Kind == AbcKinds.Note && n.Part(AbcRoles.Letter) is not null);

    /// <summary>
    /// How alike two aspect ratios are: the smaller over the larger, so 1 is the same shape. With both
    /// pages at the same width and the same size, that is a report on line breaking — a page of ours half
    /// their shape is a page with twice their systems on it.
    /// </summary>
    private static double Closeness(double ours, double theirs) =>
        ours <= 0 || theirs <= 0 ? 0 : Math.Min(ours, theirs) / Math.Max(ours, theirs);

    /// <summary>How much bigger ours is drawn before being brought back down to the reference's size.</summary>
    private const int Supersample = 4;

    /// <summary>
    /// Our page at the reference's size, drawn large and then reduced — which is how the reference got
    /// there too.
    ///
    /// <para>
    /// <strong>The line weight is the reason.</strong> A staff line is a hair: a ninth of a staff space, so
    /// at the sizes these thumbnails were made it is well under a pixel. Rasterised directly it either
    /// vanishes or snaps to a full black pixel, and neither is what the reference has — the reference was
    /// engraved large and scaled down, which leaves that hair as a soft grey line a pixel or so wide.
    /// Drawing ours the same way puts the two pages in the same condition, so what is being compared is
    /// the engraving rather than one rasteriser's idea of a sub-pixel stroke.
    /// </para>
    /// <para>
    /// It is deliberately a property of the <em>check</em> and not of the app. On screen a crisp hairline
    /// is the better staff line and this would only blur it; here the point is to match how the reference
    /// was made.
    /// </para>
    /// </summary>
    private static GrayImage Raster(FrameworkElement element, Size size)
    {
        var wide = Math.Max(1, (int)Math.Ceiling(size.Width));
        var tall = Math.Max(1, (int)Math.Ceiling(size.Height));

        var bitmap = new RenderTargetBitmap(
            wide * Supersample, tall * Supersample,
            96 * Supersample, 96 * Supersample, PixelFormats.Pbgra32);

        bitmap.Render(element);
        return GrayImage.FromBitmap(bitmap).ResampleToHeight(tall);
    }

    // ── What the run says ───────────────────────────────────────────────────

    /// <summary>
    /// The run, written beside the corpus: the numbers worth quoting, then every tune worst-first so
    /// somebody can open the twenty at the top and see what they have in common.
    /// </summary>
    private static void Report(string root, List<Scored> all, List<Scored> drawn, TimeSpan took)
    {
        var into = Path.Combine(root, "sweep");
        Directory.CreateDirectory(into);

        var text = new StringBuilder();
        var mean = drawn.Count == 0 ? 0 : drawn.Average(s => s.Overlap);
        var shape = drawn.Count == 0 ? 0 : drawn.Average(s => s.Shape);
        var control = drawn.Count == 0 ? 0 : drawn.Average(s => s.Control);
        var beat = drawn.Count == 0 ? 0 : (double)drawn.Count(s => s.Overlap > s.Control) / drawn.Count;

        text.AppendLine(CultureInfo.InvariantCulture, $"{all.Count} tunes, {drawn.Count} engraved, in {took.TotalSeconds:F1}s");
        text.AppendLine(CultureInfo.InvariantCulture, $"mean ink overlap  {mean:F4}   against its own picture");
        text.AppendLine(CultureInfo.InvariantCulture, $"mean control      {control:F4}   against a different tune's picture");
        text.AppendLine(CultureInfo.InvariantCulture, $"separation        {mean - control:F4}   the whole of what this sweep can see");
        text.AppendLine(CultureInfo.InvariantCulture, $"beat its control  {beat:P1}   of tunes scored higher against their own picture");
        text.AppendLine(CultureInfo.InvariantCulture, $"mean shape match  {shape:F4}   (1 = we broke the lines where they did)");
        // The size each reference asked for. The spread is the finding: it is why a fixed zoom cannot be
        // right, and why this is measured per tune rather than chosen once.
        var zooms = drawn.Select(z => z.Zoom).Where(v => v > 0).OrderBy(v => v).ToList();
        if (zooms.Count > 0)
            text.AppendLine(CultureInfo.InvariantCulture,
                $"drawn at zoom     {zooms[zooms.Count / 2]:F2}   median of what each reference asked for"
                + $" (a tenth under {zooms[zooms.Count / 10]:F2}, a tenth over {zooms[zooms.Count * 9 / 10]:F2})"
                + $", into its own ink width + {Margin}px");

        // Which way the shape is off, which is the half that says what to do about it. At a shared width a
        // page can only differ in shape by having a different number of systems on it, so below one means
        // ours has more of them — the notation is too big — and above one means it has fewer.
        var stretch = drawn.Select(s => s.Stretch).Where(v => v > 0).OrderBy(v => v).ToList();
        if (stretch.Count > 0)
            text.AppendLine(CultureInfo.InvariantCulture,
                $"and came out      {stretch[stretch.Count / 2]:F2}x their shape   "
                + $"(under 1 = more systems than theirs, so the notation is still too big)");
        text.AppendLine();
        text.AppendLine("by how many notes there are to get wrong");
        foreach (var (from, to) in new[] { (0, 16), (16, 48), (48, 128), (128, 320), (320, int.MaxValue) })
        {
            var band = drawn.Where(s => s.Notes >= from && s.Notes < to).ToList();
            if (band.Count == 0) continue;

            var beatable = (double)band.Count(s => s.Overlap > s.Control) / band.Count;
            var label = to == int.MaxValue ? $"{from}+" : $"{from}-{to}";

            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {label,8} notes  {band.Count,5} tunes   overlap {band.Average(s => s.Overlap):F4}"
                + $"   control {band.Average(s => s.Control):F4}"
                + $"   separation {band.Average(s => s.Overlap) - band.Average(s => s.Control):F4}"
                + $"   beat {beatable:P0}   shape {band.Average(s => s.Shape):F3}");
        }

        text.AppendLine();
        text.AppendLine("by how well the shapes matched — the two questions this conflates, separated");
        text.AppendLine("  a low score at a GOOD shape is a drawing difference; at a bad one it is a line break");
        foreach (var (from, to) in new[] { (0.0, 0.7), (0.7, 0.9), (0.9, 0.97), (0.97, 1.01) })
        {
            var band = drawn.Where(s => s.Shape >= from && s.Shape < to).ToList();
            if (band.Count == 0) continue;

            var beatable = (double)band.Count(s => s.Overlap > s.Control) / band.Count;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  shape {from:F2}-{to:F2}  {band.Count,5} tunes   overlap {band.Average(s => s.Overlap):F4}"
                + $"   control {band.Average(s => s.Control):F4}"
                + $"   separation {band.Average(s => s.Overlap) - band.Average(s => s.Control):F4}"
                + $"   beat {beatable:P0}");
        }

        text.AppendLine();
        text.AppendLine("overlap distribution");
        foreach (var (from, to) in Bands())
        {
            var n = drawn.Count(s => s.Overlap >= from && s.Overlap < to);
            var share = drawn.Count == 0 ? 0 : (double)n / drawn.Count;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {from:F1}-{to:F1}  {n,5}  {new string('#', (int)Math.Round(share * 60))}");
        }

        text.AppendLine();
        text.AppendLine("shape match distribution — how often we broke the lines the same way");
        foreach (var (from, to) in Bands())
        {
            var n = drawn.Count(s => s.Shape >= from && s.Shape < to);
            text.AppendLine(CultureInfo.InvariantCulture, $"  {from:F1}-{to:F1}  {n,5}");
        }

        if (all.Any(s => s.Trouble is not null))
        {
            text.AppendLine();
            text.AppendLine("did not engrave");
            foreach (var bad in all.Where(s => s.Trouble is not null).Take(40))
                text.AppendLine(CultureInfo.InvariantCulture, $"  {bad.Name}  {bad.Trouble}");
        }

        text.AppendLine();
        text.AppendLine("worst first — name, overlap, control, shape, notes, width, stretch, zoom");
        foreach (var one in all)
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {one.Name}  {one.Overlap:F4}  {one.Control:F4}  {one.Shape:F4}  {one.Notes,5}"
                + $"  {one.Width:F0}  {one.Stretch:F2}x  {one.Zoom:F2}");

        File.WriteAllText(Path.Combine(into, "abc-picture-sweep.txt"), text.ToString());
        Console.WriteLine(text.ToString()[..Math.Min(2000, text.Length)]);
    }

    private static IEnumerable<(double From, double To)> Bands()
    {
        for (var at = 0.0; at < 1.0; at += 0.1) yield return (at, at + 0.1);
    }

    // ── The corpus ──────────────────────────────────────────────────────────

    private const string Missing =
        "Point NEXAFLOW_ABC_CORPUS at a corpus of .abc files with a picture beside each one.";

    private sealed record Tunes(string Root, IReadOnlyList<(string Abc, string Image)> Items)
    {
        public int Count => Items.Count;

        public IEnumerable<(string Abc, string Image)> Take(int n) => Items.Take(n);
    }

    private static Tunes? Corpus()
    {
        var root = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_CORPUS");
        if (string.IsNullOrWhiteSpace(root)) root = DefaultCorpus;
        if (!Directory.Exists(root)) return null;

        var pairs = new List<(string, string)>();

        foreach (var abc in Directory.EnumerateFiles(root, "*.abc", SearchOption.AllDirectories))
        {
            // The picture sits beside the tune, in the folder next door.
            var image = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(abc)!)!,
                "images",
                Path.GetFileNameWithoutExtension(abc) + ".png");

            if (File.Exists(image)) pairs.Add((abc, image));
        }

        return pairs.Count == 0 ? null : new Tunes(root, pairs);
    }

    /// <summary>The tune's characters, decoded once and left alone — mojibake and all, as the corpus has it.</summary>
    private static string Text(string path) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(path));
}
