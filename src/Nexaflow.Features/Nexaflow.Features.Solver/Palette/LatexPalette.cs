namespace Nexaflow.Features.Solver.Palette;

/// <summary>
/// The LaTeX symbol tree: eight categories, each holding up to eight groups of eight symbols.
/// <para>
/// The shape is dictated by the navigator, which draws a fixed ring of eight positions and is the
/// <i>only</i> way into the palette — the symbols are the last ring rather than a grid off to one
/// side. So the tree is authored to eight at every level, and a level with less to say holds its
/// spare positions open rather than spreading out to fill them: the ring stays the same size and in
/// the same place, so drilling never moves the thing you were about to click.
/// </para>
/// <para>
/// There is no mode selector above the categories. Four modes existed only to keep the old top level
/// inside eight, which is a constraint of the drawing rather than anything a reader would recognise;
/// the categories are the real top level, and the recently-used strip is what makes the symbols you
/// actually use one click away.
/// </para>
/// <para>
/// Every command here is one the reference pages under <c>test-samples/markdown/latex-math-*.md</c>
/// state that the engine renders. Nothing purely typographic is offered — style switches, phantoms,
/// spacing, frames and font commands change how a formula is set rather than what it says, and a
/// palette that hands you those is a palette you have to read past to find the maths.
/// </para>
/// </summary>
public static class LatexPalette
{
    /// <summary>Positions on one ring of the navigator. Every level of this tree is authored to it.</summary>
    public const int Slots = 8;

    // ── Basics ──────────────────────────────────────────────────────────────
    private static readonly PaletteGroup Basics = Category("latex.basics", "Basics",
    [
        Leaf("latex.basics.frac", "Fractions",
        [
            F("a/b", @"\frac{}{}", 3), F("a／b", @"\dfrac{}{}", 3), F("ᵃ⁄ᵇ", @"\tfrac{}{}", 3),
            F("⅟₊", @"\cfrac{}{}", 3), F("¹⁄₂", @"\nicefrac{}{}", 3), F("ₙCᵣ", @"\binom{}{}", 3),
            F("√", @"\sqrt{}", 1), O("/", "/"),
        ]),
        Leaf("latex.basics.ops", "Operators",
        [
            O("+", "+"), O("−", "-"), O("×", @"\times"), O("÷", @"\div"),
            O("⋅", @"\cdot"), O("±", @"\pm"), O("∓", @"\mp"), O("∘", @"\circ"),
        ]),
        Leaf("latex.basics.rel", "Relations",
        [
            O("=", "="), O("≠", @"\neq"), O("≈", @"\approx"), O("≡", @"\equiv"),
            O("<", "<"), O(">", ">"), O("≤", @"\leq"), O("≥", @"\geq"),
        ]),
        Leaf("latex.basics.brackets", "Brackets",
        [
            O("(", "("), O(")", ")"), O("[", "["), O("]", "]"),
            O("{", @"\{"), O("}", @"\}"), F("(  )", @"\left( \right)", 8), F("|  |", @"\left| \right|", 8),
        ]),
        Leaf("latex.basics.powers", "Powers",
        [
            F("a²", "^{2}"), F("a³", "^{3}"), F("aᵇ", "^{}", 1), F("aᵢ", "_{}", 1),
            F("√", @"\sqrt{}", 1), F("∛", @"\sqrt[3]{}", 1), F("ⁿ√", @"\sqrt[]{}", 3), F("eˣ", "e^{}", 1),
        ]),
        Leaf("latex.basics.greek", "Greek",
        [
            S("α", @"\alpha"), S("β", @"\beta"), S("θ", @"\theta"), S("π", @"\pi"),
            S("λ", @"\lambda"), S("μ", @"\mu"), S("σ", @"\sigma"), S("ω", @"\omega"),
        ]),
        Leaf("latex.basics.sets", "Sets",
        [
            S("∈", @"\in"), S("∉", @"\notin"), S("⊂", @"\subset"), S("⊆", @"\subseteq"),
            S("∪", @"\cup"), S("∩", @"\cap"), S("∅", @"\emptyset"), S("∖", @"\setminus"),
        ]),
        Leaf("latex.basics.dots", "Dots",
        [
            S("⋯", @"\cdots"), S("…", @"\ldots"), S("⋮", @"\vdots"), S("⋱", @"\ddots"),
            S("∞", @"\infty"), O("′", "'"), O("″", "''"), S("°", @"^{\circ}"),
        ]),
    ]);

