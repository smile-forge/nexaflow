using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// Commands a corpus of published papers reached for that nothing here had: two arrows, two spaces, the retyping
/// family, and the characters that have to be escaped to be written at all (tools/latex-corpus).
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class CorpusGapTests
{
    // ── arrows ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\longleftrightarrow")]
    [DataRow(@"\longmapsto")]
    [DataRow(@"\hookleftarrow")]
    public void TheArrowsThatWereMissingRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void ALongArrowIsLongerThanItsShortForm() => UiThread.Run(() =>
    {
        // Each is built by butting a shaft against an arrowhead, the way the ones already here are.
        Assert.IsTrue(Width(@"\longleftrightarrow") > Width(@"\leftrightarrow"));
        Assert.IsTrue(Width(@"\longmapsto") > Width(@"\mapsto"));
    });

    [TestMethod]
    public void HookleftarrowIsTheMirrorOfHookrightarrow() => UiThread.Run(() =>
        Assert.AreEqual(Width(@"\hookrightarrow"), Width(@"\hookleftarrow"), 0.1));

    // ── spaces ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheSpacesAreTheWidthsTheyAreNamedFor() => UiThread.Run(() =>
    {
        // \enspace is half a quad, and an interword space is narrower again.
        static double Gap(string markup) => Width("a " + markup + " b") - Width("a b");
        Assert.AreEqual(Gap(@"\qquad") / 4.0, Gap(@"\enspace"), 1e-6);
        Assert.IsTrue(Gap(@"\space") < Gap(@"\enspace"));
        Assert.IsTrue(Gap(@"\space") > 0.0);
    });

    [TestMethod]
    public void MspaceIsHspaceInMathUnits() => UiThread.Run(() =>
    {
        Assert.AreEqual(Width(@"a\hspace{18mu}b"), Width(@"a\mspace{18mu}b"), 1e-6);
        Assert.IsTrue(Width(@"a\mspace{18mu}b") > Width(@"a\mspace{9mu}b"));
    });

    // ── retyping ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\mathord", "x")]
    [DataRow(@"\mathop", "x")]
    [DataRow(@"\mathbin", "x")]
    [DataRow(@"\mathrel", "x")]
    [DataRow(@"\mathopen", "[")]
    [DataRow(@"\mathclose", "]")]
    [DataRow(@"\mathpunct", ",")]
    [DataRow(@"\mathinner", "x")]
    public void ARetypingCommandKeepsItsArgument(string command, string content) => UiThread.Run(() =>
        Renders(command + "{" + content + "}"));

    [TestMethod]
    public void RetypingIsWhatChangesTheSpaceAroundASymbol() => UiThread.Run(() =>
    {
        // The whole reason the family exists: same glyph, different spacing either side of it.
        Assert.IsTrue(Width(@"a \mathrel{x} b") > Width(@"a \mathord{x} b"));
        Assert.IsTrue(Width(@"a \mathbin{x} b") > Width(@"a \mathord{x} b"));
    });

    [TestMethod]
    public void MathopTakesALimitRatherThanAScript() => UiThread.Run(() =>
        // \mathop{argmax}_x is the reason a paper writes it: the x becomes a limit under the name.
        Assert.AreNotEqual(Width(@"\mathord{argmax}_{x}"), Width(@"\mathop{argmax}_{x}")));

    // ── escaped literals ─────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\#")]
    [DataRow(@"\$")]
    [DataRow(@"\%")]
    [DataRow(@"\&")]
    [DataRow(@"\_")]
    public void AnEscapedLiteralRenders(string markup) => UiThread.Run(() =>
        Assert.IsTrue(Width("a" + markup + "b") > Width("ab")));

    [TestMethod]
    public void TheUnderscoreIsDrawnThereBeingNoGlyphForIt() => UiThread.Run(() =>
    {
        // OT1 has no underscore, so LaTeX draws a short rule under the baseline; so does this.
        var marks = Marks(@"\_");
        Assert.AreEqual(1, marks.Count);
        Assert.IsInstanceOfType<RuleMark>(marks.Single().Mark);
        Assert.IsTrue(Width(@"\_") > 0.0);
    });
}
