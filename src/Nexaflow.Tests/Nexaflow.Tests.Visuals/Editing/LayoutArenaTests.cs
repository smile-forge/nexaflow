using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// The promises the arena makes, which everything built on it takes for granted.
///
/// <para>
/// Four of them, and each is load-bearing. Pieces are in pre-order and a subtree is a contiguous run, so
/// descending is arithmetic and reuse is a block copy. A piece's anchor is fixed when it opens and its
/// extent learnt when it closes, so a subtree means the same thing wherever it is put down. Geometry is
/// relative, so nothing in a subtree refers to where it used to be. And what a piece holds, whatever
/// holds it holds too.
/// </para>
/// </summary>
[TestClass]
[CoversNode("latex-selection")]
public class LayoutArenaTests
{
    private sealed record Span(int Start, int Length) : ISourcePart;

    /// <summary>
    /// A little tree with the shapes that matter: something anchored away from its parent, something
    /// reaching back before its own anchor (an accidental sits before the note head it belongs to), and
    /// a piece holding nothing at all.
    /// </summary>
    private static LayoutTree Built()
    {
        var build = new LayoutBuilder();

        build.Open("page", new Span(0, 10));
        {
            build.Open("bar", new Span(0, 5), new Point(20, 5));
            {
                build.Open("note", new Span(0, 1), new Point(10, 0));
                {
                    build.Open("accidental", null, new Point(-6, 0), isInk: false);
                    build.Draw(new RuleMark(new Rect(0, 0, 4, 8), null));
                    build.Close();

                    build.Open("head", null, isInk: false);
                    build.Draw(new RuleMark(new Rect(0, 0, 9, 6), null));
                    build.Close();
                }
                build.Close();

                build.Open("rest", new Span(2, 1), new Point(40, 0));
                build.Draw(new RuleMark(new Rect(0, 0, 5, 5), null));
                build.Close();
            }
            build.Close();
        }
        build.Close();

        return build.Seal();
    }

    private static Piece Find(LayoutTree tree, string kind) =>
        tree.Root.SelfAndDescendants.Single(p => p.Kind == kind);

    [TestMethod]
    public void ASubtreeIsARunOfTheTree()
    {
        var tree = Built();

        // Pre-order: every piece comes after the one holding it, and everything inside a piece comes
        // before anything beside it. That is what makes "a piece and all of it" a slice.
        foreach (var piece in tree.Root.SelfAndDescendants)
            foreach (var child in piece.Children)
                CollectionAssert.Contains(
                    piece.SelfAndDescendants.ToList(), child,
                    $"a {child.Kind} is held by a {piece.Kind} but is not inside its run");

        var note = Find(tree, "note");

        CollectionAssert.AreEqual(
            new[] { "note", "accidental", "head" },
            note.SelfAndDescendants.Select(p => p.Kind).ToArray(),
            "a piece and everything in it, in order, and nothing else");
    }

    [TestMethod]
    public void AnAnchorIsFixedWhenAPieceOpensAndAnExtentLearntWhenItCloses()
    {
        var tree = Built();
        var note = Find(tree, "note");

        // The note was anchored at +10 in its bar and never moved, even though what it holds reaches six
        // to the left of that. One rectangle cannot say both, which is why there are two.
        Assert.AreEqual(new Vector(10, 0), note.Offset, "the anchor moved");
        Assert.AreEqual(-6, note.Box.X, 0.001, "the box should reach back past the anchor");
        Assert.AreEqual(15, note.Box.Width, 0.001, "…from the accidental's left to the head's right");
    }

    [TestMethod]
    public void WhatAPieceHoldsWhateverHoldsItHoldsToo()
    {
        var tree = Built();

        foreach (var piece in tree.Root.SelfAndDescendants)
        {
            if (piece.Box.IsEmpty) continue;

            var parent = piece.Parent;
            if (!parent.Exists || parent.Box.IsEmpty) continue;

            Assert.IsTrue(parent.Bounds.Contains(piece.Bounds),
                          $"a {parent.Kind} at {parent.Bounds} does not hold its {piece.Kind} at {piece.Bounds}");
        }
    }

