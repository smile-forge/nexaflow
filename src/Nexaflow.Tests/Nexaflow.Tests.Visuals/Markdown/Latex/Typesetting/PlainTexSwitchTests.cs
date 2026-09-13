using Nexaflow.Tests.Fixtures;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// The plain-TeX font and size switches: <c>{\cal N}</c> rather than <c>\mathcal{N}</c>. LaTeX2e deprecated them and
/// amsmath never documented them — and then a corpus of 238,000 formulas lifted from published papers turned out to
/// reject 35,392 of them, of which 33,463 were one of these (tools/latex-corpus). A formula copied out of a paper is
/// written in them.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class PlainTexSwitchTests
{
    [TestMethod]
    [DataRow(@"\cal", @"\mathcal", "N")]
    [DataRow(@"\bf", @"\mathbf", "p")]
    [DataRow(@"\it", @"\mathit", "H")]
    [DataRow(@"\mit", @"\mathit", "H")]
    [DataRow(@"\rm", @"\mathrm", "d")]
    [DataRow(@"\sf", @"\mathsf", "x")]
    [DataRow(@"\tt", @"\mathtt", "x")]
    [DataRow(@"\frak", @"\mathfrak", "g")]
    [DataRow(@"\scr", @"\mathscr", "L")]
    public void AFontSwitchIsItsMathCommandAppliedToTheRestOfTheGroup(string fontSwitch, string command, string letter) => UiThread.Run(() =>
    {
        var switched = "{" + fontSwitch + " " + letter + "}";
        var applied = command + "{" + letter + "}";

        // Which face the character came out of is the whole point of a font switch, and the one thing a width cannot
        // tell apart, since two alphabets can set a letter to the same width.
        Assert.AreEqual(FontOf(applied), FontOf(switched));
        Assert.AreEqual(Width(applied), Width(switched), 1e-6);
    });

    [TestMethod]
    [DataRow(@"\cal", "N")]
    [DataRow(@"\bf", "p")]
    [DataRow(@"\rm", "d")]
    [DataRow(@"\sf", "x")]
    [DataRow(@"\tt", "x")]
    [DataRow(@"\frak", "g")]
    [DataRow(@"\scr", "L")]
    public void AFontSwitchReachesADifferentFace(string fontSwitch, string letter) => UiThread.Run(() =>
        // The switches that name an alphabet of their own, as opposed to \it and \mit, which ask for the italic a
        // letter in maths is set in anyway.
        Assert.AreNotEqual(FontOf(letter), FontOf("{" + fontSwitch + " " + letter + "}")));

    [TestMethod]
    public void AFontSwitchStopsAtTheEndOfItsGroup() => UiThread.Run(() =>
    {
        // The whole difference between a switch and a one-argument command: {\bf a}b bolds the a only.
        Assert.AreEqual(Width(@"\mathbf{a}b"), Width(@"{\bf a}b"), 1e-6);
        Assert.AreNotEqual(Width(@"\mathbf{ab}"), Width(@"{\bf a}b"));
    });

    [TestMethod]
    public void AFontSwitchDoesNotSwallowAMatrixsSeparators() => UiThread.Run(() =>
    {
        // It consumes the rest of its group, and a cell is a group — so & and \\ have to survive it.
        const string switched = @"\begin{matrix} {\bf a} & b \\ c & d \end{matrix}";
        const string applied = @"\begin{matrix} \mathbf{a} & b \\ c & d \end{matrix}";
        Assert.AreEqual(Width(applied), Width(switched), 1e-6);
        Assert.AreEqual(TotalHeight(applied), TotalHeight(switched), 1e-6);
    });

    [TestMethod]
    [DataRow(@"\frac{\cal A}{\cal B}")]
    [DataRow(@"S_{\mathrm{\scriptsize gauged}}")]
    [DataRow(@"{\cal W}_{\mathrm{tree}} = y \Phi")]
    [DataRow(@"\left\{ \begin{array}{c} {\phi = {\bf p}^{2}} \end{array} \right.")]
    public void ASwitchRendersWhereAPaperPutsOne(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void TheSizeSwitchesThatHaveAnEquivalentHereShrink() => UiThread.Run(() =>
    {
        Assert.IsTrue(Width(@"{\scriptsize x}") < Width(@"x"));
        Assert.IsTrue(Width(@"{\tiny x}") < Width(@"{\scriptsize x}"));
    });
}
