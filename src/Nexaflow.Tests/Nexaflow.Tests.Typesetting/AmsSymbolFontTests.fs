namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils
open XamlMath
open XamlMath.Atoms

// Tests for the AMS symbol fonts: jlm_msam10 (symbols A, long bundled) and jlm_msbm10
// (symbols B, added with the blackboard-bold alphabet and the real negated relations).
//
// Every name here has to reach a glyph, not merely parse: a wrong code in DefaultTexFont.xml
// still parses to a SymbolAtom and only fails when a box is built for it.
type AmsSymbolFontTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    /// Parses the markup and builds its box, which is where a missing glyph would surface.
    static let renders (markup: string) =
        let root = (parse markup).RootAtom
        Assert.NotNull(root)
        Assert.NotNull(root.CreateBox(environment))

    static let widthOf (markup: string) = (parse markup).RootAtom.CreateBox(environment).Width

    // ── msbm10: the negated relations, previously overlaid with \not ──────────────

    [<Theory>]
    [<InlineData(@"\nless")>]
    [<InlineData(@"\ngtr")>]
    [<InlineData(@"\nleq")>]
    [<InlineData(@"\ngeq")>]
    [<InlineData(@"\nleqq")>]
    [<InlineData(@"\ngeqq")>]
    [<InlineData(@"\nleqslant")>]
    [<InlineData(@"\ngeqslant")>]
    [<InlineData(@"\nprec")>]
    [<InlineData(@"\nsucc")>]
    [<InlineData(@"\npreceq")>]
    [<InlineData(@"\nsucceq")>]
    [<InlineData(@"\nsim")>]
    [<InlineData(@"\ncong")>]
    [<InlineData(@"\nmid")>]
    [<InlineData(@"\nparallel")>]
    [<InlineData(@"\nvdash")>]
    [<InlineData(@"\nvDash")>]
    [<InlineData(@"\nVdash")>]
    [<InlineData(@"\nVDash")>]
    [<InlineData(@"\nsubseteq")>]
    [<InlineData(@"\nsupseteq")>]
    [<InlineData(@"\nsubseteqq")>]
    [<InlineData(@"\nsupseteqq")>]
    [<InlineData(@"\ntriangleleft")>]
    [<InlineData(@"\ntriangleright")>]
    [<InlineData(@"\ntrianglelefteq")>]
    [<InlineData(@"\ntrianglerighteq")>]
    [<InlineData(@"\nleftarrow")>]
    [<InlineData(@"\nrightarrow")>]
    [<InlineData(@"\nLeftarrow")>]
    [<InlineData(@"\nRightarrow")>]
    [<InlineData(@"\nleftrightarrow")>]
    [<InlineData(@"\nLeftrightarrow")>]
    [<InlineData(@"\nexists")>]
    [<InlineData(@"\nshortmid")>]
    [<InlineData(@"\nshortparallel")>]
    member _.``negated relations render``(markup: string) = renders markup

    [<Fact>]
    member _.``a negated relation is one glyph, not a \not overlay``() =
        // These used to be predefined formulas composing \not with the base relation, which came
        // out as a row of two atoms. msbm10 has the real glyph.
        Assert.IsType<SymbolAtom>((parse @"\nleq").RootAtom) |> ignore
        Assert.IsType<SymbolAtom>((parse @"\nsubseteq").RootAtom) |> ignore

    // ── msbm10: the strict/vertical negations that had no approximation at all ────

    [<Theory>]
    [<InlineData(@"\subsetneq")>]
    [<InlineData(@"\supsetneq")>]
    [<InlineData(@"\subsetneqq")>]
    [<InlineData(@"\supsetneqq")>]
    [<InlineData(@"\varsubsetneq")>]
    [<InlineData(@"\varsupsetneq")>]
    [<InlineData(@"\varsubsetneqq")>]
    [<InlineData(@"\varsupsetneqq")>]
    [<InlineData(@"\lneq")>]
    [<InlineData(@"\gneq")>]
    [<InlineData(@"\lneqq")>]
    [<InlineData(@"\gneqq")>]
    [<InlineData(@"\lvertneqq")>]
    [<InlineData(@"\gvertneqq")>]
    [<InlineData(@"\lnsim")>]
    [<InlineData(@"\gnsim")>]
    [<InlineData(@"\lnapprox")>]
    [<InlineData(@"\gnapprox")>]
    [<InlineData(@"\precneqq")>]
    [<InlineData(@"\succneqq")>]
    [<InlineData(@"\precnsim")>]
    [<InlineData(@"\succnsim")>]
    [<InlineData(@"\precnapprox")>]
    [<InlineData(@"\succnapprox")>]
    member _.``strict negations render``(markup: string) = renders markup

    // ── msbm10: relations, operators and letter-likes ────────────────────────────

    [<Theory>]
    [<InlineData(@"\approxeq")>]
    [<InlineData(@"\eqsim")>]
    [<InlineData(@"\thicksim")>]
    [<InlineData(@"\thickapprox")>]
    [<InlineData(@"\precapprox")>]
    [<InlineData(@"\succapprox")>]
    [<InlineData(@"\lessdot")>]
    [<InlineData(@"\gtrdot")>]
    [<InlineData(@"\shortmid")>]
    [<InlineData(@"\shortparallel")>]
    [<InlineData(@"\backepsilon")>]
    [<InlineData(@"\curvearrowleft")>]
    [<InlineData(@"\curvearrowright")>]
    [<InlineData(@"\ltimes")>]
    [<InlineData(@"\rtimes")>]
    [<InlineData(@"\divideontimes")>]
    [<InlineData(@"\smallsetminus")>]
    [<InlineData(@"\diagup")>]
    [<InlineData(@"\diagdown")>]
    [<InlineData(@"\varnothing")>]
    [<InlineData(@"\hslash")>]
    [<InlineData(@"\eth")>]
    [<InlineData(@"\Bbbk")>]
    [<InlineData(@"\Finv")>]
    [<InlineData(@"\Game")>]
    [<InlineData(@"\digamma")>]
    [<InlineData(@"\varkappa")>]
    [<InlineData(@"\beth")>]
    [<InlineData(@"\gimel")>]
    [<InlineData(@"\daleth")>]
    member _.``msbm symbols render``(markup: string) = renders markup

    // ── msbm10: blackboard bold ──────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\mathbb{R}")>]
    [<InlineData(@"\mathbb{N}")>]
    [<InlineData(@"\mathbb{ZQC}")>]
    [<InlineData(@"\mathbb{ABCDEFGHIJKLMNOPQRSTUVWXYZ}")>]
    member _.``blackboard bold renders``(markup: string) = renders markup

    [<Fact>]
    member _.``blackboard bold uses the msbm font, not a stand-in``() =
        // It used to be mapped onto upright roman, so this is what tells the difference.
        let bb = (parse @"\mathbb{R}").RootAtom.CreateBox(environment)
        let rm = (parse @"\mathrm{R}").RootAtom.CreateBox(environment)
        Assert.NotEqual(rm.Width, bb.Width)

    [<Fact>]
    member _.``blackboard bold has capitals only``() =
        // msbm10 carries no lowercase or digits, so those fall through to the default mapping
        // rather than failing - the same way \mathcal behaves.
        renders @"\mathbb{r}"
        renders @"\mathbb{1}"

    // ── rsfs10: the formal script alphabet ───────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\mathscr{L}")>]
    [<InlineData(@"\mathscr{F}")>]
    [<InlineData(@"\mathscr{ABCDEFGHIJKLMNOPQRSTUVWXYZ}")>]
    member _.``formal script renders``(markup: string) = renders markup

    [<Fact>]
    member _.``formal script is a different alphabet from calligraphic``() =
        // \mathscr used to be pointed at the symbol font's calligraphic capitals, i.e. at \mathcal.
        // Ralph Smith's Formal Script is its own face.
        let scr = (parse @"\mathscr{L}").RootAtom.CreateBox(environment)
        let cal = (parse @"\mathcal{L}").RootAtom.CreateBox(environment)
        Assert.NotEqual(cal.Width, scr.Width)

    [<Fact>]
    member _.``formal script has capitals only``() =
        // rsfs10 carries no lowercase or digits; those fall through to the default mapping.
        renders @"\mathscr{l}"
        renders @"\mathscr{1}"

    // ── the Computer Modern and Euler alphabets ──────────────────────────────────

    [<Theory>]
    [<InlineData(@"\mathbf{Abc123}")>]
    [<InlineData(@"\textbf{Abc 123}")>]
    [<InlineData(@"\mathsf{Abc123}")>]
    [<InlineData(@"\textsf{Abc 123}")>]
    [<InlineData(@"\mathtt{Abc123}")>]
    [<InlineData(@"\texttt{Abc 123}")>]
    [<InlineData(@"\textit{Abc 123}")>]
    [<InlineData(@"\textsc{Abc 123}")>]
    [<InlineData(@"\mathfrak{ABCabc}")>]
    [<InlineData(@"\mathrm{Abc}")>]
    [<InlineData(@"\textrm{Abc}")>]
    member _.``the alphabets render``(markup: string) = renders markup

    [<Theory>]
    [<InlineData(@"\mathbf{Hamburgefons}")>]
    [<InlineData(@"\mathsf{Hamburgefons}")>]
    [<InlineData(@"\mathtt{Hamburgefons}")>]
    [<InlineData(@"\mathfrak{Hamburgefons}")>]
    member _.``each alphabet has its own face, not a roman stand-in``(markup: string) =
        // Every one of these used to be mapped onto plain roman, so they all drew the same thing.
        // Different faces set the same word to different widths.
        let roman = (parse @"\mathrm{Hamburgefons}").RootAtom.CreateBox(environment)
        let box = (parse markup).RootAtom.CreateBox(environment)
        Assert.NotEqual(roman.Width, box.Width)

    [<Fact>]
    member _.``italic text is the text italic face, not the maths one``() =
        // \textit had been pointed at cmmi10 - maths italic, which spaces letters as though each
        // were a separate variable.
        let textIt = (parse @"\textit{difference}").RootAtom.CreateBox(environment)
        let mathIt = (parse @"\mathit{difference}").RootAtom.CreateBox(environment)
        Assert.NotEqual(mathIt.Width, textIt.Width)

    [<Fact>]
    member _.``small caps really are small capitals``() =
        // cmcsc10 keeps its small capitals in the lowercase slots, so lowercase input comes out as
        // capitals that are shorter than the real ones.
        let small = (parse @"\textsc{a}").RootAtom.CreateBox(environment)
        let capital = (parse @"\textsc{A}").RootAtom.CreateBox(environment)
        Assert.True(small.Height < capital.Height, "small caps should be shorter than capitals")
        Assert.True(small.Height > (parse @"\textrm{a}").RootAtom.CreateBox(environment).Height,
                    "small caps should be taller than lowercase roman")

    [<Fact>]
    member _.``typewriter is monospaced``() =
        let narrow = (parse @"\mathtt{iii}").RootAtom.CreateBox(environment)
        let wide = (parse @"\mathtt{mmm}").RootAtom.CreateBox(environment)
        Assert.Equal(wide.Width, narrow.Width, 6)

    // ── \boldsymbol ──────────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\boldsymbol{x}")>]
    [<InlineData(@"\boldsymbol{\alpha}")>]
    [<InlineData(@"\boldsymbol{\Gamma}")>]
    [<InlineData(@"\boldsymbol{\nabla}")>]
    [<InlineData(@"\boldsymbol{abc + \beta\gamma}")>]
    [<InlineData(@"\bm{\theta}")>]
    [<InlineData(@"\boldsymbol{\frac{\alpha}{\beta}}")>]
    [<InlineData(@"\boldsymbol{x}^{\boldsymbol{2}}")>]
    member _.``boldsymbol renders``(markup: string) = renders markup

    [<Theory>]
    [<InlineData(@"x")>]                 // a Latin variable, from the maths italic
    [<InlineData(@"\alpha")>]            // a Greek letter, chosen by name
    [<InlineData(@"\beta")>]
    [<InlineData(@"\nabla")>]            // a symbol, from the symbol font
    [<InlineData(@"\infty")>]
    member _.``boldsymbol reaches characters a text style could not``(markup: string) =
        // The point of the flag: Greek letters and symbols are resolved by name out of the maths and
        // symbol fonts, so no text style could ever have made them bold. Each has to come out wider
        // than its plain form, because it now comes from the bold companion font.
        Assert.True(widthOf (@"\boldsymbol{" + markup + "}") > widthOf markup,
                    $"{markup} did not get any bolder")

    [<Fact>]
    member _.``boldsymbol applies to the whole subtree``() =
        // Not just the first character: the flag travels down the environment.
        Assert.True(widthOf @"\boldsymbol{\alpha\beta\gamma}" > widthOf @"\alpha\beta\gamma")

    [<Fact>]
    member _.``boldsymbol ends with its argument``() =
        Assert.Equal(widthOf @"\boldsymbol{\alpha}\beta", widthOf @"\boldsymbol{\alpha}" + widthOf @"\beta", 6)

    [<Fact>]
    member _.``a character with no bold companion is left as it is``() =
        // The AMS symbol fonts have no bold face, so \boldsymbol has to leave them alone rather than
        // fail to find a glyph.
        renders @"\boldsymbol{\subsetneq}"
        renders @"\boldsymbol{\mathbb{R}}"
        Assert.Equal(widthOf @"\boldsymbol{\subsetneq}", widthOf @"\subsetneq", 6)

    // ── msam10: names that were always available, just never mapped ──────────────

    [<Theory>]
    [<InlineData(@"\circledR")>]
    [<InlineData(@"\dashrightarrow")>]
    [<InlineData(@"\dashleftarrow")>]
    [<InlineData(@"\dasharrow")>]
    member _.``the msam10 stragglers render``(markup: string) = renders markup

    // ── in context ───────────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\mathbb{R}^n \subsetneq \mathbb{C}^n")>]
    [<InlineData(@"a \nleq b \nsubseteq C")>]
    [<InlineData(@"\aleph_0 < \beth_1 \leq \gimel_2")>]
    [<InlineData(@"f: \mathbb{N} \dashrightarrow \mathbb{Q}")>]
    [<InlineData(@"\varnothing \neq \{x \in \mathbb{Z} : x \gneqq 0\}")>]
    member _.``formulas mixing the new symbols render``(markup: string) = renders markup
