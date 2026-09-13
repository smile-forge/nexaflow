using System.Collections.Generic;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// The last three constructs the reference used to list as unsupported: <c>\begin{array}{…}</c> with per-column
/// alignment, <c>|</c> rules and <c>\hline</c>; <c>\operatorname</c> and <c>\operatorname*</c>; and <c>\mbox</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class ArrayAndOperatorTests
{
    /// <summary>Every rule the formula draws, as where it starts measured from the formula's left edge.</summary>
    private static List<double> RuleOffsets(string markup) =>
        Marks(markup).Where(mark => mark.Mark is RuleMark)
                     .Select(mark => mark.X + ((RuleMark)mark.Mark).Bounds.Left)
                     .ToList();

    /// <summary>The rules the formula draws that run across it rather than down it.</summary>
    private static int AcrossRules(string markup) =>
        Marks(markup).Count(mark => mark.Mark is RuleMark rule && rule.Bounds.Width > rule.Bounds.Height);

    // ── array ────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\begin{array}{c} a \end{array}")]
    [DataRow(@"\begin{array}{cc} a & b \\ c & d \end{array}")]
    [DataRow(@"\begin{array}{lcr} a & b & c \\ dd & ee & ff \end{array}")]
    [DataRow(@"\begin{array}{cc|c} 1 & 0 & 3 \\ 0 & 1 & 4 \end{array}")]
    [DataRow(@"\begin{array}{|c|c|} a & b \\ c & d \end{array}")]
    [DataRow(@"\begin{array}{cc} \hline a & b \\ \hline c & d \\ \hline \end{array}")]
    [DataRow(@"\left[\begin{array}{cc|c} 1 & 0 & 3 \\ 0 & 1 & 4 \end{array}\right]")]
    [DataRow(@"\begin{array}{c} \begin{array}{c} a \end{array} \end{array}")]
    public void ArraysRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void ThePreambleDecidesWhereAShortCellSitsInItsColumn() => UiThread.Run(() =>
    {
        // A narrow cell above a wide one has slack in its column, and l, c and r spend that slack differently: after
        // the cell, either side of it, or before it. So where the narrow cell's glyph lands is exactly that decision.
        static double FirstGlyph(string markup) => Marks(markup).First().X;

        const string body = @" i \\ mmm \end{array}";
        var l = FirstGlyph(@"\begin{array}{l}" + body);
        var c = FirstGlyph(@"\begin{array}{c}" + body);
        var r = FirstGlyph(@"\begin{array}{r}" + body);
        Assert.IsTrue(l < c, "a left-aligned cell should sit further left than a centred one");
        Assert.IsTrue(c < r, "a centred cell should sit further left than a right-aligned one");
    });

    [TestMethod]
    public void ARuleInThePreambleIsDrawn() => UiThread.Run(() =>
        Assert.IsTrue(Marks(@"\begin{array}{c|c} a & b \end{array}").Count > Marks(@"\begin{array}{cc} a & b \end{array}").Count,
                      "the | should have added a rule to the drawing"));

    [TestMethod]
    public void HlineIsARuleRatherThanARow() => UiThread.Run(() =>
    {
        const string ruled = @"\begin{array}{c} \hline a \\ \hline b \\ \hline \end{array}";
        Assert.AreEqual(3, AcrossRules(ruled));     // above, between, below
        Assert.IsTrue(TotalHeight(ruled) < TotalHeight(@"\begin{array}{c} a \\ b \\ c \end{array}"),
                      "two rows, not five");
    });

    [TestMethod]
    [DataRow(@"\begin{array} a \end{array}")]              // no preamble at all
    [DataRow(@"\begin{array}{c@{x}c} a & b \end{array}")]  // a preamble in a language we cannot read
    public void AnArrayWeCannotDrawSaysSo(string markup) => UiThread.Run(() =>
        // Better a grid shown as the characters that asked for it than one quietly missing what was asked for.
        Assert.AreNotEqual(0, Undrawn(markup).Count));

    [TestMethod]
    [DataRow(@"\begin{array}{} a \end{array}")]      // not legal LaTeX, and the corpus has it anyway
    [DataRow(@"\begin{array}{p} a \end{array}")]     // a column type we cannot draw
    public void APreambleNamingNoColumnWeCanDrawCentresThemInstead(string markup) => UiThread.Run(() =>
    {
        // The one preamble fault that is recovered rather than marked. Refusing these bought nothing and cost the
        // whole formula, and the cells already say how many columns there are.
        Assert.AreEqual(0, Undrawn(markup).Count);
        Renders(markup);
    });

    // ── \operatorname ────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\operatorname{argmax}")]
    [DataRow(@"\operatorname{argmin}_{x} f(x)")]
    [DataRow(@"\operatorname*{argmax}_{\theta} L(\theta)")]
    [DataRow(@"\operatorname{Tr}(A) = \sum_i a_{ii}")]
    public void OperatornameRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void OperatornameIsAnOperatorNotJustUprightText() => UiThread.Run(() =>
        // The point of it: the name gets an operator's spacing either side.
        Assert.IsTrue(Width(@"a \operatorname{argmax} b") > Width(@"a \mathrm{argmax} b")));

    [TestMethod]
    public void AScriptAfterStarredOperatornameBecomesItsLimit() => UiThread.Run(() =>
    {
        // Set under the name rather than beside it, so it adds depth and no width.
        Assert.IsTrue(Width(@"\operatorname*{argmax}_{x}") < Width(@"\mathrm{argmax}_{x}"));
        Assert.IsTrue(Depth(@"\operatorname*{argmax}_{x}") > Depth(@"\mathrm{argmax}_{x}"));
    });

    // ── \mbox ────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\mbox{hello}")]
    [DataRow(@"x + \mbox{some text} = y")]
    public void MboxRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void MboxIsTextUnderAnotherName() => UiThread.Run(() =>
        Assert.AreEqual(Width(@"\text{a few words}"), Width(@"\mbox{a few words}"), 1e-6));

    [TestMethod]
    public void MboxKeepsItsSpaces() => UiThread.Run(() =>
        Assert.IsTrue(Width(@"\mbox{a b}") > Width(@"\mbox{ab}")));

    // ── where a vertical rule lands ──────────────────────────────────────────────

    [TestMethod]
    public void AVerticalRuleSitsOnTheColumnBoundaryWhicheverRowIsWidest() => UiThread.Run(() =>
    {
        // The rule is placed from the first row, but a column is as wide as its widest cell in any row. A first row
        // that is not the widest must not drag the rule off the boundary with it.
        var narrowFirst = RuleOffsets(@"\begin{array}{c|c} a & b \\ xxxx & d \end{array}");
        var widestFirst = RuleOffsets(@"\begin{array}{c|c} xxxx & b \\ a & d \end{array}");

        Assert.AreEqual(1, narrowFirst.Count);
        Assert.AreEqual(widestFirst[0], narrowFirst[0], 1e-6);
        Assert.IsTrue(narrowFirst[0] >= Width(@"xxxx") && narrowFirst[0] <= Width(@"\begin{array}{c|c} a & b \\ xxxx & d \end{array}"),
                      $"the rule at {narrowFirst[0]} is not between the first column and the edge");
    });
}
