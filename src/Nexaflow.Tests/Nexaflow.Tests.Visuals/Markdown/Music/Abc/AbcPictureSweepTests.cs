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
/// <strong>Line breaking is the thing that would swamp it, so it is normalised away first.</strong> The
/// reference was drawn at a width nobody recorded and then scaled to fit a thumbnail; we choose our own.
/// Two engravings of one tune that break into different numbers of systems are different pictures however
/// well each is drawn, and comparing them would rank the whole corpus by an accident of width. So the
/// sweep looks for the width at which <em>our</em> page is the shape theirs is, and scores there — and it
/// reports how well it managed, because a tune whose shape we cannot match at any width is telling us
/// something about our line breaking rather than about our note heads.
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

    /// <summary>Where the search starts, and how far either way it is allowed to go.</summary>
    private const double FirstGuess = 620;
    private const double Narrowest = 260;
    private const double Widest = 2600;

    /// <summary>
    /// A page taller than this is one nobody would print and one nothing should rasterise. It is a guard
    /// rather than a limit: the search should never ask for such a page, and if it does, something is
    /// wrong that a memory profile would find long after the machine had stopped responding.
    /// </summary>
    private const double Absurd = 30000;

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
                                 int Notes, string? Trouble);

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

    // ── Scoring one tune ────────────────────────────────────────────────────

    private static Scored Score((string Abc, string Image) tune, string other)
    {
        var name = Path.GetFileNameWithoutExtension(tune.Abc);

        try
        {
            var reference = GrayImage.Load(tune.Image);
            if (reference.IsEmpty) return new Scored(name, 0, 0, 0, 0, 0, "the reference is empty");

            var abc = Text(tune.Abc);
            var wanted = (double)reference.Width / reference.Height;
            var notes = Notes(abc);

            var best = Fitted(abc, wanted);
            if (best is not { } fit) return new Scored(name, 0, 0, 0, 0, notes, "it engraved to nothing");

            var ours = GrayImage.FromBitmap(Raster(fit.Element, fit.Size, reference.Height));
            var overlap = GrayImage.InkOverlap(ours, reference, Detail);

            // The same page against a picture of a different tune. Whatever two pages of music share just
            // by both being pages of music, this is it — and the gap between the two numbers is the whole
            // of what the sweep can actually see.
            var control = GrayImage.InkOverlap(ours, GrayImage.Load(other), Detail);

            return new Scored(name, overlap, fit.Shape, fit.Width, control, notes, null);
        }
        catch (Exception ex)
        {
            return new Scored(name, 0, 0, 0, 0, 0, $"{ex.GetType().Name}: {ex.Message}");
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
    /// Our page at the width whose shape is closest to the reference's, and how close that came.
    ///
    /// <para>
    /// <see cref="Scored.Shape"/> is the ratio of the two aspect ratios, the smaller over the larger, so 1
    /// is a perfect match and 0.5 means one page is twice the other's shape. It is reported beside the
    /// overlap rather than folded into it, because they are different complaints: a low shape number says
    /// we broke the lines somewhere else, and a low overlap at a <em>good</em> shape says we drew the same
    /// page differently.
    /// </para>
    /// <para>
    /// <strong>Searched rather than enumerated, and that is not an optimisation.</strong> A page gets both
    /// shorter and wider as the width grows, so its aspect climbs with the width and can be bisected. A
    /// list of candidate widths instead measures every tune at every width — including the narrow ones,
    /// where a long tune lays out into hundreds of systems that are then thrown away. Twelve of those in
    /// flight per thread took the working set to 97GB and the machine to a standstill: the run was three
    /// cores busy out of thirty-two and paging the rest of the time.
    /// </para>
    /// <para>
    /// So the search starts where most pages are, walks the way the shape says, and only ever lays out a
    /// width it has a reason to. It also keeps nothing but the numbers, and rebuilds the winner at the
    /// end — one extra layout against eleven held alive.
    /// </para>
    /// </summary>
    private static (FrameworkElement Element, Size Size, double Width, double Shape)? Fitted(string abc, double wanted)
    {
        (double Width, double Shape)? best = null;
        double? low = null, high = null;

        // Walk outward from the first guess until the wanted shape is bracketed, or an end is reached.
        var at = FirstGuess;
        for (var step = 0; step < 6; step++)
        {
            if (Shaped(abc, at) is not { } seen) break;

            Remember(at, seen.Shape);
            if (Math.Abs(seen.Aspect - wanted) < 0.001) break;

            if (seen.Aspect < wanted) { low = at; if (high is not null || at >= Widest) break; at = Math.Min(at * 1.6, Widest); }
            else { high = at; if (low is not null || at <= Narrowest) break; at = Math.Max(at / 1.6, Narrowest); }
        }

        // Then halve the bracket a few times. Four is plenty: the answer is a whole number of systems, and
        // the widths that produce one are a coarse grid however finely this looks between them.
        if (low is { } from && high is { } to)
            for (var step = 0; step < 4; step++)
            {
                var middle = (from + to) / 2;
                if (Shaped(abc, middle) is not { } seen) break;

                Remember(middle, seen.Shape);
                if (seen.Aspect < wanted) from = middle; else to = middle;
            }

        if (best is not { } winner) return null;

        var element = new AbcScore(abc, MarkdownPalette.Light, 0);
        element.Measure(new Size(winner.Width, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

        return element.DesiredSize.Width < 1 || element.DesiredSize.Height < 1
            ? null
            : (element, element.DesiredSize, winner.Width, winner.Shape);

        void Remember(double width, double shape)
        {
            if (best is { } had && shape <= had.Shape) return;
            best = (width, shape);
        }

        (double Aspect, double Shape)? Shaped(string tune, double width)
        {
            var probe = new AbcScore(tune, MarkdownPalette.Light, 0);
            probe.Measure(new Size(width, double.PositiveInfinity));

            var size = probe.DesiredSize;
            if (size.Width < 1 || size.Height < 1 || size.Height > Absurd) return null;

            var aspect = size.Width / size.Height;
            return (aspect, Closeness(aspect, wanted));
        }
    }

    /// <summary>How alike two aspect ratios are: the smaller over the larger, so 1 is the same shape.</summary>
    private static double Closeness(double ours, double theirs) =>
        ours <= 0 || theirs <= 0 ? 0 : Math.Min(ours, theirs) / Math.Max(ours, theirs);

    /// <summary>
    /// Our page, rasterised to about the height the reference was.
    ///
    /// <para>
    /// <strong>Matching the resolution is part of the comparison, not a detail of it.</strong> The corpus
    /// pictures are thumbnails of a printed page, and the scaling that made them has all but erased the
    /// thin strokes: at 800×129 the staff lines and stems are gone and only the note heads survive.
    /// Rasterising ours crisply and comparing would hold solid staff lines against nothing, and score us
    /// down for ink the reference no longer has rather than for ink we drew wrongly.
    /// </para>
    /// <para>
    /// So ours is squeezed through the same loss. Not to flatter the number — it barely moves — but so
    /// that what is left in both pictures is the same kind of thing.
    /// </para>
    /// </summary>
    private static BitmapSource Raster(FrameworkElement element, Size size, int wanted)
    {
        var scale = Math.Clamp(wanted / Math.Max(size.Height, 1), 0.05, 1.0);

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(size.Height * scale)),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);

        bitmap.Render(element);
        return bitmap;
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
        text.AppendLine(CultureInfo.InvariantCulture, $"mean shape match  {shape:F4}   (1 = our page is the shape theirs is)");
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
        text.AppendLine("worst first — name, overlap, control, shape, notes, the width we drew at");
        foreach (var one in all)
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {one.Name}  {one.Overlap:F4}  {one.Control:F4}  {one.Shape:F4}  {one.Notes,5}  {one.Width:F0}");

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
