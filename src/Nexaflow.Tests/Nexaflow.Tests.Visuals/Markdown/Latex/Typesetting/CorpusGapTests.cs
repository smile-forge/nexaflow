using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;
using Nexaflow.Markdown.Latex;

using Nexaflow.Markdown.Layout;

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

    // ── colons ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnOperatorsLoneCharacterHasItsMiddleOnTheAxis() => UiThread.Run(() =>
    {
        // TeX's rule 13, and the whole of what \vcentcolon is: a colon standing on the baseline, lifted until its middle
        // is the axis — and the same rule still hangs a big operator from the extension font about it.
        var axis = Environment().MathFont.GetAxisHeight(TexStyle.Display);

        // Lifted clear of the baseline, its box keeps no depth — TeX's never goes below zero — so it is measured from the top.
        foreach (var (operated, plain) in new[] { (@"\mathop{:}", ":"), (@"\vcentcolon", ":"), (@"\mathop{x}", "x") })
            Assert.AreEqual(axis, Height(operated) - TotalHeight(plain) / 2, 1e-6, operated);

        // The extension font's own operators straddle the axis, so their box shows it either way.
        Assert.AreEqual(axis, (Height(@"\sum") - Depth(@"\sum")) / 2, 1e-6);
    });

    [TestMethod]
    [DataRow(@"\vcentcolon")]
    [DataRow(@"\dblcolon")]
    [DataRow(@"\coloneqq")]
    [DataRow(@"\Coloneqq")]
    [DataRow(@"\eqqcolon")]
    [DataRow(@"\Eqqcolon")]
    [DataRow(@"\colonapprox")]
    [DataRow(@"\Approxcolon")]
    [DataRow(@"\colonsim")]
    [DataRow(@"\Simcolon")]
    [DataRow(@"\colondash")]
    [DataRow(@"\Dashcolon")]
    [DataRow(@"a_n \coloneqq \inf \left\{a_k \mid k \geq n\right\}")]
    public void TheMathtoolsColonsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void AColonIsButtedAgainstTheRelationBesideIt() => UiThread.Run(() =>
    {
        // mathtools pulls the pair 1.2mu closer than two relations side by side would stand, and the colons of a double one 0.9mu.
        static double Mu(double amount) => Width(@"a\mspace{" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "mu}b") - Width("ab");

        Assert.AreEqual(Mu(1.2), Width(@"\vcentcolon=") - Width(@"\coloneqq"), 1e-6);
        Assert.AreEqual(Mu(0.9), Width(@"\vcentcolon\vcentcolon") - Width(@"\dblcolon"), 1e-6);

        // The pairs are each other's mirror, and mathtools' default branch makes \coloneq the same sign as \coloneqq.
        Assert.AreEqual(Width(@"\coloneqq"), Width(@"\eqqcolon"), 1e-6);
        Assert.AreEqual(Width(@"\coloneqq"), Width(@"\coloneq"), 1e-6);
    });

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
        Assert.IsInstanceOfType<PortableRuleMark>(marks.Single().Mark);
        Assert.IsTrue(Width(@"\_") > 0.0);
    });
}
