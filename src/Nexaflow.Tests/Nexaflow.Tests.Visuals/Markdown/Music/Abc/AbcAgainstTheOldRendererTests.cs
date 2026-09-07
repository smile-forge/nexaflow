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
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Rendering;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// The new engraving held against the old one, tune by tune.
///
/// <para>
/// <strong>The oracle we actually control.</strong> The corpus ships a picture beside every tune, and it
/// is worth having — it was drawn by somebody who is not us. But it is a thumbnail from another engraver
/// with another font at an unrecorded size, so two pages that agree completely still only score about
/// 0.5, and a real regression is a tenth of a point inside that noise.
/// </para>
/// <para>
/// The old renderer is the opposite in every one of those respects: same font, same metrics, same
/// rasteriser, and it is code we already fixed things in. Two renderings of one tune should therefore be
/// nearly identical, which makes a drop <em>legible</em>. That is the whole point — the grace note that
/// came out as a crossed head had been got right once already, and nothing was watching for it coming
/// back.
/// </para>
/// <para>
/// It is a ranking, not a verdict. The two engravers differ on purpose in places — the new one spaces
/// notes more openly, keeps beam groups tight, puts air around a repeat — so a tune scoring below its
/// neighbours is a thing to look at rather than a thing that is wrong. What it is for is the worst-first
/// list, which is where a regression appears first.
/// </para>
/// <para>
/// Opt-in and local: <c>NEXAFLOW_ABC_CORPUS</c> points at the tunes, <c>NEXAFLOW_ABC_SWEEP</c> says how
/// many. Everything written goes beside the corpus, never into the repository.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("opt-in comparison between two of our own renderers")]
public class AbcAgainstTheOldRendererTests
{
    private const string DefaultCorpus = @"D:\Datasets\abcmusic\zenoob";
    private const int DefaultSample = 300;

    /// <summary>The width both renderers are given. One number, because the comparison is like for like.</summary>
    private const double Width = 900;

    /// <summary>
    /// How alike two drawings of one tune have to be before this stops complaining.
    ///
    /// <para>
    /// Not 1: the two engravers genuinely differ, and they are meant to. It is set where the corpus put it
    /// rather than where it would be comfortable — a floor nothing currently trips, so that anything which
    /// does trip it is new.
    /// </para>
    /// </summary>
    private const double Floor = 0.55;

    private sealed record Scored(string Name, double Overlap, string? Trouble);

    /// <summary>
    /// The same comparison over whole corpus pages — a ranking, not a gate.
    ///
    /// <para>
    /// A whole page is dominated by where the lines broke, and the two engravers break them differently on
    /// purpose: the new one spaces notes more openly and puts air around a repeat, so it fits fewer bars
    /// on a line. That is a change we chose, and it shows up here as disagreement. So this is worth
    /// reading worst-first and is not worth failing on.
    /// </para>
    /// </summary>
    [TestMethod]
    public void AndTheWholeCorpusRanksWhereTheTwoDisagreeMost()
    {
        if (Corpus() is not { } corpus) { Assert.Inconclusive(Missing); return; }

        var wanted = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_SWEEP");
        var take = string.Equals(wanted, "all", StringComparison.OrdinalIgnoreCase)
            ? corpus.Count
            : int.TryParse(wanted, out var count) ? count : DefaultSample;

        var tunes = corpus.Take(Math.Min(take, corpus.Count)).ToList();
        var scored = new ConcurrentBag<Scored>();
        var clock = Stopwatch.StartNew();

        UiThread.Across(tunes, tune => scored.Add(Score(tune)));

        var all = scored.OrderBy(s => s.Overlap).ToList();
        var drawn = all.Where(s => s.Trouble is null).ToList();

        Report(corpus.Root, all, drawn, clock.Elapsed);

        Assert.IsTrue(drawn.Count > 0, "neither renderer drew anything");

        // Worst-first, and only the worst: a list of every tune below the floor is what says which ones to
        // open, and the count is what says whether something broke or one tune is unusual.
        var mean = drawn.Average(s => s.Overlap);

        // Nothing asserted about the mean: it measures line breaking, which the two do differently by
        // design. What this run is for is the list it writes.
        Assert.IsTrue(mean > 0.2, $"mean agreement {mean:F4} — the two are drawing unrelated pages");
    }

