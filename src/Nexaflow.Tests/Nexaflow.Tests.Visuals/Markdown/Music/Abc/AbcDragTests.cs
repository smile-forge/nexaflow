using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// Dragging across a real tune, every pair of pieces, looking for the one that throws.
///
/// <para>
/// A selection is driven by a pointer, so it is asked about pairs nobody would choose deliberately: a
/// note and a chord three systems away, a grace note and a syllable, the same piece twice. Every one of
/// those has to come back with an answer. The suite had tests for the pairs that mean something and none
/// for the pairs that merely happen, which is why a drag across this tune took the page down.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcDragTests
{
    /// <summary>
    /// The tune the trouble was found on: grace notes, chord symbols, repeats, and a line continuation.
    /// </summary>
    private const string AuldGreyCat =
        "X: 1\nT: the Auld Grey Cat\nM: C|\nL: 1/8\nK: EDorian\n"
        + "z2 |\\\n"
        + "\"Em\"{^d}e2e2 E3F | GFGA BABc | \"D\"{c}d2d2 D3E | FAdB AFED |\n"
        + "\"Em\"{^d}e2e2 E3F | GFGA BABc | \"D\"dcBA \"B7\"BAGF | \"Em\"E4 e2z2 :|\n";

    [TestMethod]
    public void EveryDragAcrossItComesBackWithAnAnswer() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(AuldGreyCat, 900, Brushes.Black, 1.0);
        var pieces = layout.Root.SelfAndDescendants().Where(n => n.IsInk).ToList();

        Assert.IsTrue(pieces.Count > 20, $"only {pieces.Count} pieces — the tune did not engrave");

        var trouble = new List<string>();

        foreach (var from in pieces)
            foreach (var to in pieces)
            {
                try
                {
                    var chosen = ContentSelection.Between(layout.Root, from, to);

                    foreach (var (start, length) in chosen.Ranges)
                        if (start < 0 || length < 0 || start + length > layout.Abc.Length)
                            trouble.Add($"{Kind(from)}→{Kind(to)}: range {start}+{length} is outside the tune");
                }
                catch (Exception ex)
                {
                    trouble.Add($"{Kind(from)}→{Kind(to)}: {ex.GetType().Name}: {ex.Message}");
                }

                if (trouble.Count > 4) break;
            }

        Assert.AreEqual(0, trouble.Count, string.Join("\n", trouble.Take(5)));
    });

    [TestMethod]
    public void AndTheSelectionItLeavesCanBePainted() => UiThread.Run(() =>
    {
        // The drag is only half of it: what took the page down is drawing what the drag chose. An
        // exception out of OnRender stops WPF drawing the element ever again, so a selection that cannot
        // be painted is worse than one that is wrong.
        var element = new AbcElement(AuldGreyCat, MarkdownPalette.Dark);
        element.Measure(new System.Windows.Size(900, double.PositiveInfinity));
        element.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0), element.DesiredSize));

        var pieces = element.Layout!.Root.SelfAndDescendants().Where(n => n.IsInk).ToList();

        for (var at = 0; at < pieces.Count; at += 3)
        {
            element.BeginPointerSelect(Middle(pieces[at]));
            element.ExtendPointerSelect(Middle(pieces[Math.Min(at + 7, pieces.Count - 1)]));
            element.EndPointerSelect();

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                Math.Max(1, (int)element.DesiredSize.Width), Math.Max(1, (int)element.DesiredSize.Height),
                96, 96, PixelFormats.Pbgra32);

            bitmap.Render(element);   // throws here if the wash cannot be drawn
        }
    });

    [TestMethod]
    public void NothingThatDrewNothingIsLeftAsSomethingToPointAt() => UiThread.Run(() =>
    {
        // The invariant the crash came from, asserted where it belongs. Ink is a promise that a reader can
        // point at the thing; an empty rectangle cannot be pointed at, hit-tested, washed or stood beside,
        // and every query that trusted the promise had to survive one that could not keep it. One of them
        // did not — inflating an empty rectangle throws, inside OnRender, which stops the element being
        // drawn at all.
        foreach (var (what, abc) in AbcConstructs.Everything.Concat([("the Auld Grey Cat", AuldGreyCat)]))
        {
            var layout = AbcLayout.Build(abc, 700, Brushes.Black, 1.0);

            foreach (var node in layout.Root.SelfAndDescendants().Where(n => n.IsInk))
                Assert.IsTrue(!node.Bounds.IsEmpty && node.Bounds.Width > 0 && node.Bounds.Height > 0,
                              $"{what}: a {Kind(node)} is ink and drew nothing");
        }
    });

    [TestMethod]
    public void ZoomTheGraces() => UiThread.Run(() =>
    {
        var into = Environment.GetEnvironmentVariable("NEXAFLOW_ABC_COMPARE");
        if (string.IsNullOrWhiteSpace(into)) { Assert.Inconclusive("set NEXAFLOW_ABC_COMPARE"); return; }

        const string One = "X:1\nL:1/8\nK:G\n{g}A {/g}B {^d}c {gAG}d |\n";

        var element = new AbcElement(One, MarkdownPalette.Light) { Zoom = 4.0 };
        element.Measure(new System.Windows.Size(1600, double.PositiveInfinity));
        element.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0), element.DesiredSize));

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)element.DesiredSize.Width, (int)element.DesiredSize.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(into, "zoom-grace.png"));
        encoder.Save(stream);
    });

    [TestMethod]
    public void AScoreFillsThePageItIsGivenAndSitsInTheMiddleOfIt() => UiThread.Run(() =>
    {
        // What a window hands a block, and what the block does with it. The music is set into a share of
        // the width and centred in the rest — so what it must never do is take a width of its own choosing.
        const double Given = 1284;

        var score = new AbcScore(AuldGreyCat, MarkdownPalette.Light, 0);
        score.Measure(new System.Windows.Size(Given, double.PositiveInfinity));
        score.Arrange(new System.Windows.Rect(new System.Windows.Point(0, 0), score.DesiredSize));
        score.UpdateLayout();

        Assert.AreEqual(Given, score.DesiredSize.Width, 1,
                        "the block is the page: it takes the whole width and puts the margins inside");

        var music = score.Score.Layout!.Size.Width * 1.0;
        var wanted = Given * score.PageWidth;

        Assert.IsTrue(music > wanted * 0.9,
                      $"the music engraved to {music:F0} of the {wanted:F0} it was given — it is not filling the page");
    });

    [TestMethod]
    public void AndEveryLineOfAShortTuneComesOutTheSameLength() => UiThread.Run(() =>
    {
        // Five one-bar lines, each ended by the writer rather than by the width. None of them fills the
        // page and none of them should — but they have to agree with each other, or the block reads as
        // ragged rather than as short. The width is chosen once for the block; this is what says every
        // line actually reached it.
        const string Meters =
            "X:1\nM:4/4\nK:C\nA4|\nM:C\nA4|\nM:C|\nA4|\nM:6/8\nA3A3|\nM:none\nA4|\n";

        var layout = AbcLayout.Build(Meters, 900, Brushes.Black, 1.0);

        var ends = layout.Root.SelfAndDescendants()
            .Where(n => Kind(n) == "system")
            .Select(n => n.Bounds.Right)
            .ToList();

        Assert.AreEqual(5, ends.Count, "five lines, one per meter");

        // To the pixel, not to the last decimal: the ends are the sum of a chain of doubles, so they agree
        // to a rounding error and asserting exact equality would be asserting the arithmetic rather than
        // the engraving.
        Assert.IsTrue(ends.Max() - ends.Min() <= 1.5,
                      $"the lines came out different lengths: {string.Join(", ", ends.Select(e => e.ToString("F0")))}");
    });

    private static System.Windows.Point Middle(Piece node) =>
        new(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));

    private static string Kind(Piece node) => node.Exists ? node.Kind : "?";
}
