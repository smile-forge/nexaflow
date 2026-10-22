using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Coverage for <see cref="LayoutIndex"/> and the questions <see cref="LayoutQuery"/> asks through it: that passing over what
/// cannot answer leaves out nothing asking every piece would have found, and finds it in the same order — so a long document
/// answers a press, a stretch of source and the nearest thing written as a short one does.
///
/// <para>
/// Built by hand, as <see cref="LayoutQueryTests"/> are: a document of blocks one under another — enough of them to be
/// gathered into runs — each a row of letters with a script hanging below the box the block reserves, a rule nobody wrote and
/// an empty place waiting to be written in. Every question is asked all over it, and each answer checked against every piece
/// asked in turn.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-long-reading")]
public class LayoutIndexTests
{
    private const int Blocks = 150;
    private const double BlockHeight = 40;
    private const int BlockLength = 20;

    /// <summary>
    /// <see cref="Blocks"/> blocks one under another. Each reserves 30 of its 40 across: a row of two letters, with a script
    /// set below the row's box and reaching past the block's, a rule under that drawn from nothing anybody wrote, and an empty
    /// place to write in.
    /// </summary>
    private static Piece Document()
    {
        var build = new LayoutBuilder();
        build.At(new Rect(0, 0, 600, Blocks * BlockHeight), new TestPart(0, Blocks * BlockLength), "document");

        for (var block = 0; block < Blocks; block++)
        {
            var (top, start) = (block * BlockHeight, block * BlockLength);

            build.At(new Rect(0, top, 600, 30), new TestPart(start, BlockLength), "block");

            build.At(new Rect(10, top + 4, 200, 16), new TestPart(start, 12), "row");
            build.Leaf(new Rect(10, top + 4, 12, 16), new TestPart(start, 1), "char");
            build.Leaf(new Rect(30 + (block % 7), top + 4, 12, 16), new TestPart(start + 2, 1), "char");
            build.Leaf(new Rect(50, top + 22, 8, 10), new TestPart(start + 4, 3), "script");
            build.Close();

            build.Leaf(new Rect(10, top + 34, 300, 1), null, "rule");
            build.Leaf(new Rect(400, top + 6, 10, 12), new TestPart(start + 15, 0), "hole");
            build.Close();
        }

        build.Close();
        return build.Seal().Root;
    }

    /// <summary>Points all over the document and round it: on ink, between blocks, past either side, above and below it.</summary>
    private static IEnumerable<Point> Points()
    {
        for (var y = -30.0; y < (Blocks * BlockHeight) + 30; y += 9)
            foreach (var x in new[] { -20.0, 5, 15, 33, 54, 200, 405, 700 })
                yield return new Point(x, y);
    }

    /// <summary>Stretches of source all over the document: single places, a few characters, and runs across blocks.</summary>
    private static IEnumerable<(int Start, int End)> Stretches()
    {
        for (var start = -3; start < (Blocks * BlockLength) + 3; start += 3)
            foreach (var length in new[] { 0, 1, 4, 37, 290 })
                yield return (start, start + length);
    }

    /// <summary>Everything a piece and all it holds take up on the page, every one of them asked.</summary>
    private static Rect Reaching(Piece piece)
    {
        var reach = Rect.Empty;
        foreach (var inside in piece.SelfAndDescendants()) reach.Union(inside.Bounds);

        return reach;
    }

    /// <summary>The stretch of source a piece and all it holds sit in, every one of them asked.</summary>
    private static (int First, int Last) Sitting(Piece piece) =>
        (piece.SelfAndDescendants().Min(inside => inside.Sits().Start), piece.SelfAndDescendants().Max(inside => inside.Sits().End));

    [TestMethod]
    public void EveryPieceReachesAllItHolds_OnThePageAndInTheSource()
    {
        var root = Document();
        var index = root.Tree!.Indexed;

        foreach (var piece in root.SelfAndDescendants())
        {
            Assert.AreEqual(piece.Bounds, index.Where(piece.At), $"{piece} sits where it is drawn");
            Assert.AreEqual(Reaching(piece), index.Reach(piece.At), $"{piece} reaches everything it holds, a script below its box too");
            Assert.AreEqual(Sitting(piece), index.Span(piece.At), $"{piece} sits in the source all it holds sits in");
        }
    }