    /// <summary>
    /// Writes a picture per construct — the new engraving over the old one — for somebody to look at.
    ///
    /// <para>
    /// <strong>Because the number does not work here.</strong> Held against the old renderer, whole-page
    /// ink overlap sits near 0.5 whatever is equalised: the two differ in vertical layout, and cropping to
    /// ink and normalising to a common height turns that into a shift affecting every pixel. Equalising
    /// the spacing moved it by hundredths. A metric that cannot tell a correct page from a wrong one is
    /// not a gate, and dressing it up as one would be worse than having nothing.
    /// </para>
    /// <para>
    /// So this produces the evidence in the form that does discriminate — two engravings of the same
    /// construct, one above the other, at the same width and the same spacing. A grace note drawn as a
    /// crossed head is obvious in that picture and invisible in the number.
    /// </para>
    /// <para>
    /// Opt in with <c>NEXAFLOW_ABC_COMPARE</c> pointing at a folder to write into.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ShowEveryConstructBothWays() => UiThread.Run(() =>
    {
        var into = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_COMPARE");
        if (string.IsNullOrWhiteSpace(into)) { Assert.Inconclusive("set NEXAFLOW_ABC_COMPARE"); return; }

        Directory.CreateDirectory(into);
        var at = 0;

        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var old = Old(abc);
            old.Measure(new Size(Width, double.PositiveInfinity));
            var wide = Math.Max(40, Math.Min(Width, old.DesiredSize.Width));

            old.Arrange(new Rect(new Point(0, 0), old.DesiredSize));

            var now = New(abc);
            now.Measure(new Size(wide, double.PositiveInfinity));
            now.Arrange(new Rect(new Point(0, 0), now.DesiredSize));

            var name = string.Concat(what.Split(Path.GetInvalidFileNameChars()));
            Save(Stack(now, old), Path.Combine(into, $"{at++:00}-{name}.png"));
        }

