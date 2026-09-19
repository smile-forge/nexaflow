namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Catalog of sample markdown documents — one per Mermaid diagram type the renderer supports
/// (pie, flowchart, quadrant chart, sequence diagram, gantt, git graph, mindmap, state diagram,
/// class diagram, requirement diagram, kanban board, XY chart, radar chart, Ishikawa/fishbone, Sankey,
/// ER, Venn, architecture, swimlanes, Cynefin, timeline, journey, block, C4), plus an <c>extensions.md</c> exercising the non-diagram
/// Markdig extensions (emphasis extras, abbreviations, alert blocks) and four <c>latex-math-*.md</c>
/// references that exercise the LaTeX math renderer (symbols, structures incl. all matrix delimiters
/// and environments, fonts/styling, AMS symbols) — a supported construct typesets, an unsupported one
/// falls back to its raw source, so the docs double as a live map of engine support, which
/// <c>MarkdownSampleRenderTests.LatexMathSamplesTypeset</c> holds them to. Two <c>music-*.md</c> references exercise the
/// musical-notation engraver (<c>```abc</c> fences and <c>#%lilypond … #%</c> blocks → sheet music). Each
/// document showcases several variations, so the fixtures double as a human-readable reference. The
/// <c>mermaid-*</c> naming marks the diagram docs.
/// </summary>
internal sealed class MarkdownSamples : ISampleSet
{
    public string SubDirectory => "markdown";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("mermaid-pie.md",       Pie),
        SampleFile.Text("mermaid-flowchart.md", Flowchart),
        SampleFile.Text("mermaid-quadrant.md",  Quadrant),
        SampleFile.Text("mermaid-sequence.md",  Sequence),
        SampleFile.Text("mermaid-gantt.md",     Gantt),
        SampleFile.Text("mermaid-gitgraph.md",  GitGraph),
        SampleFile.Text("mermaid-mindmap.md",   Mindmap),
        SampleFile.Text("mermaid-state.md",       State),
        SampleFile.Text("mermaid-class.md",       Class),
        SampleFile.Text("mermaid-requirement.md", Requirement),
        SampleFile.Text("mermaid-kanban.md",      Kanban),
        SampleFile.Text("mermaid-xychart.md",     XyChart),
        SampleFile.Text("mermaid-radar.md",       Radar),
        SampleFile.Text("mermaid-ishikawa.md",    Ishikawa),
        SampleFile.Text("mermaid-sankey.md",      Sankey),
        SampleFile.Text("mermaid-er.md",          Er),
        SampleFile.Text("mermaid-venn.md",        Venn),
        SampleFile.Text("mermaid-architecture.md", Architecture),
        SampleFile.Text("mermaid-swimlane.md",    Swimlane),
        SampleFile.Text("mermaid-cynefin.md",     Cynefin),
        SampleFile.Text("mermaid-timeline.md",    Timeline),
        SampleFile.Text("mermaid-journey.md",     Journey),
        SampleFile.Text("mermaid-block.md",       Block),
        SampleFile.Text("mermaid-c4.md",          C4),
        SampleFile.Text("extensions.md",          Extensions),
        SampleFile.Text("latex-math-symbols.md",    LatexMathSymbols),
        SampleFile.Text("latex-math-structures.md", LatexMathStructures),
        SampleFile.Text("latex-math-fonts.md",      LatexMathFonts),
        SampleFile.Text("latex-math-amssymb.md",    LatexMathAmssymb),
        SampleFile.Text("music-abc.md",             MusicAbc),
        SampleFile.Text("music-lilypond.md",        MusicLilyPond),
        SampleFile.Text("qr-codes.md",              Qr),
        SampleFile.Text("barcodes.md",              Barcode),
        SampleFile.Text("datamatrix.md",            DataMatrix),
        SampleFile.Text("pdf417.md",                Pdf417),
        SampleFile.Text("aztec.md",                 Aztec),
        SampleFile.Text("smiles.md",                Smiles),
        SampleFile.Text("wordcloud.md",             WordCloud),
        SampleFile.Text("plots.md",                 Plots),
    ];

    private const string LatexMathSymbols =
        """
        # LaTeX math — symbols & notation

        Nexaflow renders LaTeX inside markdown: **inline** math between single dollars
        (`$a^2$`) and **display** math between double dollars (`$$ … $$`). Rendering is powered
        by the WpfMath (`xaml-math`) engine.

        **How to read this reference:** every `$$` block below uses only constructs the engine
        **supports**, so each should typeset. Standard LaTeX the engine does **not** support is
        called out under each section as *Not supported*. When a formula does fail, the app shows
        its raw `$$ … $$` source in an accent-bordered box — like the one just below, which
        deliberately uses the unsupported `\sideset`:

        $$ \sideset{_a^b}{_c^d}\sum $$

        ## Spacing

        Horizontal spacing: `\,` `\:` `\;` (thin/medium/thick), `\quad`, `\qquad`, the tie `~`,
        the control space `\ `, and `\hspace{<length>}`:

        $$ a\,b \;\; a\;b \;\; a\quad b \;\; a\qquad b \;\; a~b \;\; a\ b \;\; a\hspace{15pt}b $$

        ## Inline math

        Euler's identity $e^{i\pi} + 1 = 0$ reads inline, as do a half $\frac{1}{2}$ and a
        root $\sqrt{2}$.

        ## Superscripts and subscripts

        $$ x^2 \quad x_i \quad x_i^2 \quad x^{2n} \quad a_{i,j} \quad x^{y^{z}} \quad \sum_{i=1}^{n} i $$

        ## Greek letters — lowercase

        $$ \alpha \; \beta \; \gamma \; \delta \; \epsilon \; \varepsilon \; \zeta \; \eta \; \theta \; \vartheta \; \iota \; \kappa \; \lambda \; \mu \; \nu \; \xi \; \pi \; \varpi \; \rho \; \varrho \; \sigma \; \varsigma \; \tau \; \upsilon \; \phi \; \varphi \; \chi \; \psi \; \omega $$

        ## Greek letters — uppercase

        $$ \Gamma \; \Delta \; \Theta \; \Lambda \; \Xi \; \Pi \; \Sigma \; \Upsilon \; \Phi \; \Psi \; \Omega $$

        LaTeX gives commands only to the 11 uppercase shapes that differ from Latin letters. The others —
        Alpha, Beta, Epsilon, Zeta, Eta, Iota, Kappa, Mu, Nu, Omicron, Rho, Tau, Chi — have **no** command;
        they are typeset with the Latin capitals `A B E Z H I K M N O P T X`:

        $$ A \; B \; E \; Z \; H \; I \; K \; M \; N \; O \; P \; T \; X $$

        The uppercase forms are upright. amsmath's `\var…` spellings give the italic ones:

        $$ \varGamma \; \varDelta \; \varTheta \; \varLambda \; \varXi \; \varPi \; \varSigma \; \varUpsilon \; \varPhi \; \varPsi \; \varOmega $$

        ## Binary operators

        $$ a + b \;\; a - b \;\; a \times b \;\; a \div b \;\; a \cdot b \;\; a \pm b \;\; a \mp b \;\; a \ast b \;\; a \star b \;\; a \circ b \;\; a \bullet b \;\; a \oplus b \;\; a \otimes b \;\; a \odot b \;\; a \setminus b $$

        ## Relations

        $$ a = b \;\; a \neq b \;\; a < b \;\; a > b \;\; a \leq b \;\; a \geq b \;\; a \approx b \;\; a \equiv b \;\; a \sim b \;\; a \simeq b \;\; a \cong b \;\; a \propto b \;\; a \ll b \;\; a \gg b \;\; a \prec b \;\; a \succ b \;\; a \perp b \;\; a \parallel b \;\; a \asymp b $$

        ## Negated relations

        $$ a \nless b \;\; a \ngtr b \;\; a \nleq b \;\; a \ngeq b \;\; a \nprec b \;\; a \nsucc b \;\; a \nsim b \;\; a \ncong b \;\; a \nmid b \;\; a \nparallel b \;\; a \nvdash b \;\; A \nsubseteq B \;\; A \nsupseteq B \;\; a \nrightarrow b \;\; a \nleftrightarrow b $$

        Each of those is a single glyph from the AMS `msbm` font, not an overlay.

        The strict negations, where "not equal" is part of the symbol rather than a slash through it:

        $$ A \subsetneq B \;\; A \supsetneq B \;\; A \varsubsetneq B \;\; a \lneqq b \;\; a \gneqq b \;\; a \lvertneqq b \;\; a \gvertneqq b \;\; a \lnsim b \;\; a \gnapprox b \;\; a \precnsim b \;\; a \succnapprox b $$

        ## Set theory and logic

        $$ a \in A \;\; a \notin A \;\; a \ni A \;\; A \subset B \;\; A \subseteq B \;\; A \supset B \;\; A \supseteq B \;\; \emptyset \;\; \varnothing \;\; \forall x \;\; \exists y \;\; \nexists z \;\; \neg p \;\; p \land q \;\; p \lor q \;\; p \to q \;\; p \implies q \;\; p \impliedby q \;\; p \iff q $$

        Blackboard bold and the Hebrew letters come from the same AMS `msbm` font:

        $$ \mathbb{N} \subset \mathbb{Z} \subset \mathbb{Q} \subset \mathbb{R} \subset \mathbb{C} \qquad \aleph \;\; \beth \;\; \gimel \;\; \daleth $$

        ## Arrows

        $$ a \to b \;\; a \gets b \;\; a \rightarrow b \;\; a \leftarrow b \;\; a \Rightarrow b \;\; a \Leftarrow b \;\; a \leftrightarrow b \;\; a \Leftrightarrow b \;\; a \mapsto b \;\; a \longrightarrow b \;\; a \longleftarrow b \;\; a \hookrightarrow b \;\; a \Longrightarrow b \;\; a \Longleftarrow b \;\; a \Longleftrightarrow b \;\; a \uparrow b \;\; a \downarrow b \;\; a \rightharpoonup b $$

        Arrows that stretch to fit a label written over (and optionally under) them:

        $$ A \xrightarrow{f} B \;\; B \xleftarrow{g} C \;\; A \xrightarrow[\cong]{\varphi} B \;\; A \xleftrightarrow{h} B \;\; A \xRightarrow{p} B \;\; A \xLeftarrow{q} B \;\; a \xmapsto{\iota} b $$

        ## Accents

        $$ \hat{a} \;\; \bar{a} \;\; \vec{a} \;\; \dot{a} \;\; \ddot{a} \;\; \tilde{a} \;\; \acute{a} \;\; \grave{a} \;\; \check{a} \;\; \breve{a} \;\; \mathring{a} \;\; \widehat{abc} \;\; \widetilde{abc} \;\; \overline{abc} \;\; \underline{abc} $$

        The stretchy arrow accents draw to the width of what they mark, above it or below it:

        $$ \overrightarrow{AB} \;\; \overleftarrow{AB} \;\; \overleftrightarrow{AB} \;\; \underrightarrow{AB} \;\; \underleftarrow{AB} \;\; \underleftrightarrow{AB} $$

        ## Dots and ellipses

        $$ a_1 + a_2 + \cdots + a_n \;\; 1, 2, \ldots, n \;\; 1, 2, \dots, n \;\; \vdots \;\; \ddots $$

        amsmath names its dots for what they sit among rather than for their shape — `\dotsb` between
        binary operators, `\dotsc` with commas, `\dotsi` with integrals, `\dotsm` between factors,
        `\dotso` for anything else — and each resolves to the right one of the two:

        $$ a + \dotsb + z \;\; 1, \dotsc, n \;\; \int \dotsi \int \;\; a \dotsm z \;\; x \dotso $$

        `\vdots` and `\ddots` are most at home inside a matrix — see the
        [structures reference](latex-math-structures.md).

        ## Miscellaneous symbols

        $$ \infty \;\; \partial \;\; \nabla \;\; \aleph \;\; \hbar \;\; \ell \;\; \Re \;\; \Im \;\; \wp \;\; \angle \;\; \triangle \;\; \top \;\; \bot \;\; \surd \;\; \flat \;\; \sharp \;\; \natural \;\; \dagger \;\; \ddagger \;\; \S \;\; \P \;\; \clubsuit \;\; \diamondsuit \;\; \heartsuit \;\; \spadesuit $$

        And from the AMS `msbm` font:

        $$ \mho \;\; \hslash \;\; \Bbbk \;\; \eth \;\; \Finv \;\; \Game \;\; \digamma \;\; \varkappa \;\; \diagup \;\; \diagdown \;\; \circledR $$
        """;

    private const string LatexMathStructures =
        """
        # LaTeX math — structures

        Constructs that arrange sub-expressions: fractions, roots, big operators, integrals,
        auto-sized delimiters, **matrices** (all delimiter variants), the aligned and gathered
        environments, style switches, where an operator's limits go, stacked annotations, frames,
        phantoms, horizontal braces and stacked limits — and what a formula quietly drops from the
        paper it was lifted out of. See the
        [symbols reference](latex-math-symbols.md) for the reading convention; each `$$` block is
        supported, with engine gaps flagged as *Not supported*.

        ## Fractions

        $$ \frac{a}{b} \;\; \frac{1}{1 + \frac{1}{x}} \;\; \frac{\partial f}{\partial x} $$

        `\dfrac` and `\tfrac` force display or text style — visible on a subscript, where a plain `\frac`
        shrinks but `\dfrac` stays full size:

        $$ x_{\frac{a}{b}} \;\; x_{\dfrac{a}{b}} \;\; \tfrac{a}{b} $$

        `\cfrac[l|c|r]{…}{…}` builds a continued fraction whose nested levels stay full size:

        $$ \cfrac{1}{2 + \cfrac{1}{3 + \cfrac{1}{4}}} $$

        The inline "slash" fractions `\nicefrac{…}{…}` and `\sfrac{…}{…}`:

        $$ \nicefrac{1}{2} \;\; \sfrac{3}{4} \;\; 2\sfrac{1}{2} $$

        ## Binomial coefficients

        $$ \binom{n}{k} = \frac{n!}{k!\,(n-k)!} $$

        `\dbinom` and `\tbinom` keep their size where a plain `\binom` would shrink with the style:

        $$ x_{\binom{n}{k}} \;\; x_{\dbinom{n}{k}} \;\; x_{\tbinom{n}{k}} $$

        ## The general fraction

        `\genfrac{left}{right}{thickness}{style}{numerator}{denominator}` is the one every fraction
        above is spelled with: a pair of delimiters, the thickness of the rule, and the style to set
        it in. An empty argument takes the default — no delimiter, the usual rule, the surrounding
        style — and a thickness of `0pt` draws no rule at all, which is what makes `\binom`:

        $$ \genfrac{}{}{}{}{a}{b} \;\; \genfrac{(}{)}{0pt}{}{n}{k} \;\; \genfrac{[}{]}{1pt}{}{a}{b} \;\; \genfrac{\{}{\}}{0pt}{1}{a}{b} $$

        The style argument is a digit: `0` display, `1` text, `2` script, `3` scriptscript.

        ## Roots

        $$ \sqrt{2} \;\; \sqrt{x^2 + y^2} \;\; \sqrt[3]{x} \;\; \sqrt[n]{a} $$

        ## Sums and products

        $$ \sum_{i=1}^{n} i = \frac{n(n+1)}{2} \;\; \prod_{i=1}^{n} i = n! \;\; \coprod_{i} X_i $$

        ## Big operators

        $$ \bigcup_i A_i \;\; \bigcap_i A_i \;\; \bigsqcup_i S_i \;\; \biguplus_i M_i \;\; \bigvee_i p_i \;\; \bigwedge_i p_i \;\; \bigoplus_i V_i \;\; \bigotimes_i V_i \;\; \bigodot_i x_i $$

        ## Integrals

        $$ \int_0^1 x^2 \, dx \;\; \oint_C \vec{F} \cdot d\vec{r} \;\; \iint_D f \, dA \;\; \iiint_V f \, dV \;\; \iiiint \;\; \idotsint \;\; \oiint_S \vec{F} \cdot d\vec{S} \;\; \oiiint $$

        ## Named functions and operators

        Trigonometric and hyperbolic:

        $$ \sin x \;\; \cos x \;\; \tan x \;\; \cot x \;\; \sec x \;\; \csc x \;\; \sinh x \;\; \cosh x \;\; \tanh x \;\; \coth x \;\; \arcsin x \;\; \arccos x \;\; \arctan x $$

        Logarithms and exponential:

        $$ \log x \;\; \ln x \;\; \lg x \;\; \exp x $$

        Limits and bounds:

        $$ \lim_{x \to \infty} \frac{1}{x} = 0 \;\; \limsup_{n} a_n \;\; \liminf_{n} a_n \;\; \sup S \;\; \inf S \;\; \max_i a_i \;\; \min_i a_i $$

        The amsmath limits, including the ones that wear a decoration:

        $$ \injlim_{n} A_n \quad \projlim_{n} A_n \quad \varinjlim_{n} A_n \quad \varprojlim_{n} A_n \quad \varliminf_{n} a_n \quad \varlimsup_{n} a_n $$

        Algebra and miscellaneous:

        $$ \arg z \;\; \det A \;\; \dim V \;\; \gcd(a,b) \;\; \ker T \;\; \hom(A, B) \;\; \deg f \;\; \Pr(X) $$

        Modulo:

        $$ n \bmod m \;\; a \equiv b \pmod{n} \;\; a \equiv b \pod{n} \;\; x \mod y $$

        ## Auto-sized delimiters

        $$ \left( \frac{a}{b} \right) \;\; \left[ \frac{a}{b} \right] \;\; \left\{ \frac{a}{b} \right\} \;\; \left| \frac{a}{b} \right| \;\; \left\| \frac{a}{b} \right\| \;\; \left\langle \frac{a}{b} \right\rangle $$

        $$ \left. \frac{dy}{dx} \right|_{x=0} \;\; \left\lfloor x \right\rfloor \;\; \left\lceil x \right\rceil $$

        `\lvert`/`\rvert` and `\lVert`/`\rVert` are the same bars typed as opening and closing, so
        they space as the delimiters they stand for rather than as ordinary symbols:

        $$ \lvert x \rvert \;\; \lVert v \rVert \;\; \left\lvert \frac{a}{b} \right\rvert $$

        ## Delimiters at a size you choose

        `\left`/`\right` grow a delimiter to fit what it encloses. `\big`, `\Big`, `\bigg` and
        `\Bigg` instead give it one of four set sizes, for when the fit is not the point — nesting,
        say, where each level should be visibly larger than the one inside it:

        $$ ( \;\; \big( \;\; \Big( \;\; \bigg( \;\; \Bigg( $$

        $$ \Bigl( \bigl( a + b \bigr) \cdot c \Bigr)^n $$

        Each has `l`, `r` and `m` spellings that say what the delimiter *is* — an opening, a closing,
        or a divider — which is what decides the space around it:

        $$ \bigl[ x \bigr] \quad \{\, x \bigm| x > 0 \,\} $$

        The four sizes are absolute lengths in LaTeX rather than multiples of the current one, so a
        `\Big(` inside a subscript is the same delimiter it is outside one.

        ## Matrices

        The matrix environments differ only in the delimiters that surround them. Each works both
        as a command (`\bmatrix{ … }`) and as an environment (`\begin{bmatrix} … \end{bmatrix}`).

        No delimiters — `matrix`:

        $$ \matrix{ a & b \\ c & d } \;\; \begin{matrix} a & b \\ c & d \end{matrix} $$

        Parentheses — `pmatrix`:

        $$ \begin{pmatrix} a & b \\ c & d \end{pmatrix} $$

        Square brackets — `bmatrix`:

        $$ \begin{bmatrix} a & b \\ c & d \end{bmatrix} $$

        Curly braces — `Bmatrix`:

        $$ \begin{Bmatrix} a & b \\ c & d \end{Bmatrix} $$

        Single vertical bars, e.g. a determinant — `vmatrix`:

        $$ \begin{vmatrix} a & b \\ c & d \end{vmatrix} = ad - bc $$

        Double vertical bars, e.g. a norm — `Vmatrix`:

        $$ \begin{Vmatrix} \vec{x} \\ \vec{y} \end{Vmatrix} $$

        A 3×3 matrix, and a row vector using `\cdots`:

        $$ \begin{bmatrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{bmatrix} \;\; \begin{pmatrix} a_1 & \cdots & a_n \end{pmatrix} $$

        A general matrix elides its entries with `\cdots` (horizontal), `\vdots` (vertical) and
        `\ddots` (diagonal):

        $$ \begin{bmatrix} a_{11} & \cdots & a_{1n} \\ \vdots & \ddots & \vdots \\ a_{m1} & \cdots & a_{mn} \end{bmatrix} $$

        `smallmatrix` is the same layout set in script size, so it fits on a text line:

        $$ \left( \begin{smallmatrix} a & b \\ c & d \end{smallmatrix} \right) $$

        `\hdotsfor{n}` fills n columns with dots, standing in for a row of entries left unwritten. It
        is the one cell that takes its width from the columns instead of giving them one, and
        `\hdotsfor[s]{n}` spreads the dots out by a factor of s:

        $$ \begin{pmatrix} a_{11} & a_{12} & a_{13} \\ \hdotsfor{3} \\ a_{n1} & a_{n2} & a_{n3} \end{pmatrix} \;\; \begin{pmatrix} b_{11} & b_{12} \\ \hdotsfor[2]{2} \end{pmatrix} $$

        ## Piecewise definitions (cases)

        Both the `\cases{ … }` command and the `cases` environment:

        $$ f(x) = \cases{ 1 & x > 0 \\ 0 & x = 0 \\ -1 & x < 0 } \;\; g(x) = \begin{cases} 1 & x > 0 \\ 0 & x \leq 0 \end{cases} $$

        ## Aligned and gathered equations

        `align` (and `aligned`, `split`, and the starred `align*`) line the rows up on `&`:

        $$ \begin{align} (a+b)^2 &= a^2 + 2ab + b^2 \\ (a-b)^2 &= a^2 - 2ab + b^2 \end{align} $$

        $$ \begin{aligned} x &= y + 1 \\ y &= z - 1 \end{aligned} $$

        `gather` (and `gathered`, `gather*`) centres each row instead:

        $$ \begin{gather} a^2 + b^2 = c^2 \\ e^{i\pi} + 1 = 0 \end{gather} $$

        `multline`, `flalign`, `alignat` — with the column count it insists on — and their starred
        forms are each taken as their nearest relative here: `gather` for the first, `align` for the
        rest. What they add in a paper is flushing to the page margins, and a formula in a markdown
        document has no margins to be flush with.

        $$ \begin{alignat}{2} a &= b + c &\quad d &= e \\ f &= g &\quad h &= i \end{alignat} $$

        ## Style switches

        `\displaystyle`, `\textstyle`, `\scriptstyle` and `\scriptscriptstyle` are switches: each applies
        from where it appears to the end of its group. `\displaystyle` is what moves the limits of a big
        operator above and below it, even inline:

        $$ \textstyle\sum_{i=1}^{n} i \quad \displaystyle\sum_{i=1}^{n} i $$

        $$ {\scriptstyle a + b} \quad {\scriptscriptstyle a + b} \quad a + b $$

        ## Where an operator's limits go

        The style decides whether an operator's scripts are stacked above and below it or set beside
        it. `\limits` and `\nolimits` override that decision for the operator they follow, and
        `\displaylimits` asks for the style's own answer back:

        $$ \textstyle\sum\limits_{i=1}^{n} i \quad \displaystyle\sum\nolimits_{i=1}^{n} i \quad \prod\limits_{k} a_k $$

        They only follow an operator. Anywhere else `\limits` stays an unknown command, rather than
        being swallowed and leaving a formula that looks like it was understood.

        ## Stacked annotations

        `\overset{…}{…}` and `\underset{…}{…}` set an annotation in script size above or below a base;
        `\stackrel` does the same but is typed as a relation, so it spaces like the arrow it sits on:

        $$ \overset{\text{def}}{=} \;\; \underset{n \to \infty}{\lim} a_n \;\; A \stackrel{f}{\rightarrow} B $$

        ## Framed formulas

        `\boxed{…}` (also spelled `\fbox{…}`) draws a rectangle around its content:

        $$ \boxed{e^{i\pi} + 1 = 0} \;\; \boxed{\frac{a}{b}} $$

        ## Ink without extent, extent without ink

        `\phantom` measures its argument and prints nothing, so it reserves space; `\hphantom` and
        `\vphantom` keep only the width or only the height. The two fractions below line up because the
        second reserves the width of the first's numerator:

        $$ \frac{a + b}{c} \;\; \frac{\phantom{a + b}}{c} \;\; \frac{\hphantom{a+b}}{c} \;\; x^{\vphantom{2}} $$

        `\smash` is the inverse — it prints without claiming any height — and `\mathllap` / `\mathrlap` /
        `\mathclap` print without claiming any width, so the content overlaps its neighbours:

        $$ \smash{\frac{a}{b}} \;\; a\mathllap{/}b \;\; a\mathrlap{/}b \;\; a\mathclap{/}b $$

        ## Arrays

        `array` is the only environment taking an argument: a column preamble, where `l`, `c` and `r`
        give each column its own alignment — something no other matrix environment offers — and `|`
        asks for a rule at that boundary. `\hline` rules between rows.

        $$ \begin{array}{lcr} \text{left} & \text{centre} & \text{right} \\ a & bb & ccc \end{array} $$

        Which is how an augmented matrix is set:

        $$ \left[\begin{array}{cc|c} 1 & 0 & 3 \\ 0 & 1 & 4 \end{array}\right] $$

        $$ \begin{array}{|c|c|} \hline x & f(x) \\ \hline 0 & 1 \\ 1 & e \\ \hline \end{array} $$

        A preamble asking for something the engine cannot draw — `p{2cm}`, `@{…}` — is an error rather
        than a grid quietly missing it.

        ## Horizontal braces

        `\overbrace{…}` and `\underbrace{…}` stretch a brace to the width of what they span. Both are
        operators, so the script that follows is set beyond the brace rather than beside it — `^` labels
        an `\overbrace`, `_` labels an `\underbrace`:

        $$ \overbrace{a + b + c}^{\text{three terms}} + \underbrace{d + e}_{\text{two more}} $$

        $$ \underbrace{\overbrace{a_1 + \cdots + a_n}^{n} + b}_{\text{everything}} $$

        A brace needs no label:

        $$ \overbrace{x + y} \;\; \underbrace{x + y} $$

        ## Stacked limits

        `\substack{… \\ …}` stacks several conditions into one limit of a big operator:

        $$ \sum_{\substack{0 < i < m \\ 0 < j < n}} P(i, j) \quad \prod_{\substack{p \text{ prime} \\ p \mid n}} p $$

        ## What a paper carries that a formula does not

        Numbering, cross references and page breaks belong to a document, not to a formula. So
        `equation`, `equation*` and `subequations` are nothing more than their contents, and `\tag`,
        `\notag`, `\label`, `\eqref`, `\numberwithin`, `\raisetag`, `\intertext`, `\shortintertext`,
        `\allowdisplaybreaks`, `\displaybreak`, `\nobreakdash`, `\DeclareMathOperator`,
        `\DeclarePairedDelimiter` and `\accentedsymbol` are read and dropped. `\shoveleft` and
        `\shoveright` keep their contents and drop only the shove.

        A formula lifted straight out of a paper therefore renders, instead of failing over a number
        it could never have carried:

        $$ \begin{equation} \label{eq:euler} e^{i\pi} + 1 = 0 \tag{4.2} \end{equation} $$
        """;

    private const string LatexMathFonts =
        """
        # LaTeX math — fonts, text & styling

        Math alphabets, embedded text, colour, spacing and cancellation. Thirteen Computer Modern,
        AMS and Euler faces are bundled, so every alphabet command sets its own typeface rather than
        falling back to roman. See the [symbols reference](latex-math-symbols.md) for the reading
        convention; each `$$` block is supported, with engine gaps flagged as *Not supported*.

        ## Math alphabets

        Every math alphabet has its own face:

        $$ \mathrm{Hamburg} \;\; \mathit{Hamburg} \;\; \mathbf{Hamburg} $$

        $$ \mathsf{Hamburg} \;\; \mathtt{Hamburg} \;\; \mathfrak{Hamburg} $$

        The two script alphabets are genuinely different — `\mathcal` is the symbol font's calligraphic
        capitals, `\mathscr` is Ralph Smith's Formal Script:

        $$ \mathcal{ABCDEFG} \qquad \mathscr{ABCDEFG} $$

        `\mathbb` is blackboard bold, from the AMS `msbm` font. It and `\mathscr` are the only
        capitals-only alphabets — neither face carries lowercase or digits, so those fall back:

        $$ \mathbb{N} \subset \mathbb{Z} \subset \mathbb{Q} \subset \mathbb{R} \subset \mathbb{C} $$

        ## Bold symbols

        `\boldsymbol{…}` (or `\bm`, or amsmath's `\pmb`) is not an alphabet: it takes each character from the bold companion
        of whatever font it would otherwise come from, so it reaches Greek letters and symbols, which an
        alphabet command cannot:

        $$ \boldsymbol{\alpha} + \boldsymbol{\beta} = \boldsymbol{\gamma} \qquad \boldsymbol{\nabla} \times \boldsymbol{F} \qquad \bm{\Sigma}\bm{x} = \bm{\lambda} $$

        A character whose font has no bold companion — an AMS symbol, say — is left as it is:

        $$ \boldsymbol{A \subsetneq B} $$

        $$ \pmb{\theta} = \bm{\theta} = \boldsymbol{\theta} $$

        ## Text inside math

        `\text` and the `\text*` family treat their argument as text rather than maths, so the spaces
        inside survive — and each gets the face it asks for, `\textit` the *text* italic rather than the
        maths one, `\textsc` real small capitals rather than full-height ones:

        $$ x + y = z \;\; \text{(a labelled equation)} \;\; \textrm{roman text} \;\; \textit{italic text} $$

        $$ \textbf{bold text} \;\; \textsf{sans text} \;\; \texttt{mono text} \;\; \textsc{small caps} $$

        `\mbox{…}` is the same thing under another name:

        $$ n > 0 \;\; \mbox{for every n in the set} $$

        ## Named operators

        `\operatorname{…}` sets a name upright *and* types it as an operator, so it takes operator
        spacing and a following script becomes its limit rather than a subscript hanging off the last
        letter. The starred form puts that limit underneath in display style:

        $$ \operatorname{Tr}(A) = \sum_i a_{ii} \qquad \operatorname*{argmax}_{\theta} L(\theta) $$

        ## Colour

        $$ \color{red}{a^2} + \color{blue}{b^2} = \color{green}{c^2} $$

        `\textcolor` is accepted as a spelling of the same two-argument command:

        $$ \textcolor{red}{a^2} + \textcolor{blue}{b^2} = \textcolor{green}{c^2} $$

        ## Cancellation

        $$ \frac{\cancel{x}\, y}{\cancel{x}} = y \;\; \bcancel{a + b} \;\; \xcancel{a + b} $$

        ## Spacing

        Thin, negative, medium and thick spaces (`\,` `\!` `\:` `\;`), the wider `\quad` and
        `\qquad`, the tie `~` and control space `\ `, and explicit `\hspace{<length>}`:

        $$ x\,y \;\; x\!y \;\; x\:y \;\; x\;y \;\; x\quad y \;\; x\qquad y \;\; x~y \;\; x\ y \;\; x\hspace{12pt}y $$

        The decorations that used to be listed here as unsupported — `\overbrace`, `\underbrace`,
        `\substack`, the phantoms and the stacked annotations — all render now; they live in the
        [structures reference](latex-math-structures.md).
        """;

    private const string LatexMathAmssymb =
        """
        # LaTeX math — AMS symbols (amssymb)

        A coverage map of the AMS `amssymb` symbol set: **all 224** symbols render. Both AMS symbol fonts
        are bundled — `msam` (symbols A) and `msbm` (symbols B) — so what used to be listed here as
        *Not supported* is now just a set of glyphs.

        Every gap this page once had was a **missing font**, not a layout limitation. `msbm` carries
        blackboard bold, the Hebrew letters, and the relations where "not equal" is drawn into the symbol
        rather than slashed through it (`\subsetneq`, `\lneqq`, …); a few `msam` glyphs were present all
        along but unmapped. The negated relations are real glyphs now rather than a base relation with a
        zero-width `\not` laid over it.

        See the [symbols reference](latex-math-symbols.md) for the core LaTeX symbols and the reading convention.

        ## Relations — 66 of 66 render

        $$ \leqq \;\; \leqslant \;\; \eqslantless \;\; \lesssim \;\; \lessapprox \;\; \lll \;\; \lessgtr \;\; \lesseqgtr \;\; \lesseqqgtr \;\; \doteqdot \;\; \risingdotseq \;\; \fallingdotseq \;\; \backsim $$

        $$ \backsimeq \;\; \subseteqq \;\; \Subset \;\; \sqsubset \;\; \preccurlyeq \;\; \curlyeqprec \;\; \precsim \;\; \vartriangleleft \;\; \trianglelefteq \;\; \vDash \;\; \Vvdash \;\; \smallsmile \;\; \smallfrown $$

        $$ \bumpeq \;\; \Bumpeq \;\; \geqq \;\; \geqslant \;\; \eqslantgtr \;\; \gtrsim \;\; \gtrapprox \;\; \ggg \;\; \gtrless \;\; \gtreqless \;\; \gtreqqless \;\; \eqcirc \;\; \circeq $$

        $$ \triangleq \;\; \supseteqq \;\; \Supset \;\; \sqsupset \;\; \succcurlyeq \;\; \curlyeqsucc \;\; \succsim \;\; \vartriangleright \;\; \trianglerighteq \;\; \Vdash \;\; \between \;\; \pitchfork \;\; \varpropto $$

        $$ \blacktriangleleft \;\; \therefore \;\; \blacktriangleright \;\; \because $$

        $$ \approxeq \;\; \lessdot \;\; \precapprox \;\; \gtrdot \;\; \thicksim \;\; \thickapprox \;\; \succapprox \;\; \shortmid \;\; \shortparallel \;\; \backepsilon \;\; \eqsim $$

        ## Binary operators — 23 of 23 render

        $$ \dotplus \;\; \Cap \;\; \Cup \;\; \barwedge \;\; \veebar \;\; \doublebarwedge \;\; \boxminus \;\; \boxtimes \;\; \boxdot \;\; \boxplus \;\; \leftthreetimes \;\; \rightthreetimes \;\; \curlywedge $$

        $$ \curlyvee \;\; \circleddash \;\; \circledast \;\; \circledcirc \;\; \centerdot \;\; \intercal $$

        $$ \smallsetminus \;\; \divideontimes \;\; \ltimes \;\; \rtimes $$

        ## Arrows — 32 of 32 render

        $$ \leftleftarrows \;\; \leftrightarrows \;\; \Lleftarrow \;\; \twoheadleftarrow \;\; \leftarrowtail \;\; \looparrowleft \;\; \leftrightharpoons \;\; \circlearrowleft \;\; \Lsh \;\; \upuparrows \;\; \upharpoonleft \;\; \downharpoonleft \;\; \multimap $$

        $$ \leftrightsquigarrow \;\; \rightrightarrows \;\; \rightleftarrows \;\; \twoheadrightarrow \;\; \rightarrowtail \;\; \looparrowright \;\; \rightleftharpoons \;\; \circlearrowright \;\; \Rsh \;\; \downdownarrows \;\; \upharpoonright \;\; \downharpoonright \;\; \rightsquigarrow $$

        $$ \Rrightarrow \;\; \leadsto $$

        $$ \dashrightarrow \;\; \dashleftarrow \;\; \curvearrowleft \;\; \curvearrowright $$

        ## Negated relations — 56 of 56 render

        $$ \nless \;\; \nleq \;\; \nleqslant \;\; \nleqq \;\; \nprec \;\; \npreceq \;\; \nsim \;\; \nmid \;\; \nvdash \;\; \nVdash \;\; \ntriangleleft \;\; \ntrianglelefteq \;\; \nsubseteq $$

        $$ \ngtr \;\; \ngeq \;\; \ngeqslant \;\; \ngeqq \;\; \nsucc \;\; \nsucceq \;\; \ncong \;\; \nparallel \;\; \nvDash \;\; \ntriangleright \;\; \ntrianglerighteq \;\; \nsupseteq \;\; \nleftarrow $$

        $$ \nrightarrow \;\; \nLeftarrow \;\; \nRightarrow \;\; \nleftrightarrow \;\; \nLeftrightarrow $$

        The strict negations, where the inequality is drawn into the glyph:

        $$ \lneq \;\; \lneqq \;\; \lvertneqq \;\; \lnsim \;\; \lnapprox \;\; \precnsim \;\; \precnapprox \;\; \precneqq \;\; \nshortmid \;\; \subsetneq \;\; \varsubsetneq \;\; \subsetneqq \;\; \varsubsetneqq $$

        $$ \gneq \;\; \gneqq \;\; \gvertneqq \;\; \gnsim \;\; \gnapprox \;\; \succnsim \;\; \succnapprox \;\; \succneqq \;\; \nshortparallel \;\; \nVDash \;\; \supsetneq \;\; \varsupsetneq \;\; \supsetneqq \;\; \varsupsetneqq $$

        ## Miscellaneous & letters — 41 of 41 render

        $$ \hbar \;\; \vartriangle \;\; \triangledown \;\; \square \;\; \lozenge \;\; \circledS \;\; \angle \;\; \measuredangle \;\; \sphericalangle \;\; \nexists \;\; \backprime \;\; \varnothing \;\; \blacktriangle $$

        $$ \blacktriangledown \;\; \blacksquare \;\; \blacklozenge \;\; \bigstar \;\; \complement \;\; \yen \;\; \checkmark \;\; \maltese \;\; \ulcorner \;\; \urcorner \;\; \llcorner \;\; \lrcorner \;\; \Box $$

        $$ \Diamond $$

        $$ \hslash \;\; \mho \;\; \Finv \;\; \Game \;\; \Bbbk \;\; \eth \;\; \diagup \;\; \diagdown \;\; \digamma \;\; \varkappa \;\; \beth \;\; \gimel \;\; \daleth \;\; \circledR $$

        ## Blackboard bold

        `\mathbb` is the `msbm` alphabet. It is capitals-only, as the font carries no blackboard
        lowercase or digits:

        $$ \mathbb{ABCDEFGHIJKLMNOPQRSTUVWXYZ} $$

        ## Synonyms for existing symbols — 6 of 6 render

        $$ \doublecap \;\; \doublecup \;\; \restriction \;\; \Doteq \;\; \llless \;\; \gggtr $$
        """;

    private const string Venn =
        """
        # Mermaid — Venn diagram

        A `venn-beta` diagram shows overlapping `set` circles. Comma is the only intersection operator —
        `union A,B` is the A∩B region. A `["Label"]` renames a region; a set's `:N` is its circle's area and
        a union's how much its sets overlap. Indented `text` lines list items inside the set or union above
        them, and a `text` line at the start of a line names its region first. Front-matter `config: venn:`
        (`width`/`height`/`padding`/`useMaxWidth`/`useDebugLayout`) and the `venn1…venn8`,
        `vennTitleTextColor` and `vennSetTextColor` theme variables are honoured.

        ## Team overlap

        ```mermaid
        venn-beta
          title "Team overlap"
          set Frontend
          set Backend
          union Frontend,Backend["APIs"]
        ```

        ## Labels, sizes, and items

        ```mermaid
        venn-beta
          set A["Frontend"]:20
            text A1["React"]
            text A2["Design Systems"]
          set B["Backend"]:12
            text B1["API"]
          union A,B["Shared"]:3
            text AB1["OpenAPI"]
        ```

        ## Three sets (desirability / feasibility / viability)

        ```mermaid
        venn-beta
          set Desirable
          set Feasible
          set Viable
          union Desirable,Feasible,Viable["Innovation"]
        ```

        ## Styling and a custom palette

        ```mermaid
        ---
        config:
          themeVariables:
            venn1: "#4e79a7"
            venn2: "#e15759"
        ---
        venn-beta
          set A["Alpha"]:20
            text A1["React"]
          set B["Beta"]:12
          union A,B["AB"]:3
          style A fill:#ff6b6b
          style A,B color:#cccccc
        ```

        ## Sizes, an item naming its region, and every style

        ```mermaid
        ---
        config:
          themeVariables:
            vennTitleTextColor: "#e15759"
            vennSetTextColor: "#f2f2f2"
        ---
        venn-beta
          title "What makes a good feature"
          set Desirable:12
          set Feasible:10
          set Viable:8
          union Desirable,Feasible["Buildable"]:3
          union Feasible,Viable["Sustainable"]
          union Desirable,Viable["Marketable"]
          union Desirable,Feasible,Viable["Ship it"]
        text Desirable,Feasible,Viable "a spike"
          style Viable stroke:#59a14f, stroke-width:4, fill-opacity:0.4
          style Desirable,Feasible fill:#f28e2b, fill-opacity:0.5
          style "a spike" color:#ffd166
        ```
        """;

    private const string Architecture =
        """
        # Mermaid — Architecture diagram

        An `architecture-beta` diagram shows grouped `service` nodes joined by side-anchored edges. A
        `group id(icon)[Title]` box contains services (`service id(icon)[Title] in group`); edges attach to
        a declared `T`/`B`/`L`/`R` side (`db:R -- L:server`), carry optional arrowheads, and may cross
        groups via the `{group}` suffix. Default icons (cloud/database/disk/internet/server) render as
        built-in glyphs; `junction` nodes route four ways.

        ## Grouped services with icons

        ```mermaid
        architecture-beta
            group api(cloud)[API]
            service db(database)[Database] in api
            service disk1(disk)[Storage] in api
            service server(server)[Server] in api
            db:L -- R:server
            disk1:T -- B:server
        ```

        ## Cross-group edges and a junction

        ```mermaid
        architecture-beta
            group public(cloud)[Public]
            group private(cloud)[Private]
            service gateway(internet)[Gateway] in public
            service app(server)[App] in private
            junction j1 in private
            gateway:R --> L:app
            app:B -- T:j1
            gateway{group}:B --> T:app{group}
        ```

        ## Alignment and custom icons

        ```mermaid
        architecture-beta
            service left(server)[Left]
            service mid(server)[Middle]
            service right(server)[Right]
            left:R -- L:mid
            mid:R -- L:right
            align row left mid right
        ```
        """;

    private const string Swimlane =
        """
        # Mermaid — Swimlane diagram

        A `swimlane-beta` diagram is a flowchart divided by who owns each step: every `subgraph` written
        outside them all is a lane, a band of its own that the work in it runs through, named at the near
        end of the band. An optional direction (`TB`/`TD`/`BT`/`LR`/`RL`) follows the keyword. A lane's own
        steps come one to a rank, and an arrow to another lane is a handoff, which goes across rather than
        on. Everything else is a flowchart's: every node shape, every link, classes and styles.

        ## Vertical lanes (default TB)

        ```mermaid
        swimlane-beta
            subgraph customer[Customer]
                start([Place order])
                pay[Pay]
            end
            subgraph fulfilment[Fulfilment]
                pick{In stock?}
                ship[Ship order]
            end
            start --> pay
            pay --> pick
            pick -->|Yes| ship
            pick -.->|No| pay
        ```

        ## Horizontal lanes (LR), a lane named in words, and classes

        ```mermaid
        swimlane-beta LR
            subgraph dev[Developer]
                code[Write code]
                fix(Fix issues)
            end
            subgraph Build server
                build[Build]
                test{Tests pass?}
            end
            code ==> build
            build --> test
            test -->|Yes| done([Deploy])
            test --> fix
            classDef waiting fill:#6e6ce6,stroke:#333
            class test waiting
        ```
        """;

    private const string Cynefin =
        """
        # Mermaid — Cynefin diagram

        A `cynefin-beta` diagram places items into the five sense-making domains — `complex` (top left),
        `complicated` (top right), `chaotic` (bottom left), `clear` (bottom right) and the central
        `confusion`, drawn as a cloud. A domain's word on a line of its own opens it, and every line after
        it is an item in it, written in quotes or bare. Transitions (`domainA --> domainB : "label"`) draw
        as labelled arrows, bending round the middle where they would cross it; the grid grows to hold
        everything written in it.

        ## Making sense of the work

        ```mermaid
        cynefin-beta
            title Making sense of the work
            complex
                "Investigate root cause"
                "Run a safe-to-fail experiment"
            complicated
                "Consult an expert"
                "Analyse the trade-offs"
            clear
                "Apply the standard runbook"
            chaotic
                Stop the bleeding
            confusion
                "Unclassified incident A"
                "Unclassified incident B"
                "Unclassified incident C"
                "Unclassified incident D"
            chaotic --> complex : "Stabilised"
            complex --> complicated : "Pattern found"
            complicated --> clear
        ```

        ## With domain descriptions and theme colours

        ```mermaid
        ---
        config:
          cynefin:
            showDomainDescriptions: true
          themeVariables:
            cynefin:
              complexBg: "#4e79a7"
              clearBg: "#59a14f"
        ---
        cynefin-beta
            complex
                "Emergent practice"
            clear
                "Best practice"
        ```
        """;

    private const string Er =
        """
        # Mermaid — Entity Relationship diagram

        An `erDiagram` draws entities and what holds between them. An entity is its name over its attributes
        (`type name [PK|FK|UK] ["comment"]`), set in columns so they read down as well as across. How many of
        each entity the other has is drawn as crow's feet, written either as symbols (`||--o{`, `}o..o{`) or
        in words (`one to zero or more`); `--` identifies what it reaches and is drawn solid, `..` does not
        and is drawn dotted. A `subgraph … end` boxes what is written inside it. Front-matter `config: er:`
        (`minEntityWidth`, `fontSize`, `layoutDirection`, `fill`, `stroke`, …) is honoured. It is drawn on the
        shared layout tree, so every word is the characters written and the caret goes into them.

        ## Order example with attributes

        ```mermaid
        ---
        title: Order example
        ---
        erDiagram
            CUSTOMER ||--o{ ORDER : places
            CUSTOMER {
                string name
                string custNumber
                string sector
            }
            ORDER ||--|{ LINE-ITEM : contains
            ORDER {
                int orderNumber
                string deliveryAddress
            }
            LINE-ITEM {
                string productCode
                int quantity
                float pricePerUnit
            }
            CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
        ```

        ## Attribute keys, comments, and array types

        ```mermaid
        erDiagram
            CAR ||--o{ NAMED-DRIVER : allows
            CAR {
                string registrationNumber PK
                string make
                string model
                string[] parts
            }
            PERSON ||--o{ NAMED-DRIVER : is
            PERSON {
                string driversLicense PK "The license #"
                string(99) firstName "Only 99 characters are allowed"
                string lastName
                string phone UK
                int age
            }
            NAMED-DRIVER {
                string carRegistrationNumber PK, FK
                string driverLicence PK, FK
            }
            MANUFACTURER only one to zero or more CAR : makes
        ```

        ## Word-alias cardinality and non-identifying relationships

        ```mermaid
        erDiagram
            CAR 1 to zero or more NAMED-DRIVER : allows
            PERSON many(0) optionally to 0+ NAMED-DRIVER : is
        ```

        ## Entity name aliases

        ```mermaid
        erDiagram
            p[Person] {
                string firstName
                string lastName
            }
            a["Customer Account"] {
                string email
            }
            p ||--o| a : has
        ```

        ## Direction and styling

        ```mermaid
        ---
        config:
          er:
            layoutDirection: LR
        ---
        erDiagram
            CAR:::someclass {
                string registrationNumber
                string make
            }
            PERSON:::someclass {
                string firstName
                int age
            }
            PERSON ||--o{ CAR : drives

            classDef someclass fill:#f96
        ```
        """;

    private const string Sankey =
        """"
        # Mermaid — Sankey diagram

        A `sankey` diagram depicts a flow from one set of values to another. The body is CSV with three
        columns — `source,target,value`, one link per row; nodes are inferred from the names. Fields with
        commas are double-quoted (a literal quote is a doubled `""`). Front-matter `config: sankey:`
        (sizes, `linkColor`, `nodeAlignment`, `showValues`/`prefix`/`suffix`, `nodeWidth`/`nodePadding`,
        `labelStyle`, `nodeColors`) is honoured.

        ## Energy flow

        ```mermaid
        ---
        config:
          sankey:
            showValues: false
        ---
        sankey

        Agricultural 'waste',Bio-conversion,124.729
        Bio-conversion,Liquid,0.597
        Bio-conversion,Losses,26.862
        Bio-conversion,Solid,280.322
        Bio-conversion,Gas,81.144
        Biofuel imports,Liquid,35
        Biomass imports,Solid,35
        Coal imports,Coal,11.606
        Coal reserves,Coal,63.965
        Coal,Solid,75.571
        District heating,Industry,10.639
        District heating,Heating and cooling - commercial,22.505
        District heating,Heating and cooling - homes,46.184
        Electricity grid,Over generation / exports,104.453
        Electricity grid,Heating and cooling - homes,113.726
        Electricity grid,H2 conversion,27.14
        Electricity grid,Industry,342.165
        Electricity grid,Road transport,37.797
        Electricity grid,Agriculture,4.412
        Electricity grid,Heating and cooling - commercial,40.858
        Electricity grid,Losses,56.691
        Electricity grid,Rail transport,7.863
        Electricity grid,Lighting & appliances - commercial,90.008
        Electricity grid,Lighting & appliances - homes,93.494
        Gas imports,Ngas,40.719
        Gas reserves,Ngas,82.233
        Gas,Heating and cooling - commercial,0.129
        Gas,Losses,1.401
        Gas,Thermal generation,151.891
        Gas,Agriculture,2.096
        Gas,Industry,48.58
        Geothermal,Electricity grid,7.013
        H2 conversion,H2,20.897
        H2 conversion,Losses,6.242
        H2,Road transport,20.897
        Hydro,Electricity grid,6.995
        Liquid,Industry,121.066
        Liquid,International shipping,128.69
        Liquid,Road transport,135.835
        Liquid,Domestic aviation,14.458
        Liquid,International aviation,206.267
        Liquid,Agriculture,3.64
        Liquid,National navigation,33.218
        Liquid,Rail transport,4.413
        Marine algae,Bio-conversion,4.375
        Ngas,Gas,122.952
        Nuclear,Thermal generation,839.978
        Oil imports,Oil,504.287
        Oil reserves,Oil,107.703
        Oil,Liquid,611.99
        Other waste,Solid,56.587
        Other waste,Bio-conversion,77.81
        Pumped heat,Heating and cooling - homes,193.026
        Pumped heat,Heating and cooling - commercial,70.672
        Solar PV,Electricity grid,59.901
        Solar Thermal,Heating and cooling - homes,19.263
        Solar,Solar Thermal,19.263
        Solar,Solar PV,59.901
        Solid,Agriculture,0.882
        Solid,Thermal generation,400.12
        Solid,Industry,46.477
        Thermal generation,Electricity grid,525.531
        Thermal generation,Losses,787.129
        Thermal generation,District heating,79.329
        Tidal,Electricity grid,9.452
        UK land based bioenergy,Bio-conversion,182.01
        Wave,Electricity grid,19.013
        Wind,Electricity grid,289.366
        ```

        ## Basic (with values, and a `%%` header comment)

        ```mermaid
        sankey

        %% source,target,value
        Electricity grid,Over generation / exports,104.453
        Electricity grid,Heating and cooling - homes,113.726
        Electricity grid,H2 conversion,27.14
        ```

        ## Quoted names with commas and doubled quotes

        ```mermaid
        sankey

        Pumped heat,"Heating and cooling, homes",193.026
        Pumped heat,"Heating and cooling, ""commercial""",70.672
        ```

        ## Config — node colours, alignment, units

        ```mermaid
        ---
        config:
          sankey:
            showValues: true
            suffix: " TWh"
            nodeAlignment: left
            nodeWidth: 15
            nodePadding: 18
            linkColor: gradient
            nodeColors:
              Electricity grid: "#4e79a7"
              Industry: "#e15759"
              Losses: "#bab0ab"
        ---
        sankey

        Electricity grid,Heating and cooling - homes,113.726
        Electricity grid,Industry,342.165
        Electricity grid,Losses,56.691
        ```
        """";

    private const string Ishikawa =
        """
        # Mermaid — Ishikawa (fishbone) chart

        An `ishikawa-beta` (alias `ishikawa`) diagram does cause-and-effect / root-cause analysis. The
        first line is the effect (the fish head); every later line is a cause, and the fishbone structure
        comes purely from indentation — categories at the shallowest indent, sub-causes nested deeper.

        ## Blurry photo — nested causes

        ```mermaid
        ishikawa-beta
            Blurry Photo
            Process
                Out of focus
                Shutter speed too slow
                Protective film not removed
                Beautification filter applied
            User
                Shaky hands
            Equipment
                LENS
                    Inappropriate lens
                    Damaged lens
                    Dirty lens
                SENSOR
                    Damaged sensor
                    Dirty sensor
            Environment
                Subject moved too quickly
                Too dark
        ```

        ## Slow API response — two-space indentation

        ```mermaid
        ishikawa-beta
        Slow API Response
          Infrastructure
            Underpowered instances
            No CDN
          Code
            N+1 queries
            Unoptimized indexes
            Missing caching
          Process
            No performance budgets
            Reviews skip load testing
        ```
        """;

    private const string Radar =
        """
        # Mermaid — Radar chart

        A `radar-beta` diagram (radar / spider / Kiviat chart) plots one or more `curve` datasets over a
        set of `axis` spokes. Curve values are positional (`{1, 2, 3}`, mapped to the axes in order) or
        keyed (`{ axisId: value }`). Body options `min` / `max` / `ticks` / `graticule` / `showLegend`
        and front-matter `config: radar:` (geometry), `config: themeVariables: radar:` (styling) and the
        `cScale0…N` curve palette are honoured.

        ## Grades — labeled axes and curves, explicit range

        ```mermaid
        ---
        title: "Grades"
        ---
        radar-beta
          axis m["Math"], s["Science"], e["English"]
          axis h["History"], g["Geography"], a["Art"]
          curve a["Alice"]{85, 90, 80, 70, 75, 90}
          curve b["Bob"]{70, 75, 85, 80, 90, 85}

          max 100
          min 0
        ```

        ## Restaurant comparison — polygon graticule

        ```mermaid
        radar-beta
          title Restaurant Comparison
          axis food["Food Quality"], service["Service"], price["Price"]
          axis ambiance["Ambiance"]

          curve a["Restaurant A"]{4, 3, 2, 4}
          curve b["Restaurant B"]{3, 4, 3, 3}
          curve c["Restaurant C"]{2, 3, 4, 2}
          curve d["Restaurant D"]{2, 2, 4, 3}

          graticule polygon
          max 5
        ```

        ## Bare axes, keyed values, and options

        ```mermaid
        radar-beta
          axis axis1, axis2, axis3
          curve id1["Label1"]{1, 2, 3}
          curve id2["Label2"]{4, 5, 6}, id3{7, 8, 9}
          curve id4{ axis3: 30, axis1: 20, axis2: 10 }

          showLegend true
          ticks 5
          graticule circle
          max 30
        ```

        ## Config and theme — scale factor, tension, custom curve colours

        ```mermaid
        ---
        config:
          radar:
            axisScaleFactor: 0.9
            curveTension: 0.1
          themeVariables:
            cScale0: "#FF0000"
            cScale1: "#00FF00"
            cScale2: "#0000FF"
            radar:
              curveOpacity: 0.5
        ---
        radar-beta
          axis A, B, C, D, E
          curve c1{1, 2, 3, 4, 5}
          curve c2{5, 4, 3, 2, 1}
          curve c3{3, 3, 3, 3, 3}
        ```
        """;

    private const string XyChart =
        """
        # Mermaid — XY chart

        An `xychart` (alias `xychart-beta`) plots `bar` and `line` series against a categorical or
        numeric x-axis and a numeric y-axis. Series share the axes; a named series joins the legend.
        Front-matter `config: xyChart:` (layout/flags) and `config: themeVariables: xyChart:` (colours,
        `plotColorPalette`) are honoured.

        ## Basic — bars and a line

        ```mermaid
        xychart-beta
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
            line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
        ```

        ## Horizontal orientation

        ```mermaid
        xychart-beta horizontal
            title "Books read per month"
            x-axis [jan, feb, mar, apr, may, jun]
            y-axis "Books" 0 --> 10
            bar [2, 4, 3, 6, 5, 8]
        ```

        ## Legend — multiple named line series

        ```mermaid
        xychart-beta
          title "An Example Chart"
          x-axis ["90d", "60d", "30d", "7d", "1d", "Current"]
          y-axis "Seconds" 0 --> 198.2
          line "avg" [48.1, 41.5, 45.7, 72.8, 67.7, 59.9]
          line "p50" [38.2, 36.8, 39.7, 54.5, 49.0, 38.4]
          line "p95" [112.2, 75.3, 103.0, 177.0, 180.2, 109.4]
        ```

        ## Simplest possible — one dataset, axes auto-ranged

        ```mermaid
        xychart-beta
            line [+1.3, .6, 2.4, -.34]
        ```

        ## Custom colours via `plotColorPalette` (with `%%` comments)

        ```mermaid
        ---
        config:
          themeVariables:
            xyChart:
              plotColorPalette: '#000000, #0000FF, #00FF00, #FF0000'
        ---
        xychart-beta
        title "Different Colors in xyChart"
        x-axis "categoriesX" ["Category 1", "Category 2", "Category 3", "Category 4"]
        y-axis "valuesY" 0 --> 50
        %% Black line
        line [10, 20, 30, 40]
        %% Blue bar
        bar [20, 30, 25, 35]
        %% Green bar
        bar [15, 25, 20, 30]
        %% Red line
        line [5, 15, 25, 35]
        ```

        ## Data labels inside the bars (`showDataLabel`)

        ```mermaid
        ---
        config:
            xyChart:
                showDataLabel: true
        ---
        xychart-beta
            title "Genres in top 100 book survey of 2025"
            x-axis [comedy, romance, mystery, crime, "non fiction", other]
            y-axis "Number of Books" 0 --> 30
            bar [12, 2, 20, 25, 17, 24]
        ```

        ## Data labels outside the bars (`showDataLabelOutsideBar`)

        ```mermaid
        ---
        config:
            xyChart:
                showDataLabel: true
                showDataLabelOutsideBar: true
        ---
        xychart-beta
            title "Genres in top 100 book survey of 2025"
            x-axis [comedy, romance, mystery, crime, "non fiction", other]
            y-axis "Number of Books" 0 --> 30
            bar [12, 2, 20, 25, 17, 24]
        ```

        ## Per-point labels on a line

        ```mermaid
        xychart-beta
            title "Smallest AI models scoring above 60% on MMLU"
            x-axis "Date" ["Apr 2022", "Feb 2023", "Jul 2023", "Sep 2023", "Apr 2024"]
            y-axis "Parameters (B)" 0 --> 600
            line [540 "PaLM", 65 "LLaMA-65B", 34 "Llama 2 34B", 7 "Mistral 7B", 3.8 "Phi-3-mini"]
        ```

        ## Per-point labels mixed with unlabeled values

        ```mermaid
        xychart-beta
            title "Quarterly Performance"
            x-axis [Q1, Q2, Q3, Q4]
            y-axis "Revenue ($M)" 0 --> 100
            line [25 "Launch", 45, 72, 90 "Target Hit"]
        ```

        ## Combined layout config and theme colour

        ```mermaid
        ---
        config:
            xyChart:
                width: 900
                height: 600
                showDataLabel: true
            themeVariables:
                xyChart:
                    titleColor: "#ff0000"
        ---
        xychart-beta
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
            line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
        ```
        """;

    private const string Kanban =
        """
        # Mermaid — Kanban board

        A `kanban` board lays out columns (workflow stages) left-to-right, each holding a stack of
        cards. Hierarchy comes from indentation — columns at the shallowest indent, cards beneath.
        A node is `id[Title]`, `[Title]` or bare `Title`; cards may carry a `@{ … }` metadata block
        with `ticket`, `assigned` and `priority` (`Very High` / `High` / `Low` / `Very Low`).

        ```mermaid
        ---
        config:
          kanban:
            ticketBaseUrl: 'https://mermaidchart.atlassian.net/browse/#TICKET#'
        ---
        kanban
          Todo
            [Create Documentation]
            docs[Create Blog about the new diagram]
          [In progress]
            id6[Create renderer so that it works in all cases. We also add some extra text here for testing purposes. And some more just for the extra flare.]
          id9[Ready for deploy]
            id8[Design grammar]@{ assigned: 'knsv' }
          id10[Ready for test]
            id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
            id66[last item]@{ priority: 'Very Low', assigned: 'knsv' }
          id11[Done]
            id5[define getData]
            id2[Title of diagram is more than 100 chars when user duplicates diagram with 100 char]@{ ticket: MC-2036, priority: 'Very High'}
            id3[Update DB function]@{ ticket: MC-2037, assigned: knsv, priority: 'High' }

          id12[Can't reproduce]
            id3[Weird flickering in Firefox]
        ```

        Minimal board

        ```mermaid
        kanban
          column1[Backlog]
            task1[Investigate flaky test]
            task2[Write design doc]
          column2[Doing]
            task3[Implement parser]
          column3[Done]
        ```
        """;

    private const string Requirement =
        """
        # Mermaid — Requirement diagram

        A `requirementDiagram` (SysML-style) draws requirements and the elements that meet them as
        boxes — what kind of thing it is over its name, then a row for each field (`id`, `text`,
        `risk`, `verifymethod`; an element's `type` and `docref`) — joined by lines saying what holds
        between them: `contains` is the whole and its parts, a solid line with a crosshair at the end
        that holds, and the rest (`copies`, `derives`, `satisfies`, `verifies`, `refines`, `traces`)
        are dashed arrows. It is drawn on the shared layout tree, so every word is the characters
        written and the caret goes into them.

        Full example

        ```mermaid
        requirementDiagram

        requirement test_req {
            id: 1
            text: the test text.
            risk: high
            verifymethod: test
        }

        functionalRequirement test_req2 {
            id: 1.1
            text: the second test text.
            risk: low
            verifymethod: inspection
        }

        performanceRequirement test_req3 {
            id: 1.2
            text: the third test text.
            risk: medium
            verifymethod: demonstration
        }

        interfaceRequirement test_req4 {
            id: 1.2.1
            text: the fourth test text.
            risk: medium
            verifymethod: analysis
        }

        physicalRequirement test_req5 {
            id: 1.2.2
            text: the fifth test text.
            risk: medium
            verifymethod: analysis
        }

        designConstraint test_req6 {
            id: 1.2.3
            text: the sixth test text.
            risk: medium
            verifymethod: analysis
        }

        element test_entity {
            type: simulation
        }

        element test_entity2 {
            type: word doc
            docref: reqs/test_entity
        }

        element test_entity3 {
            type: "test suite"
            docref: github.com/all_the_tests
        }

        test_entity - satisfies -> test_req2
        test_req - traces -> test_req2
        test_req - contains -> test_req3
        test_req3 - contains -> test_req4
        test_req4 - derives -> test_req5
        test_req5 - refines -> test_req6
        test_entity3 - verifies -> test_req5
        test_entity - copies -> test_entity2
        ```

        Reverse-arrow form, direction and styling

        ```mermaid
        requirementDiagram
            direction LR

            requirement db_req {
                id: 2
                text: store the records durably.
                risk: high
                verifymethod: test
            }
            element database {
                type: postgres
                docref: infra/db
            }

            database <- satisfies - db_req
            classDef important fill:#f9f,stroke:#333,color:#000
            class db_req important
            style database fill:#bbf,stroke:#338
        ```
        """;

    private const string Class =
        """
        # Mermaid — Class diagram

        A `classDiagram` draws UML classes as boxes with name / attribute / method
        compartments, connected by relationships, on the shared layout tree: the classes
        are put in ranks by how far along the relations reach them, and the operator at
        each end says what that end draws (hollow triangle for inheritance, filled or
        hollow diamond for composition and aggregation, a circle for an interface offered
        as a lollipop). Members are read as Mermaid reads them — `~T~` drawn between angle
        brackets, a trailing `$` underlined and a trailing `*` italic, a return type after
        a colon — and a namespace boxes the classes written inside it.

        Inheritance with members, a title, and notes (with `<br>`)

        ```mermaid
        ---
        title: Animal example
        ---
        classDiagram
            note "From Duck till Zebra"
            Animal <|-- Duck
            note for Duck "can fly<br>can swim<br>can dive<br>can help in debugging"
            Animal <|-- Fish
            Animal <|-- Zebra
            Animal : +int age
            Animal : +String gender
            Animal: +isMammal()
            Animal: +mate()
            class Duck{
                +String beakColor
                +swim()
                +quack()
            }
            class Fish{
                -int sizeInFeet
                -canEat()
            }
            class Zebra{
                +bool is_wild
                +run()
            }
        ```

        Class body block

        ```mermaid
        classDiagram
            class BankAccount {
                +String owner
                +BigDecimal balance
                +deposit(amount)
                +withdrawal(amount)
            }
        ```

        Members, return types and generics (incl. nested generics)

        ```mermaid
        classDiagram
            class Square~Shape~{
                int id
                List~int~ position
                setPoints(List~int~ points)
                getPoints() List~int~
            }
            Square : -List~string~ messages
            Square : +setMessages(List~string~ messages)
            Square : +getMessages() List~string~
            Square : +getDistanceMatrix() List~List~int~~
        ```

        Visibility, static and abstract classifiers

        ```mermaid
        classDiagram
            class ClassWithMembers {
                +String id
                +int count$
                +publicMethod()
                -privateMethod()
                #protectedMethod()
                ~packagePrivateMethod()
                +staticMethod()$
                +abstractMethod()*
            }
        ```

        Every relationship type

        ```mermaid
        classDiagram
            classA <|-- classB
            classC *-- classD
            classE o-- classF
            classG <-- classH
            classI -- classJ
            classK <.. classL
            classM <|.. classN
            classO .. classP
        ```

        Annotations (stereotypes)

        ```mermaid
        classDiagram
            class Shape {
                <<interface>>
                noOfVertices
                draw()
            }
            class Color {
                <<enumeration>>
                RED
                GREEN
                BLUE
            }
        ```

        Cardinality / multiplicity / two-way relations and labels

        ```mermaid
        classDiagram
            Customer "1" --> "*" Ticket
            Student "1" --o "1..*" Course
            Galaxy --> "many" Star : Contains
            Planets "0..12" --> "*" Star
            Animal <|--|> Zebra
        ```

        Nested (hierarchical) namespaces

        ```mermaid
        classDiagram
            namespace Company.Engineering.Backend {
                class Developer {
                    +writeCode()
                }
            }
            namespace Company.Engineering.Frontend {
                class Designer {
                    +createMockup()
                }
            }
            namespace Company.Engineering {
                class TechLead {
                    +planSprint()
                }
            }
            TechLead --> Developer : leads
            TechLead --> Designer : leads
        ```

        Multiplicity on a relationship whose ends live inside namespaces

        ```mermaid
        classDiagram
            namespace Ordering {
                class Order {
                    +int id
                }
                class LineItem {
                    +int quantity
                }
            }
            namespace Billing {
                class Invoice
            }
            Order "1" --> "*" LineItem : contains
            Order "1" --> "0..1" Invoice : billed by
        ```

        Lollipop interfaces

        ```mermaid
        classDiagram
            class Class01 {
                int amount
                draw()
            }
            Class01 --() bar
            Class02 --() bar
            foo ()-- Class01
        ```

        Notes

        ```mermaid
        classDiagram
            class Duck
            note "A general note about this diagram"
            note for Duck "Can fly\nCan swim\nCan dive"
        ```

        Direction and styling

        ```mermaid
        classDiagram
            direction LR
            class Student {
                +String name
            }
            class Course
            Student "1" --> "*" Course : enrolled
            classDef highlight fill:#f9f,stroke:#333,color:#000
            class Student:::highlight
            style Course fill:#bbf,stroke:#338
        ```
        """;

    private const string State =
        """
        # Mermaid — State diagram

        A `stateDiagram-v2` models states and the transitions between them, drawn on the shared
        layout tree. `[*]` is the one dot a scope starts at and the one it stops at; `state X { … }`
        nests a composite state, laid out in its own space so a `direction` line runs it its own way;
        `<<choice>>`, `<<fork>>`/`<<join>>`, `--` regions, notes on one line or across several, and
        `classDef`/`class`/`:::`/`style` are all read and drawn.

        Simple sample

        ```mermaid
        ---
        title: Simple sample
        ---
        stateDiagram-v2
            [*] --> Still
            Still --> [*]

            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]
        ```

        Older renderer keyword

        ```mermaid
        stateDiagram
            [*] --> Still
            Still --> [*]

            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]
        ```

        States and descriptions

        ```mermaid
        stateDiagram-v2
            stateId
        ```

        ```mermaid
        stateDiagram-v2
            state "This is a state description" as s2
        ```

        ```mermaid
        stateDiagram-v2
            s2 : This is a state description
        ```

        Transitions

        ```mermaid
        stateDiagram-v2
            s1 --> s2
        ```

        ```mermaid
        stateDiagram-v2
            s1 --> s2: A transition
        ```

        Start and end

        ```mermaid
        stateDiagram-v2
            [*] --> s1
            s1 --> [*]
        ```

        Composite states

        ```mermaid
        stateDiagram-v2
            [*] --> First
            state First {
                [*] --> second
                second --> [*]
            }

            [*] --> NamedComposite
            NamedComposite: Another Composite
            state NamedComposite {
                [*] --> namedSimple
                namedSimple --> [*]
                namedSimple: Another simple
            }
        ```

        Nested composite states

        ```mermaid
        stateDiagram-v2
            [*] --> First

            state First {
                [*] --> Second

                state Second {
                    [*] --> second
                    second --> Third

                    state Third {
                        [*] --> third
                        third --> [*]
                    }
                }
            }
        ```

        Sibling composites

        ```mermaid
        stateDiagram-v2
            [*] --> First
            First --> Second
            First --> Third

            state First {
                [*] --> fir
                fir --> [*]
            }
            state Second {
                [*] --> sec
                sec --> [*]
            }
            state Third {
                [*] --> thi
                thi --> [*]
            }
        ```

        Choice

        ```mermaid
        stateDiagram-v2
            state if_state <<choice>>
            [*] --> IsPositive
            IsPositive --> if_state
            if_state --> False: if n < 0
            if_state --> True : if n >= 0
        ```

        Forks and joins

        ```mermaid
        stateDiagram-v2
            state fork_state <<fork>>
            [*] --> fork_state
            fork_state --> State2
            fork_state --> State3

            state join_state <<join>>
            State2 --> join_state
            State3 --> join_state
            join_state --> State4
            State4 --> [*]
        ```

        Notes

        ```mermaid
        stateDiagram-v2
            State1: The state with a note
            note right of State1
                Important information! You can write
                notes.
            end note
            State1 --> State2
            note left of State2 : This is the note to the left.
        ```

        Concurrency

        ```mermaid
        stateDiagram-v2
            [*] --> Active

            state Active {
                [*] --> NumLockOff
                NumLockOff --> NumLockOn : EvNumLockPressed
                NumLockOn --> NumLockOff : EvNumLockPressed
                --
                [*] --> CapsLockOff
                CapsLockOff --> CapsLockOn : EvCapsLockPressed
                CapsLockOn --> CapsLockOff : EvCapsLockPressed
                --
                [*] --> ScrollLockOff
                ScrollLockOff --> ScrollLockOn : EvScrollLockPressed
                ScrollLockOn --> ScrollLockOff : EvScrollLockPressed
            }
        ```

        Direction

        ```mermaid
        stateDiagram
            direction LR
            [*] --> A
            A --> B
            B --> C
            state B {
              direction LR
              a --> b
            }
            B --> D
        ```

        Comments

        ```mermaid
        stateDiagram-v2
            [*] --> Still
            Still --> [*]
        %% this is a comment
            Still --> Moving
            Moving --> Still %% another comment
            Moving --> Crash
            Crash --> [*]
        ```

        Styling with classDefs

        ```mermaid
        stateDiagram
            direction TB

            accTitle: This is the accessible title
            accDescr: This is an accessible description

            classDef notMoving fill:white
            classDef movement font-style:italic
            classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow

            [*]--> Still
            Still --> [*]
            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]

            class Still notMoving
            class Moving, Crash movement
            class Crash badBadEvent
        ```

        Inline class operator

        ```mermaid
        stateDiagram
            direction TB

            classDef notMoving fill:white
            classDef movement font-style:italic
            classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow

            [*] --> Still:::notMoving
            Still --> [*]
            Still --> Moving:::movement
            Moving --> Still
            Moving --> Crash:::movement
            Crash:::badBadEvent --> [*]
        ```

        Spaces in state names

        ```mermaid
        stateDiagram
            classDef yourState font-style:italic,font-weight:bold,fill:white

            yswsii: Your state with spaces in it
            [*] --> yswsii:::yourState
            [*] --> SomeOtherState
            SomeOtherState --> YetAnotherState
            yswsii --> YetAnotherState
            YetAnotherState --> [*]
        ```
        """;

    private const string Extensions =
        """
        ---
        title: Markdig extensions
        author: Nexaflow
        tags: [emphasis, abbreviations, alerts]
        ---

        # Markdig extensions

        The `--- … ---` block above is YAML front matter — document metadata that is
        parsed but **not rendered** (same as Markdig's HTML output).

        Showcases the non-diagram extensions Nexaflow renders: **emphasis extras**,
        **abbreviations**, and GitHub **alert blocks**.

        ## Emphasis extras

        Plain `*emphasis*` and `**strong**` still work, alongside the extras:

        - Strikethrough: ~~deleted text~~
        - Subscript: H~2~O and CO~2~
        - Superscript: E = mc^2^ and the 1^st^ / 2^nd^ place
        - Marked / highlight: ==important phrase== in a sentence
        - Inserted: ++added text++

        They compose too: ~~**struck bold**~~ and ==marked `code`==.

        ## Abbreviations

        The first standard for the web was HTML, served over HTTP by the W3C.
        Hover any of those to see the expansion.

        *[HTML]: HyperText Markup Language
        *[HTTP]: HyperText Transfer Protocol
        *[W3C]: World Wide Web Consortium

        ## Alert blocks

        > [!NOTE]
        > Useful information that users should know, even when skimming content.

        > [!TIP]
        > Helpful advice for doing things better or more easily.

        > [!IMPORTANT]
        > Key information users need to know to achieve their goal.

        > [!WARNING]
        > Urgent info that needs immediate user attention to avoid problems.

        > [!CAUTION]
        > Advises about risks or negative outcomes of certain actions.

        Alerts hold normal markdown — lists, `code`, **emphasis**:

        > [!TIP]
        > You can nest content:
        >
        > 1. First step
        > 2. Second step with ==marked== text and an abbreviation: HTML.
        """;

    private const string Mindmap =
        """
        # Mermaid — Mindmap

        A `mindmap` is a single-rooted tree whose hierarchy comes from indentation.
        Text delimiters set the node shape: `[square] (rounded) ((circle)) {{hexagon}}`,
        `)cloud(` and `))bang((`.

        ```mermaid
        mindmap
          root((mindmap))
            Origins
              Long history
              ::icon(fa fa-book)
              Popularisation
                British popular psychology author Tony Buzan
            Research
              On effectiveness<br/>and features
              On Automatic creation
                Uses
                  Creative techniques
                  Strategic planning
                  Argument mapping
            Tools
              Pen and paper
              Mermaid
        ```

        With shapes

        ```mermaid
        mindmap
          id1[Root topic]
            id2(Rounded)
            id3((Circle))
            id4{{Hexagon}}
            id5)Cloud(
            id6))Bang((
        ```
        """;

    private const string GitGraph =
        """
        # Mermaid — Git graph

        A `gitGraph` draws a commit history: each branch is a coloured lane, with
        commits, branch-offs, merges and cherry-picks connecting them.

        ```mermaid
        gitGraph
           commit
           commit
           branch develop
           checkout develop
           commit
           commit
           checkout main
           merge develop
           commit
           commit
        ```

        With tags, commit types and a cherry-pick

        ```mermaid
        gitGraph
           commit id: "init"
           commit id: "v1" tag: "v1.0.0"
           branch develop
           commit id: "feat-a"
           commit id: "feat-b" type: HIGHLIGHT
           checkout main
           commit id: "hotfix" type: REVERSE
           merge develop tag: "release"
           branch feature order: 3
           commit id: "exp"
           checkout main
           cherry-pick id: "exp"
        ```
        """;

    private const string Gantt =
        """
        # Mermaid — Gantt chart

        A `gantt` chart schedules tasks on a date axis. Tasks carry an id, a start
        (a date or `after <id>`) and an end (a duration, a date, or `until <id>`);
        tags `done`/`active`/`crit`/`milestone` style the bar.

        ```mermaid
        gantt
            title A Gantt Diagram
            dateFormat YYYY-MM-DD
            section Section
                A task          :a1, 2014-01-01, 30d
                Another task    :after a1, 20d
            section Another
                Task in Another :2014-01-12, 12d
                another task    :24d
        ```

        With states and a milestone

        ```mermaid
        gantt
            title Project schedule
            dateFormat YYYY-MM-DD
            axisFormat %m/%d
            section Design
                Spec      :done,      des1, 2024-01-01, 10d
                Mockups   :active,    des2, after des1, 8d
                Review    :crit,      des3, after des2, 4d
            section Build
                Backend   :           b1,   after des2, 20d
                Frontend  :crit,       b2,   after des3, 18d
                Launch    :milestone, m1,   after b1, 0d
        ```
        """;

    private const string Pie =
        """
        # Mermaid — Pie chart

        A `pie` chart renders as a labelled pie with a legend beside it. Adding
        `showData` prints the raw values alongside the percentages.

        ```mermaid
        pie showData title Browser market share (Q1)
            "Chrome" : 64.5
            "Safari" : 18.2
            "Edge" : 5.1
            "Firefox" : 3.2
            "Other" : 9.0
        ```

        with config

        ```mermaid
        ---
        config:
          pie:
            textPosition: 0.5
          themeVariables:
            pieOuterStrokeWidth: "5px"
        ---
        pie showData
            title Key elements in Product X
            "Calcium" : 42.96
            "Potassium" : 50.05
            "Magnesium" : 10.01
            "Iron" :  5
        ```

        A `donutHole` cuts the middle out, `legendPosition` moves the legend, and
        `highlightSlice` pulls one slice out of the chart.

        ```mermaid
        ---
        config:
          pie:
            donutHole: 0.45
            legendPosition: bottom
            highlightSlice: Rent
            textPosition: 0.65
          themeVariables:
            pie1: "#7c9cff"
            pieOpacity: 0.9
        ---
        pie showData
            title Where the money goes
            "Rent" : 1200
            "Food" : 480
            "Travel" : 260
            "Saving" : 300
        ```


        """;

    private const string Flowchart =
        """
        # Mermaid — Flowchart

        `flowchart` / `graph` diagrams are laid out in ranks, top-down (or `LR`, `RL`, `BT`),
        by how far along the links reach each node. Node shapes, edge labels, subgraphs with
        their own direction, and `classDef` / `class` / `style` / `linkStyle` are all supported.

        ```mermaid
        flowchart TD
            A[Start] --> B{Is it working?}
            B -->|Yes| C[Ship it]
            B -->|No| D[Debug]
            D --> B
            C --> E([Done])
        ```

        Symbol variations

        ```mermaid
        flowchart RL
            A@{ shape: manual-file, label: "File Handling"}
            B@{ shape: manual-input, label: "User Input"}
            C@{ shape: docs, label: "Multiple Documents"}
            D@{ shape: procs, label: "Process Automation"}
            E@{ shape: paper-tape, label: "Paper Records"}
        	F@{ shape: tag-doc, label: "Tagged document" }
        	G@{ shape: tag-rect, label: "Tagged process" }
        ```

        Chained links

        ```mermaid
        graph LR
            A[Square Rect] -- Link text --> B((Circle))
            A --> C(Round Rect)
            B --> D{Rhombus}
            C --> D
        ```

        Multidirection arrows

        ```mermaid
        flowchart LR
            A o--o B
            B <--> C
            C x--x D
        ```

        Extra dashes


        ```mermaid
        flowchart TD
            A[Start] --> B{Is it?}
            B -->|Yes| C[OK]
            C --> D[Rethink]
            D --> B
            B ---->|No| E[End]
        ```

        split extra dashes

        ```mermaid
        flowchart TD
            A[Start] --> B{Is it?}
            B -- Yes --> C[OK]
            C --> D[Rethink]
            D --> B
            B -- No ----> E[End]
        ```

        subgraphs

        ```mermaid
        flowchart TB
            c1-->a2
            subgraph one
            a1-->a2
            end
            subgraph two
            b1-->b2
            end
            subgraph three
            c1-->c2
            end
            one --> two
            three --> two
            two --> c2
        ```

        Direction in subgraphs with comment
        ```mermaid
        flowchart LR
          subgraph TOP
          %% this is a comment A -- text --> B{node}
            direction TB
            subgraph B1
                direction RL
                i1 -->f1
            end
            subgraph B2
                direction BT
                i2 -->f2
            end
          end
          A --> TOP --> B
          B1 --> B2
        ```

        New format

        ```mermaid
        flowchart LR
            A[Hard edge] -->|Link text| B(Round edge)
            B --> C{Decision}
            C -->|One| D[Result one]
            C -->|Two| E[Result two]
        ```

        Line styles

        ```mermaid
        flowchart LR
            A e1@==> B
            A e2@--> C
            e1@{ curve: linear }
            e2@{ curve: natural }
        ```

        Classes, styles and numbered links

        ```mermaid
        flowchart TD
            A[Start]:::chosen --> B{Decide}
            B -- keep --> C[(Store)]
            B -- drop --> D>Discard]
            classDef chosen fill:#6e6ce6,stroke:#333,stroke-width:2px
            classDef default stroke-dasharray: 4 2
            class C chosen
            style D fill:#963,color:#fff
            linkStyle 0 stroke:#f66,stroke-width:3px
            click A "https://example.com/start" "Where it begins"
        ```


        """;

    private const string Quadrant =
        """
        # Mermaid — Quadrant chart

        A `quadrantChart` plots points in a unit square split into four labelled
        quadrants, with low→high axis captions along each edge.

        ```mermaid
        quadrantChart
            title Reach and engagement of campaigns
            x-axis Low Reach --> High Reach
            y-axis Low Engagement --> High Engagement
            quadrant-1 We should expand
            quadrant-2 Need to promote
            quadrant-3 Re-evaluate
            quadrant-4 May be improved
            Campaign A: [0.3, 0.6]
            Campaign B: [0.45, 0.23]
            Campaign C: [0.57, 0.69]
            Campaign D: [0.78, 0.34]
            Campaign E: [0.40, 0.34]
            Campaign F: [0.35, 0.78]
        ```

        With Styling

        ```mermaid
        quadrantChart
          title Reach and engagement of campaigns
          x-axis Low Reach --> High Reach
          y-axis Low Engagement --> High Engagement
          quadrant-1 We should expand
          quadrant-2 Need to promote
          quadrant-3 Re-evaluate
          quadrant-4 May be improved
          Campaign A: [0.9, 0.0] radius: 12
          Campaign B:::class1: [0.8, 0.1] color: #ff3300, radius: 10
          Campaign C: [0.7, 0.2] radius: 25, color: #00ff33, stroke-color: #10f0f0
          Campaign D: [0.6, 0.3] radius: 15, stroke-color: #00ff0f, stroke-width: 5px ,color: #ff33f0
          Campaign E:::class2: [0.5, 0.4]
          Campaign F:::class3: [0.4, 0.5] color: #0000ff
          classDef class1 color: #109060
          classDef class2 color: #908342, radius : 10, stroke-color: #310085, stroke-width: 10px
          classDef class3 color: #f00fff, radius : 10
        ```


        """;

    private const string Sequence =
        """
        # Mermaid — Sequence diagram

        A `sequenceDiagram` draws participant lifelines with messages flowing
        top-to-bottom. Arrow forms set the line and head: `->>` solid, `-->>`
        dashed, `-)` async, `-x` cross, and a self-message loops back.

        ```mermaid
        sequenceDiagram
            participant A as Alice
            participant J as John
            A->>J: Hello John, how are you?
            J-->>A: Great!
            A-)J: See you later!
            A->>A: thinking it over
        ```

        Actors

        ```mermaid
        sequenceDiagram
            actor Alice
            actor Bob
            Alice->>Bob: Hi Bob
            Bob->>Alice: Hi Alice
        ```

        Boundaries

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "boundary" }
            participant Bob
            Alice->>Bob: Request from boundary
            Bob->>Alice: Response to boundary
        ```

        Control

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "control" }
            participant Bob
            Alice->>Bob: Control request
            Bob->>Alice: Control response
        ```

        Entity

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "entity" }
            participant Bob
            Alice->>Bob: Entity request
            Bob->>Alice: Entity response
        ```

        Database

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "database" }
            participant Bob
            Alice->>Bob: DB query
            Bob->>Alice: DB result
        ```

        Collections

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "collections" }
            participant Bob
            Alice->>Bob: Collections request
            Bob->>Alice: Collections response
        ```

        Queue

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "queue" }
            participant Bob
            Alice->>Bob: Queue message
            Bob->>Alice: Queue response
        ```

        Aliases

        ```mermaid
        sequenceDiagram
            participant API@{ "type": "boundary" } as Public API
            actor DB@{ "type": "database" } as User Database
            participant Svc@{ "type": "control" } as Auth Service
            API->>Svc: Authenticate
            Svc->>DB: Query user
            DB-->>Svc: User data
            Svc-->>API: Token
        ```

        inline alias syntax

        ```mermaid
        sequenceDiagram
            participant API@{ "type": "boundary", "alias": "Public API" }
            participant Auth@{ "type": "control", "alias": "Auth Service" }
            participant DB@{ "type": "database", "alias": "User Database" }
            API->>Auth: Login request
            Auth->>DB: Query user
            DB-->>Auth: User data
            Auth-->>API: Access token
        ```

        alias precedence

        ```mermaid
        sequenceDiagram
            participant API@{ "type": "boundary", "alias": "Internal Name" } as External Name
            participant DB@{ "type": "database", "alias": "Internal DB" } as External DB
            API->>DB: Query
            DB-->>API: Result
        ```

        actor creation

        ```mermaid
        sequenceDiagram
            Alice->>Bob: Hello Bob, how are you ?
            Bob->>Alice: Fine, thank you. And you?
            create participant Carl
            Alice->>Carl: Hi Carl!
            create actor D as Donald
            Carl->>D: Hi!
            destroy Carl
            Alice-xCarl: We are too many
            destroy Bob
            Bob->>Alice: I agree
        ```

        Grouping

        ```mermaid
        sequenceDiagram
            box Purple Alice & John
            participant A
            participant J
            end
            box Another Group
            participant B
            participant C
            end
            A->>J: Hello John, how are you?
            J->>A: Great!
            A->>B: Hello Bob, how is Charley?
            B->>C: Hello Charley, how are you?
        ```

        Central Connections

        ```mermaid
        sequenceDiagram
            participant Alice
            participant John
            Alice->>()John: Hello John
            Alice()->>John: How are you?
            John()->>()Alice: Great!
        ```

        Activations

        ```mermaid
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            activate John
            John-->>Alice: Great!
            deactivate John
        ```

        Nested Activations

        ```mermaid
        sequenceDiagram
            Alice->>+John: Hello John, how are you?
            Alice->>+John: John, can you hear me?
            John-->>-Alice: Hi Alice, I can hear you!
            John-->>-Alice: I feel great!
        ```

        Notes

        ```mermaid
        sequenceDiagram
            participant John
            Note right of John: Text in note
        ```

        Spanning Notes

        ```mermaid
        sequenceDiagram
            participant Alice as Alice<br/>Johnson
            Alice->John: Hello John,<br/>how are you?
            Note over Alice,John: A typical interaction<br/>But now in two lines
        ```

        loops

        ```mermaid
        sequenceDiagram
            Alice->>Bob: Hello Bob, how are you?
            alt is sick
                Bob->>Alice: Not so good :(
            else is well
                Bob->>Alice: Feeling fresh like a daisy
            end
            opt Extra response
                Bob->>Alice: Thanks for asking
            end
        ```

        Parrallel actions

        ```mermaid
        sequenceDiagram
            par Alice to Bob
                Alice->>Bob: Go help John
            and Alice to John
                Alice->>John: I want this done today
                par John to Charlie
                    John->>Charlie: Can we do this today?
                and John to Diana
                    John->>Diana: Can you help us today?
                end
            end
        ```

        Break

        ```mermaid
        sequenceDiagram
            Consumer-->API: Book something
            API-->BookingService: Start booking process
            break when the booking process fails
                API-->Consumer: show failure
            end
            API-->BillingService: Start billing process
        ```

        Background hilighting with comments and entity codes

        ```mermaid
        sequenceDiagram
            participant Alice
            participant John

            rect rgb(191, 223, 255)
        	%% this is a comment
            note right of Alice: Alice calls John.
            Alice->>+John: Hello John, how are you?
            rect rgb(200, 150, 255)
            Alice->>+John: John, can you hear me?
            John-->>-Alice: Hi Alice, I can hear you!
            end
            John-->>-Alice: I feel #9829; great!
            end
            Alice ->>+ John: Did you want to go to the game tonight?
            John -->>- Alice: Yeah! See you there.
        ```

        Sequence Numbers

        ```mermaid
        sequenceDiagram
            autonumber
            Alice->>John: Hello John, how are you?
            loop HealthCheck
                John->>John: Fight against hypochondria
            end
            Note right of John: Rational thoughts!
            John-->>Alice: Great!
            John->>Bob: How about you?
            Bob-->>John: Jolly good!
        ```


        """;

    private const string MusicAbc =
        """
        # Musical notation — ABC

        A fenced code block whose language is `abc` engraves ABC notation to sheet music.

        ## features

        ```abc
        X:1
        T:Notes / pitches
        M:C
        L:1/4
        K:C treble
        C, D, E, F, | G, A, B, C | D E F G | A B c d | e f g a | b c' d' e' | f' g' a' b' |]
        ```

        ```abc
        X:1
        T:Note lengths
        M:
        K:C
        A/4 A/2 A/ A A2 A3 A4 A6 A7 A8 A12 A16 |]
        ```

        ```abc
        X:1
        T:Beams
        M:C
        K:C
        A B c d AB cd | ABcd ABc2 | ABcdABcd |]
        ```

        ```abc
        X:1
        T:Bar lines
        M:C
        K:C
        [| A4 A4 | A4 A4 || A4 A4 | A4 A4 |]
        |: A4 A4 | A4 A4 :: A4 A4 | A4 A4 ::
        A4 A4 | A4 A4 |1 A4 A4 :|2 A4 A4 | A4 A4 |]
        ```

        ```abc
        X:1
        T:Unit note length
        T:Same notes / different notation
        M:
        K:C
        L:1/16
        A/2 A A2 A4 A8 A16 |]
        L:1/8
        A/4 A/2 A A2 A4 A8 |]
        L:1/4
        A/8 A/4 A/2 A A2 A4 |]
        ```

        ```abc
        X:1
        T:Broken rhythm markers
        M:3/4
        K:C
        A>A A2>A2 | A<A A2<A2 | A>>A A2>>>A2 | A<<A A2<<<A2 |]
        ```

        ```abc
        X:1
        T:Tuplets
        M:C
        K:C
        (2AB (3ABA (4ABAB (5ABABA (6ABABAB (7ABABABA|]
        ```

        ```abc
        X:1
        T:Ties and slurs
        M:C
        K:C
        (AA) (A(A)A) ((AA)A) (A|A) A-A A-A-A A2-|A4|]
        ```

        ```abc
        X:1
        T:Accidentals
        M:C
        K:C
        __A _A =A ^A ^^A |]
        ```

        ```abc
        X:1
        T:Chord symbols
        M:C
        K:C
        "A"A "Gm7"D "Bb"F "F#"A |]
        ```

        ```abc
        X:1
        T:Accents
        M:C
        K:C
        ~A ~c .A .c vA vc uA uc|]
        ```

        ```abc
        X:1
        T:Grace notes
        M:6/8
        K:C
        {g}A3 A{g}AA|{gAGAG}A3 {g}A{d}A{e}A|]
        ```

        ```abc
        X:1
        T:Chords
        M:2/4
        K:C
        [CEGc] [C2G2] [CE][DF] | [D2F2][EG][FA] [A4d4]|]
        ```

        ```abc
        X:1
        T:Keys and modes
        M:4/4
        K:C
        T:C/CMAJOR/Cmajor
        CDEF GABc |\
        K:CMAJOR
        CDEF GABc |\
        K:Cmajor
        CDEF GABc |]
        T:C maj/ C major/C Major
        K:C maj
        CDEF GABc |\
        K: C major
        CDEF GABc |\
        K:C Major
        CDEF GABc |]
        T:C Lydian/C Ionian/C Mixolydian
        K:C Lydian
        CDEF GABc |\
        K:C Ionian
        CDEF GABc |\
        K:C Mixolydian
        CDEF GABc |]
        T:C Dorian/C Minor/Cm
        K:C Dorian
        CDEF GABc |\
        K:C Minor
        CDEF GABc |\
        K:Cm
        CDEF GABc |]
        T:C Aeolian/C Phrygian/C Locrian
        K:C Aeolian
        CDEF GABc |\
        K:C Phrygian
        CDEF GABc |\
        K:C Locrian
        CDEF GABc |]
        ```

        ## songs

        ```abc
        X:1
        T:Speed the Plough
        M:4/4
        C:Trad.
        K:G
        |:GABc dedB|dedB dedB|c2ec B2dB|c2A2 A2BA|
          GABc dedB|dedB dedB|c2ec B2dB|A2F2 G4:|
        |:g2gf gdBd|g2f2 e2d2|c2ec B2dB|c2A2 A2df|
          g2gf g2Bd|g2f2 e2d2|c2ec B2dB|A2F2 G4:|
        ```

        ```abc
        X:1
        T:Paddy O'Rafferty (Jig)
        C:Trad.
        O:Irish
        R:Jig
        M:6/8
        K:D
        dff cee|def gfe|dff cee|dfe dBA|
        dff cee|def gfe|faf gfe|1 dfe dBA:|2 dfe dcB|]
        ~A3 B3|gfe fdB|AFA B2c|dfe dcB|
        ~A3 ~B3|efe efg|faf gfe|1 dfe dcB:|2 dfe dBA|]
        fAA eAA|def gfe|fAA eAA|dfe dBA|
        fAA eAA|def gfe|faf gfe|dfe dBA:|
        ```

        ```abc
        X:1
        T:Kitchen Girl (Reel)
        C:Trad.
        O:American
        R:Reel
        M:C
        K:D
        [a4c4] [g4B4]|efed c2cd|e2f2 gaba|g2e2 e2fg|
        a4 g4|efed cdef|g2d2 efed|c2A2 A4:|
        K:G
        ABcA BAGB|ABAG EDEG|A2AB c2d2|e3f edcB|
        ABcA BAGB|ABAG EGAB|cBAc BAG2|A4 A4:|
        ```

        ```abc
        X:1                        % tune no 1
        T:Dusty Miller (commented) % title
        T:Binny's Jig              % an alternative title
        C:Trad.                    % traditional
        O:English                  % origin
        R:DH                       % double hornpipe
        M:3/4                      % meter
        K:G                        % key
        B>cd BAG|FA Ac BA|B>cd BAG|DG GB AG:|
        Bdd gfg|aA Ac BA|Bdd gfa|gG GB AG:|
        BG G/2G/2G BG|FA Ac BA|BG G/2G/2G BG|DG GB AG:|
        W:Hey, the dusty miller, and his dusty coat;
        W:He will win a shilling, or he spend a groat.
        W:Dusty was the coat, dusty was the colour;
        W:Dusty was the kiss, that I got frae the miller.
        ```

        ```abc
        X:1
        T:Old Sir Simon the King (commented)
        C:Trad.               % composer
        S:Offord MSS          % source
        N:see also Playford   % notes
        M:9/8                 % meter
        R:SJ                  % rhythm
        Q:1/4=160             % tempo
        Z:originally in C     % transcription notes
        K:G                   % key
        D|GFG GAG G2D|GFG GAG F2D|EFE EFE EFG|A2G F2E D2:|
        D|GAG GAB d2D|GAG GAB c2D|[1 EFE EFE EFG|A2G F2E D2:|
        M:12/8                % change meter for a bar
        [2 E2E EFE E2E EFG|\
        M:9/8                 % change back again
        A2G F2E D2|]
        ```

        ```abc
        X:1
        T:Jericho (chord symbols)
        T:Joshua fought the battle of Jericho
        C:Anon.
        M:C
        L:1/8
        K:Dm
        "Dm"D^CDE FF G2|"Dm"A A2 A-A4|"A7"G G2 G-G4|"Dm"A A2 A-A4|
        "Dm"D^CDE FF G2|"Dm"A A2 A-A2 FG|"A7"A2 G2 F2 E2|"Dm"D6"^Fine"||dd|
        "Dm"dA AA A3 A|"Dm"A A3- "A7"A2 AA|"Dm"AA AA A2 A2|"A7"A6 ^c2|
        "Dm"d2 A2 "A7"A A3|"Dm"A2 A2- "A7"A2 AA|"Dm"AA G2 "A7"E2 D2|"Dm"D8|]
        ```

        ```abc
        X:1
        T:Lyrics
        N:see https://www.youtube.com/watch?v=RWNeCjid0zc
        M:4/4
        L:1/4
        K:C
        % use the w: field to add lyrics, with each word lined up on a note
        A A A A | A A A A |
        w:words line up on notes
        %
        % to align syllables on notes, use hyphens and/or spaces to split the words up
        A A A A | A A A A |
        w:syl-la-ble, syl- la- ble
        %
        % to align two (or more) syllables on a single note, don't split them up or use backslash hypen \-
        A2  A2 | A2 A2 | 
        w:syllable, syl\-la\-ble
        %
        % to align two (or more) words on a single note, use a tilde ~ between the words
        A4 | A A A A | 
        w:word~word syl-la-ble
        %
        % to align two (or more) notes on a syllable or word, use an underscore
        A2  A2 | A A A A  | 
        w:word_ syl-la-ble_
        %
        % to skip one (or more) notes, i.e. to include blank syllables, use an asterisk *
        A A A A | A A A A |
        w:word * * * syl-la-ble *
        %
        % to save typing in lots of asterisks, advance to the next barline with a bar symbol |
        A A A A | A A A A |
        w:word | syl-la-ble |
        %
        % to include multipe verses, use multiple w: fields
        A A A A | A A A A |
        w:syl-la-ble | syl- la- ble
        w:word | syl-la-ble 
        %
        % to include more verses underneath use W: fields (upper case)
        W: This is verse two of my song
        W: Syl-la-ble, word
        W: 
        W: This is verse three of my song
        W: Word, word, syl-la-ble!
        W: 
        %%writefields N
        ```

        ```abc
        X:0
        T: Ding Dong! Merrily On High
        Z: From Arbeau's "Orchesographie."
        Z: Copyright © 2005 by Douglas D. Anderson
        Z: Released to the Public Domain
        L: 1/4
        M: none
        V: P1 name="Soprano"
        %%MIDI program 1 19
        V: P2 name="Alto"
        %%MIDI program 2 60
        V: P3 name="Tenor"
        %%MIDI program 3 57
        V: P4 name="Bass"
        %%MIDI program 4 58
        K: Bb
        [V: P1]  B B c/ B/ A/ G/ | F3 F | G B B A | B2 B2 | B B c/ B/ A/ G/ | F3 F | G B B A | B2 B2[|: (f3/ e/ d/e/f/d/ | e3/ d/c/d/e/c/ | d3/ c/B/c/d/B/ | c3/ B/A/B/c/A/ | B3/ A/G/A/B/G/ | A3/) G/ F F | G B B A | B2 B2 :|]|] Z
        w: Ding Dong! mer- ri ly on high In heav'n the bells are ring- ing Ding, dong! ver- i ly the sky Is riv'n with an- gel sing- ing Glo-______________________________ ri a, Ho- san na in ex- cel sis!
        [V: P2]  F F G/ G/ E/ E/ | C3 F | F E C F | F2 F2 | F F G/ G/ E/ E/ | C3 F | F E C F | F2 F2[|:z (F2 B | B/A/G/F/ G/F/ E |z F/E/ D G | G/F/E/D/ E/D/ C |z D/C/ B, E | C/D/E/) D/ C F | F E C F | F2 F2 :|]|] Z
        [V: P3]  D B, G,/ G,/ C/ B,/ | A,3 B, | B, B, C C | D2 D2 | D B, G,/ G,/ C/ B,/ | A,3 B, | B, B, C C | D2 D2[|:z (C D B, | C B,2 C |z A, B, G, | A, G,2 A, |z F, G, G, | C) A, B, C | B, B, C C | D2 D2 :|]|] Z
        [V: P4]  B,, D, E,/ E,/ C,/ C,/ | F,3 D, | E, G, F, F, | B,,2 B,,2 | B,, D, E,/ E,/ C,/ C,/ | F,3 D, | E, G, F, F, | B,,2 B,,2[|:z (A, B, D, | C, D, E,/D,/ C, |z F, G, B,, | A,, B,, C,/B,,/ A,,) |z (D, E, G, | F,) G, A, B, | E, G, F, F, | B,,2 B,,2 :|]|] Z
        ```
        """;

    private const string MusicLilyPond =
        """
        # Musical notation — LilyPond

        A `#%lilypond … #%` block engraves LilyPond to sheet music. It draws the same
        engraver as the ABC blocks, so the two notations reach the same page — but three
        things LilyPond does are worth knowing, because they have no ABC counterpart:

        - **Bar lines come from the meter.** A `|` is a *check*, not a bar line; a tune with
          none in it still bars itself. `\partial` shortens the pickup.
        - **Beams come from the meter too.** Eighths group by the half-bar in common time and
          by the dotted quarter in a compound one. A manual `[ ]` beam wins where one is written.
        - **Accidentals are printed, not written.** A note name carries its own alteration
          (`fis` is F sharp whatever the key), so the engraver prints an accidental only where
          the note departs from what is already in force in the bar.

        ## features

        #%lilypond
        \header { title = "Notes / pitches" }
        {
          \time 4/4
          c4 d e f | g a b c' | d' e' f' g' | a' b' c'' d'' |
          e'' f'' g'' a'' | b'' c''' d''' e''' \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Note lengths" }
        { \cadenzaOn \autoBeamOff c'\breve c'1 c'2 c'4 c'8 c'16 c'32 c'64 \bar "|." }
        #%

        #%lilypond
        \header { title = "Beams — the meter decides" }
        \relative c'' {
          \time 4/4 c8 d e f g a b c |
          \time 6/8 c,8 d e f g a |
          \time 3/4 c,8 d e f g a |
          \time 4/4 c,8[ d e] f[ g a b c] \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Bar lines" }
        \relative c'' {
          \time 4/4
          c1 c \bar "||"
          c c \bar ".|:"
          c c \bar ":|.|:"
          c c \bar ":|."
          c c \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Repeats and voltas" }
        \relative c'' {
          \time 4/4
          \repeat volta 2 { c4 d e f | g a b c }
          \repeat volta 2 { c4 b a g }
          \alternative { { f4 e d c } { a'4 b c d } }
        }
        #%

        #%lilypond
        \header { title = "Tuplets" }
        \relative c'' {
          \time 4/4
          \tuplet 3/2 { c8 d e } \tuplet 3/2 { f g a } \tuplet 3/2 { b c d } \tuplet 3/2 { e d c } |
          \tuplet 5/4 { c,16 d e f g } \tuplet 6/4 { a b c d e f } c4 c \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Ties and slurs" }
        \relative c'' {
          \time 4/4
          c4( d e f) | g4( a) b( c) | c2~ c | c4\( d( e) f\) \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Accidentals" }
        \header { subtitle = "printed only where the note departs from the bar" }
        \relative c'' {
          \time 5/4
          ceses4 ces c cis cisis \bar "||"
          \key d \major
          fis4 f fis fis fis \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Chord symbols" }
        <<
          \new ChordNames \chordmode { c1 | a1:m | d1:m7 | g1:7 }
          \new Staff \relative c' { \time 4/4 c4 d e f | e f g a | f g a b | b a g f \bar "|." }
        >>
        #%

        #%lilypond
        \header { title = "Articulations" }
        \relative c'' {
          \time 4/4
          c4-. d-> e-- f-^ | g\staccato a\accent b\tenuto c\marcato |
          c\fermata b\trill a\upbow g\downbow | f\mordent e\prall d\turn c\coda \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Grace notes" }
        \relative c'' {
          \time 6/8
          \grace d8 c4. \grace { d16 e } c4. |
          \acciaccatura d8 c4. \appoggiatura d8 c4. \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Chords" }
        \relative c' {
          \time 2/4
          <c e g c'>2 | <c e>4 <d f> | <e g>4 <f a> | <g b>4 <a c> | <c e g>2 \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Keys and modes" }
        \relative c'' {
          \time 4/4
          \key c \major      c4 d e f |
          \key c \minor      c4 d es f |
          \key c \dorian     c4 d es f |
          \key c \mixolydian c4 d e f |
          \key c \lydian     c4 d e fis |
          \key c \phrygian   c4 des es f |
          \key c \locrian    c4 des es f |
          \key c \aeolian    c4 d es f \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Meter" }
        \relative c'' {
          \time 4/4 c1 |
          \time 2/2 c |
          \numericTimeSignature \time 4/4 c |
          \time 3/4 c2. |
          \time 6/8 c4. c \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Rests" }
        \relative c'' {
          \time 4/4
          c4 r c2 | r1 | R1 | c4 s c s \bar "|."
        }
        #%

        #%lilypond
        \header { title = "Lyrics" }
        <<
          \new Staff \new Voice = "melody" \relative c' {
            \time 4/4
            c4 c g' g | a a g2 | f4 f e e | d d c2
          }
          \new Lyrics \lyricsto "melody" {
            Twin -- kle, twin -- kle, lit -- tle star,
            How I won -- der what you are.
          }
          \new Lyrics \lyricsto "melody" {
            Up a -- bove the world so high,
            Like a dia -- mond in the sky.
          }
        >>
        #%

        #%lilypond
        \header { title = "Lyrics — extenders and skips" }
        <<
          \new Staff \new Voice = "tune" \relative c' {
            \time 4/4
            c4 d e f | g a b c | d1 | c1
          }
          \new Lyrics \lyricsto "tune" {
            Held o -- ver __ _ two notes, then a _ skip.
          }
        >>
        #%

        ## songs

        #%lilypond
        \header {
          title = "Speed the Plough"
          composer = "Trad."
          poet = "Reel"
        }
        \relative c'' {
          \numericTimeSignature \time 4/4 \key g \major
          \repeat volta 2 {
            g8 a b c d e d b | d e d b d e d b | c4 e8 c b4 d8 b | c4 a a b8 a |
            g8 a b c d e d b | d e d b d e d b | c4 e8 c b4 d8 b | a4 fis g2
          }
          \repeat volta 2 {
            g'4 g8 fis g d b d | g4 fis e d | c4 e8 c b4 d8 b | c4 a a d8 fis |
            g4 g8 fis g4 b,8 d | g4 fis e d | c4 e8 c b4 d8 b | a4 fis g2
          }
        }
        #%

        #%lilypond
        \header {
          title = "Ah! vous dirai-je, maman"
          subtitle = "with a pickup, a chord line and two verses"
          composer = "Trad."
        }
        \score {
          <<
            \new ChordNames \chordmode { s4 | c2 f4 c | f4 c g2 | c1 }
            \new Staff \new Voice = "air" \relative c' {
              \time 4/4 \key c \major \partial 4
              g'4 | c c g g | a a g2 | f4 f e e | d d c2 \bar "|."
            }
            \new Lyrics \lyricsto "air" {
              _ Twin -- kle, twin -- kle, lit -- tle star,
              How I won -- der what you are.
            }
            \new Lyrics \lyricsto "air" {
              _ Up a -- bove the world so high,
              Like a dia -- mond in the sky.
            }
          >>
        }
        #%

        Each `\new Staff` is a staff, and voices that run in step are bracketed into one
        system, sharing a bar grid with the bar lines running through:

        #%lilypond
        \header {
          title = "Four-part harmony"
          subtitle = "four voices, one bracketed system"
        }
        \score {
          \new ChoirStaff <<
            \new Staff \with { instrumentName = "Soprano" } \relative c'' {
              \time 4/4 \key g \major \partial 4
              d4 | g g a b | b a g2 | g4 g a b | b a g2 \bar "|."
            }
            \new Staff \with { instrumentName = "Alto" } \relative c' {
              \time 4/4 \key g \major \partial 4
              d4 | d e e d | g fis d2 | d4 e e d | g fis d2 \bar "|."
            }
            \new Staff \with { instrumentName = "Tenor" } \relative c' {
              \clef bass \time 4/4 \key g \major \partial 4
              b4 | b c c b | d d b2 | b4 c c b | d d b2 \bar "|."
            }
            \new Staff \with { instrumentName = "Bass" } \relative c {
              \clef bass \time 4/4 \key g \major \partial 4
              g4 | g c a g | e d g2 | g4 c a g | e d g2 \bar "|."
            }
          >>
        }
        #%

        The complex "Exercise 3" — a real worksheet. Both staves of the `PianoStaff` engrave
        (the blank upper one the student writes into, and the given cantus firmus below it),
        and the Scheme, figured bass and `\markup` around them are tolerated and reported:

        #%lilypond
        #(set-global-staff-size 24)
        global = { \time 4/4 \numericTimeSignature \key c \major }
        cf = \relative {
          \clef bass
          \global
          c4 c' b a | g a f d | e f g g, | c1
        }
        upper = \relative c'' {
          \global
          r4 s4 s2 | s1*2 | s2 s4 s
          \bar "||"
        }
        bassFigures = \figuremode {
          s1*2 | s4 <6> <6 4> <7> | s1
        }
        \markup { "Exercise 3: Write 8th notes against the given bass line." }
        \score {
          \new PianoStaff <<
            \new Staff { \upper }
            \new Staff = lower { << \cf \new FiguredBass \bassFigures >> }
          >>
          \layout {}
        }
        #%

        The dialect is auto-detected when the tag is omitted (`#%` alone).
        """;

    private const string Qr =
        """"
        # QR codes

        A `qr` fence becomes a scannable QR code, generated locally. The body is a flat list of
        `key: value` lines ΓÇö a `type:`, then the fields that type needs.

        A QR code only carries text; a phone offers to *join this network* or *add this contact*
        because the text follows a convention. The `type:` is what writes that convention for you.

        ## A link

        ```qr
        type: url
        url: https://markdown.org/tools/diagrams/qr/
        ```

        ## Plain text

        ```qr
        type: text
        text: Hello from markdown.org
        ```

        ## An email

        ```qr
        type: email
        email: hi@example.com
        subject: Hello
        body: Just scanned your code
        ```

        ## A phone number, and a text message

        ```qr
        type: phone
        phone: +1 (555) 123-4567
        ```

        ```qr
        type: sms
        number: +15551234567
        message: Hello there
        ```

        ## A Wi-Fi network

        Higher error correction (`ec: H`) survives a scuffed print, and costs a slightly larger code.

        ```qr
        type: wifi
        ssid: Nexaflow Guest
        password: s3cr3t-pass
        security: WPA
        hidden: false
        ec: H
        cellSize: 5
        ```

        ## A contact card

        ```qr
        type: vcard
        name: Ada Lovelace
        org: Analytical Engines
        title: Engineer
        phone: +15551234567
        email: ada@example.com
        url: https://example.com
        address: 12 Baker St, London
        ```

        ## A place on the map

        ```qr
        type: geo
        lat: 51.5074
        lng: -0.1278
        ```

        ## A calendar event

        ```qr
        type: event
        title: Launch party
        location: The Office
        start: 2026-08-01T18:00
        end: 2026-08-01T21:00
        ```

        ## A contact card, compactly

        MECARD trades the organisation and job title for a much smaller symbol, and older readers know it.

        ```qr
        type: mecard
        name: Ada Lovelace
        phone: +15551234567
        email: ada@example.com
        ```

        ## A bank payment (GiroCode)

        An EPC069-12 credit transfer - the code printed on European invoices. The IBAN is checked before
        the code is drawn, because a typo there scans perfectly and then fails at the bank.

        ```qr
        type: epc
        name: Red Cross
        iban: BE72 0000 0000 1616
        bic: BPOTBEB1
        amount: 25.00
        message: Urgency fund
        ```

        ## A crypto payment

        ```qr
        type: crypto
        coin: bitcoin
        address: 1BoatSLRHtKNngkdXEeobR76b53LETtpyT
        amount: 0.01
        ```

        ## Settings

        Any block takes `ec` (`L`/`M`/`Q`/`H`), `cellSize`, `margin`, and a `dark` / `light` colour
        pair. Left out, the colours follow the theme's QR tokens ΓÇö which stay dark-on-light whatever
        the theme does, because an inverted code stops scanning.

        ```qr
        type: url
        url: https://markdown.org
        ec: Q
        cellSize: 8
        margin: 2
        dark: #1a3a5c
        light: #f4f7fb
        ```

        ## When a block is wrong

        A block that cannot be built shows the reason in place of the picture ΓÇö a misspelled setting,
        a field belonging to another type, a missing required field, or content too long to fit.

        ```qr
        type: wifi
        password: hunter2
        ```
        """";

    private const string Timeline =
        """
        # Mermaid — Timeline

        A `timeline` lays periods along a spine with their events stacked beneath. Several events share a
        period on one line (`2004 : Facebook : Google`) or on continuation lines starting with `:`, and
        `<br>` breaks a label. With `section`s every period in a section shares that section's colour;
        without them each period takes the next colour. `direction TD` turns the spine vertical.

        ## History of social media

        ```mermaid
        timeline
            title History of Social Media Platform
            2002 : LinkedIn
            2004 : Facebook
                 : Google
            2005 : YouTube
            2006 : Twitter
        ```

        ## Sections, one colour, and theme slots

        ```mermaid
        ---
        config:
          timeline:
            disableMulticolor: true
          themeVariables:
            cScale0: "#4e79a7"
            cScaleLabel0: "#ffffff"
        ---
        timeline
            title Timeline of Industrial Revolution
            section 17th-20th century
                Industry 1.0 : Machinery, Water power, Steam <br>power
                Industry 2.0 : Electricity, Internal combustion engine, Mass production
                Industry 3.0 : Electronics, Computers, Automation
            section 21st century
                Industry 4.0 : Internet, Robotics, Internet of Things
                Industry 5.0 : Artificial intelligence, Big data, 3D printing
        ```

        ## Top-down

        ```mermaid
        timeline
            direction TD
            title Release train
            section Q1
                January : Kick-off : Planning
                February : Alpha
            section Q2
                April : Beta
                June : Launch
        ```
        """;

    private const string Journey =
        """
        # Mermaid — User journey

        A `journey` diagram walks through the steps of a task as sections of scored tasks
        (`Task name: score: actor, actor`). Each score from 1 to 5 draws a face — sad, neutral or happy —
        floating higher for a better experience, and every actor gets a colour shown in the legend and as a
        dot on the tasks they take part in.

        ## My working day

        ```mermaid
        journey
            title My working day
            section Go to work
              Make tea: 5: Me
              Go upstairs: 3: Me
              Do work: 1: Me, Cat
            section Go home
              Go downstairs: 5: Me
              Sit down: 5: Me
        ```

        ## With colours from the config

        ```mermaid
        ---
        config:
          journey:
            width: 130
            actorColours: ["#e15759", "#4e79a7", "#59a14f"]
            sectionFills: ["#f28e2b", "#76b7b2"]
        ---
        journey
            title Buying a coffee
            Wake up: 2: Me
            section Order
              Queue: 2: Me
              Order: 4: Me, Barista
              Pay: 3: Me, Barista, Card reader
            section Enjoy
              Wait: 3: Me
              First sip: 5
        ```
        """;

    private const string Block =
        """
        # Mermaid — Block diagram

        A `block-beta` diagram places blocks on a grid you control: `columns N` sets the width, items fill
        rows left to right, `id:N` spans columns, `space` leaves a cell empty, and `block:id … end` nests a
        grid inside a block. Blocks take the flowchart bracket shapes, `id<["label"]>(right)` is a fat block
        arrow, edges join any two items by id, and `style`/`classDef` colour individual blocks.

        ## Architecture sketch

        ```mermaid
        block-beta
          columns 3
          Frontend blockArrowId6<[" "]>(right) Backend
          space:2 down<[" "]>(down)
          Disk left<[" "]>(left) Database[("Database")]

          classDef front fill:#696,stroke:#333;
          classDef back fill:#969,stroke:#333;
          class Frontend front
          class Backend,Database back
        ```

        ## Nested blocks and edges

        ```mermaid
        block-beta
          columns 1
          db(("DB"))
          blockArrowId6<["&nbsp;&nbsp;&nbsp;"]>(down)
          block:ID
            A
            B["A wide one in the middle"]
            C
          end
          space
          D
          ID --> D
          C --> D
          style B fill:#969,stroke:#333,stroke-width:4px
        ```

        ## Shapes, widths and config

        ```mermaid
        ---
        config:
          block:
            padding: 12
        ---
        block-beta
          columns 4
          a["Spans the whole row"]:4
          b("round") c(["stadium"]) d[["subroutine"]] e[("cylinder")]
          f(("circle")) g>"flag"] h{"rhombus"} i{{"hexagon"}}
          j[/"parallelogram"/] k[\"alt"\] l[/"trapezoid"\] m[\"alt"/]
          n((("double circle"))) both<["both"]>(x) updown<["up-down"]>(y) space
        ```
        """;
    private const string Barcode =
        """"
        # Barcodes

        A `barcode` fence draws a scannable linear barcode, generated locally. The body is a flat list of
        `key: value` lines: a `format:`, a `value:`, and whatever settings you want.

        The `value` is editable where it is rendered — click into it and type, and the bars follow.

        ## The general-purpose one

        Code 128 carries any ASCII, and moves between its subsets to keep the symbol short.

        ```barcode
        format: CODE128
        value: MARKDOWN-128
        ```

        Its subsets can be asked for by name when a scanner insists on one.

        ```barcode
        format: CODE128A
        value: MARKDOWN128A
        ```

        ```barcode
        format: CODE128B
        value: Markdown 128B
        ```

        ```barcode
        format: CODE128C
        value: 12345678
        ```

        ## Retail

        The check digit is worked out for you — leave it off, or write it and have it verified.

        ```barcode
        format: EAN13
        value: 590123412345
        ```

        ```barcode
        format: EAN8
        value: 9638507
        ```

        ```barcode
        format: UPC
        value: 03600029145
        ```

        ```barcode
        format: UPCE
        value: 01234565
        ```

        The add-ons, printed beside a main code to carry a price or an issue number.

        ```barcode
        format: EAN5
        value: 54495
        ```

        ```barcode
        format: EAN2
        value: 53
        ```

        ## Publications

        ISBN, ISSN and ISMN are not symbologies. Each is a numbering scheme that reserved an EAN-13 prefix,
        so what is drawn is an EAN-13 — optionally with an add-on after it.

        ```barcode
        format: ISBN
        value: 978-1-56581-231-4 90000
        ```

        ```barcode
        format: ISSN
        value: 0311-175X 00 17
        ```

        ```barcode
        format: ISMN
        value: 979-0-2605-3211-3
        ```

        ## The older formats

        Code 39 carries upper case, digits and a little punctuation. It is not dense, and it is read by
        almost everything.

        ```barcode
        format: CODE39
        value: MARKDOWN-39
        ```

        Interleaved 2 of 5 packs digits in pairs — one in the bars, the next in the spaces between them —
        which is why it needs an even count.

        ```barcode
        format: ITF
        value: 1234567890
        ```

        ```barcode
        format: ITF14
        value: 1540014128876
        ```

        MSI, in its four check-digit flavours.

        ```barcode
        format: MSI
        value: 1234567
        ```

        ```barcode
        format: MSI10
        value: 1234567
        ```

        ```barcode
        format: MSI11
        value: 1234567
        ```

        ```barcode
        format: MSI1010
        value: 1234567
        ```

        ```barcode
        format: MSI1110
        value: 1234567
        ```

        Pharmacode carries a number rather than text, and is read right to left.

        ```barcode
        format: pharmacode
        value: 1234
        ```

        Codabar wraps its value in a start and stop mark, A to D. Write your own pair or let it add one.

        ```barcode
        format: codabar
        value: 40156
        ```

        ## Settings

        Any block takes `width`, `height`, `displayValue`, `fontSize`, `textAlign`, `lineColor`,
        `background` and `margin`. Left out, the colours follow the theme's barcode tokens — which stay
        dark-on-light whatever the theme does, because an inverted barcode stops scanning.

        ```barcode
        format: CODE39
        value: MARKDOWN-39
        width: 2
        height: 80
        displayValue: true
        lineColor: #1d4ed8
        background: #ffffff
        margin: 10
        ```

        ```barcode
        format: CODE128
        value: NO TEXT UNDER THIS ONE
        displayValue: false
        height: 50
        ```

        ## When the value will not encode

        A value the format cannot carry does not take the barcode off the page. It stays, in the shape the
        format would have taken, struck through and waved under, with the reason on hover — because while
        you are typing a value it is invalid far more often than not.

        ```barcode
        format: EAN13
        value: 590
        ```

        A block that cannot be understood at all is a different thing, and falls back to its source.

        ```barcode
        format: NOTAFORMAT
        value: 12345
        ```
        """";

    private const string C4 =
        """
        # Mermaid — C4 diagrams

        C4 models software at four zoom levels — context, containers, components, and the code — plus
        deployment and dynamic views. The `C4Context`, `C4Container`, `C4Component`, `C4Dynamic` and
        `C4Deployment` headers are Mermaid's, but the body accepts the fuller
        [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) macro set: `$techn` and `$descr`
        on elements and relationships, `$tags` with `AddElementTag`, `UpdateElementStyle`, `SHOW_LEGEND`
        and the `SHOW_PERSON_*` shape variants. Elements are laid out by the shared graph engine, so a
        C4 diagram pans, zooms and selects like a flowchart.

        ## System context

        ```mermaid
        C4Context
        title System Context diagram for Internet Banking System

        Person(customer, "Personal Banking Customer", "A customer of the bank, with personal accounts.")
        System(banking, "Internet Banking System", "Allows customers to view information about their accounts.")
        System_Ext(mail, "E-mail System", "The internal Microsoft Exchange e-mail system.")
        SystemDb_Ext(mainframe, "Mainframe Banking System", "Stores all of the core banking information.")

        Rel(customer, banking, "Views account balances, makes payments using")
        Rel(banking, mail, "Sends e-mail using", "SMTP")
        Rel_Back(customer, mail, "Sends e-mails to")
        Rel(banking, mainframe, "Gets account information from", "XML/HTTPS")

        SHOW_LEGEND()
        ```

        ## Containers, boundaries and tags

        ```mermaid
        C4Container
        title Container diagram for the Internet Banking System

        AddElementTag("critical", $bgColor="#c0392b", $fontColor="#ffffff", $legendText="Business critical")

        Person(customer, "Personal Banking Customer", "A customer of the bank.")

        System_Boundary(c1, "Internet Banking", "System") {
          Container(spa, "Single-Page App", "JavaScript, Angular", "Provides banking functionality in the browser.")
          Container(api, "API Application", "Java, Docker", "Provides banking functionality via a JSON/HTTPS API.", $tags="critical")
          ContainerDb(db, "Database", "SQL Database", "Stores user registration information and access logs.")
          ContainerQueue(events, "Event Bus", "Kafka", "Carries domain events between services.")
        }

        System_Ext(mail, "E-mail System", "The internal Microsoft Exchange system.")

        Rel(customer, spa, "Visits bigbank.com/ib using", "HTTPS")
        Rel(spa, api, "Makes API calls to", "JSON/HTTPS")
        Rel(api, db, "Reads from and writes to", "JDBC")
        Rel(api, events, "Publishes to", "Kafka protocol")
        BiRel(api, mail, "Sends and receives e-mail using")
        ```

        ## Components, with a person outline and left-to-right layout

        ```mermaid
        C4Component
        title Component diagram for the API Application

        LAYOUT_LEFT_RIGHT()
        SHOW_PERSON_OUTLINE()

        Person(customer, "Banking Customer")

        Container_Boundary(api, "API Application") {
          Component(signin, "Sign In Controller", "MVC Rest Controller", "Allows users to sign in.")
          Component(security, "Security Component", "Spring Bean", "Provides functionality related to signing in.")
          ComponentDb(store, "User Store", "Spring Bean", "Reads user records from the database.")
        }

        Rel(customer, signin, "Submits credentials to", "JSON/HTTPS")
        Rel(signin, security, "Uses")
        Rel(security, store, "Uses")
        ```

        ## Dynamic — numbered interactions

        ```mermaid
        C4Dynamic
        title Dynamic diagram for the Internet Banking System

        Person(customer, "Banking Customer")
        Container(spa, "Single-Page App", "Angular")
        Container(api, "API Application", "Java")
        ContainerDb(db, "Database", "SQL Database")

        Rel(customer, spa, "Submits credentials", "HTTPS", $index=Index())
        Rel(spa, api, "Validates credentials", "JSON/HTTPS", $index=Index())
        Rel(api, db, "select * from users where …", "JDBC", $index=Index())
        Rel_Back(api, db, "Returns the user record", $index=Index())
        Rel_Back(customer, spa, "Sends back an authentication token", $index=Index())
        ```

        ## Sequence — a Nexaflow extension

        `C4Sequence` mirrors C4-PlantUML's `C4_Sequence.puml`, which Mermaid has no keyword for. Elements
        become participant cards, a `Boundary` groups them, and each `Rel` is a message carrying its
        technology. Native `sequenceDiagram` control lines — `alt`, `loop`, `note over`, `activate` —
        work inside it, because it is drawn by the very same renderer.

        ```mermaid
        C4Sequence
        title Sign-in sequence

        SHOW_INDEX()
        SHOW_FOOT_BOXES(false)

        Person(customer, "Banking Customer")
        Container(spa, "Single-Page App", "Angular")
        Boundary(b, "API Application", "Container")
        Component(signin, "Sign In Controller", "Spring MVC")
        ComponentDb(users, "User Store", "Spring Bean")
        Boundary_End()

        Rel(customer, spa, "Submits credentials", "HTTPS")
        Rel(spa, signin, "POST /signin", "JSON/HTTPS")
        alt credentials valid
        Rel(signin, users, "Looks the user up", "JDBC")
        Rel_Back(signin, users, "Returns the record")
        else rejected
        Rel_Back(spa, signin, "401 Unauthorized")
        end
        Rel_Back(customer, spa, "Shows the dashboard")
        ```

        ## Deployment — nested nodes

        ```mermaid
        C4Deployment
        title Deployment diagram for the Internet Banking System — Live

        Deployment_Node(plc, "Big Bank plc", "Big Bank plc data center") {
          Deployment_Node(dn, "bigbank-api***", "Ubuntu 16.04 LTS", "A web server rack") {
            Deployment_Node(apache, "Apache Tomcat", "Apache Tomcat 8.x") {
              Container(api, "API Application", "Java, Docker", "Provides banking functionality via an API.")
            }
          }
          Deployment_Node(bigbankdb, "bigbank-db01", "Oracle 12c") {
            ContainerDb(db, "Database", "Relational database schema", "Stores user registration information.")
          }
        }

        Rel(api, db, "Reads from and writes to", "JDBC")
        ```
        """;
    private const string DataMatrix =
        """"
        # Data Matrix

        A `datamatrix` fence draws a Data Matrix symbol — ECC 200 — generated locally. It takes the same
        `type:` lines a `qr` fence does, because a Wi-Fi descriptor or a vCard decodes the same from any
        symbol, plus the formats that exist only as Data Matrix.

        ## The shared types

        ```datamatrix
        type: url
        url: https://markdown.org
        ```

        ```datamatrix
        type: wifi
        ssid: MyNetwork
        password: s3cr3t-pass
        security: WPA
        ```

        ## Shape and size

        The smallest symbol that fits is chosen. `shape:` keeps it square or rectangular; `size:` fixes it.

        ```datamatrix
        type: text
        text: RECTANGULAR
        shape: rectangle
        ```

        ```datamatrix
        type: text
        text: Fixed at 26 by 26
        size: 26x26
        cellSize: 5
        ```

        ## GS1

        Write the element string with each application identifier in brackets. The symbol starts with
        FNC1, the brackets come off, and a separator goes after each variable-length element that is
        followed by another.

        ```datamatrix
        type: gs1
        data: (01)04150012345623(17)271231(10)LOT7(21)SN1
        ```

        ## Pharmacy packs

        A PPN is derived from the pack's PZN — the check characters are the part nobody gets right by hand —
        and wrapped in Macro 06 with the batch, expiry and serial as data identifiers.

        ```datamatrix
        type: ppn
        pzn: 01234562
        lot: A1B2
        expiry: 271231
        serial: 0001
        ```

        The same pack as GS1 sees it: an NTIN under AI 01.

        ```datamatrix
        type: ntin
        pzn: 01234562
        expiry: 271231
        lot: A1B2
        serial: 0001
        ```

        ## Royal Mail Mailmark

        A Mailmark 2D is a fixed-length message in the symbol size its format mandates — format 7 is 51
        characters in 24×24, format 9 is 90 in 32×32, format 29 is 70 in 16×48.

        ```datamatrix
        type: mailmark
        format: 7
        message: JGB 0111A123456712345678SW1A1AA  1       MARKDOWN12
        ```

        ## Styled

        ```datamatrix
        type: text
        text: styled
        cellSize: 6
        margin: 2
        dark: #1D4ED8
        light: #EFF6FF
        ```

        ## One the format cannot carry

        ```datamatrix
        type: ppn
        pzn: 01234563
        ```
        """";

    private const string Pdf417 =
        """"
        # PDF417

        A `pdf417` fence draws a PDF417 symbol — the stacked barcode on driving licences, boarding passes
        and shipping labels. It takes the same `type:` lines a `qr` fence does.

        ## The shared types

        ```pdf417
        type: url
        url: https://markdown.org
        ```

        ```pdf417
        type: text
        text: PDF417 carries a lot of text for its size.
        ```

        ## Shape

        Columns and error correction are yours to set; left alone the encoder picks a symbol about three
        times as wide as it is tall, with the parity the standard recommends for the payload.

        ```pdf417
        type: text
        text: Two columns, high error correction
        columns: 2
        ec: 5
        ```

        ```pdf417
        type: text
        text: Eight columns makes a wider, shorter symbol
        columns: 8
        ```

        Rows are drawn three module widths tall by default — the standard's floor, so a scanner sweeping
        across the symbol stays inside one row.

        ```pdf417
        type: text
        text: Taller rows
        rowHeight: 5
        cellSize: 3
        ```

        ## Truncated

        Dropping the right row indicator and the stop pattern saves eighteen modules a row. Worth it on a
        document that will not be damaged at its right edge; not on a parcel.

        ```pdf417
        type: text
        text: Truncated symbol
        truncated: true
        ```

        ## Styled

        ```pdf417
        type: text
        text: styled
        cellSize: 3
        margin: 2
        dark: #1D4ED8
        light: #EFF6FF
        ```

        ## One that cannot be drawn

        ```pdf417
        type: text
        text: x
        columns: 99
        ```
        """";

    private const string Aztec =
        """"
        # Aztec Code

        An `aztec` fence draws an Aztec Code — the symbology on rail and air tickets, which puts its
        finder pattern in the middle rather than the corners and so needs almost no quiet zone. It takes
        the same `type:` lines a `qr` fence does, and GS1 element strings besides.

        ## The shared types

        ```aztec
        type: url
        url: https://markdown.org
        ```

        ```aztec
        type: text
        text: An Aztec Code
        ```

        ```aztec
        type: wifi
        ssid: Guest
        password: welcome
        security: WPA
        ```

        ## Compact and full range

        There are two families. A compact symbol is smaller for the same message and stops at four
        layers; the full range goes to thirty-two and carries a reference grid through its larger sizes.
        Left alone the encoder takes the compact family while the message fits it, which is what you
        want; `format:` is there for a reader that insists on one or the other.

        ```aztec
        type: text
        text: Compact by default
        ```

        ```aztec
        type: text
        text: The same message, full range
        format: full
        ```

        A long message reaches the full range on its own.

        ```aztec
        type: text
        text: An Aztec symbol grows a layer at a time rather than jumping between versions, so a message of any length lands in a symbol barely larger than it needs. This one is well past what the compact family can hold, so it is drawn in the full range, where a reference grid runs through the larger sizes to keep a reader registered.
        ```

        ## Size and error correction

        `layers:` fixes the size outright — for a printed form with a box to fill — and fails rather than
        growing if the message will not fit. `ecc:` is the least share of the symbol that must be error
        correction; whatever the message leaves over becomes error correction anyway, so the number you
        set is a floor and not a target.

        ```aztec
        type: text
        text: Four layers, fixed
        format: compact
        layers: 4
        ```

        ```aztec
        type: text
        text: Half of this symbol is error correction
        ecc: 50
        ```

        ## GS1

        A `gs1` block takes the element string as people write it and puts the wire form in the symbol —
        brackets off, FNC1 in front, and a separator after each variable-length element that needs one.

        ```aztec
        type: gs1
        data: (01)04150123456782(10)LOT7(21)SN9
        ```

        ## Declaring a character set

        `eci:` writes an ECI number at the head of the message. Without it the bytes are UTF-8 and a
        reader is left to assume so, which nearly all of them do.

        ```aztec
        type: text
        text: Grüße aus Köln
        eci: 26
        ```

        ## Styled

        ```aztec
        type: text
        text: styled
        cellSize: 6
        margin: 2
        dark: #1D4ED8
        light: #EFF6FF
        ```

        ## One that cannot be drawn

        ```aztec
        type: text
        text: This message is far longer than a single-layer compact symbol can hold, and the block asks for one anyway.
        format: compact
        layers: 1
        ```
        """";

    private const string WordCloud =
        """"
        # Word clouds

        A `wordcloud` fence packs words into a picture, each set at the size its weight comes to. Every line is
        `something: value`: the settings are the lines above the words, and the first word closes them — after it,
        every line is a word and what it counts for. A `#` starts a comment.

        A cloud needs words to look like one. A dozen of them is a dozen words; the picture below is a hundred and
        twenty, which is where the packing starts to show.

        ## What a cloud looks like

        ```wordcloud
        design: 900
        system: 517
        colour: 374
        layout: 297
        type: 248
        grid: 215
        space: 190
        scale: 171
        contrast: 155
        rhythm: 143
        balance: 132
        texture: 123
        pattern: 116
        shape: 109
        line: 103
        form: 98
        hierarchy: 93
        alignment: 89
        proximity: 85
        typography: 82
        serif: 79
        kerning: 76
        leading: 73
        baseline: 71
        ligature: 69
        glyph: 66
        italic: 64
        roman: 63
        caption: 61
        heading: 59
        palette: 58
        hue: 56
        saturation: 55
        tone: 54
        tint: 52
        accent: 51
        neutral: 50
        gradient: 49
        opacity: 48
        shadow: 47
        canvas: 46
        margin: 45
        gutter: 44
        column: 44
        module: 43
        template: 42
        mockup: 41
        prototype: 41
        wireframe: 40
        sketch: 39
        draft: 39
        revision: 38
        proof: 38
        render: 37
        export: 36
        asset: 36
        artboard: 35
        layer: 35
        mask: 34
        motion: 34
        easing: 34
        timing: 33
        transition: 33
        duration: 32
        spring: 32
        bounce: 32
        fade: 31
        slide: 31
        rotate: 30
        parallax: 30
        keyframe: 30
        curve: 29
        anticipation: 29
        overlap: 29
        stagger: 28
        brand: 28
        identity: 28
        logo: 28
        mark: 27
        voice: 27
        story: 27
        message: 26
        audience: 26
        context: 26
        craft: 26
        detail: 26
        polish: 25
        restraint: 25
        clarity: 25
        intent: 25
        constraint: 24
        tradeoff: 24
        iteration: 24
        critique: 24
        legible: 24
        readable: 23
        focus: 23
        label: 23
        landmark: 23
        ratio: 23
        target: 22
        keyboard: 22
        screen: 22
        reader: 22
        print: 22
        mobile: 22
        tablet: 21
        desktop: 21
        viewport: 21
        breakpoint: 21
        responsive: 21
        fluid: 21
        adaptive: 21
        container: 20
        stack: 20
        flow: 20
        wrap: 20
        snap: 20
        anchor: 20
        offset: 20
        ```

        ## Shapes

        The words are packed outwards from the middle and run out where the outline is — nothing is clipped, so a
        shape needs plenty of words before it reads as one.

        ```wordcloud
        shape: cardioid
        ellipticity: 1
        height: 620
        weather: 700
        rain: 402
        cloud: 291
        wind: 231
        storm: 193
        frost: 167
        mist: 148
        snow: 133
        thunder: 121
        sunshine: 111
        drizzle: 103
        shower: 96
        gale: 90
        breeze: 85
        fog: 80
        hail: 76
        sleet: 73
        lightning: 69
        rainbow: 66
        overcast: 64
        pressure: 61
        front: 59
        isobar: 57
        forecast: 55
        warning: 53
        flood: 52
        drought: 50
        humid: 49
        muggy: 47
        crisp: 46
        chill: 45
        thaw: 44
        freeze: 43
        melt: 42
        damp: 41
        dry: 40
        bright: 39
        dull: 38
        grey: 37
        blue: 37
        morning: 36
        evening: 35
        night: 35
        dawn: 34
        dusk: 33
        noon: 33
        season: 32
        spring: 32
        summer: 31
        autumn: 31
        winter: 30
        monsoon: 30
        cyclone: 29
        hurricane: 29
        tornado: 28
        blizzard: 28
        squall: 28
        swell: 27
        tide: 27
        current: 26
        coast: 26
        moor: 26
        valley: 25
        ridge: 25
        summit: 25
        plain: 25
        estuary: 24
        channel: 24
        strait: 24
        bay: 23
        temperature: 23
        degrees: 23
        celsius: 23
        minimum: 22
        maximum: 22
        average: 22
        record: 22
        anomaly: 21
        trend: 21
        pattern: 21
        satellite: 21
        radar: 21
        station: 20
        buoy: 20
        balloon: 20
        model: 20
        ensemble: 20
        hindcast: 19
        nowcast: 19
        outlook: 19
        ```

        ```wordcloud
        shape: star
        ellipticity: 1
        height: 620
        space: 700
        orbit: 402
        launch: 291
        rocket: 231
        booster: 193
        payload: 167
        capsule: 148
        module: 133
        station: 121
        probe: 111
        lander: 103
        rover: 96
        flyby: 90
        transit: 85
        eclipse: 80
        comet: 76
        asteroid: 73
        meteor: 69
        nebula: 66
        galaxy: 64
        cluster: 61
        quasar: 59
        pulsar: 57
        supernova: 55
        redshift: 53
        parallax: 52
        spectrum: 50
        telescope: 49
        mirror: 47
        aperture: 46
        exposure: 45
        photon: 44
        neutrino: 43
        gravity: 42
        tidal: 41
        escape: 40
        velocity: 39
        apogee: 38
        perigee: 37
        inclination: 37
        trajectory: 36
        burn: 35
        thrust: 35
        stage: 34
        fairing: 33
        gimbal: 33
        telemetry: 32
        downlink: 32
        beacon: 31
        antenna: 31
        solar: 30
        panel: 30
        battery: 29
        cryogenic: 29
        propellant: 28
        oxidiser: 28
        plume: 28
        reentry: 27
        ablation: 27
        splashdown: 26
        lunar: 26
        martian: 26
        venusian: 25
        jovian: 25
        saturnian: 25
        kuiper: 25
        oort: 24
        heliosphere: 24
        corona: 24
        flare: 23
        observatory: 23
        survey: 23
        catalogue: 23
        ephemeris: 22
        occultation: 22
        albedo: 22
        regolith: 22
        crater: 21
        rille: 21
        mare: 21
        ```

        ## Turned words

        `rotate:` is the share of the words turned. They take any angle between `minRotation:` and `maxRotation:`;
        `rotationSteps: 2` gives the tidier look of words either level or on their side.

        ```wordcloud
        rotate: 0.35
        rotationSteps: 2
        minRotation: -90
        maxRotation: 0
        design: 900
        system: 517
        colour: 374
        layout: 297
        type: 248
        grid: 215
        space: 190
        scale: 171
        contrast: 155
        rhythm: 143
        balance: 132
        texture: 123
        pattern: 116
        shape: 109
        line: 103
        form: 98
        hierarchy: 93
        alignment: 89
        proximity: 85
        typography: 82
        serif: 79
        kerning: 76
        leading: 73
        baseline: 71
        ligature: 69
        glyph: 66
        italic: 64
        roman: 63
        caption: 61
        heading: 59
        palette: 58
        hue: 56
        saturation: 55
        tone: 54
        tint: 52
        accent: 51
        neutral: 50
        gradient: 49
        opacity: 48
        shadow: 47
        canvas: 46
        margin: 45
        gutter: 44
        column: 44
        module: 43
        template: 42
        mockup: 41
        prototype: 41
        wireframe: 40
        sketch: 39
        draft: 39
        revision: 38
        proof: 38
        render: 37
        export: 36
        asset: 36
        artboard: 35
        layer: 35
        mask: 34
        motion: 34
        ```

        ## Colours

        `theme` takes the palette's series colours in turn, so a cloud follows the theme as every other chart does.
        Colours written out are taken in turn instead, and `random-dark` / `random-light` scatter them.

        ```wordcloud
        color: #e06c75 #e5c07b #98c379 #61afef
        background: #1c1f26
        weather: 700
        rain: 402
        cloud: 291
        wind: 231
        storm: 193
        frost: 167
        mist: 148
        snow: 133
        thunder: 121
        sunshine: 111
        drizzle: 103
        shower: 96
        gale: 90
        breeze: 85
        fog: 80
        hail: 76
        sleet: 73
        lightning: 69
        rainbow: 66
        overcast: 64
        pressure: 61
        front: 59
        isobar: 57
        forecast: 55
        warning: 53
        flood: 52
        drought: 50
        humid: 49
        muggy: 47
        crisp: 46
        chill: 45
        thaw: 44
        freeze: 43
        melt: 42
        damp: 41
        dry: 40
        bright: 39
        dull: 38
        grey: 37
        blue: 37
        morning: 36
        evening: 35
        night: 35
        dawn: 34
        dusk: 33
        noon: 33
        season: 32
        spring: 32
        summer: 31
        autumn: 31
        ```

        ```wordcloud
        color: random-dark
        background: #ffffff
        space: 700
        orbit: 402
        launch: 291
        rocket: 231
        booster: 193
        payload: 167
        capsule: 148
        module: 133
        station: 121
        probe: 111
        lander: 103
        rover: 96
        flyby: 90
        transit: 85
        eclipse: 80
        comet: 76
        asteroid: 73
        meteor: 69
        nebula: 66
        galaxy: 64
        cluster: 61
        quasar: 59
        pulsar: 57
        supernova: 55
        redshift: 53
        parallax: 52
        spectrum: 50
        telescope: 49
        mirror: 47
        aperture: 46
        exposure: 45
        photon: 44
        neutrino: 43
        gravity: 42
        tidal: 41
        escape: 40
        velocity: 39
        apogee: 38
        perigee: 37
        inclination: 37
        trajectory: 36
        burn: 35
        thrust: 35
        stage: 34
        fairing: 33
        gimbal: 33
        telemetry: 32
        downlink: 32
        beacon: 31
        antenna: 31
        ```

        ## Letters and pictures

        `shape:` bends the cloud towards an outline. `letters:` and `mask:` mark out where the words may go at
        all, so the cloud fills a shape rather than tending towards one. Keep `maxSize:` well down: the words
        have to be small against the letters or there is nothing left of the shape to read.

        ```wordcloud
        letters: CLOUD
        minSize: 4
        maxSize: 26
        scale: log
        gridSize: 2
        gap: 1
        rotate: 0
        design: 900
        system: 517
        colour: 374
        layout: 297
        type: 248
        grid: 215
        space: 190
        scale: 171
        contrast: 155
        rhythm: 143
        balance: 132
        texture: 123
        pattern: 116
        shape: 109
        line: 103
        form: 98
        hierarchy: 93
        alignment: 89
        proximity: 85
        typography: 82
        serif: 79
        kerning: 76
        leading: 73
        baseline: 71
        ligature: 69
        glyph: 66
        italic: 64
        roman: 63
        caption: 61
        heading: 59
        palette: 58
        hue: 56
        saturation: 55
        tone: 54
        tint: 52
        accent: 51
        neutral: 50
        gradient: 49
        opacity: 48
        shadow: 47
        canvas: 46
        margin: 45
        gutter: 44
        column: 44
        module: 43
        template: 42
        mockup: 41
        prototype: 41
        wireframe: 40
        sketch: 39
        draft: 39
        revision: 38
        proof: 38
        render: 37
        export: 36
        asset: 36
        artboard: 35
        layer: 35
        mask: 34
        motion: 34
        easing: 34
        timing: 33
        transition: 33
        duration: 32
        spring: 32
        bounce: 32
        fade: 31
        slide: 31
        rotate: 30
        parallax: 30
        keyframe: 30
        curve: 29
        anticipation: 29
        overlap: 29
        stagger: 28
        brand: 28
        identity: 28
        logo: 28
        mark: 27
        voice: 27
        story: 27
        message: 26
        audience: 26
        context: 26
        craft: 26
        detail: 26
        polish: 25
        restraint: 25
        clarity: 25
        intent: 25
        constraint: 24
        tradeoff: 24
        iteration: 24
        critique: 24
        legible: 24
        readable: 23
        focus: 23
        label: 23
        landmark: 23
        ratio: 23
        target: 22
        keyboard: 22
        screen: 22
        reader: 22
        print: 22
        mobile: 22
        tablet: 21
        desktop: 21
        viewport: 21
        breakpoint: 21
        responsive: 21
        fluid: 21
        adaptive: 21
        container: 20
        stack: 20
        flow: 20
        wrap: 20
        snap: 20
        anchor: 20
        offset: 20
        ```

        A `mask:` works the same way with a picture — its opaque part if it has transparency, its dark part if
        it has not — named as an `![](…)` names one and found beside the document.

        ## Scales

        `scale:` decides what the middle of the range looks like. Counts spread over orders of magnitude read better
        on `log`; `linear` is the plain reading, and `sqrt` — the default — spreads the light words apart.

        ```wordcloud
        scale: log
        minSize: 10
        the: 12000
        of: 6400
        and: 5900
        to: 4100
        in: 3200
        hexadecimal: 40
        onomatopoeia: 12
        ```

        ## When it will not draw

        A weight that is not a number leaves its line waved under and the rest of the words a cloud; a setting given
        something it cannot take stops the block being one at all, and its lines are shown as they were written.

        ```wordcloud
        WPF: 120
        XAML: lots
        MVVM: 74
        ```

        ## A word named as a setting

        The settings are the lines above the words, so `scale`, `colour` and `gap` below the first word are words.
        Only the very first word needs quoting to be one.

        ```wordcloud
        shape: circle
        "shape": 90
        colour: 74
        scale: 61
        gap: 50
        font: 44
        hue: 38
        outline: 32
        weight: 27
        ```
        """";



    private const string Smiles =
        """"
        # Chemical structures

        A `smiles` fence draws molecules from SMILES strings — one per line, with an optional caption in
        double quotes. The `chemistry` keyword may open the block, and `#` starts a comment.

        ## The format's own example

        ```smiles
        chemistry
        c1ccccc1 "Benzene"
        CC(=O)O "Acetic acid"
        CCO "Ethanol"
        ```

        ## Common molecules

        ```smiles
        O "Water"
        Oc1ccccc1 "Phenol"
        CC(=O)Oc1ccccc1C(=O)O "Aspirin"
        Cn1cnc2c1c(=O)n(C)c(=O)n2C "Caffeine"
        OCC1OC(O)C(O)C(O)C1O "Glucose"
        ```

        ## Rings

        ```smiles
        # fused, bridged and spiro
        c1ccc2ccccc2c1 "Naphthalene"
        C1CC2CCC1C2 "Norbornane"
        C1CCC2(CC1)CCCC2 "Spiro[4.5]decane"
        C1CCCCCCCCCCC1 "Cyclododecane"
        ```

        ## Stereo

        ```smiles
        N[C@@H](C)C(=O)O "L-alanine"
        N[C@H](C)C(=O)O "D-alanine"
        C/C=C\C "cis-2-butene"
        C/C=C/C "trans-2-butene"
        ```

        ## Charges, isotopes and salts

        ```smiles
        [NH4+].[Cl-] "Ammonium chloride"
        C[N+](=O)[O-] "Nitromethane"
        [13CH4] "Carbon-13 methane"
        [O-]S(=O)(=O)[O-].[Na+].[Na+] "Sodium sulfate"
        ```

        ## When it will not draw

        ```smiles
        C(C)(C)(C)(C)C "Five bonds to one carbon"
        c1ccnc1 "Pyrrole without its hydrogen"
        C1CCC "A ring never closed"
        ```
        """";

    private const string Plots =
        """"
        # Correlation plots

        Four fences draw a table of values against a pair of axes: `scatter`, `bubble`, `heatmap` and
        `density2d`. They share one grammar, because they differ in what is drawn rather than in what is
        written — which is the division ggplot2 makes, and the reason a bubble plot is a scatter plot with
        one more column rather than a language of its own.

        Every line is either a `key: value` setting or a row of the table. The settings are the lines above
        the table and the first row closes them, so a column headed `size` is a column. A `#` starts a
        comment.

        ## The smallest plot there is

        Two columns of numbers and nothing else. The first is x, the second is y.

        ```scatter
        1.2  3.4
        2.5  5.1
        3.1  6.8
        4.4  7.2
        5.0  9.6
        ```

        ## Naming the columns

        A first row no cell of which is a number names them, and the names are what the axes are called.
        Cells are separated by spaces or commas alike, so a table lined up in columns and a table written
        with commas are the same table.

        ```scatter
        title: Weight against fuel economy
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg
        2620    21.0
        2875    21.0
        2320    22.8
        3215    21.4
        3440    18.7
        3460    18.1
        3570    14.3
        3190    24.4
        2200    32.4
        1615    30.4
        1835    33.9
        5250    10.4
        5424    10.4
        3840    13.3
        1935    27.3
        2140    26.0
        ```

        ## Mapping a column to a channel

        **A setting that names a column maps it; anything else is set for every mark.** So `colour: origin`
        tells the groups apart and `size: 4` sets the size of all of them, and the two need no separate
        syntax. A column may feed more than one channel — `colour` and `shape` together is how a chart reads
        in colour and in print alike.

        ```scatter
        title: Fuel economy by cylinders
        colour: cyl
        shape: cyl
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg   cyl
        2620    21.0  six
        2320    22.8  four
        3440    18.7  eight
        3570    14.3  eight
        3190    24.4  four
        2200    32.4  four
        1615    30.4  four
        5250    10.4  eight
        3170    15.8  eight
        2770    19.7  six
        3460    18.1  six
        1835    33.9  four
        ```

        ## A bubble plot

        The same grammar; the `bubble` fence simply maps a third column to size. Sizes are spread over area
        rather than radius, because a circle of twice the radius carries four times the ink.

        ```bubble
        title: Wealth, longevity and population
        xTitle: GDP per head
        yTitle: Life expectancy
        sizeRange: 5 34
        colour: region
        xScale: log

        gdp    life  pop   region
        1280   52.9  1032  Africa
        38225  81.7  742   Europe
        9771   76.5  4545  Asia
        14103  75.1  648   Americas
        54225  82.5  41    Oceania
        2104   64.9  1380  Asia
        780    59.3  430   Africa
        22400  78.9  310   Europe
        ```

        ## Correlation

        `fit:` draws a line through the points — `lm` for least squares, `loess` for a curve that follows
        them — with the band its own uncertainty makes. `stats:` writes what the points say about each
        other on the panel: `r`, `r2`, `n` and `p`, by `method: pearson`, `spearman` or `kendall`.

        ```scatter
        title: How closely they move together
        fit: lm
        se: true
        stats: r r2 n p
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg
        2620    21.0
        2875    21.0
        2320    22.8
        3215    21.4
        3440    18.7
        3460    18.1
        3570    14.3
        3190    24.4
        2200    32.4
        1615    30.4
        1835    33.9
        5250    10.4
        5424    10.4
        3840    13.3
        1935    27.3
        2140    26.0
        ```

        ## A correlation matrix

        A `heatmap` reads a table down its side as well as across it, where every row is one cell wider than
        the header — which is how anybody writes a correlation matrix. `midpoint: 0` makes the colour mean
        the sign.

        ```heatmap
        title: How the measures move together
        gradient: rdbu
        midpoint: 0
        fillLimits: -1 1
        labels: true

                mpg    disp    hp     drat   wt     qsec
        mpg     1.00  -0.85  -0.78   0.68  -0.87   0.42
        disp   -0.85   1.00   0.79  -0.71   0.89  -0.43
        hp     -0.78   0.79   1.00  -0.45   0.66  -0.71
        drat    0.68  -0.71  -0.45   1.00  -0.71   0.09
        wt     -0.87   0.89   0.66  -0.71   1.00  -0.17
        qsec    0.42  -0.43  -0.71   0.09  -0.17   1.00
        ```

        ## A heat map of a long table

        Written the ordinary way round — a row per cell, its third column the value — the same fence draws a
        grid. An axis of numbers is still read in order.

        ```heatmap
        title: Sightings by month
        gradient: viridis

        month  year   count
        Jan    2023   12
        Feb    2023   28
        Mar    2023   45
        Jan    2024   19
        Feb    2024   36
        Mar    2024   61
        Jan    2025   24
        Feb    2025   41
        Mar    2025   77
        ```

        ## Too many points to draw one by one

        `geom: hex` and `geom: bin2d` count the rows into bins instead, and `bins:` says how many. A
        hexagonal lattice has no corners two bins share, so no point is counted into a bin further from it
        than another.

        ```heatmap
        geom: hex
        bins: 8
        gradient: magma
        xTitle: x
        yTitle: y

        x     y
        -1.2  -0.9
        -0.8  -1.4
        -1.0  -0.6
        -0.4  -0.8
        -1.6  -1.1
        -0.9  -0.3
        -1.1  -1.8
        -0.6  -1.2
        1.8   1.6
        1.4   2.1
        2.0   1.2
        1.6   1.9
        2.2   1.7
        1.2   1.4
        1.9   2.3
        1.5   1.1
        0.2   0.4
        0.6   0.1
        -0.2  0.3
        0.4   0.8
        ```

        ## How thickly they lie

        A `density2d` fence draws the contours of a kernel density estimate — Silverman's rule for the width
        of the kernel, as ggplot2 takes it. `contour:` chooses `bands`, `lines` or `raster`, `levels:` how
        many, and `points: true` shows the rows themselves through it.

        ```density2d
        title: Where the points really are
        contour: bands
        levels: 7
        gradient: viridis
        xTitle: x
        yTitle: y

        x     y
        -1.2  -0.9
        -0.8  -1.4
        -1.0  -0.6
        -0.4  -0.8
        -1.6  -1.1
        -0.9  -0.3
        -1.1  -1.8
        -0.6  -1.2
        -1.4  -0.5
        -0.7  -1.6
        1.8   1.6
        1.4   2.1
        2.0   1.2
        1.6   1.9
        2.2   1.7
        1.2   1.4
        1.9   2.3
        1.5   1.1
        2.1   1.8
        1.3   1.5
        0.2   0.4
        0.6   0.1
        -0.2  0.3
        0.4   0.8
        ```

    
        ## Naming, fading and unstacking

        `subtitle:` and `caption:` sit above and below the plot, `legendTitle:` names the key, `label:` names
        each mark, and `alpha:` fades them by a column. `jitter:` moves marks off their place so rows landing
        on the same value stop hiding one another — always the same way for the same block.

        ```scatter
        title: Wealth and longevity
        subtitle: Eight economies
        legendTitle: Region
        colour: region
        label: place
        alpha: life
        jitter: 0.02
        caption: Source: made up for the example
        xTitle: GDP per head
        yTitle: Life expectancy

        gdp    life  region    place
        1280   52.9  Africa    Chad
        38225  81.7  Europe    France
        9771   76.5  Asia      China
        14103  75.1  Americas  Brazil
        54225  82.5  Oceania   Australia
        2104   64.9  Asia      India
        780    59.3  Africa    Niger
        22400  78.9  Europe    Poland
        ```

        ## A shape, a flip, and a line per group

        `aspect:` holds the panel to a shape, `flip:` swaps the axes — each title going with its own channel —
        and `group:` fits a line per group rather than one through everything. A band is part of the answer,
        so the axis opens out to show all of it unless `yLimits:` says otherwise.

        ```scatter
        title: Fuel economy by weight
        aspect: 1
        flip: true
        fit: lm
        group: cyl
        colour: cyl
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg   cyl
        2620    21.0  six
        2320    22.8  four
        3440    18.7  eight
        3570    14.3  eight
        3190    24.4  four
        2200    32.4  four
        1615    30.4  four
        5250    10.4  eight
        3170    15.8  eight
        2770    19.7  six
        3460    18.1  six
        1835    33.9  four
        ```

        ## A matrix worked out rather than written

        `geom: corr` is given the observations and correlates every numeric column with every other,
        by whatever `method:` names. A tile is worked out rather than written, so it is drawn but not
        typed into — the column names down its two axes are.

        ```heatmap
        title: How the measures move together
        geom: corr
        labels: true
        aspect: 1

        mpg   disp   hp   drat  wt
        21.0  160.0  110  3.90  2.620
        22.8  108.0  93   3.85  2.320
        21.4  258.0  110  3.08  3.215
        18.7  360.0  175  3.15  3.440
        14.3  360.0  245  3.21  3.570
        24.4  146.7  62   3.69  3.190
        19.2  167.6  123  3.92  3.440
        16.4  275.8  180  3.07  4.070
        10.4  472.0  205  2.93  5.250
        32.4  78.7   66   4.08  2.200
        30.4  75.7   52   4.93  1.615
        33.9  71.1   65   4.22  1.835
        15.5  318.0  150  2.76  3.520
        13.3  350.0  245  3.73  3.840
        27.3  79.0   66   4.08  1.935
        26.0  120.3  91   4.43  2.140
        15.8  351.0  264  4.22  3.170
        19.7  145.0  175  3.62  2.770
        15.0  301.0  335  3.54  3.570
        21.4  121.0  109  4.11  2.780
        ```

        ## A panel per group

        `facet:` splits the plot into a panel per value of a column, `facetCols:` saying how many stand
        side by side. Every panel is drawn on the same scales, which is what lets one be read against
        another, and the numbers go round the outside alone.

        ```scatter
        title: Fuel economy by weight
        facet: cyl
        colour: cyl
        fit: lm
        facetCols: 3
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg   cyl
        2620    21.0  six
        2875    21.0  six
        2320    22.8  four
        3215    21.4  six
        3440    18.7  eight
        3570    14.3  eight
        3190    24.4  four
        3150    22.8  four
        4070    16.4  eight
        3730    17.3  eight
        5250    10.4  eight
        2200    32.4  four
        1615    30.4  four
        1835    33.9  four
        2465    21.5  four
        3520    15.5  eight
        1935    27.3  four
        2770    19.7  six
        3460    18.1  six
        2780    21.4  four
        ```

        ## Scales and gridlines

        `xScale:` and `yScale:` take `linear`, `log`, `log2`, `ln`, `sqrt` and `reverse`; `xLimits:` and
        `xBreaks:` say where the axis starts, stops and is marked; `grid:` takes `both`, `x`, `y` or `none`.
        A value a scale has nowhere to put — nought on a log axis — is waved rather than drawn at the edge.

        ```scatter
        xScale: log
        yLimits: 0 100
        yBreaks: 0 25 50 75 100
        grid: y
        xTitle: Budget
        yTitle: Share reached

        budget  reached
        120     8
        450     19
        1800    34
        6200    52
        24000   71
        90000   88
        ```

        ## What cannot be read

        A setting given something it cannot take stops the block being a plot, and its lines are shown as
        they were written — a question about the whole picture. A cell that will not read loses only its own
        mark, because it is the row somebody is editing.

        ```scatter
        x: weight
        y: mpg

        weight  mpg
        3504    18.0
        2372    lots
        1613    35.0
        ```

        ```scatter
        width: wide

        1 2
        3 4
        ```
        """";
}