    [TestMethod]
    public void APointIsAskedOfWhatReachesIt_AndOfEverythingThatDoes_InOrder()
    {
        var root = Document();

        var reaching = root.SelfAndDescendants().Select(piece => (piece.At, Reach: Reaching(piece))).ToList();

        foreach (var point in Points())
        {
            var asked = LayoutQuery.PlacedNear(root, reach => reach.Contains(point)).Select(placed => placed.Piece.At).ToList();
            var every = reaching.Where(piece => piece.Reach.Contains(point)).Select(piece => piece.At).ToList();

            CollectionAssert.AreEqual(every, asked, $"at {point}");
        }
    }

    [TestMethod]
    public void AStretchOfSourceIsAskedOfWhatSitsInIt_AndOfEverythingThatDoes_InOrder()
    {
        var root = Document();

        var sitting = root.SelfAndDescendants().Select(piece => (piece.At, Span: Sitting(piece))).ToList();

        foreach (var (start, end) in Stretches())
        {
            var asked = LayoutQuery.SittingIn(root, start, end).Select(piece => piece.At).ToList();
            var every = sitting.Where(piece => piece.Span.First <= end && piece.Span.Last >= start).Select(piece => piece.At).ToList();

            CollectionAssert.AreEqual(every, asked, $"over [{start}, {end}]");
        }
    }

    [TestMethod]
    public void AQuestionOfWhatABlockHoldsPassesOverRunsOfBlocks_AndMissesNoneThatAnswer()
    {
        var root = Document();

        var blocks = root.Children.Select(block => (Block: block, Reach: Reaching(block))).ToList();

        foreach (var point in Points())
        {
            var asked = LayoutQuery.ChildrenWhere(root, (reach, _) => reach.Contains(point)).ToList();
            var every = blocks.Where(block => block.Reach.Contains(point)).Select(block => block.Block).ToList();

            CollectionAssert.AreEqual(every, asked, $"at {point}");
        }
    }

    [TestMethod]
    public void OpeningEveryRunAndEveryPieceComesToEachPieceOnce()
    {
        var root = Document();
        var index = root.Tree!.Indexed;

        var reached = new List<int>();
        var open = new Stack<int>();
        open.Push(root.At);

        while (open.TryPop(out var item))
        {
            if (item >= 0) reached.Add(item);
            foreach (var (inside, _, _) in index.Opened(item)) open.Push(inside);
        }

        reached.Sort();
        CollectionAssert.AreEqual(root.SelfAndDescendants().Select(piece => piece.At).ToList(), reached);
    }

    [TestMethod]
    public void APressMeansWhatAskingEveryPieceWouldSay()
    {
        var root = Document();

        foreach (var point in Points())
            Assert.AreEqual(PressedByAll(root, point), root.PieceAt(point), $"a press at {point}");
    }

