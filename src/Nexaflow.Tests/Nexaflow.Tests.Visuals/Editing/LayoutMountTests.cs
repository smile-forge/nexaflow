using System.Linq;

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

    /// <summary>A tree whose top is called <paramref name="kind"/> holding a piece for each of <paramref name="reading"/>'s parts.</summary>
    private static LayoutTree Laid(string kind, ContentPart reading, LayoutPoint at)
    {
        var build = new LayoutBuilder(WpfMedium.OnThisThread);
        build.Open(kind, stops: LayoutStops.None);
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
            build.Covers(new LayoutRect(0, 0, 40 + (10 * part.Length), 14));
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
