using Nexaflow.Tests.Fixtures;
using XamlMath;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// Places the engine had quietly departed from TeX's own layout rules. None shows up against a specification —
/// each came from putting our rendering beside a published paper's, formula by formula (tools/latex-corpus).
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class TexLayoutRuleTests
{
    // ── where an integral's limits go ────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\int")]
    [DataRow(@"\oint")]
    public void AnIntegralSetsItsLimitsBesideIt(string operatorName) => UiThread.Run(() =>
    {
        // TeX gives an integral \nolimits by default and \sum \limits, in every style — which is why \int_0^\infty
        // reads the way it does in every published paper. Setting them beside makes the operator wider and shorter
        // than stacking them would.
        var beside = operatorName + @"_{0}^{n}";
        var stacked = operatorName + @"\limits_{0}^{n}";
        Assert.IsTrue(Width(beside) > Width(stacked), "side-set limits widen the operator");
        Assert.IsTrue(TotalHeight(beside) < TotalHeight(stacked), "and stop it growing upwards");
    });

    [TestMethod]
    public void ASumStillStacksItsLimits() => UiThread.Run(() =>
        Assert.IsTrue(TotalHeight(@"\sum_{0}^{n}") > TotalHeight(@"\int_{0}^{n}")));

    [TestMethod]
    public void AnOperatorWithNoLimitsAtAllIsStillTheDisplaySize() => UiThread.Run(() =>
    {
        // Choosing the display form of the glyph happens where the scripts are set, so an operator that has none
        // must not take the short way out and come out at the size of the letters.
        Assert.IsTrue(TotalHeight(@"\int") > TotalHeight(@"\textstyle\int"), "a lone integral should take the display glyph");

        // The same glyph as one carrying limits, give or take how far the limits themselves hang.
        Assert.IsTrue(TotalHeight(@"\int") > 0.95 * TotalHeight(@"\int_{0}"),
                      "a lone integral should be the glyph an integral with limits uses");
    });

    // ── a row separator at the end of a matrix ───────────────────────────────────

    [TestMethod]
    [DataRow(@"\begin{matrix} a & b \\ c & d \end{matrix}")]
    [DataRow(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}")]
    [DataRow(@"\begin{cases} a & b \\ c & d \end{cases}")]
    public void ATrailingRowSeparatorClosesTheLastRowRatherThanOpeningAnother(string markup) => UiThread.Run(() =>
    {
        // "a & b \\ c & d \\" is a normal way to write a matrix out. The empty row it used to leave behind was a
        // blank line the grid grew to fit — and the delimiters grew again to cover that, which is what left the
        // last row sitting near the middle of its brackets.
        var trailing = markup.Replace(@" \end", @" \\ \end");
        Assert.AreEqual(Height(markup), Height(trailing), 1e-6);
        Assert.AreEqual(TotalHeight(markup), TotalHeight(trailing), 1e-6);
    });

    // ── a script on an accented base ─────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\dot{C}^{\mu}", @"\dot{C}")]
    [DataRow(@"\hat{a}_{b\sigma}", @"\hat{a}")]
    [DataRow(@"\vec{v}^{2}", @"\vec{v}")]
    [DataRow(@"\tilde{C}^{\mu}", @"\tilde{C}")]
    public void AScriptAfterAnAccentAttachesToTheAccentedBase(string markup, string accented) => UiThread.Run(() =>
    {
        // It used to fall through to a "there is no base to hand" path, which hangs the script off an empty box
        // standing next to the accent — so it was set at the height of nothing at all, level with the letter
        // instead of with the accent.
        Renders(markup);
        Assert.IsTrue(TotalHeight(markup) > TotalHeight(accented), "the script should add to the accented base");
    });

    [TestMethod]
    public void ASuperscriptReachesAboveTheAccentBelowIt() => UiThread.Run(() =>
    {
        // The accent is part of the nucleus the script sits on, so the script clears it. Were it not, the accent
        // would still be the tallest thing in the box and adding the script would not change the height at all.
        Assert.IsTrue(Height(@"\dot{C}^{\mu}") > Height(@"\dot{C}"), "a script on an accented base should reach past the accent");
        Assert.IsTrue(Height(@"\dot{C}^{\mu}") > Height(@"C^{\mu}"), "and sit higher than the same script on a bare letter");
    });
}