    // ── Numbers ─────────────────────────────────────────────────────────────
    private static readonly PaletteGroup Numbers = Category("latex.numbers", "Numbers",
    [
        Leaf("latex.numbers.constants", "Constants",
        [
            S("π", @"\pi"), S("e", "e"), S("i", "i"), S("∞", @"\infty"),
            S("φ", @"\varphi"), S("τ", @"\tau"), S("ℏ", @"\hbar"), S("ℓ", @"\ell"),
        ]),
        Leaf("latex.numbers.fractions", "Vulgar fractions",
        [
            F("½", @"\frac{1}{2}"), F("⅓", @"\frac{1}{3}"), F("¼", @"\frac{1}{4}"), F("⅕", @"\frac{1}{5}"),
            F("⅔", @"\frac{2}{3}"), F("¾", @"\frac{3}{4}"), F("⅜", @"\frac{3}{8}"), F("⅝", @"\frac{5}{8}"),
        ]),
        Leaf("latex.numbers.sets", "Number sets",
        [
            S("ℝ", @"\mathbb{R}"), S("ℤ", @"\mathbb{Z}"), S("ℕ", @"\mathbb{N}"), S("ℚ", @"\mathbb{Q}"),
            S("ℂ", @"\mathbb{C}"), S("∅", @"\varnothing"), S("ℵ", @"\aleph"), S("ℶ", @"\beth"),
        ]),
        Leaf("latex.numbers.compare", "Compare",
        [
            O("≤", @"\leq"), O("≥", @"\geq"), O("≪", @"\ll"), O("≫", @"\gg"),
            O("≈", @"\approx"), O("∝", @"\propto"), O("∼", @"\sim"), O("≅", @"\cong"),
        ]),
        Leaf("latex.numbers.modular", "Modular",
        [
            O("mod", @"\bmod"), F("(mod n)", @"\pmod{}", 1), F("(n)", @"\pod{}", 1), O("mod", @"\mod"),
            Fn("gcd", @"\gcd"), O("≡", @"\equiv"), O("∤", @"\nmid"), O("|", "|"),
        ]),
        Leaf("latex.numbers.scientific", "Scientific",
        [
            F("×10ˣ", @"\times 10^{}", 1), F("a⁻¹", "^{-1}"), O("±", @"\pm"), O("≈", @"\approx"),
            O("⋅", @"\cdot"), O("≪", @"\ll"), O("≫", @"\gg"), S("°", @"^{\circ}"),
        ]),
        Leaf("latex.numbers.factorials", "Factorials",
        [
            O("n!", "!"), F("ₙCᵣ", @"\binom{}{}", 3), F("ₙCᵣ", @"\dbinom{}{}", 3), F("ₙCᵣ", @"\tbinom{}{}", 3),
            F("∏", @"\prod_{}^{}", 4), S("Γ", @"\Gamma"), Fn("gcd", @"\gcd"), O("mod", @"\bmod"),
        ]),
        Leaf("latex.numbers.misc", "Misc",
        [
            S("ℜ", @"\Re"), S("ℑ", @"\Im"), S("℘", @"\wp"), S("⊙", @"\odot"),
            S("†", @"\dagger"), S("‡", @"\ddagger"), S("§", @"\S"), S("¶", @"\P"),
        ]),
    ]);

