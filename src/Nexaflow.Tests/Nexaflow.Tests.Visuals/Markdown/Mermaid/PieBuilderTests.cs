using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>pie</c> block drawn on the shared layout tree: a wedge per slice standing for the line it was written on, its
/// share written on it, and a legend whose value is the number itself.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("pie")]
public class PieBuilderTests
{
    private const string Pets = "pie showData\n  title Pets\n  \"Dogs\" : 30\n  \"Cats\" : 10";

    private static Laid Build(string source, double room = 700) =>
        PieBuilder.Build(source, MarkdownPalette.Dark, 1.0, room);

    private static IEnumerable<Piece> Pieces(Laid laid, string kind) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind);

    private static string Written(string source, ISourcePart? part) =>
        part is null ? "" : source.Substring(part.Start, part.Length);

    private static Brush? Fill(Piece piece)
    {
        foreach (var mark in piece.Marks)
            if (mark is GeometryMark { Fill: { } fill }) return fill;

        return null;
    }

    [TestMethod]
    public void EverySliceIsAWedgeStandingForTheLineItWasWrittenOn() => UiThread.Run(() =>
    {
        var laid = Build(Pets);
        var wedges = Pieces(laid, PiePiece.Wedge).ToList();

        Assert.AreEqual(2, wedges.Count);
        CollectionAssert.AreEqual(new[] { "\"Dogs\" : 30", "\"Cats\" : 10" },
                                  wedges.Select(wedge => Written(Pets, wedge.Part)).ToArray());

        Assert.IsTrue(wedges.All(wedge => wedge.Region is not null), "a wedge stands in its own shape, not its box");
    });

    [TestMethod]
    public void APressOnAWedgeMeansThatSliceAndNotItsNeighbour() => UiThread.Run(() =>
    {
        var laid = Build(Pets);
        var wedges = Pieces(laid, PiePiece.Wedge).ToList();

        var shared = 0;

        foreach (var wedge in wedges)
        {
            var others = wedges.Where(other => other.At != wedge.At).ToList();

            foreach (var point in Points(wedge.Bounds))
            {
                if (!wedge.Region!.FillContains(point - wedge.Anchor)) continue;

                Assert.AreEqual(wedge.Sits().Start, laid.PieceAt(point).Selectable().Sits().Start,
                                $"a press at {point} in {Written(Pets, wedge.Part)} came back as something else");

                // The whole point of a wedge standing in its shape: the boxes overlap, so a box would answer this
                // press with whichever slice happened to be asked first.
                if (others.Any(other => other.Bounds.Contains(point))) shared++;
            }
        }

        Assert.IsTrue(shared > 0, "the wedges' boxes overlap, or this proves nothing");
    });

    /// <summary>Points across a rectangle, a few pixels apart — somewhere to press.</summary>
    private static IEnumerable<Point> Points(Rect where)
    {
        for (var x = where.X + 4; x < where.Right; x += 9)
            for (var y = where.Y + 4; y < where.Bottom; y += 9)
                yield return new Point(x, y);
    }

    [TestMethod]
    public void AWedgeItsShareAndItsLegendRowAllStandForTheOneSlice() => UiThread.Run(() =>
    {
        var laid = Build(Pets);
        var dogs = Pets.IndexOf("\"Dogs\"", System.StringComparison.Ordinal);

        // Choosing the slice covers everything drawn for it, wherever it was drawn.
        var washed = laid.Root.RangeRects(dogs, "\"Dogs\" : 30".Length).Count
                     + laid.Root.RangeRegions(dogs, "\"Dogs\" : 30".Length).Count;

        Assert.IsTrue(washed >= 3, $"the wedge, its share and its legend row, but {washed} were washed");

        foreach (var kind in new[] { PiePiece.Wedge, PiePiece.Share, PiePiece.Row })
            Assert.IsTrue(Pieces(laid, kind).Any(piece => piece.Sits().Start == dogs), $"no {kind} stands for the slice");
    });

    [TestMethod]
    public void TheValueInTheLegendIsTheNumberItself() => UiThread.Run(() =>
    {
        var laid = Build(Pets);
        var values = Pieces(laid, PiePiece.Value).ToList();

        CollectionAssert.AreEqual(new[] { "30", "10" }, values.Select(value => Written(Pets, value.Part)).ToArray());
        Assert.IsTrue(values.All(value => value.Words is { Maps: true }), "so the caret can stand between its digits");

        Assert.IsTrue(Pieces(laid, PiePiece.Share).All(share => share.Words is { Maps: false }),
                      "where a share is worked out, and has nowhere to put one");
    });

    [TestMethod]
    public void WithoutShowDataTheLegendSaysOnlyWhatEachSliceIsAndItsShare() => UiThread.Run(() =>
    {
        var laid = Build("pie\n  \"Dogs\" : 30\n  \"Cats\" : 10");

        Assert.AreEqual(0, Pieces(laid, PiePiece.Value).Count());
        Assert.AreEqual(2, Pieces(laid, PiePiece.Label).Count());
    });

    [TestMethod]
    public void TheFrontMattersColoursAreWhatTheSlicesAreDrawnIn() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  themeVariables:\n    pie1: \"#ff0000\"\n---\npie\n  \"Dogs\" : 30\n  \"Cats\" : 10");
        var wedges = Pieces(laid, PiePiece.Wedge).ToList();

        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), ((SolidColorBrush)Fill(wedges[0])!).Color);
        Assert.AreNotEqual(Color.FromRgb(0xFF, 0, 0), ((SolidColorBrush)Fill(wedges[1])!).Color,
                           "the second takes the theme's, since nothing was written for it");
    });

    [TestMethod]
    public void ASliceTheConfigPicksOutIsPulledOutOfTheChart() => UiThread.Run(() =>
    {
        var plain = Build(Pets);
        var picked = Build("---\nconfig:\n  pie:\n    highlightSlice: Dogs\n---\n" + Pets);

        var before = Pieces(plain, PiePiece.Wedge).First().Bounds;
        var after = Pieces(picked, PiePiece.Wedge).First().Bounds;

        // Dogs is three quarters of the chart from twelve o'clock, so the middle of it points down and to the right.
        Assert.IsTrue(after.X > before.X && after.Y > before.Y, "out along its own middle");
    });

    [TestMethod]
    public void TheDonutHoleIsNoPartOfAnySlice() => UiThread.Run(() =>
    {
        var laid = Build("---\nconfig:\n  pie:\n    donutHole: 0.5\n---\n" + Pets);
        var wedges = Pieces(laid, PiePiece.Wedge).ToList();

        // The middle of the chart is the middle of everything the wedges cover together.
        var across = wedges.Select(wedge => wedge.Bounds).Aggregate(Rect.Union);
        var middle = new Point(across.X + (across.Width / 2), across.Y + (across.Height / 2));

        Assert.IsFalse(wedges.Any(wedge => wedge.Region!.FillContains(middle - wedge.Anchor)),
                       "the hole belongs to none of them");
    });

    [TestMethod]
    public void TheLegendGoesWhereTheFrontMatterAsks() => UiThread.Run(() =>
    {
        var right = Build(Pets);
        var left = Build("---\nconfig:\n  pie:\n    legendPosition: left\n---\n" + Pets);

        Assert.IsTrue(Pieces(right, PiePiece.Legend).Single().Bounds.X > Pieces(right, PiePiece.Wedge).First().Bounds.X);
        Assert.IsTrue(Pieces(left, PiePiece.Legend).Single().Bounds.X < Pieces(left, PiePiece.Wedge).First().Bounds.X);
    });

    [TestMethod]
    public void InARoomTooNarrowForBothTheLegendGoesUnderTheChart() => UiThread.Run(() =>
    {
        var wide = Build(Pets, room: 700);
        var narrow = Build(Pets, room: 260);

        Assert.IsTrue(Pieces(wide, PiePiece.Legend).Single().Bounds.Left > Pieces(wide, PiePiece.Wedge).First().Bounds.Right,
                      "beside the chart, where there is room for it");

        Assert.IsTrue(Pieces(narrow, PiePiece.Legend).Single().Bounds.Top >= Pieces(narrow, PiePiece.Wedge).First().Bounds.Bottom - 1,
                      "and under it where there is not");

        Assert.IsTrue(narrow.Size.Width <= 260, $"the chart is fitted into the room it was given, and took {narrow.Size.Width}");
    });

    [TestMethod]
    public void APieOfNothingWorthDrawingIsShownAsItWasWritten() => UiThread.Run(() =>
    {
        const string source = "pie\n  \"Dogs\" : lots";
        var laid = Build(source);

        Assert.IsTrue(Pieces(laid, LayoutText.SourceKind).Any(), "the lines are on the page");
        Assert.AreEqual(0, Pieces(laid, PiePiece.Wedge).Count());
        StringAssert.Contains(laid.Trouble.Single().Message, "not a number");
    });

    [TestMethod]
    public void ItDispatchesThroughTheDiagramRenderer() => UiThread.Run(() =>
    {
        var content = (ContentElement)DiagramRenderer.Render("mermaid", Pets, MarkdownPalette.Dark);

        content.Measure(new Size(700, double.PositiveInfinity));
        Assert.IsTrue(content.DesiredSize.Width > 0 && content.DesiredSize.Height > 0);
        Assert.AreEqual(0, content.Diagnostics.Count);
    });

    [TestMethod]
    public void TheTitleIsSetOverTheChart() => UiThread.Run(() =>
    {
        var laid = Build(Pets);
        var title = Pieces(laid, MermaidPiece.Title).Single();

        Assert.AreEqual("Pets", Written(Pets, title.Part));
        Assert.IsTrue(title.Bounds.Bottom <= Pieces(laid, PiePiece.Wedge).First().Bounds.Top + 0.001);
    });
}
