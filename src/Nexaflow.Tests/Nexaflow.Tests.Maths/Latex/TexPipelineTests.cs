using System;
using System.IO;
using System.Linq;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// The one rule the reading stages have: whatever they do to the tree, it still prints as the source it
/// came from.
///
/// <para>
/// Everything else rests on that. The tree is what gets edited rather than the string — what is drawn
/// points back at it, an edit changes it, and it says what it is in source again — so a stage that
/// quietly changed the characters would not be a bug in one feature, it would be the editor losing the
/// user's text.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex-parse-tree")]
public class TexPipelineTests
{
    /// <summary>Something no command table will ever have heard of.</summary>
    private static bool Nothing(string name) => false;

    private static bool Everything(string name) => true;

    [TestMethod]
    public void ShowingAnyStretchAtAllLeavesTheSourceExactlyAsItWas()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);
            var tree = TexParser.Parse(latex);

            // Every stretch of it, not a handful: a caret can be anywhere, and the interesting places
            // are exactly the ones nobody would think to write down — halfway through a command name,
            // across a closing brace, over the & between two cells.
            for (var start = 0; start < latex.Length; start++)
                for (var length = 1; start + length <= latex.Length; length++)
                    Assert.AreEqual(latex, TexPipeline.ShownAsWritten(tree, start, length).Print(),
                        $"{what}: showing {start}+{length} changed the source");
        }
    }

    [TestMethod]
    public void AndSoDoesBeingAbleToDrawNoneOfIt()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);

            Assert.AreEqual(latex, TexPipeline.Checked(TexParser.Parse(latex), Nothing).Print(),
                $"{what}: finding nothing drawable changed the source");
        }
    }

    [TestMethod]
    public void AndBothOfThemAtOnce()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);

            for (var start = 0; start < latex.Length; start += 3)
                Assert.AreEqual(latex, TexPipeline.Read(latex, Nothing, (start, 5)).Print(),
                    $"{what}: reading it with {start}+5 under the caret changed the source");
        }
    }

    [TestMethod]
    public void BeingAbleToDrawEverythingChangesNothingAtAll()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var tree = TexParser.Parse(LatexConstructs.Flatten(written));

            Assert.AreSame(tree, TexPipeline.Checked(tree, Everything),
                $"{what}: a tree with nothing wrong came back rebuilt");
        }
    }

    [TestMethod]
    public void WhatCannotBeDrawnSaysWhyAndKeepsItsArgument()
    {
        // Only the name is shown. `\textrm{Hello}` set in the wrong face is much closer to right than a
        // blank, and the argument of an unknown command is usually ordinary maths.
        var tree = TexPipeline.Checked(TexParser.Parse(@"\wat{x + y}"), Nothing);

        var shown = tree.SelfAndDescendants().Where(node => node.Kind == TexKind.Verbatim).ToList();
        Assert.AreEqual(1, shown.Count, "the whole command was shown, not just its name");
        Assert.AreEqual(@"\wat", shown[0].Text);
        Assert.IsNotNull(shown[0].Trouble, "nothing was said about why it could not be drawn");

        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == TexKind.Char && node.Text == "x"),
            "the argument stopped being maths");
    }

    [TestMethod]
    public void AStretchBeingTypedSaysNothingAboutItself()
    {
        // The other reason a piece is shown rather than read, and it is nobody's fault: telling somebody
        // their half-written command is invalid on every keystroke is the wrong thing to draw.
        var tree = TexPipeline.ShownAsWritten(TexParser.Parse(@"\frac{a}{b}"), 0, 5);

        foreach (var node in tree.SelfAndDescendants().Where(node => node.Kind == TexKind.Verbatim))
            Assert.IsNull(node.Trouble, $"{node.Text} was complained about while it was being typed");
    }

    [TestMethod]
    public void ARealCorpusPrintsBackThroughEveryStage()
    {
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS");
        if (string.IsNullOrWhiteSpace(corpus) || !File.Exists(corpus))
            Assert.Inconclusive($"set NEXAFLOW_LATEX_CORPUS to a file of formulas (got: {corpus ?? "nothing"})");

        var seen = 0;
        var faults = 0;
        var first = "";

        foreach (var raw in File.ReadLines(corpus))
        {
            var latex = raw.Trim();
            if (latex.Length == 0) continue;

            seen++;

            // A caret a third of the way in, which lands mid-construct far more often than an endpoint
            // would, and is where the widening has to be right.
            var read = TexPipeline.Read(latex, Nothing, (latex.Length / 3, 7));
            if (read.Print() == latex) continue;

            faults++;
            if (faults == 1) first = $"\n  {latex}\n  came back as\n  {read.Print()}";
        }

        Assert.IsTrue(seen > 1000, $"only {seen} formula(s) in {corpus} — is that the right file?");
        Assert.AreEqual(0, faults, $"of {seen} formulas, {faults} did not print back{first}");
    }

    /// <summary>
    /// A sign written as several things is gathered into the one node it means, and the source it prints
    /// back is untouched.
    ///
    /// <para>
    /// The mirror of macro expansion. Expansion hangs on structure standing for no source; this re-nests
    /// structure standing for all of it. Read strictly, <c>\not\!p</c> gives the kern to <c>\not</c> as its
    /// argument and leaves the letter outside as a neighbour — which is neither what a physicist wrote nor
    /// something the builder could act on without reaching out of its own node.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ASlashAndWhatItCrossesAreOneSign()
    {
        const string latex = @"\not\!p";
        var tree = TexPipeline.Gathered(TexParser.Parse(latex));

        Assert.AreEqual(latex, tree.Print(), "a stage may re-nest anything and may change no character");

        var sign = tree.Children.Single();
        Assert.AreEqual(@"\not", sign.Part(TexRole.Name)?.Text, "one node, and it is the \\not");
        Assert.AreEqual("p", sign.Part(TexRole.Base)?.Print(),
            "what the slash is drawn over is the letter — a kern is not something to draw over");
        Assert.AreEqual(@"\!", sign.Part(TexRole.Element)?.Print(),
            "and the kern that puts it there came inside rather than being dropped");
    }

    /// <summary>Nothing to gather leaves the tree exactly as it was — the same instance, not a copy.</summary>
    [TestMethod]
    public void AndAFormulaWithNoneOfThatIsUntouched()
    {
        var read = TexParser.Parse(@"\frac{a}{b} + \not= x");
        Assert.AreSame(read, TexPipeline.Gathered(read));
    }

    /// <summary>
    /// Every corpus construct still prints as what it was written as, once gathered. The stage rewrites
    /// the shape of a tree and the invariant it may not break is that one.
    /// </summary>
    [TestMethod]
    public void GatheringNeverCostsACharacter()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);
            Assert.AreEqual(latex, TexPipeline.Gathered(TexParser.Parse(latex)).Print(), what);
        }
    }
}
