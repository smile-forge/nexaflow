using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// Changing a formula by changing its tree.
///
/// <para>
/// No fonts, no typesetter, no desktop: an edit is a tree in and a tree out, and everything worth
/// asking about one can be asked here. Which is the point of it being here rather than on the layout —
/// the layout says what the reader pointed at, and what to do about it is a question about the formula.
/// </para>
/// <para>
/// The pair is what these turn on. A single edit is allowed to make a tree that reading its own source
/// would not produce — that is what an unbraced argument gaining a second token <em>is</em>, and
/// teaching the edit to brace it comes next. An edit and its undo are not allowed to: whatever they do
/// in between, what comes out must be the tree that went in, or nothing above them can be trusted.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex-edit")]
public class TexEditTests
{
    /// <summary>Every construct, flattened, as the tree the builder would be handed.</summary>
    private static IEnumerable<(string What, TexNode Tree)> Constructs() =>
        LatexConstructs.Everything.Select(c => (c.Item1, TexPipeline.Read(LatexConstructs.Flatten(c.Item2))));

    // ── Telling two trees apart ─────────────────────────────────────────────

    [TestMethod]
    public void ATreeIsTheSameAsItselfAndAsAReadingOfItsOwnSource()
    {
        foreach (var (what, tree) in Constructs())
        {
            Assert.IsTrue(tree.Same(tree), what);
            Assert.IsTrue(tree.Same(TexPipeline.Read(tree.Print())),
                $"{what}: reading back what it prints as gave a different tree");
        }
    }

    [TestMethod]
    public void PrintingAlikeIsNotBeingTheSameTree()
    {
        // The reason an edit cannot be checked by comparing source. A run of one thing and the thing
        // itself are the same characters and different trees, and which one an edit produced is exactly
        // what decides whether the next keystroke lands inside it or beside it.
        var alone = TexNode.Leaf(TexKind.Char, "x");
        var wrapped = TexNode.Branch(TexKind.Sequence, [alone]);

        Assert.AreEqual(alone.Print(), wrapped.Print(), "they print alike");
        Assert.IsFalse(alone.Same(wrapped), "and are not the same tree");
    }

    [TestMethod]
    public void ADifferenceAnywhereIsADifference()
    {
        var plain = TexNode.Leaf(TexKind.Char, "x");

        Assert.IsFalse(plain.Same(TexNode.Leaf(TexKind.Char, "y")), "different text");
        Assert.IsFalse(plain.Same(TexNode.Leaf(TexKind.Char, "x", TexRole.Base)), "different role");
        Assert.IsFalse(plain.Same(TexNode.Shown("x")), "different kind");
        Assert.IsFalse(plain.Same(TexNode.Leaf(TexKind.Char, "x", trouble: "no")), "different trouble");
        Assert.IsFalse(plain.Same(null), "and nothing at all is not it either");
    }

    // ── What an edit does ───────────────────────────────────────────────────

    [TestMethod]
    public void RemovingAPartTakesItsCharactersAndLeavesTheRest()
    {
        var reading = TexReading.Of(TexPipeline.Read("a+b"));
        var plus = reading.Root.SelfAndDescendants().Single(part => part.Text == "+");

        Assert.AreEqual("ab", TexEdit.Remove(plus).Print());
    }

    [TestMethod]
    public void WhatTheEditDidNotTouchIsTheObjectItWas()
    {
        // The reason the tree is immutable. Only the spine down to the change is rebuilt, so a fraction
        // beside an edit is not merely equal to what it was — it is what it was, which is what stops an
        // edit anywhere reformatting everything.
        var reading = TexReading.Of(TexPipeline.Read(@"\frac{a}{b}+c"));
        var fraction = reading.Root.SelfAndDescendants().First(part => part.Kind == TexKind.Command);
        var last = reading.Root.Children[^1];

        var edited = TexEdit.Remove(last);

        Assert.AreSame(fraction.Node, edited.SelfAndDescendants().First(node => node.Kind == TexKind.Command),
            "the fraction was not rebuilt");
    }

    [TestMethod]
    public void APieceThatStandsForCharactersHoldsNoParts()
    {
        var reading = TexReading.Of(TexPipeline.Read("ab"));
        var letter = reading.Root.SelfAndDescendants().First(part => part.Text == "a");

        // Printing takes the parts of anything that has them and ignores its text, so a leaf given a
        // part would quietly stop saying what it said. Refused rather than allowed to happen quietly.
        Assert.ThrowsExactly<ArgumentException>(
            () => TexEdit.Insert(letter, 0, TexNode.Leaf(TexKind.Char, "z")));
    }

    // ── An edit and its undo ────────────────────────────────────────────────

    [TestMethod]
    public void PuttingSomethingInAndTakingItBackOutLeavesTheTreeAsItWas()
    {
        var random = new Random(20260904);
        var tried = 0;

        foreach (var (what, before) in Constructs())
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var reading = TexReading.Of(before);
                var holders = reading.Root.SelfAndDescendants()
                    .Where(part => !part.Derived && part.Children.Count > 0)
                    .ToList();

                if (holders.Count == 0) break;

                var into = holders[random.Next(holders.Count)];
                var at = random.Next(into.Children.Count + 1);
                var where = Path(into).Append(at).ToList();

                var grown = TexEdit.Insert(into, at, TexNode.Leaf(TexKind.Char, "z"));
                var back = TexEdit.Remove(At(TexReading.Of(grown), where));

                Assert.AreEqual(before.Print(), back.Print(),
                    $"{what}: putting a z at {string.Join('/', where)} and taking it out changed the source");
                Assert.IsTrue(before.Same(back),
                    $"{what}: putting a z at {string.Join('/', where)} and taking it out changed the tree");

                tried++;
            }
        }

        Assert.IsTrue(tried > 100, $"only {tried} pair(s) were tried — is the construct table empty?");
    }

    // ── Finding the same place in a tree that has been rebuilt ──────────────

    /// <summary>
    /// Which child, at each step down from the root. An edit rebuilds the spine, so nothing above the
    /// change is the object it was and a part cannot be found again by identity — but the shape above it
    /// is untouched, so the way down to it is.
    /// </summary>
    private static IReadOnlyList<int> Path(TexPart part)
    {
        var path = new List<int>();

        for (var here = part; here.Parent is { } parent; here = parent)
            path.Insert(0, Index(parent, here));

        return path;
    }

    private static TexPart At(TexReading reading, IEnumerable<int> path)
    {
        var part = reading.Root;
        foreach (var step in path) part = part.Children[step];
        return part;
    }

    private static int Index(TexPart parent, TexPart child)
    {
        for (var i = 0; i < parent.Children.Count; i++)
            if (ReferenceEquals(parent.Children[i], child)) return i;

        throw new ArgumentException("that part is not one of this part's own", nameof(child));
    }
}
