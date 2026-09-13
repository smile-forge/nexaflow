using System.Linq;
using Nexaflow.Tests.Fixtures;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// What the amsmath survey (tools/amsmath-coverage) turned up and is still pinned here: where an operator's limits
/// go, the \big family of set-size delimiters, \genfrac — the general fraction the others are spelled with — and
/// escaped braces inside a group.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class AmsMathDisplayTests
{
    // ── display environments ─────────────────────────────────────────────────────

    [TestMethod]
    public void MultlineSetsItsLinesLikeGather() => UiThread.Run(() =>
    {
        const string markup = @"\begin{multline} a + b \\ c + d \end{multline}";
        const string gathered = @"\begin{gathered} a + b \\ c + d \end{gathered}";
        Assert.AreEqual(Width(gathered), Width(markup), 1e-6);
        Assert.AreEqual(TotalHeight(gathered), TotalHeight(markup), 1e-6);
    });

    // ── where an operator's limits go ────────────────────────────────────────────

    [TestMethod]
    public void LimitsStacksTheScriptsWhereTheStyleWouldHaveSetThemBeside() => UiThread.Run(() =>
    {
        // Text style puts an operator's scripts beside it; \limits overrides that, and the operator grows taller
        // and stops being widened by them.
        const string beside = @"\textstyle\sum_{i}^{n} x";
        const string stacked = @"\textstyle\sum\limits_{i}^{n} x";
        Assert.IsTrue(TotalHeight(stacked) > TotalHeight(beside), @"\limits should stack the scripts");
        Assert.IsTrue(Width(stacked) < Width(beside), "stacked scripts should no longer widen the operator");
    });

    [TestMethod]
    public void NolimitsSetsTheScriptsBesideWhereTheStyleWouldHaveStackedThem() => UiThread.Run(() =>
    {
        const string stacked = @"\displaystyle\sum_{i}^{n} x";
        const string beside = @"\displaystyle\sum\nolimits_{i}^{n} x";
        Assert.IsTrue(TotalHeight(beside) < TotalHeight(stacked), @"\nolimits should unstack the scripts");
        Assert.IsTrue(Width(beside) > Width(stacked), "scripts set beside the operator widen it");
    });

    [TestMethod]
    public void ALimitControlIsOnlyACommandAfterAnOperator() => UiThread.Run(() =>
        // Anywhere it could not mean anything it is left undrawn and set as its own characters, rather than being
        // quietly eaten and leaving a formula that looks like it was understood.
        CollectionAssert.AreEqual(new[] { @"\limits" }, Undrawn(@"x\limits_{i}").ToList()));

    // ── \big, \Big, \bigg, \Bigg ─────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\bigl( x \bigr)")]
    [DataRow(@"\Bigl[ x \Bigr]")]
    [DataRow(@"\biggl\{ x \biggr\}")]
    [DataRow(@"\Biggl\langle x \Biggr\rangle")]
    [DataRow(@"\big| x \big|")]
    [DataRow(@"x \bigm| y")]
    public void TheSizedDelimitersRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void TheFourSizesStepUpStartingAboveThePlainDelimiter() => UiThread.Run(() =>
    {
        var sizes = new[] { @"\big(", @"\Big(", @"\bigg(", @"\Bigg(" }.Select(markup => TotalHeight(markup)).ToList();
        Assert.IsTrue(sizes[0] > TotalHeight(@"("), @"\big should be taller than a plain (");
        CollectionAssert.AreEqual(sizes.OrderBy(size => size).ToList(), sizes);
        Assert.AreEqual(4, sizes.Distinct().Count());
    });

    [TestMethod]
    public void ASizedDelimiterDoesNotShrinkWithTheStyle() => UiThread.Run(() =>
    {
        // TeX gives \big and its friends absolute lengths rather than sizes relative to the style, so a \Big(
        // inside a script is the delimiter it was outside one.
        Assert.IsTrue(TotalHeight(@"\scriptstyle(") < TotalHeight(@"("), "a plain delimiter does shrink");
        Assert.IsTrue(TotalHeight(@"\scriptstyle\Big(") >= TotalHeight(@"\Big("), @"a \Big one should not");
    });

    [TestMethod]
    public void TheMSpellingSpacesAsARelation() => UiThread.Run(() =>
    {
        // Same delimiter at the same size either way; what the l/r/m spellings change is the class, and so the
        // space around it.
        Assert.AreEqual(TotalHeight(@"\bigl("), TotalHeight(@"\bigr("), 1e-6);
        Assert.IsTrue(Width(@"x \bigm| y") > Width(@"x \big| y"), "a relation takes more space around it");
    });

    [TestMethod]
    public void ASizedDelimiterNeedsSomethingThatIsADelimiter() => UiThread.Run(() =>
        Assert.AreNotEqual(0, Undrawn(@"\big x").Count));

    // ── \genfrac ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GenfracWithNothingAskedForIsFrac() => UiThread.Run(() =>
    {
        Assert.AreEqual(Width(@"\frac{n}{k}"), Width(@"\genfrac{}{}{}{}{n}{k}"), 1e-6);
        Assert.AreEqual(TotalHeight(@"\frac{n}{k}"), TotalHeight(@"\genfrac{}{}{}{}{n}{k}"), 1e-6);
    });

    [TestMethod]
    [DataRow(@"\genfrac{(}{)}{0pt}{4}{n}{k}", @"\genfrac{(}{)}{0pt}{}{n}{k}")]   // there is no style 4
    [DataRow(@"\genfrac{(}{)}{banana}{}{n}{k}", @"\genfrac{(}{)}{}{}{n}{k}")]    // not a length
    [DataRow(@"\genfrac{(}{)}{1}{}{n}{k}", @"\genfrac{(}{)}{}{}{n}{k}")]         // a number with no unit
    public void GenfracFallsBackWhereItCannotReadAThicknessOrAStyle(string markup, string asIf) => UiThread.Run(() =>
    {
        // It used to refuse the formula outright. It now sets the fraction as though the argument had been left
        // empty — and does so silently, which is the one place where something unreadable leaves no mark on the
        // formula at all.
        Assert.AreEqual(0, Undrawn(markup).Count);
        Assert.AreEqual(Width(asIf), Width(markup), 1e-6);
        Assert.AreEqual(TotalHeight(asIf), TotalHeight(markup), 1e-6);
    });

    // ── escaped braces inside a group ────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"{\{}")]
    [DataRow(@"\frac{\{}{x}")]
    [DataRow(@"\text{\{}")]
    [DataRow(@"\genfrac{\{}{\}}{0pt}{}{n}{k}")]
    public void AnEscapedBraceInsideAGroupIsACharacterNotANestingLevel(string markup) => UiThread.Run(() =>
        // \genfrac{\{}{\}} is what turned this up: the } of \} was closing the group it sat in, so every one of
        // these was an "Illegal end, missing '}'".
        Renders(markup));

    [TestMethod]
    public void AnEscapedBraceDoesNotCloseTheGroupAroundIt() => UiThread.Run(() =>
        Assert.AreEqual(Width(@"\{"), Width(@"{\{}"), 1e-6));
}
