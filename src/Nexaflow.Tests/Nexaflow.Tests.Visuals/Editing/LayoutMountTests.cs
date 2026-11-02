using System.Linq;

using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// A laid tree put down inside another (<see cref="LayoutMount"/>) is walked, placed and asked exactly as though its pieces had been laid
/// where it is put down — the mount and the mounted tree's top being no pieces of their own — and every piece names its part where what
/// it was laid from now stands.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("layout-frames")]
public class LayoutMountTests
{
    /// <summary>Three lines, read as three pieces of one reading.</summary>
    private static readonly ContentNode Lines = ContentNode.Branch("lines",
        [ContentNode.Leaf("line", "one\n", offset: 0), ContentNode.Leaf("line", "two\n", offset: 4), ContentNode.Leaf("line", "three\n", offset: 8)],
        offset: 0);

    private static readonly LayoutPoint Down = new(5, 20);

    [TestMethod]
    public void AMountedTree_IsWalkedPlacedAndNamedAsThoughLaidWhereItIsPutDown() => UiThread.Run(() =>
    {
        var was = ContentPart.Of(Lines);
        var unit = Laid("unit", was, default);

        var mounted = Page(build => build.Mount(new LayoutMount(unit, was, 10), Down));
        var laidThere = Page(build => Blocks(build, ContentPart.Of(Lines, 10), Down));

        Alike(laidThere, mounted);
    });

    [TestMethod]
    public void ATreeMountedInsideAMountedTree_IsWalkedAsThoughLaidWhereBothArePutDown() => UiThread.Run(() =>
    {
        var was = ContentPart.Of(Lines);

        // The lines put down in a unit standing three lines along, which is put down ten characters further on again.
        var inner = Laid("inner", was, default);
        var outerBuild = new LayoutBuilder(WpfMedium.OnThisThread);
        outerBuild.Open("unit", stops: LayoutStops.None);
        outerBuild.Mount(new LayoutMount(inner, was, 14), new LayoutPoint(0, 60));
        outerBuild.Close();
        var outer = outerBuild.Seal();

        var mounted = Page(build => build.Mount(new LayoutMount(outer, ContentPart.Of(ContentNode.Branch("lines", [], offset: 0)), 10), Down));
        var laidThere = Page(build => Blocks(build, ContentPart.Of(Lines, 24), new LayoutPoint(Down.X, Down.Y + 60)));

        Alike(laidThere, mounted);
    });

    [TestMethod]
    public void AMountIsOnePieceInTheTreeHoldingIt_HoweverMuchItHolds() => UiThread.Run(() =>
    {
        var was = ContentPart.Of(Lines);
        var unit = Laid("unit", was, default);
        var page = Page(build => build.Mount(new LayoutMount(unit, was, 10), Down));

        Assert.AreEqual(2, page.Tree!.Count, "the page and its mount");
        Assert.AreEqual(3, page.Children.Count, "and walked, the lines the mount holds");
        Assert.IsTrue(page.Children.All(line => line.Parent == page), "each held by the page");
    });

    [TestMethod]
    public void EveryQuestionOfTheLayout_IsAnsweredThroughAMountAsThoughItsPiecesWereLaidThere() => UiThread.Run(() =>
    {
        var was = ContentPart.Of(Lines);
        var unit = Laid("unit", was, default);

        var mounted = Page(build => build.Mount(new LayoutMount(unit, was, 10), Down));
        var laidThere = Page(build => Blocks(build, ContentPart.Of(Lines, 10), Down));

        // A press anywhere — on a line, between two, past them all.
        for (var y = 0.0; y < 90; y += 3)
            for (var x = 0.0; x < 140; x += 7)
            {
                var at = new LayoutPoint(x, y);
                Assert.AreEqual(Said(laidThere.PieceAt(at)), Said(mounted.PieceAt(at)), $"what a press at {at} lands on");
                Assert.AreEqual(laidThere.OffsetAt(at), mounted.OffsetAt(at), $"what a press at {at} means");
            }

        // Every place a caret stands, and every stretch of the source.
        CollectionAssert.AreEqual(laidThere.Tree!.Places.Select(place => (place.Offset, place.Trailing)).ToList(),
                                  mounted.Tree!.Places.Select(place => (place.Offset, place.Trailing)).ToList(), "where a caret stands");

        for (var offset = 0; offset <= 26; offset++)
        {
            Assert.AreEqual(laidThere.StopAt(offset), mounted.StopAt(offset), $"the stop at {offset}");
            Assert.AreEqual(laidThere.CaretRect(offset), mounted.CaretRect(offset), $"the caret at {offset}");

            for (var length = 0; offset + length <= 26; length += 3)
                CollectionAssert.AreEqual(laidThere.RangeRects(offset, length).ToList(), mounted.RangeRects(offset, length).ToList(),
                                          $"what {offset}+{length} covers");
        }
    });