    // ── Calculus ────────────────────────────────────────────────────────────
    private static readonly PaletteGroup Calculus = Category("latex.calculus", "Calculus",
    [
        Leaf("latex.calc.derivatives", "Derivatives",
        [
            O("∂", @"\partial"), F("d/dx", @"\frac{d}{dx}"), F("∂/∂x", @"\frac{\partial}{\partial x}"),
            F("d²/dx²", @"\frac{d^{2}}{dx^{2}}"), O("′", "'"), O("″", "''"),
            F("ȧ", @"\dot{}", 1), F("ä", @"\ddot{}", 1),
        ]),
        Leaf("latex.calc.integrals", "Integrals",
        [
            O("∫", @"\int"), F("∫ᵇₐ", @"\int_{}^{}", 4), O("∮", @"\oint"), O("∬", @"\iint"),
            O("∭", @"\iiint"), O("⨌", @"\iiiint"), O("∯", @"\oiint"), O("∰", @"\oiiint"),
        ]),
        Leaf("latex.calc.big", "Big operators",
        [
            F("∑", @"\sum_{}^{}", 4), F("∏", @"\prod_{}^{}", 4), O("∐", @"\coprod"), O("⋃", @"\bigcup"),
            O("⋂", @"\bigcap"), O("⨁", @"\bigoplus"), O("⨂", @"\bigotimes"), O("⋁", @"\bigvee"),
        ]),
        Leaf("latex.calc.limits", "Limits",
        [
            F("lim", @"\lim_{x \to 0}", 8), F("lim ∞", @"\lim_{x \to \infty}"),
            F("limsup", @"\limsup_{n}", 1), F("liminf", @"\liminf_{n}", 1),
            Fn("sup", @"\sup"), Fn("inf", @"\inf"), Fn("max", @"\max"), Fn("min", @"\min"),
        ]),
        Leaf("latex.calc.vector", "Vector",
        [
            O("∇", @"\nabla"), F("a⃗", @"\vec{}", 1), F("A⃗B", @"\overrightarrow{}", 1), F("â", @"\hat{}", 1),
            O("⋅", @"\cdot"), O("×", @"\times"), O("‖", @"\|"), O("⊥", @"\perp"),
        ]),
        Leaf("latex.calc.differentials", "Differentials",
        [
            O("dx", @"\,dx"), O("dy", @"\,dy"), O("dt", @"\,dt"), O("dθ", @"\,d\theta"),
            O("∂x", @"\partial x"), O("dA", @"\,dA"), O("dV", @"\,dV"), O("dS", @"\,dS"),
        ]),
        Leaf("latex.calc.series", "Series",
        [
            F("∑₀^∞", @"\sum_{n=0}^{\infty}"), F("∏₁^∞", @"\prod_{n=1}^{\infty}"),
            F("limₙ", @"\lim_{n \to \infty}"), S("⋯", @"\cdots"),
            S("⋯", @"\dotsb"), S("∞", @"\infty"), F("ₙCᵣ", @"\binom{n}{k}"), S("→", @"\to"),
        ]),
        Leaf("latex.calc.complex", "Complex",
        [
            S("i", "i"), S("ℜ", @"\Re"), S("ℑ", @"\Im"), F("z̄", @"\bar{}", 1),
            Fn("arg", @"\arg"), F("e^iθ", @"e^{i\theta}"), F("|z|", @"\left| \right|", 8), S("∠", @"\angle"),
        ]),
    ]);

    // ── Greek ───────────────────────────────────────────────────────────────
    private static readonly PaletteGroup Greek = Category("latex.greek", "Greek",
    [
        Leaf("latex.greek.lower1", "α – θ",
        [
            S("α", @"\alpha"), S("β", @"\beta"), S("γ", @"\gamma"), S("δ", @"\delta"),
            S("ε", @"\varepsilon"), S("ζ", @"\zeta"), S("η", @"\eta"), S("θ", @"\theta"),
        ]),
        Leaf("latex.greek.lower2", "ι – ρ",
        [
            S("ι", @"\iota"), S("κ", @"\kappa"), S("λ", @"\lambda"), S("μ", @"\mu"),
            S("ν", @"\nu"), S("ξ", @"\xi"), S("π", @"\pi"), S("ρ", @"\rho"),
        ]),
        Leaf("latex.greek.lower3", "σ – ω",
        [
            S("σ", @"\sigma"), S("τ", @"\tau"), S("υ", @"\upsilon"), S("φ", @"\varphi"),
            S("χ", @"\chi"), S("ψ", @"\psi"), S("ω", @"\omega"), S("ϑ", @"\vartheta"),
        ]),
        Leaf("latex.greek.caps1", "Γ – Υ",
        [
            S("Γ", @"\Gamma"), S("Δ", @"\Delta"), S("Θ", @"\Theta"), S("Λ", @"\Lambda"),
            S("Ξ", @"\Xi"), S("Π", @"\Pi"), S("Σ", @"\Sigma"), S("Υ", @"\Upsilon"),
        ]),
        Leaf("latex.greek.caps2", "Φ – Ω",
        [
            S("Φ", @"\Phi"), S("Ψ", @"\Psi"), S("Ω", @"\Omega"), S("∇", @"\nabla"),
        ]),
        Leaf("latex.greek.variants", "Variants",
        [
            S("ε", @"\epsilon"), S("ϵ", @"\varepsilon"), S("ϑ", @"\vartheta"), S("ϖ", @"\varpi"),
            S("ϱ", @"\varrho"), S("ς", @"\varsigma"), S("φ", @"\varphi"), S("ϕ", @"\phi"),
        ]),
        Leaf("latex.greek.italiccaps", "Italic caps",
        [
            S("Γ", @"\varGamma"), S("Δ", @"\varDelta"), S("Θ", @"\varTheta"), S("Λ", @"\varLambda"),
            S("Ξ", @"\varXi"), S("Π", @"\varPi"), S("Σ", @"\varSigma"), S("Ω", @"\varOmega"),
        ]),
        Leaf("latex.greek.hebrew", "Hebrew",
        [
            S("ℵ", @"\aleph"), S("ℶ", @"\beth"), S("ℷ", @"\gimel"), S("ℸ", @"\daleth"),
        ]),
    ]);

