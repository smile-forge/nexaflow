using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Latex.Stages;
using Nexaflow.Markdown.Settings;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// What every name in a formula means is said in the tree, where it was written, so nothing that sets a formula looks a name
/// up — and saying it moves nothing, so editing finds every part where it was, and a later stage can gather the pieces into
/// bigger things.
/// </summary>
[TestClass]
[CoversNode("maths-latex-pipeline")]
public class ResolveCommandsTests
{
    [TestMethod]
    public void SayingWhatANameMeansMovesNothing()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            var parsed = TexParser.Parse(LatexConstructs.Flatten(written));
            var resolved = new ResolveCommands().Run(parsed);

            Assert.AreEqual(Shape(parsed), Shape(resolved), $"{what}: a part moved, or changed its kind or its role");
        }
    }

    [TestMethod]
    public void EveryConstructIsACommandLaTeXHas()
    {
        foreach (var (what, written) in LatexConstructs.Everything)
        {
            // A macro means what it expands to, which hangs beneath it.
            var resolved = Resolved(LatexConstructs.Flatten(written));
            var unknown = resolved.SelfAndDescendants()
                .Where(node => node.Kind == TexKinds.Command && node.Role is not (TexRole.Begin or TexRole.End)
                               && node is not TexCommandNode && node.Part(Roles.Derived) is null)
                .Select(node => node.Part(Roles.Name)?.Text)
                .ToList();

            Assert.AreEqual(0, unknown.Count, $"{what}: nothing was said about {string.Join(", ", unknown)}");
        }
    }

    [TestMethod]
    public void ACommandSaysWhichConstructItIs()
    {
        Assert.IsInstanceOfType<TexFraction>(Meaning(@"\frac{a}{b}"));
        Assert.IsInstanceOfType<TexRoot>(Meaning(@"\sqrt[3]{x}"));
        Assert.AreEqual(new TexStyledFraction(TexStyle.Display), Meaning(@"\dfrac{a}{b}"));
        Assert.AreEqual(new TexOverArrow(ArrowDecoration.HeadRight, Over: true), Meaning(@"\overrightarrow{AB}"));
        Assert.AreEqual(new TexSwitch("mathcal", null), Meaning(@"\cal"));
        Assert.AreEqual(new TexSwitch(null, TexStyle.Display), Meaning(@"\displaystyle"));
        Assert.AreEqual(new TexFace("mathrm", Words: false), Meaning(@"\mathrm{d}"));
        Assert.AreEqual(new TexFace(TexVocabulary.Text, Words: true), Meaning(@"\mbox{if}"), "\\mbox is words, set in the text face");
        Assert.AreEqual(new TexSpace(TexUnit.Mu, 18), Meaning(@"\quad"));
        Assert.IsInstanceOfType<TexDiscarded>(Meaning(@"\label{eq}"));
    }

    [TestMethod]
    public void ASymbolSaysItsClass_AndAnIntegralThatItsLimitsGoBesideIt()
    {
        var alpha = (TexNamedSymbol)Meaning(@"\alpha")!;
        Assert.AreEqual(TexAtomType.Ordinary, alpha.Symbol.Class);

        var sum = (TexNamedSymbol)Meaning(@"\sum")!;
        Assert.AreEqual(TexAtomType.BigOperator, sum.Symbol.Class);
        Assert.IsFalse(sum.LimitsBeside);

        Assert.IsTrue(((TexNamedSymbol)Meaning(@"\int")!).LimitsBeside);
    }

    [TestMethod]
    public void WhatIsWrittenWithACommandIsReadIntoWhatItMeans()
    {
        Assert.AreEqual(new TexSpace(TexUnit.Em, 2), Meaning(@"\hspace{2em}"));
        Assert.AreEqual(new TexTag("3", Starred: false), Meaning(@"\tag{3}"));
        
        Assert.AreEqual(new TexContinuedFraction(TexAlignment.Left), Meaning(@"\cfrac[l]{1}{x}"));

        var sized = (TexSizedDelimiter)Meaning(@"\Bigl(")!;
        Assert.AreEqual("(", sized.Delimiter.Name);
        Assert.AreEqual(TexAtomType.Opening, sized.Class);
        Assert.AreEqual(1.75, sized.MinHeight, 1e-9);

        Assert.AreEqual("Vert", ((TexDelimiter)Meaning(@"\left\|")!).Symbol?.Name, "\\| is TeX's own spelling of \\Vert");
    }

    [TestMethod]
    public void AColourIsReadAsABrowserReadsIt()
    {
        Assert.AreEqual(new TexColour(new HexColor(0xFF, 0xFF, 0x00, 0x00), Switch: true), Meaning(@"\color{Red}"));
        Assert.AreEqual(new TexColour(new HexColor(0xFF, 0x00, 0xFF, 0x00), Switch: false), Meaning(@"\textcolor[HTML]{00FF00}{x}"));
        Assert.AreEqual(new TexColour(new HexColor(0x88, 0xFF, 0x00, 0x00), Switch: true), Meaning(@"\color{#8F00}"));

        Assert.IsInstanceOfType<TexUnset>(Meaning(@"\color{notacolour}"), "a name that is no colour is shown as written");
        Assert.IsInstanceOfType<TexUnset>(Meaning(@"\color[rgb]{1,0,0}"), "a model other than HTML is not read");
    }

    [TestMethod]
    public void ACommandLaTeXHasAndNothingDrawsIsSaidToBeOne_AndOneItHasNotIsLeftAsRead()
    {
        Assert.IsInstanceOfType<TexUnset>(Meaning(@"\colorbox{red}{x}"));
        Assert.IsNull(Meaning(@"\wat{x}"), "a name LaTeX does not have means nothing");
    }

    [TestMethod]
    public void AnEnvironmentSaysHowItArrangesWhatItHolds()
    {
        var matrix = (TexMatrixArrangement)Arrangement(@"\begin{pmatrix} a & b \end{pmatrix}")!;
        Assert.AreEqual(("(", ")"), (matrix.Left, matrix.Right));

        Assert.IsInstanceOfType<TexContents>(Arrangement(@"\begin{equation} x \end{equation}"));

        var array = (TexArrayArrangement)Arrangement(@"\begin{array}{c|l} a & b \end{array}")!;
        CollectionAssert.AreEqual(new[] { TexAlignment.Center, TexAlignment.Left }, array.Columns!.Alignments.ToList());
        CollectionAssert.AreEqual(new[] { 1 }, array.Columns.VerticalRules.ToList());

        Assert.IsNull(((TexArrayArrangement)Arrangement(@"\begin{array}{} a \end{array}")!).Columns,
                      "a preamble naming no column centres every one");
        Assert.IsNull(Arrangement(@"\begin{array}{cx} a \end{array}"), "a column nothing sets is no table to set");
    }

    [TestMethod]
    public void ACharacterThatIsNotALetterSaysWhatItIs()
    {
        var plus = Characters("a+b").Single();
        Assert.AreEqual(TexCharacter.Symbol, plus.Character);
        Assert.AreEqual(("plus", TexAtomType.BinaryOperator), (plus.Symbol!.Name, plus.Symbol.Class));

        Assert.AreEqual(TexCharacter.Tie, Characters("a~b").Single().Character);
        Assert.AreEqual(0, Characters("ab1").Count, "a letter and a digit are set as themselves");
        Assert.AreEqual(0, Characters("f'").Count, "a prime marking what it follows is the script's");
    }

    private static TexMeaning? Meaning(string latex) =>
        Resolved(latex).SelfAndDescendants().OfType<TexCommandNode>().FirstOrDefault()?.Meaning;

    private static TexArrangement? Arrangement(string latex) =>
        Resolved(latex).SelfAndDescendants().OfType<TexGridNode>().FirstOrDefault()?.Arrangement;

    private static List<TexCharNode> Characters(string latex) => [.. Resolved(latex).SelfAndDescendants().OfType<TexCharNode>()];

    private static ContentNode Resolved(string latex) => TexPipeline.Of().Run(TexParser.Parse(latex));

    /// <summary>Every piece's kind, role and characters, in order and nested as written — what editing finds a part by.</summary>
    private static string Shape(ContentNode node) =>
        $"{node.Kind}:{node.Role}:{node.Text}[{string.Join(",", node.Children.Select(Shape))}]";
}
