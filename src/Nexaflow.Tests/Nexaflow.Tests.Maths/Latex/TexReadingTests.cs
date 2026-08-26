using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// The positioned view: a parse tree with every part's place and parent worked out.
///
/// <para>
/// The tree itself deliberately knows neither, so that a subtree can be moved without a position going
/// stale and shared without a parent pointer being wrong. Both facts are wanted often enough to be
/// worked out once per reading, and thrown away when the source changes — which is the only moment
/// either of them could stop being true.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex")]
public class TexReadingTests
{
    [TestMethod]
    public void EveryPartKnowsWhereItIs()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);
            var reading = TexReading.Of(latex);

            foreach (var part in reading.Root.SelfAndDescendants())
                Assert.AreEqual(latex.Substring(part.Start, part.Length), part.Node.Print(),
                    $"{what}: {part}");
        }
    }

    [TestMethod]
    public void AndWhatHoldsIt()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var reading = TexReading.Of(LatexConstructs.Flatten(written));

            foreach (var part in reading.Root.SelfAndDescendants())
            {
                if (ReferenceEquals(part, reading.Root))
                {
                    Assert.IsNull(part.Parent, $"{what}: the whole formula is held by nothing");
                    continue;
                }

                Assert.IsNotNull(part.Parent, $"{what}: {part} is held by nothing");
                CollectionAssert.Contains(part.Parent.Children.ToList(), part,
                    $"{what}: {part} is not among the parts of what it says holds it");
            }
        }
    }

    [TestMethod]
    public void APartIsInsideWhateverHoldsIt()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var reading = TexReading.Of(LatexConstructs.Flatten(written));

            foreach (var part in reading.Root.SelfAndDescendants())
            {
                if (part.Parent is not { } parent) continue;

                Assert.IsTrue(part.Start >= parent.Start && part.End <= parent.End,
                    $"{what}: {part} is not inside {parent}");
            }
        }
    }

    [TestMethod]
    public void AGroupsContentsAreWhatIsBetweenItsBraces()
    {
        // The seam between the two readings of a formula: a typesetter drops the braces as soon as it
        // has understood them, so its box for an argument covers what is inside them, where the part
        // that IS the argument here is the group.
        var group = TexReading.Of("{a+b}").Root.Children[0];

        Assert.AreEqual(TexKind.Group, group.Kind);
        Assert.AreEqual((0, 5), group.Span);
        Assert.AreEqual((1, 3), group.Contents);
    }

    [TestMethod]
    public void AnythingElseIsItsOwnContents()
    {
        var symbol = TexReading.Of(@"\alpha").Root.Children[0];

        Assert.AreEqual(symbol.Span, symbol.Contents);
    }

    [TestMethod]
    public void AnUnclosedGroupStillHasContents()
    {
        var group = TexReading.Of("{ab").Root.Children[0];

        Assert.AreEqual((1, 2), group.Contents, "everything past the brace that was typed");
    }

    [TestMethod]
    public void TheArgumentOfAConstructIsFoundFromWhereItWasDrawn()
    {
        // What the editor has to be able to do: it knows a stretch of source, because that is what the
        // layout gives it, and needs the part that stretch is the whole of. For `{a}` the typesetter's
        // box covers the a; the part that is the numerator is `{a}`.
        var reading = TexReading.Of(@"\frac{a}{b}");

        var numerator = reading.Wrapping(6, 1);
        Assert.IsNotNull(numerator);
        Assert.AreEqual(TexRole.Numerator, numerator.Role);
        Assert.AreEqual("{a}", numerator.Node.Print());
    }

    [TestMethod]
    public void AndWhenTheArgumentIsSeveralThingsInBraces()
    {
        var reading = TexReading.Of(@"\frac{a+b}{c}");

        var numerator = reading.Wrapping(6, 3);
        Assert.IsNotNull(numerator);
        Assert.AreEqual(TexRole.Numerator, numerator.Role);
        Assert.AreEqual("{a+b}", numerator.Node.Print());
    }

    [TestMethod]
    public void APartCanBeNamedByMoreThanOneThing()
    {
        // A formula that is one fraction: the whole formula and the fraction stand for the same
        // characters, and which of them a question is about depends on the question.
        var naming = TexReading.Of(@"\frac{a}{b}").Naming(0, 11).ToList();

        Assert.AreEqual(2, naming.Count);
        Assert.AreEqual(TexKind.Sequence, naming[0].Kind, "outermost first");
        Assert.AreEqual(TexKind.Command, naming[1].Kind);
    }

    [TestMethod]
    public void MachineryIsNotOneOfTheParts()
    {
        // A command's own name, a group's braces, a row's line break: in the tree so it can be written
        // back out, not because anything is written in them.
        var fraction = TexReading.Of(@"\frac{a}{b}").Root.Children[0];

        Assert.AreEqual(3, fraction.Children.Count, "the name and two arguments");
        CollectionAssert.AreEquivalent(
            new[] { TexRole.Numerator, TexRole.Denominator },
            fraction.Parts.Select(part => part.Role).ToList());
    }
}