    // ── Logic & sets ────────────────────────────────────────────────────────
    private static readonly PaletteGroup Logic = Category("latex.logic", "Logic & sets",
    [
        Leaf("latex.logic.arrows", "Arrows",
        [
            S("→", @"\rightarrow"), S("←", @"\leftarrow"), S("↔", @"\leftrightarrow"), S("↦", @"\mapsto"),
            S("⇒", @"\Rightarrow"), S("⇐", @"\Leftarrow"), S("⇔", @"\Leftrightarrow"), S("↪", @"\hookrightarrow"),
        ]),
        Leaf("latex.logic.longarrows", "Long arrows",
        [
            S("⟶", @"\longrightarrow"), S("⟵", @"\longleftarrow"), S("⟹", @"\Longrightarrow"),
            S("⟸", @"\Longleftarrow"), S("⟺", @"\Longleftrightarrow"), S("↑", @"\uparrow"),
            S("↓", @"\downarrow"), S("⇀", @"\rightharpoonup"),
        ]),
        Leaf("latex.logic.logic", "Logic",
        [
            S("∀", @"\forall"), S("∃", @"\exists"), S("∄", @"\nexists"), S("¬", @"\neg"),
            S("∧", @"\land"), S("∨", @"\lor"), S("⊤", @"\top"), S("⊥", @"\bot"),
        ]),
        Leaf("latex.logic.implies", "Implication",
        [
            S("→", @"\to"), S("⟹", @"\implies"), S("⟸", @"\impliedby"), S("⟺", @"\iff"),
            S("⊢", @"\vdash"), S("⊨", @"\models"),
        ]),
        Leaf("latex.logic.setrel", "Set relations",
        [
            S("∈", @"\in"), S("∉", @"\notin"), S("∋", @"\ni"), S("⊂", @"\subset"),
            S("⊆", @"\subseteq"), S("⊃", @"\supset"), S("⊇", @"\supseteq"), S("⊄", @"\nsubseteq"),
        ]),
        Leaf("latex.logic.setops", "Set operations",
        [
            S("∪", @"\cup"), S("∩", @"\cap"), S("∖", @"\setminus"), S("∅", @"\emptyset"),
            S("⊕", @"\oplus"), S("⊗", @"\otimes"), S("∗", @"\ast"), S("⋆", @"\star"),
        ]),
        Leaf("latex.logic.negated", "Negated",
        [
            S("≮", @"\nless"), S("≯", @"\ngtr"), S("≰", @"\nleq"), S("≱", @"\ngeq"),
            S("≁", @"\nsim"), S("≇", @"\ncong"), S("∤", @"\nmid"), S("∦", @"\nparallel"),
        ]),
        Leaf("latex.logic.order", "Order",
        [
            S("≺", @"\prec"), S("≻", @"\succ"), S("≪", @"\ll"), S("≫", @"\gg"),
            S("≍", @"\asymp"), S("≃", @"\simeq"), S("∼", @"\sim"), S("∝", @"\propto"),
        ]),
    ]);

