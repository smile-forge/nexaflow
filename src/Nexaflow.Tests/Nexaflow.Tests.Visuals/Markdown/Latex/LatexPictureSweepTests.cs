using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

using Rect = System.Windows.Rect;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// A quarter of a million real formulas, drawn, and held against two different things — because the two
/// questions worth asking about a drawing want different answers.
///
/// <para><strong>Is it right?</strong> The corpus ships a picture of each of its formulas made by real
/// LaTeX, and that is the only reference here that was never ours. But it is <em>a</em> truth rather
/// than ground truth: two rasterisers never agree on a pixel, so the comparison has to be a fuzzy one,
/// and a fuzzy comparison can only rank. Measured — see <see cref="GrayImage.InkOverlap"/> — damaging a
/// formula on purpose costs it about 0.05 of score while one correct formula differs from the next by
/// 0.25. So the score sorts the corpus for somebody to look at, and it is not asked to do more.</para>
///
/// <para><strong>Has it moved?</strong> Nothing external can answer that, and nothing external needs
/// to. Once a drawing has been looked at and accepted, <em>our own</em> record of it is the reference,
/// and the comparison against it is exact. What is kept is not the picture but the two trees — the
/// parse tree and the layout tree — because the picture is painted out of the layout tree, so trees
/// that match draw the same pixels, and trees can differ where pixels do not. That last case is the one
/// worth catching: a formula that still draws correctly but whose parts have moved is a formula whose
/// selection, caret and editing have quietly changed.</para>
///
/// <para>
/// So: the accepted trees are the gate, and the corpus score says which way a change moved — closer to
/// LaTeX or further from it.
/// </para>
///
/// <para>
/// Opt-in, and local: <c>NEXAFLOW_LATEX_PICTURES</c> points at the corpus folder (the one holding
/// <c>final_png_formulas.txt</c>, <c>corresponding_png_images.txt</c> and <c>generated_png_images\</c>),
/// and everything this writes goes beside it rather than into the repository — a run is a gigabyte of
/// drawings. <c>NEXAFLOW_LATEX_BLESS=1</c> accepts what this run drew as the reference for the next
/// one, which is the deliberate act that says somebody looked.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("opt-in sweep over an external corpus of rendered formulas")]
public class LatexPictureSweepTests
{
    /// <summary>
    /// What we draw at. Larger than the corpus's own rendering on purpose: the comparison brings both to
    /// a common height, and shrinking our detail into that loses less than enlarging it would invent.
    /// </summary>
    private const double Scale = 20;

    /// <summary>At or below this the corpus and we disagree enough that somebody should look.</summary>
    private const double Review = 0.35;

    /// <summary>Where our own drawings go, sharded so that no folder holds a quarter of a million files.</summary>
    private const string Drawings = "drawings";

    /// <summary>How many rows of the review page get a picture drawn for them; the rest are scored only.</summary>
    private const int Shown = 5000;

    /// <summary>Where the corpus keeps LaTeX's own rendering of each formula.</summary>
    private const string References = "generated_png_images";

