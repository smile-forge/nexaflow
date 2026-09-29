using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// Changing content by changing its tree.
///
/// <para>
/// Read in LaTeX, because the surgery is asked of whatever tree it is handed and LaTeX's are the deepest
/// the repository has — nested arguments, tables of cells, and macros that hang what they stand for
/// underneath themselves. A tree in and a tree out: no typesetter, no desktop, nothing to draw.
/// </para>
/// <para>
/// The pair is what these turn on. A single edit is allowed to make a tree that reading its own source
/// would not produce. An edit and its undo are not: whatever they do in between, what comes out must be
/// the tree that went in, or nothing built on them can be trusted. <see cref="ContentNode.Same"/> is the
/// oracle for that, which is why it is pinned down here first.
/// </para>
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class AstEditTests
{
    // ── Telling two trees apart ─────────────────────────────────────────────

    [TestMethod]
    public void ATreeIsTheSameAsItselfAndAsAReadingOfItsOwnSource()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var tree = TexParser.Parse(LatexConstructs.Flatten(written));

            Assert.IsTrue(tree.Same(tree), what);
            Assert.IsTrue(tree.Same(TexParser.Parse(tree.Print())),
                $"{what}: reading back what it prints as gave a different tree");
        }
    }

    [TestMethod]
    public void PrintingAlikeIsNotBeingTheSameTree()
    {
        // The reason an edit cannot be checked by comparing source. A run of one thing and the thing
        // itself are the same characters and different trees, and which one an edit produced is exactly
        // what decides whether the next keystroke lands inside it or beside it.
        var alone = ContentNode.Leaf(Kinds.Char, "x");
        var wrapped = ContentNode.Branch(Kinds.Sequence, [alone]);

        Assert.AreEqual(alone.Print(), wrapped.Print(), "they print alike");
        Assert.IsFalse(alone.Same(wrapped), "and are not the same tree");
    }

    [TestMethod]
    public void ADifferenceAnywhereIsADifference()
    {
        var plain = ContentNode.Leaf(Kinds.Char, "x");

        Assert.IsFalse(plain.Same(ContentNode.Leaf(Kinds.Char, "y")), "different text");
        Assert.IsFalse(plain.Same(ContentNode.Leaf(Kinds.Char, "x", Roles.Body)), "different role");
        Assert.IsFalse(plain.Same(ContentNode.Shown("x")), "different kind");
        Assert.IsFalse(plain.Same(ContentNode.Leaf(Kinds.Char, "x", trouble: "no")), "different trouble");
        Assert.IsFalse(plain.Same(null), "and nothing at all is not it either");
    }

    // ── Surgery is faithful ─────────────────────────────────────────────────

    [TestMethod]
    public void PuttingAPartBackWhereItWasLeavesTheSourceExactlyAsItWas()
    {
        // Every part of every construct, because the spine is rebuilt through whatever holds it and a
        // language's own node type carries what it knows across that rebuild or quietly does not.
        var tried = 0;

        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);
            var reading = ContentReading.Of(TexParser.Parse(latex));

            foreach (var part in reading.Root.SelfAndDescendants().Where(part => !part.Derived))
            {
                Assert.AreEqual(latex, AstEdit.Replace(part, part.Node).Print(),
                    $"{what}: putting {part} back where it stood changed the source");
                tried++;
            }
        }

        Assert.IsTrue(tried > 1000, $"only {tried} part(s) were replaced — is the construct table empty?");
    }

    // ── What was never written is never a target ────────────────────────────

    [TestMethod]
    public void NothingWorkedOutStandsForCharacters()
    {
        var found = 0;

        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);
            var reading = ContentReading.Of(TexParser.Parse(latex));

            foreach (var part in reading.Root.SelfAndDescendants().Where(part => part.Derived))
            {
                Assert.AreEqual(0, part.Length, $"{what}: {part} stands for characters");
                Assert.AreEqual(string.Empty, part.Print(), $"{what}: {part} prints something");
                found++;
            }
        }

        // The assertions above are worth nothing if nothing devolved, so say which it was.
        Assert.IsTrue(found > 0, "no construct hung anything under itself, so nothing above was checked");
    }

    [TestMethod]
    public void AndIsNeverWhatACaretIsIn()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);
            var reading = ContentReading.Of(TexParser.Parse(latex));

            for (var offset = 0; offset <= latex.Length; offset++)
                Assert.IsFalse(reading.Innermost(offset) is { Derived: true },
                    $"{what}: a caret at {offset} is inside something nobody wrote");
        }
    }

    // ── What an edit does ───────────────────────────────────────────────────

    [TestMethod]
    public void RemovingAPartTakesItsCharactersAndLeavesTheRest()
    {
        var reading = ContentReading.Of(TexParser.Parse("a+b"));
        var plus = reading.Root.SelfAndDescendants().Single(part => part.Text == "+");

        Assert.AreEqual("ab", AstEdit.Remove(plus).Print());
    }

    [TestMethod]
    public void WhatTheEditDidNotTouchIsTheObjectItWas()
    {
        // The reason the tree is immutable. Only the spine down to the change is rebuilt, so a fraction
        // beside an edit is not merely equal to what it was — it is what it was, which is what stops an
        // edit anywhere reformatting everything.
        //
        // Taken out from inside the second fraction rather than off the end of the formula: a part the root
        // holds directly is swapped by rebuilding one list, which shares everything whatever the walk up
        // does. Only a change further down makes the walk say anything.
        var reading = ContentReading.Of(TexParser.Parse(@"\frac{a}{b} + \frac{c}{d}"));
        var fractions = reading.Root.SelfAndDescendants().Where(part => part.Kind == TexKinds.Command).ToList();
        var inside = fractions[^1].SelfAndDescendants().Single(part => part.Text == "c");

        var edited = AstEdit.Remove(inside);

        Assert.AreEqual(@"\frac{a}{b} + \frac{}{d}", edited.Print(), "the c came out of the second fraction");
        Assert.AreSame(fractions[0].Node,
            edited.SelfAndDescendants().First(node => node.Kind == TexKinds.Command),
            "the fraction beside the edit was rebuilt");
    }

    [TestMethod]
    public void APieceThatStandsForCharactersHoldsNoParts()
    {
        var reading = ContentReading.Of(TexParser.Parse("ab"));
        var letter = reading.Root.SelfAndDescendants().First(part => part.Text == "a");

        // Printing takes the parts of anything that has them and ignores its text, so a leaf given a
        // part would quietly stop saying what it said. Refused rather than allowed to happen quietly.
        Assert.ThrowsExactly<ArgumentException>(
            () => AstEdit.Insert(letter, 0, ContentNode.Leaf(Kinds.Char, "z")));
    }

    // ── An edit and its undo ────────────────────────────────────────────────

    [TestMethod]
    public void PuttingSomethingInAndTakingItBackOutLeavesTheTreeAsItWas()
    {
        var random = new Random(20260929);
        var tried = 0;

        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var before = TexParser.Parse(LatexConstructs.Flatten(written));

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var reading = ContentReading.Of(before);
                var holders = reading.Root.SelfAndDescendants()
                    .Where(part => !part.Derived && part.Children.Count > 0)
                    .ToList();

                if (holders.Count == 0) break;

                var into = holders[random.Next(holders.Count)];
                var at = random.Next(into.Children.Count + 1);
                var where = Path(into).Append(at).ToList();

                var grown = AstEdit.Insert(into, at, ContentNode.Leaf(Kinds.Char, "z"));
                var back = AstEdit.Remove(At(ContentReading.Of(grown), where));

                Assert.AreEqual(before.Print(), back.Print(),
                    $"{what}: putting a z at {string.Join('/', where)} and taking it out changed the source");
                Assert.IsTrue(before.Same(back),
                    $"{what}: putting a z at {string.Join('/', where)} and taking it out changed the tree");

                tried++;
            }
        }

        Assert.IsTrue(tried > 100, $"only {tried} pair(s) were tried — is the construct table empty?");
    }

    /// <summary>
    /// Which part, at each step down from the root. An edit rebuilds the spine, so nothing above the
    /// change is the object it was and a part cannot be found again by identity — but the shape above it
    /// is untouched, so the way down to it is.
    /// </summary>
    private static IReadOnlyList<int> Path(ContentPart part)
    {
        var path = new List<int>();

        for (var here = part; here.Parent is not null; here = here.Parent) path.Insert(0, here.Order);

        return path;
    }

    private static ContentPart At(ContentReading reading, IEnumerable<int> path)
    {
        var part = reading.Root;

        foreach (var step in path) part = part.Children[step];

        return part;
    }
}