    // ── Delimiters & accents ────────────────────────────────────────────────
    private static readonly PaletteGroup Delimiters = Category("latex.delim", "Delimiters",
    [
        Leaf("latex.delim.brackets", "Brackets",
        [
            O("(", "("), O(")", ")"), O("[", "["), O("]", "]"),
            O("{", @"\{"), O("}", @"\}"), O("⟨", @"\langle"), O("⟩", @"\rangle"),
        ]),
        Leaf("latex.delim.floors", "Floors & bars",
        [
            O("⌊", @"\lfloor"), O("⌋", @"\rfloor"), O("⌈", @"\lceil"), O("⌉", @"\rceil"),
            O("|", "|"), O("‖", @"\|"), F("|x|", @"\lvert \rvert", 7), F("‖v‖", @"\lVert \rVert", 7),
        ]),
        Leaf("latex.delim.auto", "Auto-sized",
        [
            F("(  )", @"\left( \right)", 8), F("[  ]", @"\left[ \right]", 8),
            F("{  }", @"\left\{ \right\}", 9), F("|  |", @"\left| \right|", 8),
            F("‖  ‖", @"\left\| \right\|", 9), F("⟨  ⟩", @"\left\langle \right\rangle", 15),
            F("⌊  ⌋", @"\left\lfloor \right\rfloor", 15), F("  |ₐ", @"\left. \right|_{}", 1),
        ]),
        Leaf("latex.delim.sizes", "Sizes",
        [
            O("big", @"\big"), O("Big", @"\Big"), O("bigg", @"\bigg"), O("Bigg", @"\Bigg"),
            O("bigl", @"\bigl"), O("bigr", @"\bigr"), O("Bigl", @"\Bigl"), O("Bigr", @"\Bigr"),
        ]),
        Leaf("latex.delim.accents", "Accents",
        [
            F("â", @"\hat{}", 1), F("ā", @"\bar{}", 1), F("ȧ", @"\dot{}", 1), F("ä", @"\ddot{}", 1),
            F("ã", @"\tilde{}", 1), F("a⃗", @"\vec{}", 1), F("á", @"\acute{}", 1), F("à", @"\grave{}", 1),
        ]),
        Leaf("latex.delim.wide", "Wide accents",
        [
            F("â̂", @"\widehat{}", 1), F("ã̃", @"\widetilde{}", 1), F("a̅", @"\overline{}", 1),
            F("a̲", @"\underline{}", 1), F("ǎ", @"\check{}", 1), F("ă", @"\breve{}", 1),
            F("å", @"\mathring{}", 1), F("á", @"\acute{}", 1),
        ]),
        Leaf("latex.delim.over", "Arrows over",
        [
            F("A⃗B", @"\overrightarrow{}", 1), F("A⃖B", @"\overleftarrow{}", 1),
            F("A⃡B", @"\overleftrightarrow{}", 1), F("AB⃗", @"\underrightarrow{}", 1),
            F("AB⃖", @"\underleftarrow{}", 1), F("AB⃡", @"\underleftrightarrow{}", 1),
        ]),
        Leaf("latex.delim.extensible", "Labelled arrows",
        [
            F("→ᶠ", @"\xrightarrow{}", 1), F("←ᶠ", @"\xleftarrow{}", 1), F("⇒ᶠ", @"\xRightarrow{}", 1),
            F("⇐ᶠ", @"\xLeftarrow{}", 1), F("↔ᶠ", @"\xleftrightarrow{}", 1), F("↦ᶠ", @"\xmapsto{}", 1),
        ]),
    ]);

