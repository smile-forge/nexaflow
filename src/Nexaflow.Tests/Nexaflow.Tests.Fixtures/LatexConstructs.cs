namespace Nexaflow.Tests.Features.Fixtures;

/// <summary>
/// Every construct the maths engine knows, gathered into a handful of formulas — the standing,
/// cheap version of the 238k-formula corpus sweep, and the record of what is supported.
///
/// <para>
/// Shared rather than kept beside whichever suite wrote it down first, because two things now ask
/// questions of the same list and they must be asking about the same constructs: the layout tests
/// (does every piece of the picture name a real part of the source?) and the parse-tree tests (does
/// reading it and writing it back give exactly what was read?). A construct added to one and not the
/// other would look covered and not be.
/// </para>
/// </summary>
public static class LatexConstructs
{
    public static readonly (string What, string Latex)[] Everything =
    [
        ("fractions and binomials",
            @"\frac{a}{b} + \dfrac{c}{d} + \tfrac{e}{f} + \cfrac{1}{2 + \cfrac{1}{3}} + \nicefrac{g}{h}
              + \sfrac{i}{j} + \binom{n}{k} + \dbinom{p}{q} + \tbinom{r}{s}"),

        ("roots, bars and boxes",
            @"\sqrt{x} + \sqrt[3]{y+1} + \overline{ab} + \underline{cd} + \overbrace{e+f} + \underbrace{g+h}
              + \overset{a}{b} + \underset{c}{d} + \stackrel{e}{f} + \boxed{k} + \cancel{m} + \bcancel{n}
              + \xcancel{p}"),

        ("scripts, primes and big operators",
            @"x^{2} + y_{i} + z^{a^{b}} + w_{c_{d}} + f'' + g'_{k} + \sum_{i=0}^{n} i + \prod_{j=1}^{m} j
              + \int_{0}^{1} x \, dx + \oint_C F + \iint_D f + \iiint_E g + \lim_{x \to \infty} h
              + \sup S + \inf S + \max_i a + \min_i b + \sin x + \cos y + \arctan z + \coth w"),

        ("accents and arrows",
            @"\vec{a} + \hat{b} + \tilde{c} + \bar{d} + \dot{e} + \ddot{f} + \overrightarrow{AB}
              + \overleftarrow{CD} + \overleftrightarrow{EF} + \underrightarrow{GH} + \underleftarrow{IJ}
              + \xrightarrow{k} + \xleftarrow{l} + \xleftrightarrow{m} + \xmapsto{n}"),

        ("fences and delimiters",
            @"\left( a \right) + \left[ b \right] + \left\{ c \right\} + \left| d \right|
              + \left\langle e \right\rangle + \left( \frac{f}{g} \right)^{2}
              + \bigl( h \bigr) + \Bigl[ i \Bigr] + \biggl\{ j \biggr\} + \Biggl| k \Biggr|
              + \left[ l \left( m \right)^{2} n \right]"),

        ("matrices and environments",
            @"\begin{matrix} 1 & 2 \\ 3 & 4 \end{matrix} + \begin{pmatrix} a & b \\ c & d \end{pmatrix}
              + \begin{bmatrix} e & f \\ g & h \end{bmatrix} + \begin{vmatrix} i & j \\ k & l \end{vmatrix}
              + \begin{Vmatrix} m & n \\ o & p \end{Vmatrix} + \begin{smallmatrix} q & r \\ s & t \end{smallmatrix}
              + \begin{cases} u & x > 0 \\ v & x \le 0 \end{cases}"),

        ("aligned and gathered blocks",
            @"\begin{align} a &= b + c \\ d &= e + f \end{align}"),

        ("stacked and gathered",
            @"\begin{gather} x = y \\ z = w \end{gather}"),

        ("text styles and fonts",
            @"\mathrm{abc} + \text{def} + \mbox{ghi} + \textbf{jkl} + \textit{mno} + \texttt{pqr}
              + {\cal S} + {\bf T} + {\it U} + {\rm V} + {\frak W} + {\scr X}
              + \boldsymbol{y} + \operatorname{tr} Z"),

        ("spacing, dots and modular arithmetic",
            @"a \, b \; c \quad d \qquad e ~ f + \vdots + \ddots + \cdots + \ldots
              + n \bmod m + p \pmod{q} + r \mod s + t \pod{u}"),

        ("colour, phantoms and overlap",
            @"\textcolor{red}{a} + \colorbox{yellow}{b} + \phantom{c} + \hphantom{d} + \vphantom{e}
              + \smash{f} + \llap{g} + \rlap{h}"),

        ("styles and sizes",
            @"\displaystyle \frac{a}{b} + \textstyle \frac{c}{d} + \scriptstyle \frac{e}{f}
              + \scriptscriptstyle \frac{g}{h} + \mathord{i} + \mathbin{j} + \mathrel{k} + \mathop{l}"),

        ("greek, relations and symbols",
            @"\alpha \beta \gamma \Delta \Omega + \infty + \partial + \nabla + \pm \mp \times \div
              + \leq \geq \neq \approx \equiv \sim \propto + \subset \supset \in \notin \cup \cap
              + \rightarrow \Rightarrow \leftrightarrow \mapsto + \forall \exists \neg \wedge \vee"),
    ];

    /// <summary>One line, so the offsets in a failure message are the offsets you can count to.</summary>
    public static string Flatten(string latex) =>
        string.Join(" ", latex.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));

    /// <summary>Every construct formula, each on one line.</summary>
    public static IEnumerable<string> Flattened() =>
        Everything.Select(entry => Flatten(entry.Latex));
}