    /// <summary>
    /// What a press means, asked of every leaf in drawing order by <see cref="LayoutQuery"/>'s own rules, for a tree with no
    /// shapes and no clips: the deepest ink under the point — itself where it stands for a place, an empty one too, and
    /// otherwise the nearest thing holding it that names some source — and where none is, what the nearest ink means: the
    /// deeper, and then the first drawn, among the equally near.
    /// </summary>
    private static Piece PressedByAll(Piece root, Point point)
    {
        static Piece Named(Piece leaf) => leaf.Ancestors().FirstOrDefault(up => up.Part is { Length: > 0 });

        var under = root.Leaves()
            .Select(leaf => (Leaf: leaf, Means: leaf.Stands() ? leaf : Named(leaf), Where: leaf.Bounds))
            .Where(leaf => leaf.Means.Exists && leaf.Where.Width > 0 && leaf.Where.Height > 0
                           && point.X >= leaf.Where.X - 0.5 && point.X <= leaf.Where.Right + 0.5
                           && point.Y >= leaf.Where.Y - 0.5 && point.Y <= leaf.Where.Bottom + 0.5)
            .Select(leaf => (leaf.Means, Rank: (Named: leaf.Leaf.Stands() ? 1 : 0, leaf.Leaf.Depth)))
            .ToList();

        if (under.Count > 0) return under.First(leaf => leaf.Rank == under.Max(other => other.Rank)).Means;

        var leaves = root.Leaves()
            .Select(leaf => (Leaf: leaf, Means: leaf.Selectable(), Where: leaf.Bounds))
            .Where(leaf => leaf.Means.Exists)
            .ToList();

        double Distance(Rect where)
        {
            var dx = Math.Max(Math.Max(where.X - point.X, point.X - where.Right), 0);
            var dy = Math.Max(Math.Max(where.Y - point.Y, point.Y - where.Bottom), 0);
            return (dx * dx) + (dy * dy);
        }

        var nearest = leaves.Min(leaf => Distance(leaf.Where));
        var tied = leaves.Where(leaf => Distance(leaf.Where) == nearest).ToList();

        return tied.First(leaf => leaf.Leaf.Depth == tied.Max(other => other.Leaf.Depth)).Means;
    }

    [TestMethod]
    public void BetweenTwoThingsAsNearAsEachOther_ThePressMeansTheOneDrawnFirst()
    {
        var build = new LayoutBuilder();
        build.At(new Rect(0, 0, 100, 100), new TestPart(0, 2), "page");
        build.Leaf(new Rect(0, 0, 10, 10), new TestPart(0, 1), "char");
        build.Leaf(new Rect(30, 0, 10, 10), new TestPart(1, 1), "char");
        build.Close();

        var root = build.Seal().Root;

        Assert.AreEqual(0, root.PieceAt(new Point(20, 5)).Sits().Start, "as near the second as the first, so the first");
    }

    [TestMethod]
    public void WhereNoPlaceIsAtAnOffset_TheStepsEitherSideAreTheOnesEveryPlaceAskedWouldGive()
    {
        var root = Document();
        var places = root.Tree!.Places;

        for (var offset = -2; offset < (Blocks * BlockLength) + 2; offset++)
        {
            var first = Enumerable.Range(0, places.Count).Where(at => places[at].Offset == offset).DefaultIfEmpty(-1);

            Assert.AreEqual(first.First(), root.StopAt(offset), $"the innermost stop at {offset}");
            Assert.AreEqual(first.Last(), root.StopAt(offset, outermost: true), $"the outermost stop at {offset}");

            var ahead = Enumerable.Range(0, places.Count).Where(at => places[at].Offset > offset).DefaultIfEmpty(-1).First();
            var behind = Enumerable.Range(0, places.Count).Where(at => places[at].Offset < offset).DefaultIfEmpty(-1).Last();

            Assert.AreEqual(ahead, root.StopPast(offset, forward: true), $"the first stop past {offset}");
            Assert.AreEqual(behind, root.StopPast(offset, forward: false), $"the last stop before {offset}");
        }
    }

    [TestMethod]
    public void AnOffsetMovesToTheNearestStop_TheEarlierWhereTwoAreAsNear()
    {
        var root = Document();
        var laid = new Laid(root.Tree!, new Size(600, Blocks * BlockHeight), []);
        var stops = laid.Stops;

        for (var offset = -2; offset < (Blocks * BlockLength) + 2; offset++)
        {
            var nearest = stops.MinBy(stop => Math.Abs(stop - offset));
            Assert.AreEqual(nearest, laid.NearestStop(offset), $"the stop nearest {offset}");
        }
    }

    [TestMethod]
    public void ATreeSettledElsewhereIsAskedWhereItNowStands()
    {
        var root = Document();
        var before = root.PieceAt(new Point(15, 10));

        root.Tree!.Settle(new Vector(0, 1000));

        Assert.AreEqual(before, root.PieceAt(new Point(15, 1010)), "the same letter, where it was moved to");
        Assert.AreEqual(root.Tree.At(root.At).Bounds, root.Tree.Indexed.Where(root.At), "and what is kept moved with it");
    }
}
