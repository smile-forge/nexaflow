using Nexaflow.Tests.Fixtures;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// The amsmath constructs closed after surveying the package against the engine (tools/amsmath-coverage). Each is
/// cheap on its own; together they were the bulk of what was missing that a formula in a markdown document would
/// actually reach for.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class AmsMathGapTests
{
    // ── italic capital Greek ─────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("Gamma")]
    [DataRow("Delta")]
    [DataRow("Theta")]
    [DataRow("Lambda")]
    [DataRow("Xi")]
    [DataRow("Pi")]
    [DataRow("Sigma")]
    [DataRow("Upsilon")]
    [DataRow("Phi")]
    [DataRow("Psi")]
    [DataRow("Omega")]
    public void ItalicGreekCapitalsAreTheMathsItalicNotTheRoman(string name) => UiThread.Run(() =>
    {
        // \varGamma and \Gamma are the same letter from two faces: the upright roman and the maths italic. Which
        // font each resolves to is the whole difference — \Delta and \Lambda happen to have the same advance width
        // in both, so measuring them would prove nothing.
        Assert.AreEqual(1, FontOf(@"\" + name));       // cmr10, upright
        Assert.AreEqual(0, FontOf(@"\var" + name));    // cmmi10, italic
    });

    // ── the semantic dots ────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\dotsb", @"\cdots")]     // between binary operators and relations
    [DataRow(@"\dotsi", @"\cdots")]     // with integrals
    [DataRow(@"\dotsm", @"\cdots")]     // between factors
    [DataRow(@"\dotsc", @"\ldots")]     // with commas
    [DataRow(@"\dotso", @"\ldots")]     // anything else
    public void EachDotsCommandResolvesToTheShapeAmsmathGivesIt(string dots, string shape) => UiThread.Run(() =>
    {
        // The two shapes are the same width, so only where the ink sits tells them apart: \cdots rides the axis,
        // \ldots sits on the baseline.
        Renders(dots);
        Assert.AreEqual(InkTop(shape), InkTop(dots), 1e-6);
        Assert.AreNotEqual(InkTop(@"\ldots"), InkTop(@"\cdots"));   // the comparison above has teeth
    });

    // ── the limit-like operators ─────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\injlim")]
    [DataRow(@"\projlim")]
    [DataRow(@"\varinjlim")]
    [DataRow(@"\varprojlim")]
    [DataRow(@"\varliminf")]
    [DataRow(@"\varlimsup")]
    [DataRow(@"\injlim_{n} A_n")]
    [DataRow(@"\varprojlim_{n} A_n")]
    public void TheLimitOperatorsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void TheDecoratedLimitsAreTallerThanABareLim() => UiThread.Run(() =>
    {
        // \varliminf is lim underlined, \varlimsup lim overlined, \varinjlim lim over an arrow.
        var bare = TotalHeight(@"\lim");
        foreach (var markup in new[] { @"\varliminf", @"\varlimsup", @"\varinjlim", @"\varprojlim" })
            Assert.IsTrue(TotalHeight(markup) > bare, $"{markup} is no taller than a plain lim");
    });

    // ── stretchy arrow accents ───────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\overrightarrow{AB}")]
    [DataRow(@"\overleftarrow{AB}")]
    [DataRow(@"\overleftrightarrow{AB}")]
    [DataRow(@"\underrightarrow{AB}")]
    [DataRow(@"\underleftarrow{AB}")]
    [DataRow(@"\underleftrightarrow{AB}")]
    public void ArrowAccentsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void AnUnderArrowGrowsDownwardsAndAnOverArrowUpwards() => UiThread.Run(() =>
    {
        var bare = Formula(@"AB");
        var over = Formula(@"\overrightarrow{AB}");
        var under = Formula(@"\underrightarrow{AB}");

        Assert.IsTrue(over.Height > bare.Height, "an over-arrow should add height");
        Assert.AreEqual(bare.Depth, over.Depth, 1e-6);
        Assert.IsTrue(under.Depth > bare.Depth, "an under-arrow should add depth");
        Assert.AreEqual(bare.Height, under.Height, 1e-6);
    });

    // ── one-sided vertical bars ──────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\lvert x \rvert")]
    [DataRow(@"\lVert x \rVert")]
    [DataRow(@"\left\lvert \frac{a}{b} \right\rvert")]
    public void TheOneSidedBarsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void AOneSidedBarIsTheSameGlyphAsThePlainOne() => UiThread.Run(() =>
    {
        Assert.AreEqual(Width(@"\lvert"), Width(@"\vert"), 1e-6);
        Assert.AreEqual(Width(@"\lVert"), Width(@"\Vert"), 1e-6);
    });

    // ── binomials in a forced style ──────────────────────────────────────────────

    [TestMethod]
    public void DbinomAndTbinomKeepTheirSizeInsideAScript() => UiThread.Run(() =>
    {
        // The whole point of them: a plain \binom shrinks with the surrounding style, these do not.
        var plain = Width(@"x_{\binom{n}{k}}");
        Assert.IsTrue(Width(@"x_{\dbinom{n}{k}}") > plain);
        Assert.IsTrue(Width(@"x_{\tbinom{n}{k}}") > plain);
    });

    // ── \pmb ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PmbIsBoldsymbol() => UiThread.Run(() =>
        // amsmath's \pmb fakes bold by overprinting, because it predates having a bold face. There is a real one
        // here, so \pmb takes it.
        Assert.AreEqual(Width(@"\boldsymbol{\alpha}"), Width(@"\pmb{\alpha}"), 1e-6));
}
