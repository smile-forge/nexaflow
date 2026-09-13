using Nexaflow.Tests.Fixtures;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// The AMS symbol fonts: jlm_msam10 (symbols A, long bundled) and jlm_msbm10 (symbols B, added with the
/// blackboard-bold alphabet and the real negated relations), the formal script and the Computer Modern alphabets,
/// and \boldsymbol.
/// <para>
/// Every name here has to reach a glyph, not merely read: a wrong code in DefaultTexFont.xml still reads as a symbol
/// and only fails when it is set.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class AmsSymbolFontTests
{
    // ── msbm10: the negated relations, previously overlaid with \not ──────────────

    [TestMethod]
    [DataRow(@"\nless")]
    [DataRow(@"\ngtr")]
    [DataRow(@"\nleq")]
    [DataRow(@"\ngeq")]
    [DataRow(@"\nleqq")]
    [DataRow(@"\ngeqq")]
    [DataRow(@"\nleqslant")]
    [DataRow(@"\ngeqslant")]
    [DataRow(@"\nprec")]
    [DataRow(@"\nsucc")]
    [DataRow(@"\npreceq")]
    [DataRow(@"\nsucceq")]
    [DataRow(@"\nsim")]
    [DataRow(@"\ncong")]
    [DataRow(@"\nmid")]
    [DataRow(@"\nparallel")]
    [DataRow(@"\nvdash")]
    [DataRow(@"\nvDash")]
    [DataRow(@"\nVdash")]
    [DataRow(@"\nVDash")]
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
    [DataRow(@"\nexists")]
    [DataRow(@"\nshortmid")]
    [DataRow(@"\nshortparallel")]
    public void NegatedRelationsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    [DataRow(@"\nleq")]
    [DataRow(@"\nsubseteq")]
    public void ANegatedRelationIsOneGlyphNotANotOverlay(string markup) => UiThread.Run(() =>
        // These used to be predefined formulas composing \not with the base relation, which came out as two
        // glyphs laid over each other. msbm10 has the real glyph.
        Assert.AreEqual(1, Marks(markup).Count));

    // ── msbm10: the strict/vertical negations that had no approximation at all ────

    [TestMethod]
    [DataRow(@"\subsetneq")]
    [DataRow(@"\supsetneq")]
    [DataRow(@"\subsetneqq")]
    [DataRow(@"\supsetneqq")]
    [DataRow(@"\varsubsetneq")]
    [DataRow(@"\varsupsetneq")]
    [DataRow(@"\varsubsetneqq")]
    [DataRow(@"\varsupsetneqq")]
    [DataRow(@"\lneq")]
    [DataRow(@"\gneq")]
    [DataRow(@"\lneqq")]
    [DataRow(@"\gneqq")]
    [DataRow(@"\lvertneqq")]
    [DataRow(@"\gvertneqq")]
    [DataRow(@"\lnsim")]
    [DataRow(@"\gnsim")]
    [DataRow(@"\lnapprox")]
    [DataRow(@"\gnapprox")]
    [DataRow(@"\precneqq")]
    [DataRow(@"\succneqq")]
    [DataRow(@"\precnsim")]
    [DataRow(@"\succnsim")]
    [DataRow(@"\precnapprox")]
    [DataRow(@"\succnapprox")]
    public void StrictNegationsRender(string markup) => UiThread.Run(() => Renders(markup));

    // ── msbm10: relations, operators and letter-likes ────────────────────────────

    [TestMethod]
    [DataRow(@"\approxeq")]
    [DataRow(@"\eqsim")]
    [DataRow(@"\thicksim")]
    [DataRow(@"\thickapprox")]
    [DataRow(@"\precapprox")]
    [DataRow(@"\succapprox")]
    [DataRow(@"\lessdot")]
    [DataRow(@"\gtrdot")]
    [DataRow(@"\shortmid")]
    [DataRow(@"\shortparallel")]
    [DataRow(@"\backepsilon")]
    [DataRow(@"\curvearrowleft")]
    [DataRow(@"\curvearrowright")]
    [DataRow(@"\ltimes")]
    [DataRow(@"\rtimes")]
    [DataRow(@"\divideontimes")]
    [DataRow(@"\smallsetminus")]
    [DataRow(@"\diagup")]
    [DataRow(@"\diagdown")]
    [DataRow(@"\varnothing")]
    [DataRow(@"\hslash")]
    [DataRow(@"\eth")]
    [DataRow(@"\Bbbk")]
    [DataRow(@"\Finv")]
    [DataRow(@"\Game")]
    [DataRow(@"\digamma")]
    [DataRow(@"\varkappa")]
    [DataRow(@"\beth")]
    [DataRow(@"\gimel")]
    [DataRow(@"\daleth")]
    public void MsbmSymbolsRender(string markup) => UiThread.Run(() => Renders(markup));

    // ── msbm10: blackboard bold ──────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\mathbb{R}")]
    [DataRow(@"\mathbb{N}")]
    [DataRow(@"\mathbb{ZQC}")]
    [DataRow(@"\mathbb{ABCDEFGHIJKLMNOPQRSTUVWXYZ}")]
    public void BlackboardBoldRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void BlackboardBoldUsesTheMsbmFontNotAStandIn() => UiThread.Run(() =>
        // It used to be mapped onto upright roman, so this is what tells the difference.
        Assert.AreNotEqual(Width(@"\mathrm{R}"), Width(@"\mathbb{R}")));

    [TestMethod]
    public void BlackboardBoldHasCapitalsOnly() => UiThread.Run(() =>
    {
        // msbm10 carries no lowercase or digits, so those fall through to the default mapping rather than failing —
        // the same way \mathcal behaves.
        Renders(@"\mathbb{r}");
        Renders(@"\mathbb{1}");
    });

    // ── rsfs10: the formal script alphabet ───────────────────────────────────────

    [TestMethod]
    [DataRow(@"\mathscr{L}")]
    [DataRow(@"\mathscr{F}")]
    [DataRow(@"\mathscr{ABCDEFGHIJKLMNOPQRSTUVWXYZ}")]
    public void FormalScriptRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void FormalScriptIsADifferentAlphabetFromCalligraphic() => UiThread.Run(() =>
        // \mathscr used to be pointed at the symbol font's calligraphic capitals, i.e. at \mathcal. Ralph Smith's
        // Formal Script is its own face.
        Assert.AreNotEqual(Width(@"\mathcal{L}"), Width(@"\mathscr{L}")));

    [TestMethod]
    public void FormalScriptHasCapitalsOnly() => UiThread.Run(() =>
    {
        // rsfs10 carries no lowercase or digits; those fall through to the default mapping.
        Renders(@"\mathscr{l}");
        Renders(@"\mathscr{1}");
    });

    // ── the Computer Modern and Euler alphabets ──────────────────────────────────

    [TestMethod]
    [DataRow(@"\mathbf{Abc123}")]
    [DataRow(@"\textbf{Abc 123}")]
    [DataRow(@"\mathsf{Abc123}")]
    [DataRow(@"\textsf{Abc 123}")]
    [DataRow(@"\mathtt{Abc123}")]
    [DataRow(@"\texttt{Abc 123}")]
    [DataRow(@"\textit{Abc 123}")]
    
    [DataRow(@"\mathfrak{ABCabc}")]
    [DataRow(@"\mathrm{Abc}")]
    [DataRow(@"\textrm{Abc}")]
    public void TheAlphabetsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    [DataRow(@"\mathbf{Hamburgefons}")]
    [DataRow(@"\mathsf{Hamburgefons}")]
    [DataRow(@"\mathtt{Hamburgefons}")]
    [DataRow(@"\mathfrak{Hamburgefons}")]
    public void EachAlphabetHasItsOwnFaceNotARomanStandIn(string markup) => UiThread.Run(() =>
        // Every one of these used to be mapped onto plain roman, so they all drew the same thing. Different faces
        // set the same word to different widths.
        Assert.AreNotEqual(Width(@"\mathrm{Hamburgefons}"), Width(markup)));

    [TestMethod]
    public void ItalicTextIsTheTextItalicFaceNotTheMathsOne() => UiThread.Run(() =>
        // \textit had been pointed at cmmi10 — maths italic, which spaces letters as though each were a separate
        // variable.
        Assert.AreNotEqual(Width(@"\mathit{difference}"), Width(@"\textit{difference}")));

    [TestMethod]
    public void TypewriterIsMonospaced() => UiThread.Run(() =>
        Assert.AreEqual(Width(@"\mathtt{mmm}"), Width(@"\mathtt{iii}"), 1e-6));

    // ── \boldsymbol ──────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\boldsymbol{x}")]
    [DataRow(@"\boldsymbol{\alpha}")]
    [DataRow(@"\boldsymbol{\Gamma}")]
    [DataRow(@"\boldsymbol{\nabla}")]
    [DataRow(@"\boldsymbol{abc + \beta\gamma}")]
    
    [DataRow(@"\boldsymbol{\frac{\alpha}{\beta}}")]
    [DataRow(@"\boldsymbol{x}^{\boldsymbol{2}}")]
    public void BoldsymbolRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    [DataRow(@"x")]          // a Latin variable, from the maths italic
    [DataRow(@"\alpha")]     // a Greek letter, chosen by name
    [DataRow(@"\beta")]
    [DataRow(@"\nabla")]     // a symbol, from the symbol font
    [DataRow(@"\infty")]
    public void BoldsymbolReachesCharactersATextStyleCouldNot(string markup) => UiThread.Run(() =>
        // Greek letters and symbols are resolved by name out of the maths and symbol fonts, so no text style could
        // ever have made them bold. Each has to come out wider than its plain form, because it now comes from the
        // bold companion font.
        Assert.IsTrue(Width(@"\boldsymbol{" + markup + "}") > Width(markup), $"{markup} did not get any bolder"));

    [TestMethod]
    public void BoldsymbolAppliesToTheWholeSubtree() => UiThread.Run(() =>
        // Not just the first character: bold travels down the environment.
        Assert.IsTrue(Width(@"\boldsymbol{\alpha\beta\gamma}") > Width(@"\alpha\beta\gamma")));

    [TestMethod]
    public void BoldsymbolEndsWithItsArgument() => UiThread.Run(() =>
        Assert.AreEqual(Width(@"\boldsymbol{\alpha}") + Width(@"\beta"), Width(@"\boldsymbol{\alpha}\beta"), 1e-6));

    [TestMethod]
    public void ACharacterWithNoBoldCompanionIsLeftAsItIs() => UiThread.Run(() =>
    {
        // The AMS symbol fonts have no bold face, so \boldsymbol has to leave them alone rather than fail to find a
        // glyph.
        Renders(@"\boldsymbol{\subsetneq}");
        Renders(@"\boldsymbol{\mathbb{R}}");
        Assert.AreEqual(Width(@"\subsetneq"), Width(@"\boldsymbol{\subsetneq}"), 1e-6);
    });

    // ── msam10: names that were always available, just never mapped ──────────────

    [TestMethod]
    [DataRow(@"\circledR")]
    [DataRow(@"\dashrightarrow")]
    [DataRow(@"\dashleftarrow")]
    [DataRow(@"\dasharrow")]
    public void TheMsamStragglersRender(string markup) => UiThread.Run(() => Renders(markup));

    // ── in context ───────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\mathbb{R}^n \subsetneq \mathbb{C}^n")]
    [DataRow(@"a \nleq b \nsubseteq C")]
    [DataRow(@"\aleph_0 < \beth_1 \leq \gimel_2")]
    [DataRow(@"f: \mathbb{N} \dashrightarrow \mathbb{Q}")]
    [DataRow(@"\varnothing \neq \{x \in \mathbb{Z} : x \gneqq 0\}")]
    public void FormulasMixingTheNewSymbolsRender(string markup) => UiThread.Run(() => Renders(markup));
}