    [TestMethod]
    public void GeometryIsRelativeSoASubtreeMeansTheSameThingWhereverItIs()
    {
        // The same pieces, built once in a page anchored at nought and once in a page anchored away from
        // it. Nothing inside changes: every piece's own place is identical, and only the absolute answers
        // move — which is precisely what makes a subtree reusable.
        var here = Built();
        var there = Shifted(new Vector(100, 40));

        var mine = here.Root.SelfAndDescendants.ToList();
        var theirs = there.Root.SelfAndDescendants.ToList();

        Assert.AreEqual(mine.Count, theirs.Count);

        for (var at = 1; at < mine.Count; at++)
        {
            Assert.AreEqual(mine[at].Offset, theirs[at].Offset, $"{mine[at].Kind} was anchored differently");
            Assert.AreEqual(mine[at].Box, theirs[at].Box, $"{mine[at].Kind} came out a different size");

            Assert.AreEqual(mine[at].Bounds.X + 100, theirs[at].Bounds.X, 0.001, $"{mine[at].Kind} landed wrong");
            Assert.AreEqual(mine[at].Bounds.Y + 40, theirs[at].Bounds.Y, 0.001, $"{mine[at].Kind} landed wrong");
        }
    }

    [TestMethod]
    public void APieceThatDrewNothingIsNotSomethingToPointAt()
    {
        var build = new LayoutBuilder();

        build.Open("page", new Span(0, 3));
        build.Open("empty", new Span(0, 1));   // named, and draws nothing at all
        build.Close();
        build.Close();

        var tree = build.Seal();

        Assert.IsFalse(Find(tree, "empty").IsInk,
                       "ink is a promise a reader can point at the thing, and an empty rectangle cannot be");
    }

    [TestMethod]
    public void ARunIsWalkedOneStepAtATime()
    {
        var build = new LayoutBuilder();
        var notes = new System.Collections.Generic.List<int>();

        build.Open("page", new Span(0, 9));
        for (var at = 0; at < 3; at++)
        {
            notes.Add(build.Open("note", new Span(at, 1), new Point(at * 10, 0)));
            build.Draw(new RuleMark(new Rect(0, 0, 5, 5), null));
            build.Close();
        }
        build.Close();

        build.Runs(notes, vertical: false);
        var tree = build.Seal();

        var first = tree.At(notes[0]);

        Assert.AreEqual(tree.At(notes[1]), first.Along(vertical: false, forward: true));
        Assert.AreEqual(tree.At(notes[2]), first.Along(false, true).Along(false, true));
        Assert.IsFalse(first.Along(false, forward: false).Exists, "there is nothing before the first");
        Assert.IsFalse(first.Along(vertical: true, forward: true).Exists, "it is on no downward run");
    }

    [TestMethod]
    public void APieceFromAnAbandonedTreeSaysSo()
    {
        // Four fields across the two elements hold a piece across a drag; today a rebuild mid-gesture
        // leaves them pointing into a tree nobody is drawing any more, with nothing able to tell.
        var held = Find(Built(), "note");

        Assert.AreNotSame(held.Tree, Built(), "two builds are two trees");
        Assert.AreNotEqual(held, Find(Built(), "note"), "…and a piece of one is not a piece of the other");
    }

    private static LayoutTree Shifted(Vector by)
    {
        var build = new LayoutBuilder();

        build.Open("page", new Span(0, 10), new Point(by.X, by.Y));
        {
            build.Open("bar", new Span(0, 5), new Point(20, 5));
            {
                build.Open("note", new Span(0, 1), new Point(10, 0));
                {
                    build.Open("accidental", null, new Point(-6, 0), isInk: false);
                    build.Draw(new RuleMark(new Rect(0, 0, 4, 8), null));
                    build.Close();

                    build.Open("head", null, isInk: false);
                    build.Draw(new RuleMark(new Rect(0, 0, 9, 6), null));
                    build.Close();
                }
                build.Close();

                build.Open("rest", new Span(2, 1), new Point(40, 0));
                build.Draw(new RuleMark(new Rect(0, 0, 5, 5), null));
                build.Close();
            }
            build.Close();
        }
        build.Close();

        return build.Seal();
    }
}
