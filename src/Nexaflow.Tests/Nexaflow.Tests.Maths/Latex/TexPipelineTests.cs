using System;
using System.IO;
using System.Linq;
using Nexaflow.Markdown.Latex;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Latex.Stages;
using Nexaflow.Markdown.Pipeline.Stages;

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
[CoversNode("maths-latex-pipeline")]
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
                    Assert.AreEqual(latex, new ShowAsWritten(start, length).Run(tree).Print(),
                        $"{what}: showing {start}+{length} changed the source");
        }
    }

    [TestMethod]
    public void AndSoDoesBeingAbleToDrawNoneOfIt()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);

            Assert.AreEqual(latex, new CheckDrawable(Nothing).Run(TexParser.Parse(latex)).Print(),
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

            Assert.AreSame(tree, new CheckDrawable(Everything).Run(tree),
                $"{what}: a tree with nothing wrong came back rebuilt");
        }
    }

    [TestMethod]
    public void WhatCannotBeDrawnSaysWhyAndKeepsItsArgument()
    {
        // Only the name is shown. `\textrm{Hello}` set in the wrong face is much closer to right than a
        // blank, and the argument of an unknown command is usually ordinary maths.
        var tree = new CheckDrawable(Nothing).Run(TexParser.Parse(@"\wat{x + y}"));

        var shown = tree.SelfAndDescendants().Where(node => node.Kind == Kinds.Verbatim).ToList();
        Assert.AreEqual(1, shown.Count, "the whole command was shown, not just its name");
        Assert.AreEqual(@"\wat", shown[0].Text);
        Assert.IsNotNull(shown[0].Trouble, "nothing was said about why it could not be drawn");

        Assert.IsTrue(tree.SelfAndDescendants().Any(node => node.Kind == Kinds.Char && node.Text == "x"),
            "the argument stopped being maths");
    }

    [TestMethod]
    public void AStretchBeingTypedSaysNothingAboutItself()
    {
        // The other reason a piece is shown rather than read, and it is nobody's fault: telling somebody
        // their half-written command is invalid on every keystroke is the wrong thing to draw.
        var tree = new ShowAsWritten(0, 5).Run(TexParser.Parse(@"\frac{a}{b}"));

        foreach (var node in tree.SelfAndDescendants().Where(node => node.Kind == Kinds.Verbatim))
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
        var tree = new GatherSigns().Run(TexParser.Parse(latex));

        Assert.AreEqual(latex, tree.Print(), "a stage may re-nest anything and may change no character");

        var sign = tree.Children.Single();
        Assert.AreEqual(@"\not", sign.Part(Roles.Name)?.Text, "one node, and it is the \\not");
        Assert.AreEqual("p", sign.Part(TexRole.Base)?.Print(),
            "what the slash is drawn over is the letter — a kern is not something to draw over");
        Assert.AreEqual(@"\!", sign.Part(Roles.Element)?.Print(),
            "and the kern that puts it there came inside rather than being dropped");
    }

    /// <summary>Nothing to gather leaves the tree exactly as it was — the same instance, not a copy.</summary>
    [TestMethod]
    public void AndAFormulaWithNoneOfThatIsUntouched()
    {
        var read = TexParser.Parse(@"\frac{a}{b} + \not= x");
        Assert.AreSame(read, new GatherSigns().Run(read));
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
            Assert.AreEqual(latex, new GatherSigns().Run(TexParser.Parse(latex)).Print(), what);
        }
    }

    /// <summary>
    /// The rule, stage by stage rather than end to end, so a failure names the actor that broke it.
    /// </summary>
    [TestMethod]
    public void EveryStageLeavesTheSourceExactlyAsItFoundIt()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var latex = LatexConstructs.Flatten(written);
            var tree = TexParser.Parse(latex);

            foreach (var stage in TexPipeline.Of(Nothing, holes: true).Stages)
            {
                tree = stage.Run(tree);
                Assert.AreEqual(latex, tree.Print(), $"{what}: after {stage.Name}");
            }
        }
    }

    [TestMethod]
    public void AMacroIsReadAsWritten_AndWhatItMeansIsAStagesToSay()
    {
        var parsed = TexParser.Parse(@"\neq");
        Assert.IsFalse(parsed.SelfAndDescendants().Any(node => node.Role == Roles.Derived),
            "the parser reads what was written; what a name stands for is a stage's to say");

        var expanded = new ExpandMacros().Run(parsed);
        var meaning = expanded.Children.Single().Part(Roles.Derived);

        Assert.IsNotNull(meaning, "the expansion hangs under the macro it came from");
        Assert.AreEqual(@"\not\equals", string.Concat(meaning.Children.Select(child => child.Print())));
        Assert.AreEqual(@"\neq", expanded.Print(), "and the source is exactly what was written");
    }

    [TestMethod]
    public void AnExpansionThatNamesAMacroIsExpandedInTurn()
    {
        // \iff is a thick space either side of \Longleftrightarrow, which is itself shorthand.
        var iff = new ExpandMacros().Run(TexParser.Parse(@"\iff")).Children.Single();
        var inner = iff.Part(Roles.Derived)!.SelfAndDescendants()
            .Single(node => node.Kind == TexKinds.Command && node.Part(Roles.Name)?.Text == @"\Longleftrightarrow");

        Assert.IsNotNull(inner.Part(Roles.Derived), "the macro inside the expansion was left unexpanded");
    }

    [TestMethod]
    public void ExpandingWhatIsAlreadyExpandedChangesNothing()
    {
        var once = new ExpandMacros().Run(TexParser.Parse(@"a \neq b \iff \cos x"));

        Assert.AreSame(once, new ExpandMacros().Run(once), "a second pass hung a second expansion");
    }

    [TestMethod]
    public void AnArgumentLeftEmptyGetsAHole_UnlessItsCommandMayTakeItEmpty()
    {
        static int Holes(string latex) =>
            TexPipeline.Read(latex, holes: true).SelfAndDescendants().Count(node => node.Kind == Kinds.Hole);

        Assert.AreEqual(1, Holes(@"\frac{}{2}"), "an empty numerator is somewhere still to write");
        Assert.AreEqual(0, Holes(@"\genfrac{}{}{}{}{a}{b}"),
            "\\genfrac's first four arguments mean the default when empty, not something missing");
        Assert.AreEqual(0, Holes(@"\hbar"),
            "the empty group in \\hbar's definition is how it draws, not a gap the writer left");
    }
}
