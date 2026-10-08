using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Latex;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// What a formula lets be written into it (<see cref="TexParser.Rewrite"/>): the reader means words, and the parser
/// says how those words have to be spelled for the formula to still be the formula.
///
/// <para>
/// Every rule here is one TeX reads. A handful of characters mean something of their own and each does damage arriving
/// unspelled — a percent comments out the rest of the line, a dollar closes the formula in a document, a brace closes
/// a group it was never opened inside, a backslash makes a command of whatever letters follow it — so each goes in as
/// the thing it is. And the shape of a grid takes no words at all, because a column specification is letters saying
/// how many columns there are rather than anything a reader meant.
/// </para>
/// <para>
/// Asked of the parser directly, which is where the rule is; what asks it in the app is the engine, for every write
/// that holds words as the reader means them rather than source (<see cref="ContentWrite.Meant"/>).
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex")]
public class TexTranspileTests
{
    private const string Formula = @"\frac{a}{b} + \begin{array}{cc} 1 & 2 \end{array}";

    [TestMethod]
    public void WordsThatMeanNothingElseGoInAsTheyAre()
    {
        Assert.AreEqual("xy", Written(At("a"), "xy"));
        Assert.AreEqual("1 + 2", Written(At("a"), "1 + 2"), "a plus is a plus");
    }

    [TestMethod]
    public void EveryCharacterTeXKeepsForItselfGoesInAsThatCharacter()
    {
        var into = At("a");

        Assert.AreEqual(@"\%", Written(into, "%"), "left bare it would comment out the rest of the formula");
        Assert.AreEqual(@"\$", Written(into, "$"), "and a dollar would close the formula in a document");
        Assert.AreEqual(@"\{\}", Written(into, "{}"), "braces close groups they were never opened inside");
        Assert.AreEqual(@"\&", Written(into, "&"), "an ampersand splits a cell");
        Assert.AreEqual(@"\#", Written(into, "#"));
        Assert.AreEqual(@"\_", Written(into, "_"), "an underscore would make a subscript of what follows");
        Assert.AreEqual(@"\^{}", Written(into, "^"), "and a caret a superscript — the accent takes an argument, so it is given none");
        Assert.AreEqual(@"\backslash{}", Written(into, @"\"), "a backslash would make a command of the letters after it");
    }

    [TestMethod]
    public void AndWhatComesOfItIsStillAFormulaThatReadsBack()
    {
        // The point of spelling them: what lands has to be something the parser gives back unchanged, and the oracle
        // has to still be able to say where every piece of it was read from.
        foreach (var said in new[] { "%", "$", "{", "}", "&", "#", "_", "^", @"\", @"100% of \it {here}" })
        {
            var into = At("a");
            var landed = Formula[..into.End] + Written(into, said) + Formula[into.End..];

            Assert.AreEqual(landed, TexParser.Parse(landed).Print(), said);
            Assert.AreEqual(0, AstOracle.Faults(landed, TexParser.Parse(landed)).Count(), said);
        }
    }

    [TestMethod]
    public void ABreakIsASpace_BecauseAFormulaIsOneExpression()
    {
        Assert.AreEqual("a b", Written(At("a"), "a\nb"));
        Assert.AreEqual("a b", Written(At("a"), "a\r\nb"));
    }

    [TestMethod]
    public void TheShapeOfAGridTakesNoWordsAtAll()
    {
        // The cc of \begin{array}{cc} says how many columns there are and how each is set. Words there do not mean
        // something else — they leave a grid that no longer says how wide it is.
        var spec = Spec();

        Assert.IsNull(Rewritten(spec, "x"), "nothing goes into a column specification");
        Assert.AreEqual("x", Written(At("1"), "x"), "and a cell of the same grid takes words as anywhere else does");
    }

    [TestMethod]
    public void AChangeWritingNothingMeantIsTheChangeItWas()
    {
        var change = ContentChange.Write(0, 0, "anything");

        Assert.AreSame(change, TexParser.Rewrite(change), "a write nobody asked to be made safe is left alone");
    }

    /// <summary>The piece of the formula written as <paramref name="said"/>.</summary>
    private static ContentPart At(string said)
    {
        var reading = ContentReading.Of(TexParser.Parse(Formula));

        return reading.Root.SelfAndDescendants().First(part => part.Length > 0 && part.Print() == said);
    }

    /// <summary>The <c>{cc}</c> of the array — its shape, not its contents.</summary>
    private static ContentPart Spec()
    {
        var reading = ContentReading.Of(TexParser.Parse(Formula));

        return reading.Root.SelfAndDescendants()
            .First(part => part.Role == TexRole.Option && part.Parent?.Kind == TexKinds.Environment);
    }

    /// <summary>What <paramref name="text"/> is written as into <paramref name="part"/>, or null where it cannot be.</summary>
    private static ContentChange? Rewritten(ContentPart part, string text) =>
        TexParser.Rewrite(new ContentChange([ContentWrite.Words(part, part.End, 0, text)], part.End + text.Length));

    private static string Written(ContentPart part, string text)
    {
        var change = Rewritten(part, text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }
}