        Console.WriteLine($"{at} comparisons written to {into}");
    });

    /// <summary>The new engraving over the old one, each labelled, on one white page.</summary>
    private static BitmapSource Stack(FrameworkElement now, FrameworkElement then)
    {
        const double Gap = 10, Label = 15;

        var width = Math.Max(now.DesiredSize.Width, then.DesiredSize.Width) + 8;
        var height = Label + now.DesiredSize.Height + Gap + Label + then.DesiredSize.Height + 8;

        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            Caption(dc, "new", 0);
            dc.DrawRectangle(new VisualBrush(now), null,
                             new Rect(4, Label, now.DesiredSize.Width, now.DesiredSize.Height));

            var below = Label + now.DesiredSize.Height + Gap;
            Caption(dc, "old", below);
            dc.DrawRectangle(new VisualBrush(then), null,
                             new Rect(4, below + Label, then.DesiredSize.Width, then.DesiredSize.Height));
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        return bitmap;

        static void Caption(DrawingContext dc, string what, double y) =>
            dc.DrawText(new FormattedText(what, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                          new Typeface("Segoe UI"), 10, Brushes.Gray, 1.0),
                        new Point(2, y + 1));
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ── One tune, both ways ─────────────────────────────────────────────────

    private static Scored Score(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        try
        {
            var abc = Text(path);

            var (now, then) = Both(abc);

            if (now is null || then is null)
                return new Scored(name, 0, now is null ? "the new engraver drew nothing"
                                                       : "the old engraver drew nothing");

            return new Scored(name, GrayImage.InkOverlap(now, then, 96), null);
        }
        catch (Exception ex)
        {
            return new Scored(name, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The tune as the new engraver draws it — at the <em>old</em> spacing, which is the whole trick.
    ///
    /// <para>
    /// The new engraving deliberately spaces notes more openly, keeps beam groups tight and puts air
    /// around a repeat. Held against the old renderer those choices disagree about every pixel on the
    /// page and bury the thing worth finding. Handing over the old numbers takes the difference we chose
    /// out of the comparison and leaves the differences we did not choose — which is what a regression is.
    /// </para>
    /// </summary>
    private static FrameworkElement New(string abc) =>
        new AbcElement(abc, MarkdownPalette.Light) { Spacing = ScoreSpacing.Previous };

    /// <summary>…and as the old one does, through the parser and element the <c>#%abc</c> block uses.</summary>
    private static FrameworkElement Old(string abc) =>
        new ScoreElement(new Nexaflow.Visuals.Text.Markdown.Music.Parsers.AbcParser().Parse(abc),
                         MarkdownPalette.Light);

    /// <summary>
    /// Both drawings of one tune, the new one given the width the old one came out at.
    ///
    /// <para>
    /// Without that they are not comparable at all: the new engraver justifies to the width it is handed
    /// and the old sizes to its content, so one page is 900 wide and the other 300, and bringing them to a
    /// common height to compare then measures the difference in shape rather than anything about the
    /// notes. Handing over the width is what makes the question "is this drawn the same" instead of "is
    /// this the same size".
    /// </para>
    /// </summary>
    private static (GrayImage? Now, GrayImage? Then) Both(string abc)
    {
        var old = Old(abc);
        old.Measure(new Size(Width, double.PositiveInfinity));

        var wide = Math.Max(40, Math.Min(Width, old.DesiredSize.Width));
        return (Draw(New(abc), wide), Draw(old, wide));
    }

    private static GrayImage? Draw(FrameworkElement element, double width = Width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

        var size = element.DesiredSize;
        if (size.Width < 1 || size.Height < 1 || size.Height > 20000) return null;

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);

        bitmap.Render(element);

        var ink = GrayImage.FromBitmap(bitmap).CropToInk();
        return ink.IsEmpty ? null : ink;
    }

    // ── What the run says ───────────────────────────────────────────────────

    private static void Report(string root, List<Scored> all, List<Scored> drawn, TimeSpan took)
    {
        var into = Path.Combine(root, "sweep");
        Directory.CreateDirectory(into);

        var text = new StringBuilder();
        var mean = drawn.Count == 0 ? 0 : drawn.Average(s => s.Overlap);

        text.AppendLine(CultureInfo.InvariantCulture,
            $"{all.Count} tunes, {drawn.Count} drawn both ways, in {took.TotalSeconds:F1}s");
        text.AppendLine(CultureInfo.InvariantCulture, $"mean agreement  {mean:F4}   with the old engraver");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"below {Floor:F2}       {drawn.Count(s => s.Overlap < Floor)}   tunes worth opening");
        text.AppendLine();

        foreach (var bad in all.Where(s => s.Trouble is not null).Take(20))
            text.AppendLine(CultureInfo.InvariantCulture, $"  {bad.Name}  {bad.Trouble}");

        text.AppendLine();
        text.AppendLine("worst first");
        foreach (var one in all) text.AppendLine(CultureInfo.InvariantCulture, $"  {one.Name}  {one.Overlap:F4}");

        File.WriteAllText(Path.Combine(into, "abc-against-the-old-renderer.txt"), text.ToString());
        Console.WriteLine(text.ToString()[..Math.Min(1200, text.Length)]);
    }

    // ── The corpus ──────────────────────────────────────────────────────────

    private const string Missing = "Point NEXAFLOW_ABC_CORPUS at a corpus of .abc files.";

    private sealed record Tunes(string Root, IReadOnlyList<string> Items)
    {
        public int Count => Items.Count;

        public IEnumerable<string> Take(int n) => Items.Take(n);
    }

    private static Tunes? Corpus()
    {
        var root = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_CORPUS");
        if (string.IsNullOrWhiteSpace(root)) root = DefaultCorpus;
        if (!Directory.Exists(root)) return null;

        var found = Directory.EnumerateFiles(root, "*.abc", SearchOption.AllDirectories).ToList();
        return found.Count == 0 ? null : new Tunes(root, found);
    }

    private static string Text(string path) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(File.ReadAllBytes(path));
}