    // ── Functions ───────────────────────────────────────────────────────────
    private static readonly PaletteGroup Functions = Category("latex.fn", "Functions",
    [
        Leaf("latex.fn.trig", "Trig",
        [
            Fn("sin", @"\sin"), Fn("cos", @"\cos"), Fn("tan", @"\tan"),
            Fn("cot", @"\cot"), Fn("sec", @"\sec"), Fn("csc", @"\csc"),
        ]),
        Leaf("latex.fn.inverse", "Inverse",
        [
            Fn("arcsin", @"\arcsin"), Fn("arccos", @"\arccos"), Fn("arctan", @"\arctan"),
            Fn("sin⁻¹", @"\sin^{-1}"), Fn("cos⁻¹", @"\cos^{-1}"), Fn("tan⁻¹", @"\tan^{-1}"),
        ]),
        Leaf("latex.fn.hyperbolic", "Hyperbolic",
        [
            Fn("sinh", @"\sinh"), Fn("cosh", @"\cosh"), Fn("tanh", @"\tanh"), Fn("coth", @"\coth"),
        ]),
        Leaf("latex.fn.logs", "Logarithms",
        [
            Fn("ln", @"\ln"), Fn("log", @"\log"), Fn("lg", @"\lg"),
            Fn("exp", @"\exp"), F("logₙ", @"\log_{}", 1), F("eˣ", "e^{}", 1),
        ]),
        Leaf("latex.fn.named", "Named ops",
        [
            Fn("arg", @"\arg"), Fn("det", @"\det"), Fn("dim", @"\dim"), Fn("gcd", @"\gcd"),
            Fn("ker", @"\ker"), Fn("hom", @"\hom"), Fn("deg", @"\deg"), Fn("Pr", @"\Pr"),
        ]),
        Leaf("latex.fn.bounds", "Bounds",
        [
            Fn("sup", @"\sup"), Fn("inf", @"\inf"), Fn("max", @"\max"), Fn("min", @"\min"),
            F("limsup", @"\limsup_{n}", 1), F("liminf", @"\liminf_{n}", 1),
        ]),
        Leaf("latex.fn.amslimits", "Categorical",
        [
            F("injlim", @"\injlim_{n}", 1), F("projlim", @"\projlim_{n}", 1),
            F("varinjlim", @"\varinjlim_{n}", 1), F("varprojlim", @"\varprojlim_{n}", 1),
            F("varliminf", @"\varliminf_{n}", 1), F("varlimsup", @"\varlimsup_{n}", 1),
        ]),
        Leaf("latex.fn.probability", "Probability",
        [
            Fn("Pr", @"\Pr"), S("𝔼", @"\mathbb{E}"), S("ℙ", @"\mathbb{P}"), O("|", "|"),
            O("∼", @"\sim"), S("μ", @"\mu"), S("σ", @"\sigma"), S("ρ", @"\rho"),
        ]),
    ]);

    // ── Structures ──────────────────────────────────────────────────────────
    private static readonly PaletteGroup Structures = Category("latex.struct", "Structures",
    [
        Leaf("latex.struct.matrices", "Matrices",
        [
            F("( )", Matrix("pmatrix")), F("[ ]", Matrix("bmatrix")), F("| |", Matrix("vmatrix")),
            F("‖ ‖", Matrix("Vmatrix")), F("{ }", Matrix("Bmatrix")), F("▦", Matrix("matrix")),
            F("small", Matrix("smallmatrix")),
            F("array", "\\begin{array}{cc}\n a & b \\\\\n c & d\n\\end{array}"),
        ]),
        Leaf("latex.struct.align", "Cases & align",
        [
            F("{ …", "\\begin{cases}\n a & x < 0 \\\\\n b & x \\ge 0\n\\end{cases}"),
            F("align", "\\begin{aligned}\n a &= b \\\\\n &= c\n\\end{aligned}"),
            F("gather", "\\begin{gathered}\n a = b \\\\\n c = d\n\\end{gathered}"),
            O("&", " & "), F(@"\\", " \\\\\n"), F("text", @"\text{}", 1),
            F("stack", @"\substack{}", 1),
        ]),
        Leaf("latex.struct.overunder", "Over & under",
        [
            F("over‾", @"\overbrace{}^{}", 4), F("under_", @"\underbrace{}_{}", 4),
            F("a̅", @"\overline{}", 1), F("a̲", @"\underline{}", 1),
            F("aᵇ", @"\overset{}{}", 3), F("aᵦ", @"\underset{}{}", 3), F("→ᶠ", @"\stackrel{}{}", 3),
        ]),
        Leaf("latex.struct.fracstyles", "Fraction styles",
        [
            F("a/b", @"\frac{}{}", 3), F("a／b", @"\dfrac{}{}", 3), F("ᵃ⁄ᵇ", @"\tfrac{}{}", 3),
            F("⅟₊", @"\cfrac{}{}", 3), F("¹⁄₂", @"\nicefrac{}{}", 3), F("³⁄₄", @"\sfrac{}{}", 3),
            F("ₙCᵣ", @"\binom{}{}", 3), F("ₙCᵣ", @"\dbinom{}{}", 3),
        ]),
        Leaf("latex.struct.scripts", "Sub & superscript",
        [
            F("aᵇ", "^{}", 1), F("aᵢ", "_{}", 1), F("aᵢᵇ", "_{}^{}", 4), F("a²", "^{2}"),
            F("a³", "^{3}"), F("a⁻¹", "^{-1}"), F("eˣ", "e^{}", 1), F("10ˣ", "10^{}", 1),
        ]),
        Leaf("latex.struct.roots", "Roots",
        [
            F("√", @"\sqrt{}", 1), F("∛", @"\sqrt[3]{}", 1), F("ⁿ√", @"\sqrt[]{}", 3), S("√", @"\surd"),
        ]),
        Leaf("latex.struct.arrays", "Arrays",
        [
            F("lcr", "\\begin{array}{lcr}\n a & b & c \\\\\n d & e & f\n\\end{array}"),
            F("│", "\\begin{array}{cc|c}\n 1 & 0 & 3 \\\\\n 0 & 1 & 4\n\\end{array}"),
            F("▤", "\\begin{array}{|c|c|}\n\\hline\n x & y \\\\\n\\hline\n\\end{array}"),
            O("─", @"\hline"), F("⋯", @"\hdotsfor{2}"), O("&", " & "), F(@"\\", " \\\\\n"),
        ]),
        Leaf("latex.struct.multiline", "Multiline",
        [
            F("multline", "\\begin{multline}\n a + b \\\\\n + c + d\n\\end{multline}"),
            F("split", "\\begin{split}\n a &= b \\\\\n &= c\n\\end{split}"),
            F("gather", "\\begin{gather}\n a = b \\\\\n c = d\n\\end{gather}"),
            F("align", "\\begin{align}\n a &= b \\\\\n c &= d\n\\end{align}"),
            F("alignat", "\\begin{alignat}{2}\n a &= b & c &= d\n\\end{alignat}"),
        ]),
    ]);