    [TestMethod]
    public void NothingWeDrawHasMovedAndWhatWeDrawLooksLikeLaTeX()
    {
        if (Folder() is not { } corpus) return;

        var work = Path.Combine(corpus, "latex-picture");
        var accepted = Path.Combine(work, "accepted");
        var clock = Stopwatch.StartNew();

        var drawn = Draw(corpus);
        Assert.IsTrue(drawn.Count > 1000, $"only {drawn.Count} formula(s) had a picture to be held against");

        Leave(work, drawn, clock.Elapsed);

        if (Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_BLESS") == "1")
        {
            Keep(accepted, drawn);
            Assert.Inconclusive($"accepted this run's {drawn.Count} drawing(s) as the reference "
                                + $"({clock.Elapsed.TotalMinutes:F1} min).");
            return;
        }

        var was = Kept(accepted);
        if (was.Count == 0)
            Assert.Inconclusive($"nothing accepted yet. Read {Path.Combine(work, "review", "index.html")} "
                                + "worst-first, then re-run with NEXAFLOW_LATEX_BLESS=1 to accept it.");

        // Cut three ways, because the three mean different things. How a formula is *read* is most of
        // the work on this branch and is meant to change. Where its pieces are *set* is what a reader
        // sees, and is not. What each piece was set *from* is what a caret and a selection run on, and
        // changes with the reading — so it has to be visible without hiding the middle one.
        var moved = drawn
            .Select(row => (Row: row, Before: was.GetValueOrDefault(row.Entry.Id)))
            .Where(pair => pair.Before is not null && pair.Before != pair.Row.Text)
            .Select(pair => (pair.Row, Was: Parts(pair.Before!), Now: Parts(pair.Row.Text)))
            .ToList();

        if (moved.Count == 0) return;

        var set = moved.Where(pair => Geometry(pair.Was.Layout) != Geometry(pair.Now.Layout)).ToList();
        var named = moved.Count(pair => pair.Was.Layout != pair.Now.Layout) - set.Count;
        var told = moved.Count(pair => pair.Was.Layout == pair.Now.Layout
                                      && pair.Was.Parse == pair.Now.Parse
                                      && pair.Was.Trouble != pair.Now.Trouble);
        var read = moved.Count - set.Count - named - told;

        Paint(work, moved.Select(pair => pair.Row).ToList());

        var complaint = new StringBuilder();
        complaint.AppendLine($"{moved.Count} of {drawn.Count} formula(s) differ from the accepted reference:");
        complaint.AppendLine($"  {read,8} read differently, and set in the same places from the same source");
        complaint.AppendLine($"  {named,8} set in the same places, from different source — selection moved, "
                             + "the drawing did not");
        complaint.AppendLine($"  {told,8} set and read the same, and reported differently — a squiggle changed");
        complaint.AppendLine($"  {set.Count,8} set in different places — these are the ones a reader sees");

        if (set.Count > 0)
        {
            complaint.AppendLine("The score beside each says where it now stands against LaTeX's own "
                                 + "rendering — lower is further away.");
            foreach (var pair in set.OrderBy(pair => pair.Row.Overlap).Take(25))
                complaint.AppendLine($"  {pair.Row.Entry.Id}  {pair.Row.Overlap:F3}  "
                                     + Shorten(pair.Row.Entry.Formula));
            if (set.Count > 25) complaint.AppendLine($"  … and {set.Count - 25} more");
        }

        complaint.AppendLine($"Both readings of each are under {work}; NEXAFLOW_LATEX_BLESS=1 accepts them.");
        Assert.Fail(complaint.ToString());
    }

    // ── the run ──────────────────────────────────────────────────────────────────

    /// <summary>One formula: what we made of it, and how close that came to the corpus's own picture.</summary>
    private sealed record Drawn(CorpusEntry Entry, string Text, double Overlap, bool Drew, string? Error);

    /// <summary>Every formula the corpus has a picture of, drawn and scored against it.</summary>
    private static List<Drawn> Draw(string corpus)
    {
        var stride = Number("NEXAFLOW_LATEX_CORPUS_STRIDE", 1);
        var limit = Number("NEXAFLOW_LATEX_CORPUS_LIMIT", 0);

        var entries = Corpus.Load(corpus, limit, skip: 0).Entries
            .Where(entry => entry.ImageName is not null)
            .Where((_, at) => at % stride == 0)
            .ToList();

        var drawn = new Drawn[entries.Count];

        // Indices rather than the entries themselves: the body has to know where to put its answer, and
        // asking a list where one of its items is would be a quarter of a million linear searches.
        var order = new int[entries.Count];
        for (var at = 0; at < order.Length; at++) order[at] = at;

        UiThread.Across(order, at => drawn[at] = Compare(corpus, entries[at]));

        return drawn.ToList();
    }

    /// <summary>Our reading and drawing of one formula, beside the corpus's picture of it.</summary>
    private static Drawn Compare(string corpus, CorpusEntry entry)
    {
        GrayImage? theirs = null;
        var reference = Path.Combine(corpus, References, entry.ImageName!);
        if (File.Exists(reference))
        {
            try { theirs = GrayImage.Load(reference).CropToInk(); }
            catch { theirs = null; }
        }

        try
        {
            if (LatexLayout.Build(entry.Formula, Scale) is not { } layout)
                return new Drawn(entry, "unread\n", 0, false, "read nothing");

            var text = Reading(entry.Formula, layout);

            if (Picture(layout) is not { } ours) return new Drawn(entry, text, 0, false, "drew nothing");

            var overlap = theirs is null ? 0 : GrayImage.InkOverlap(GrayImage.FromBitmap(ours).CropToInk(), theirs);
            return new Drawn(entry, text, overlap, true, theirs is null ? "no reference rendering" : null);
        }
        catch (Exception e)
        {
            return new Drawn(entry, $"threw {e.GetType().Name}\n", 0, false, $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// Both readings of the formula, as text: what the parser made of the source, and what was laid out
    /// from that. This is the thing compared exactly between runs, so everything in it is something a
    /// change to would matter — and nothing in it is a pixel.
    /// </summary>
    private static string Reading(string latex, LatexLayout layout)
    {
        var text = new StringBuilder();

        text.Append("parse\n");
        Parsed(TexReading.Of(latex).Root, 1, text);

        text.Append("layout ").Append(Round(layout.Size.Width)).Append('x').Append(Round(layout.Size.Height)).Append('\n');
        foreach (var node in layout.Tree.Root.SelfAndDescendants())
            text.Append(' ', Depth(node))
                .Append(node.Kind).Append(' ')
                .Append(Round(node.Bounds.X)).Append(',').Append(Round(node.Bounds.Y)).Append(' ')
                .Append(Round(node.Bounds.Width)).Append('x').Append(Round(node.Bounds.Height))
                .Append(Named)
                .Append(node.SourceStart).Append('+').Append(node.SourceLength)
                .Append('\n');

        foreach (var trouble in layout.Tree.Diagnostics)
            text.Append("! ").Append(trouble.Start).Append('+').Append(trouble.Length)
                .Append(' ').Append(trouble.Message).Append('\n');

        return text.ToString();
    }

    private static void Parsed(TexPart part, int depth, StringBuilder text)
    {
        text.Append(' ', depth).Append(part.Kind).Append(' ').Append(part.Role);
        if (part.Children.Count == 0) text.Append(" \"").Append(part.Text).Append('"');
        text.Append('\n');

        foreach (var child in part.Children) Parsed(child, depth + 1, text);
    }

    private static int Depth(ILayoutNode node)
    {
        var depth = 1;
        for (var at = node.Parent; at is not null; at = at.Parent) depth++;
        return depth;
    }

    /// <summary>Rounded, so that a difference below what anyone could see is not called a difference.</summary>
    private static string Round(double value) => value.ToString("F2");

    private static RenderTargetBitmap? Picture(LatexLayout layout)
    {
        // Room around it: at this scale a glyph can spill a little past the laid-out box, and the crop
        // that follows only has to find the ink, not to be told where it is.
        const int pad = (int)Scale;
        var width = (int)Math.Ceiling(layout.Size.Width) + (2 * pad);
        var height = (int)Math.Ceiling(layout.Size.Height) + (2 * pad);
        if (width <= 0 || height <= 0 || (long)width * height > 40_000_000) return null;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.PushTransform(new TranslateTransform(pad, pad));
            layout.Paint(dc, Brushes.Black);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// Draws these formulas again and writes the pictures, for the rows of the review page somebody is
    /// going to look at. Separate from the scoring pass because the scoring pass keeps no pictures: the
    /// disk is the whole cost of this sweep, and a quarter of a million of them are never opened.
    /// </summary>
    private static void Paint(string work, IReadOnlyList<Drawn> rows)
    {
        foreach (var shard in rows.Where(row => row.Drew).Select(row => row.Entry.Id[..2]).Distinct())
            Directory.CreateDirectory(Path.Combine(work, Drawings, shard));

        var order = new int[rows.Count];
        for (var at = 0; at < order.Length; at++) order[at] = at;

        UiThread.Across(order, at =>
        {
            var row = rows[at];
            try
            {
                if (LatexLayout.Build(row.Entry.Formula, Scale) is not { } layout) return;
                if (Picture(layout) is not { } drawing) return;
                Save(drawing, Path.Combine(work, Drawings, row.Entry.Id[..2], row.Entry.Id + ".png"));
            }
            catch
            {
                // Its row shows the reason there is no picture instead.
            }
        });
    }

    // ── the reference ────────────────────────────────────────────────────────────

    /// <summary>
    /// Every reading kept in a folder, by formula. Stored a file per shard rather than a file per
    /// formula: a quarter of a million small writes is minutes of filesystem and virus scanner, and two
    /// hundred and fifty-six is seconds. The run already holds every reading in memory, so nothing was
    /// gained by having written them one at a time either.
    /// </summary>
    private static Dictionary<string, string> Kept(string folder)
    {
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder)) return kept;

        foreach (var shard in Directory.EnumerateFiles(folder, "*.txt"))
        {
            string? id = null;
            var reading = new StringBuilder();

            foreach (var line in File.ReadLines(shard))
            {
                if (!line.StartsWith("## ", StringComparison.Ordinal))
                {
                    reading.Append(line).Append('\n');
                    continue;
                }

                if (id is not null) kept[id] = reading.ToString();
                id = line[3..];
                reading.Clear();
            }

            if (id is not null) kept[id] = reading.ToString();
        }

        return kept;
    }

    /// <summary>
    /// A stored reading cut into the three things it says: how the formula was read, where its pieces were
    /// set, and what was reported about it.
    /// <para>
    /// The third one earns its place. The trouble lines sit at the end of the layout section with no header
    /// of their own, so cutting only twice folded them into the layout — and because a <c>!</c> line carries
    /// no source-naming marker, <see cref="Geometry"/> passed it through untouched. A change to what is
    /// <em>reported</em> then came out under "set in different places — these are the ones a reader sees",
    /// which is the one line here that has to be trustworthy. 1,091 formulas said so while every score,
    /// every bucket and the mean overlap were byte-identical.
    /// </para>
    /// </summary>
    private static (string Parse, string Layout, string Trouble) Parts(string reading)
    {
        var layoutAt = reading.IndexOf("\nlayout ", StringComparison.Ordinal);
        if (layoutAt < 0) return (reading, "", "");

        var rest = reading[layoutAt..];
        var troubleAt = rest.IndexOf("\n! ", StringComparison.Ordinal);

        return troubleAt < 0
            ? (reading[..layoutAt], rest, "")
            : (reading[..layoutAt], rest[..troubleAt], rest[troubleAt..]);
    }

    /// <summary>Where a marker sits between where a piece was set and what it was set from.</summary>
    private const string Named = " <- ";

    /// <summary>
    /// The layout half with what each piece was set <em>from</em> taken off, leaving only where it
    /// landed. The two move for different reasons and only one of them is what a reader sees.
    /// </summary>
    private static string Geometry(string layout)
    {
        var text = new StringBuilder(layout.Length);

        foreach (var line in layout.Split('\n'))
        {
            var at = line.IndexOf(Named, StringComparison.Ordinal);
            text.Append(at < 0 ? line : line[..at]).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Writes every reading into <paramref name="folder"/>, a file per shard.</summary>
    private static void Keep(string folder, List<Drawn> drawn)
    {
        Directory.CreateDirectory(folder);

        foreach (var shard in drawn.GroupBy(row => row.Entry.Id[..2]))
        {
            var text = new StringBuilder();
            foreach (var row in shard.OrderBy(row => row.Entry.Id, StringComparer.Ordinal))
                text.Append("## ").Append(row.Entry.Id).Append('\n').Append(row.Text);

            File.WriteAllText(Path.Combine(folder, shard.Key + ".txt"), text.ToString());
        }
    }

    // ── what it leaves behind ────────────────────────────────────────────────────

    /// <summary>The readings, the scores, a summary, and the side-by-side page — worst first.</summary>
    private static void Leave(string work, List<Drawn> drawn, TimeSpan taken)
    {
        Keep(Path.Combine(work, "ours"), drawn);

        var worst = drawn.OrderBy(row => row.Overlap).ToList();

        var summary = new StringBuilder();
        summary.AppendLine($"{drawn.Count} formula(s) drawn beside their corpus rendering "
                           + $"in {taken.TotalMinutes:F1} min at scale {Scale}");
        foreach (var band in new[] { 0.8, 0.65, 0.5, Review, 0.25 })
            summary.AppendLine($"  at or above {band:F2}: {drawn.Count(row => row.Overlap >= band),8}");
        summary.AppendLine($"  no drawing:        {drawn.Count(row => row.Error is not null),8}");
        summary.AppendLine($"  mean overlap:      {drawn.Average(row => row.Overlap),8:F4}");

        File.WriteAllText(Path.Combine(work, "summary.txt"), summary.ToString());

        using (var scores = new StreamWriter(Path.Combine(work, "scores.txt")))
            foreach (var row in worst)
                scores.WriteLine($"{row.Entry.Id} {row.Overlap:F4} {row.Error ?? ""}");

        // Only the rows somebody will read are drawn to disk. The whole corpus is scored and the whole
        // of it is in scores.txt, but writing a picture per formula is a quarter of a million small
        // writes, which is the entire cost of the sweep — and nobody scrolls to the hundred-thousandth
        // row of a page sorted worst-first.
        var shown = worst.Take(Math.Max(Shown, worst.Count(row => row.Overlap <= Review))).ToList();
        Paint(work, shown);

        var pairs = shown
            .Select(row => new Pair(row.Entry,
                                    $"../../{References}/{row.Entry.ImageName}",
                                    row.Drew ? $"../{Drawings}/{row.Entry.Id[..2]}/{row.Entry.Id}.png" : null,
                                    row.Error, row.Overlap, row.Overlap <= Review))
            .ToList();

        LatexPictureReport.Write(Path.Combine(work, "review"), pairs, LatexPictureReport.DefaultPageSize);
    }

    // ── odds and ends ────────────────────────────────────────────────────────────

    private static string? Folder()
    {
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_PICTURES");

        if (string.IsNullOrWhiteSpace(corpus) || !Directory.Exists(corpus))
        {
            Assert.Inconclusive($"set NEXAFLOW_LATEX_PICTURES to the corpus folder (got: {corpus ?? "nothing"})");
            return null;
        }

        return corpus;
    }

    private static int Number(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var value) && value > 0 ? value : fallback;

    private static string Shorten(string formula) =>
        formula.Length <= 160 ? formula : formula[..160] + " …";
}