    [TestMethod]
    public void AMountedTree_IsPaintedWhereItIsPutDown_ItsTopAsItWasLaid() => UiThread.Run(() =>
    {
        var was = ContentPart.Of(Lines);

        // The lines in a tree whose top keeps its picture once painted — as a line of code is laid — put down inside a unit put down in turn.
        var inner = Laid("inner", was, default, new LayoutPaint(Kept: new LayoutKept()));
        var outerBuild = new LayoutBuilder(WpfMedium.OnThisThread);
        outerBuild.Open("unit", stops: LayoutStops.None);
        outerBuild.Mount(new LayoutMount(inner, was, 14), new LayoutPoint(0, 60));
        outerBuild.Close();
        var outer = outerBuild.Seal();

        var mounted = Page(build => build.Mount(new LayoutMount(outer, ContentPart.Of(ContentNode.Branch("lines", [], offset: 0)), 10), Down));
        var laidThere = Page(build => Blocks(build, ContentPart.Of(Lines, 24), new LayoutPoint(Down.X, Down.Y + 60)));

        CollectionAssert.AreEqual(Pixels(laidThere), Pixels(mounted), "what is painted, and where");

        // And a piece painted on its own, where it sits on the page.
        foreach (var (want, got) in laidThere.Children.Zip(mounted.Children))
            Assert.AreEqual(PaintedOne(want), PaintedOne(got), $"{want.Kind} {want.Part?.Start} painted on its own");
    });

    /// <summary>The page painted black on white, pixel by pixel.</summary>
    private static int[] Pixels(LayoutPiece page)
    {
        var picture = WpfPainter.Painted(page, new Rect(0, 0, 160, 160), 1, Brushes.Black, Brushes.White)!;
        var pixels = new int[picture.PixelWidth * picture.PixelHeight];
        picture.CopyPixels(pixels, picture.PixelWidth * 4, 0);

        return pixels;
    }

    /// <summary>Where <paramref name="piece"/> is painted when it is painted on its own.</summary>
    private static Rect PaintedOne(LayoutPiece piece)
    {
        var drawn = new DrawingGroup();
        using (var dc = drawn.Open()) WpfPainter.PaintOne(dc, piece, Brushes.Black);

        return drawn.Bounds;
    }

    private static string Said(LayoutPiece piece) => piece.Exists ? $"{piece.Kind} {piece.Part?.Start}+{piece.Part?.Length} {piece.Bounds}" : "nothing";

    /// <summary>A tree whose top is called <paramref name="kind"/>, painted as <paramref name="paints"/> says, holding a piece for each of <paramref name="reading"/>'s parts.</summary>
    private static LayoutTree Laid(string kind, ContentPart reading, LayoutPoint at, LayoutPaint? paints = null)
    {
        var build = new LayoutBuilder(WpfMedium.OnThisThread);
        build.Open(kind, stops: LayoutStops.None, paints: paints);
        Blocks(build, reading, at);
        build.Close();

        return build.Seal();
    }

    /// <summary>A piece for each of <paramref name="reading"/>'s parts, one under another from <paramref name="at"/>, each with a mark in it.</summary>
    private static void Blocks(LayoutBuilder build, ContentPart reading, LayoutPoint at)
    {
        var y = at.Y;

        foreach (var part in reading.Children)
        {
            build.Open("line", part, new LayoutPoint(at.X, y));
            build.Draw(new RuleMark(new LayoutRect(0, 0, 40 + (10 * part.Length), 14), null));
            build.Close();

            y += 16;
        }
    }

    /// <summary>A page holding what <paramref name="put"/> puts in it.</summary>
    private static LayoutPiece Page(Action<LayoutBuilder> put)
    {
        var build = new LayoutBuilder(WpfMedium.OnThisThread);
        build.Open("page", stops: LayoutStops.None);
        put(build);
        build.Close();

        return build.Seal().Root;
    }

    /// <summary>Two pages that agree about every piece: what it is, where it sits, what it names, how deep it is and what holds it.</summary>
    private static void Alike(LayoutPiece want, LayoutPiece got)
    {
        var wanted = want.SelfAndDescendants().ToList();
        var found = got.SelfAndDescendants().ToList();
        CollectionAssert.AreEqual(wanted.Select(piece => piece.Kind).ToList(), found.Select(piece => piece.Kind).ToList(), "the pieces walked");

        for (var at = 0; at < wanted.Count; at++)
        {
            var (a, b) = (wanted[at], found[at]);

            Assert.AreEqual(a.Bounds, b.Bounds, $"where piece {at} ({a.Kind}) sits");
            Assert.AreEqual((a.Part?.Start, a.Part?.Length), (b.Part?.Start, b.Part?.Length), $"what piece {at} ({a.Kind}) names");
            Assert.AreEqual(a.Depth, b.Depth, $"how deep piece {at} ({a.Kind}) is");
            Assert.AreEqual(a.Parent.Kind, b.Parent.Kind, $"what holds piece {at} ({a.Kind})");
            Assert.AreEqual(((ContentPart?)a.Part)?.Node, ((ContentPart?)b.Part)?.Node, $"piece {at} ({a.Kind}) names the same part");
        }

        CollectionAssert.AreEqual(want.Placed().Select(placed => placed.Where).ToList(), got.Placed().Select(placed => placed.Where).ToList(),
                                  "placed as a walk carries the frame down");
    }
}
