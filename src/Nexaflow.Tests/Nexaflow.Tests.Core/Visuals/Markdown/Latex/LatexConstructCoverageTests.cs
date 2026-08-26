using System.Collections.Generic;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Rendering;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// Every construct the typesetter knows, gathered into a handful of formulas, each asked the questions
/// the whole feature rests on: does every piece of layout name a real part of the source, name a
/// <em>different</em> part from the piece holding it, and name something inside it?
///
/// <para>
/// This is the cheap standing version of the corpus sweep. The sweep reads a quarter of a million real
/// formulas and takes twenty minutes, which means in practice it runs when someone remembers; these run
/// in the ordinary suite. It cannot cover every nesting of everything — nothing can — but a construct
/// that breaks its own spans breaks them the first time it appears, and this is that first time for all
/// of them.
/// </para>
/// <para>
/// Every formula here must typeset. A construct that stops parsing is a regression whether or not its
/// spans are sound, so the list doubles as the record of what this typesetter supports.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class LatexConstructCoverageTests
{
    private const double Scale = 16;

    private static readonly (string What, string Latex)[] Everything =
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

    [TestMethod]
    public void EveryConstructTypesets() => UiThread.Run(() =>
    {
        foreach (var (what, latex) in Everything)
            Assert.IsNotNull(LatexLayout.Build(Flatten(latex), Scale), $"{what} no longer typesets");
    });

    [TestMethod]
    public void NoConstructNamesSourceItDoesNotOwn() => UiThread.Run(() =>
    {
        // The typesetter's spans, before the tree has repaired anything. A span reaching outside the very
        // text it indexes costs a symbol its selectability outright.
        foreach (var (what, latex) in Everything)
        {
            var capture = Capture(Flatten(latex));
            Assert.AreEqual(0, capture.Rejected,
                $"{what}: " + string.Join("; ", capture.RejectedSpans));
        }
    });

    [TestMethod]
    public void NoConstructNeedsItsNameTakenOffIt() => UiThread.Run(() =>
    {
        // The standing version of what the corpus sweep proved once over 238k formulas: nothing repeats a
        // name its own ancestor carries, and nothing names source outside the piece containing it. Both
        // are typesetter faults the tree can only repair by discarding the link — which costs a term the
        // ability to be selected in its own right, quietly, while everything still draws correctly.
        foreach (var (what, latex) in Everything)
        {
            var capture = Capture(Flatten(latex));
            Assert.AreEqual(0, capture.Disowned.Count,
                $"{what}: " + string.Join("; ", capture.Disowned));
        }
    });

    [TestMethod]
    public void EveryConstructNestsItsNames() => UiThread.Run(() =>
    {
        // The same invariant asked of the finished tree rather than of the capture, so a repair that
        // failed to repair would still be caught.
        foreach (var (what, latex) in Everything)
        {
            var layout = LatexLayout.Build(Flatten(latex), Scale);
            Assert.IsNotNull(layout, what);

            foreach (var node in layout.Tree.Root.SelfAndDescendants().Where(n => n.SourceLength > 0))
            {
                Assert.IsFalse(
                    node.Ancestors().Any(a => a.SourceStart == node.SourceStart && a.SourceLength == node.SourceLength),
                    $"{what}: {node} repeats a name its ancestor carries");

                if (node.Parent is { SourceLength: > 0 } parent)
                    Assert.IsTrue(
                        node.SourceStart >= parent.SourceStart && node.SourceEnd() <= parent.SourceEnd(),
                        $"{what}: {node} names source outside its parent {parent}");
            }
        }
    });

    private static LatexLayoutCapture Capture(string latex)
    {
        var capture = new LatexLayoutCapture(Scale, latex);
        WpfTeXFormulaParser.Instance.Parse(latex)
            .RenderTo(capture, WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale), 0, 0);
        capture.FinishRendering();
        return capture;
    }

    /// <summary>One line, so the offsets in a failure message are the offsets you can count to.</summary>
    private static string Flatten(string latex) =>
        string.Join(" ", latex.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
}
