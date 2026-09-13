using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// The LaTeX symbols and commands added on top of the JMathTeX symbol set: \mathring, the over-arrows, the long and
/// hooked arrows, the dots, the spacing commands, the amssymb negations and synonyms, the fraction family, the
/// multiple integrals and the modulo operators.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class AdditionalSymbolsTests
{
    [TestMethod]
    [DataRow(@"\mathring{a}")]
    [DataRow(@"\overrightarrow{AB}")]
    [DataRow(@"\overleftarrow{AB}")]
    [DataRow(@"\mapsto")]
    [DataRow(@"a \mapsto b")]
    [DataRow(@"\longrightarrow")]
    [DataRow(@"\longleftarrow")]
    [DataRow(@"\hookrightarrow")]
    [DataRow(@"\Longrightarrow")]
    [DataRow(@"\Longleftrightarrow")]
    [DataRow(@"\vdots")]
    [DataRow(@"\ddots")]
    [DataRow(@"\dots")]
    [DataRow(@"\S")]
    [DataRow(@"\P")]
    [DataRow(@"\notin")]
    [DataRow(@"\varnothing")]
    [DataRow(@"\nexists")]
    [DataRow(@"\implies")]
    [DataRow(@"\iff")]
    [DataRow(@"\lhook")]
    [DataRow(@"\rhook")]
    [DataRow(@"\begin{pmatrix}a & \cdots & b \\ \vdots & \ddots & \vdots \\ c & \cdots & d\end{pmatrix}")]
    [DataRow(@"a\quad b")]
    [DataRow(@"a\qquad b")]
    [DataRow(@"a\ b")]
    [DataRow(@"a~b")]
    [DataRow(@"a\hspace{2em}b")]
    [DataRow(@"a\hspace{-3pt}b")]
    public void TheCommandRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    [DataRow(@"\quad")]
    [DataRow(@"\qquad")]
    [DataRow(@"\ ")]
    [DataRow(@"~")]
    [DataRow(@"\hspace{2em}")]
    [DataRow(@"\hspace{20pt}")]
    [DataRow(@"\hspace{1cm}")]
    [DataRow(@"\hspace{-3pt}")]
    [DataRow(@"\hspace*{2em}")]
    public void ASpacingCommandIsRoomAndNoInk(string markup) => UiThread.Run(() =>
    {
        Renders(markup);
        Assert.AreNotEqual(0.0, Width(markup));
        Assert.AreEqual(0, Marks(markup).Count);
    });

    [TestMethod]
    [DataRow(@"\hspace{2xyz}")]
    [DataRow(@"\hspace{abc}")]
    public void HspaceWithAnInvalidLengthIsSetAsItsOwnCharacters(string markup) => UiThread.Run(() =>
        Assert.AreNotEqual(0, Undrawn(markup).Count));

    [TestMethod]
    public void MathringIsSetAsAnAccent() => UiThread.Run(() =>
        Assert.IsTrue(Height(@"\mathring{a}") > Height(@"a")));

    [TestMethod]
    [DataRow(@"\overrightarrow{AB}")]
    [DataRow(@"\overleftarrow{AB}")]
    public void OverArrowsStandOverWhatTheyMark(string markup) => UiThread.Run(() =>
        Assert.IsTrue(Height(markup) > Height(@"AB")));

    [TestMethod]
    [DataRow(@"\vdots")]
    [DataRow(@"\ddots")]
    public void VerticalAndDiagonalDotsAreThreeDots(string markup) => UiThread.Run(() =>
        Assert.AreEqual(3, Marks(markup).Count));

    [TestMethod]
    [DataRow(@"\S")]
    [DataRow(@"\P")]
    [DataRow(@"\varnothing")]
    [DataRow(@"\lhook")]
    [DataRow(@"\rhook")]
    public void GlyphBackedSymbolsAreOneGlyph(string markup) => UiThread.Run(() =>
        Assert.AreEqual(1, Marks(markup).Count));

    /// <summary>amssymb negated relations and synonyms.</summary>
    [TestMethod]
    [DataRow(@"\nless")]
    [DataRow(@"\ngtr")]
    [DataRow(@"\nleq")]
    [DataRow(@"\ngeq")]
    [DataRow(@"\nleqslant")]
    [DataRow(@"\ngeqslant")]
    [DataRow(@"\nleqq")]
    [DataRow(@"\ngeqq")]
    [DataRow(@"\nprec")]
    [DataRow(@"\nsucc")]
    [DataRow(@"\npreceq")]
    [DataRow(@"\nsucceq")]
    [DataRow(@"\nsim")]
    [DataRow(@"\ncong")]
    [DataRow(@"\nvdash")]
    [DataRow(@"\nvDash")]
    [DataRow(@"\nVdash")]
    [DataRow(@"\nmid")]
    [DataRow(@"\nparallel")]
    [DataRow(@"\nsubseteq")]
    [DataRow(@"\nsupseteq")]
    [DataRow(@"\nsubseteqq")]
    [DataRow(@"\nsupseteqq")]
    [DataRow(@"\ntriangleleft")]
    [DataRow(@"\ntriangleright")]
    [DataRow(@"\ntrianglelefteq")]
    [DataRow(@"\ntrianglerighteq")]
    [DataRow(@"\nleftarrow")]
    [DataRow(@"\nrightarrow")]
    [DataRow(@"\nLeftarrow")]
    [DataRow(@"\nRightarrow")]
    [DataRow(@"\nleftrightarrow")]
    [DataRow(@"\nLeftrightarrow")]
    [DataRow(@"\doublecup")]
    [DataRow(@"\doublecap")]
    [DataRow(@"\restriction")]
    [DataRow(@"\Doteq")]
    [DataRow(@"\llless")]
    [DataRow(@"\gggtr")]
    public void AmssymbSymbolsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    [DataRow(@"\dfrac{a}{b}")]
    [DataRow(@"\tfrac{a}{b}")]
    [DataRow(@"\cfrac{a}{b}")]
    [DataRow(@"\cfrac[l]{a}{b}")]
    [DataRow(@"\cfrac[r]{a}{b}")]
    [DataRow(@"\cfrac{1}{2+\cfrac{1}{3}}")]
    public void DfracTfracAndCfracAreFractionsWithABar(string markup) => UiThread.Run(() =>
    {
        Renders(markup);
        Assert.IsTrue(Marks(markup).Any(mark => mark.Mark is RuleMark), "a fraction draws its bar");
    });

    [TestMethod]
    [DataRow(@"\nicefrac{a}{b}")]
    [DataRow(@"\sfrac{a}{b}")]
    [DataRow(@"\nicefrac{1}{2}")]
    public void NicefracAndSfracAreSlashedFractions(string markup) => UiThread.Run(() =>
    {
        Renders(markup);
        Assert.IsFalse(Marks(markup).Any(mark => mark.Mark is RuleMark), "a slashed fraction draws no bar");
    });

    [TestMethod]
    [DataRow(@"\iint")]
    [DataRow(@"\iiint")]
    [DataRow(@"\iiiint")]
    [DataRow(@"\idotsint")]
    [DataRow(@"\oiint")]
    [DataRow(@"\oiiint")]
    [DataRow(@"\iint_D f")]
    [DataRow(@"a \bmod b")]
    [DataRow(@"a \equiv b \pmod{n}")]
    [DataRow(@"x \pod{p}")]
    public void IntegralsAndModuloRender(string markup) => UiThread.Run(() => Renders(markup));
}