    /// <summary>The eight categories: the navigator's top ring.</summary>
    public static IReadOnlyList<PaletteGroup> Categories { get; } =
        [Basics, Numbers, Calculus, Greek, Logic, Delimiters, Functions, Structures];

    /// <summary>Finds a group by id anywhere in the tree.</summary>
    public static PaletteGroup? Find(string id)
    {
        foreach (var category in Categories)
            if (category.Find(id) is { } hit) return hit;
        return null;
    }

    /// <summary>Every group that actually holds keys.</summary>
    public static IEnumerable<PaletteGroup> LeafGroups()
        => Categories.SelectMany(c => c.Children);

    /// <summary>
    /// A category: no keys of its own, and at most <see cref="Slots"/> groups under it. Checked here
    /// as well as in a test, because the failure it guards against is silent — the navigator draws a
    /// fixed ring and simply drops a ninth group, so the mistake shows up as a category nobody can
    /// reach rather than as anything that looks wrong.
    /// </summary>
    private static PaletteGroup Category(string id, string label, IReadOnlyList<PaletteGroup> groups)
        => groups.Count <= Slots
            ? new PaletteGroup(id, label, [], groups)
            : throw new InvalidOperationException(
                $"the {label} category has {groups.Count} groups; the navigator shows at most {Slots}");

    /// <summary>A group of symbols, padded out to the ring's eight positions.</summary>
    private static PaletteGroup Leaf(string id, string label, IReadOnlyList<PaletteKey> keys)
        => keys.Count <= Slots
            ? new PaletteGroup(id, label, PaletteText.Fill(keys, Slots))
            : throw new InvalidOperationException(
                $"the {label} group has {keys.Count} keys; the navigator shows at most {Slots}");

    /// <summary>A symbol key. Inserted with a trailing space so commands don't run together.</summary>
    private static PaletteKey S(string label, string command)
        => new(label, command + " ", command, PaletteKeyKind.Symbol);

    /// <summary>An operator key.</summary>
    private static PaletteKey O(string label, string command, int caretBack = 0)
        => new(label, caretBack > 0 ? command : command + " ", command, PaletteKeyKind.Operator, caretBack);

    /// <summary>A function name — brackets the selection, like the calculator's does.</summary>
    private static PaletteKey Fn(string label, string command, int caretBack = 0)
        => caretBack > 0
            ? new PaletteKey(label, command, command, PaletteKeyKind.Function, caretBack)
            : new PaletteKey(label, command + "(", command, PaletteKeyKind.Function)
              { InsertKind = KeyInsert.Wrapping, Close = ")" };

    /// <summary>A structural key — these carry a caret offset so you land inside the braces.</summary>
    private static PaletteKey F(string label, string command, int caretBack = 0)
        => new(label, command, command, PaletteKeyKind.Function, caretBack);

    /// <summary>A 2×2 starter in one of the matrix environments, which differ only in their brackets.</summary>
    private static string Matrix(string environment)
        => $"\\begin{{{environment}}}\n a & b \\\\\n c & d\n\\end{{{environment}}}";
}
