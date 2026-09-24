using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// The LaTeX commands added on top of the JMathTeX command set:
/// style switches (\displaystyle and family), annotations (\overset, \underset, \stackrel), extent (\phantom,
/// \smash, the laps), frames (\boxed, \fbox), font switches (\mathbb and family, the \text* family), extensible
/// arrows, and the matrix-like environments.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class AdditionalCommandsTests
{
    /// <summary>The formula's width, and where its ink runs from and to measured from its own left edge, with how many marks it drew.</summary>
    private static (double Width, double InkLeft, double InkRight, int Marks) Bounds(string markup)
    {
        var capture = Laid(markup);
        var tree = capture.Tree!;
        var left = -tree.AnchorOf(0).X;
        var marks = Enumerable.Range(0, tree.Count).Sum(at => tree.MarksOf(at).Length);
        return (Width(markup), left, left + capture.Size.Width, marks);
    }

    [TestMethod]
    [DataRow(@"\displaystyle x")]
    [DataRow(@"\textstyle x")]
    [DataRow(@"\scriptstyle x")]
    [DataRow(@"\scriptscriptstyle x")]
    [DataRow(@"\displaystyle\sum_{i=1}^{n} i")]
    [DataRow(@"\displaystyle\frac{a}{b}")]
    public void StyleSwitchesRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void AStyleSwitchTakesTheRestOfItsGroupNotJustTheNextElement() => UiThread.Run(() =>
        // The limits have to end up inside the switch: display style is what puts them above and below the
        // operator, and in a text-style formula only a switch that reaches the scripts can stack them.
        Assert.IsTrue(TotalHeight(@"\displaystyle\sum_{i=1}^{n}", TexStyle.Text) > TotalHeight(@"\sum_{i=1}^{n}", TexStyle.Text),
                      "the limits should be stacked"));

    [TestMethod]
    public void AStyleSwitchInsideAGroupEndsWithTheGroup() => UiThread.Run(() =>
        // The "b" is outside the braces, so it keeps the outer style.
        Assert.IsTrue(Width(@"{\scriptstyle a} b") > Width(@"\scriptstyle a b")));

    [TestMethod]
    [DataRow(@"\textbf{bold text} \;\; \textsf{sans text} \;\; \texttt{mono text} \;\; \textsc{small caps}")]
    [DataRow(@"\boldsymbol{\alpha} + \boldsymbol{\beta} = \boldsymbol{\gamma} \qquad \boldsymbol{\nabla} \times \boldsymbol{F} \qquad \bm{\Sigma}\bm{x} = \bm{\lambda}")]
    [DataRow(@"\color{red}{a^2} + \color{blue}{b^2} = \color{green}{c^2}")]
    [DataRow(@"\textcolor{red}{a^2} + \textcolor{blue}{b^2} = \textcolor{green}{c^2}")]
    [DataRow(@"\matrix{ a & b \\ c & d } \;\; \begin{matrix} a & b \\ c & d \end{matrix}")]
    [DataRow(@"f(x) = \cases{ 1 & x > 0 \\ 0 & x = 0 \\ -1 & x < 0 } \;\; g(x) = \begin{cases} 1 & x > 0 \\ 0 & x \leq 0 \end{cases}")]
    [DataRow(@"\pmatrix{ a & b \\ c & d }")]
    [DataRow(@"\begin{alignat}{2} a &= b + c &\quad d &= e \\ f &= g &\quad h &= i \end{alignat}")]
    [DataRow(@"\color[HTML]{FF8800}{x}")]
    [DataRow(@"\textcolor{red}{x}")]
    [DataRow(@"\color{red}{x}")]
    [DataRow(@"\textsc{Abc 123}")]
    [DataRow(@"\bm{\theta}")]
    public void WhatTheSamplesWriteIsDrawn(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void AColourReachesTheGlyphsItColours_AndASwitchReachesTheRestOfItsGroup() => UiThread.Run(() =>
    {
        // The ink each glyph chose, in the order drawn — null where it chose none and takes the theme's.
        System.Windows.Media.Color? Ink(string markup, int glyph) =>
            (Marks(markup).Select(one => one.Mark).OfType<Nexaflow.Visuals.Text.Editing.GlyphMark>().ElementAt(glyph).Foreground
                as System.Windows.Media.SolidColorBrush)?.Color;

        Assert.AreEqual(System.Windows.Media.Colors.Red, Ink(@"\textcolor{red}{a} b", 0));
        Assert.IsNull(Ink(@"\textcolor{red}{a} b", 1), "what follows the argument is not coloured");

        Assert.IsNull(Ink(@"{a \color{blue} b c} d", 0), "a switch colours nothing before it");
        Assert.AreEqual(System.Windows.Media.Colors.Blue, Ink(@"{a \color{blue} b c} d", 1));
        Assert.AreEqual(System.Windows.Media.Colors.Blue, Ink(@"{a \color{blue} b c} d", 2), "and everything after it in its group");
        Assert.IsNull(Ink(@"{a \color{blue} b c} d", 3), "and nothing past the group's end");
    });

    [TestMethod]
    public void TheCountAlignatIsWrittenWithIsNotACell() => UiThread.Run(() =>
        Assert.AreEqual(Width(@"\begin{align} a &= b \end{align}"), Width(@"\begin{alignat}{1} a &= b \end{alignat}"), 0.01));

    [TestMethod]
    [DataRow(@"\begin{pmatrix} a_{11} & a_{12} & a_{13} \\ \hdotsfor{3} \\ a_{n1} & a_{n2} & a_{n3} \end{pmatrix} \;\; \begin{pmatrix} b_{11} & b_{12} \\ \hdotsfor[2]{2} \end{pmatrix}")]
    [DataRow(@"\begin{matrix} a & b \\ \hdotsfor{2} \end{matrix}")]
    public void ARowOfDotsIsDrawn(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void ARowOfDotsFillsTheColumnsItStandsAcrossAndWidensNone() => UiThread.Run(() =>
    {
        const string full = @"\begin{matrix} aaa & bbb & ccc \\ \hdotsfor{3} \end{matrix}";

        Assert.AreEqual(Width(@"\begin{matrix} aaa & bbb & ccc \end{matrix}"), Width(full), 0.01, "the table is as wide as its entries");

        var dots = Marks(full).Count(one => one.Mark is Nexaflow.Visuals.Text.Editing.GlyphMark) - 9;
        Assert.IsTrue(dots > 6, $"the dots run the width of three columns, not one ({dots} of them)");
    });

    [TestMethod]
    [DataRow(@"\overset{a}{b}")]
    [DataRow(@"\underset{a}{b}")]
    public void OversetAndUndersetRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void UndersetSetsItsAnnotationAsCloseAsOversetDoes() => UiThread.Run(() =>
    {
        // The gap the annotation is held at is the only thing that differs between the two, and it is asked for in
        // the same unit, so an annotation set below must sit exactly as far from the base as the same annotation set
        // above. (It did not: the under gap was built with the *over* unit — an 18x gap for a value meant as mu.)
        var above = Height(@"\overset{a}{X}") - Height(@"X");
        var below = Depth(@"\underset{a}{X}") - Depth(@"X");
        Assert.AreEqual(above, below, 1e-6);
    });

    [TestMethod]
    public void StackrelIsSpacedAsARelation() => UiThread.Run(() =>
    {
        Assert.AreEqual(Width(@"a \mathrel{\stackrel{f}{\rightarrow}} b"), Width(@"a \stackrel{f}{\rightarrow} b"), 1e-6);
        Assert.IsTrue(Width(@"a \stackrel{f}{\rightarrow} b") > Width(@"a \mathord{\stackrel{f}{\rightarrow}} b"));
    });

    [TestMethod]
    [DataRow(@"\phantom{x}")]
    [DataRow(@"\hphantom{x}")]
    [DataRow(@"\vphantom{x}")]
    public void ThePhantomFamilyRendersAndDrawsNothing(string markup) => UiThread.Run(() =>
    {
        Renders(markup);
        Assert.AreEqual(0, Marks(markup).Count);
    });

    [TestMethod]
    public void SmashKeepsTheWidthAndDropsTheHeight() => UiThread.Run(() =>
    {
        var smashed = Formula(@"\smash{\frac{a}{b}}");
        Assert.AreEqual(Width(@"\frac{a}{b}"), smashed.Width, 1e-6);
        Assert.AreEqual(0.0, smashed.Height);
        Assert.AreEqual(0.0, smashed.Depth);
    });

    [TestMethod]
    [DataRow(@"\mathllap{x}")]
    [DataRow(@"\mathrlap{x}")]
    [DataRow(@"\mathclap{x}")]
    [DataRow(@"\llap{x}")]
    [DataRow(@"\rlap{x}")]
    public void TheLapFamilyDropsTheWidthAndKeepsTheHeight(string markup) => UiThread.Run(() =>
    {
        Renders(markup);
        Assert.AreEqual(0.0, Width(markup));
        Assert.IsTrue(Height(markup) > 0.0);
    });

    [TestMethod]
    [DataRow(@"\overbrace{a+b}")]
    [DataRow(@"\overbrace{a+b}^{n}")]
    [DataRow(@"\overbrace{a+b}^n")]
    [DataRow(@"\underbrace{a+b}")]
    [DataRow(@"\underbrace{a+b}_{n}")]
    [DataRow(@"\underbrace{\overbrace{a+b}^{n}+c}_{m}")]
    public void BracesRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void ABraceTakesTheScriptThatFollowsItAsItsLabel() => UiThread.Run(() =>
    {
        // \overbrace is an operator: the "n" belongs above the brace, centred over it, so it adds height and no
        // width.
        Assert.AreEqual(Width(@"\overbrace{a+b}"), Width(@"\overbrace{a+b}^{n}"), 1e-6);
        Assert.IsTrue(Height(@"\overbrace{a+b}^{n}") > Height(@"\overbrace{a+b}"));
        Assert.AreEqual(Width(@"\underbrace{a+b}"), Width(@"\underbrace{a+b}_{n}"), 1e-6);

        // A script on the other side is not the brace's label, and stays an ordinary script beside it.
        Assert.IsTrue(Width(@"\overbrace{a+b}_{n}") > Width(@"\overbrace{a+b}"));
    });

    [TestMethod]
    public void ABraceWithNoLabelRenders() => UiThread.Run(() =>
        // The delimiter can stand alone, and the script it does not have must not be reached for.
        Assert.IsTrue(Bounds(@"\overbrace{a+b}").Marks > 0));

    [TestMethod]
    [DataRow(@"\overbrace{a+b+c}^{\text{three terms}}")]
    [DataRow(@"\underbrace{d+e}_{\text{two more}}")]
    [DataRow(@"\overbrace{a+b}")]
    public void ABraceIsDrawnAcrossItsBaseNotBesideIt(string markup) => UiThread.Run(() =>
    {
        // A label wider than the base widens the whole construct, and the brace is centred in it. The delimiter is
        // padded to reach that width — with the leftover, not with the width itself, which would push the brace half
        // a width to the right and out of its own box.
        var (width, inkLeft, inkRight, _) = Bounds(markup);

        // A brace overhangs its span a little by design, so the bound is loose; the bug it guards against slid the
        // brace by half a width, which is nowhere near this.
        var tolerance = width * 0.25;
        Assert.IsTrue(inkLeft > -tolerance, $"ink starts at {inkLeft}, left of the box (width {width})");
        Assert.IsTrue(inkRight < width + tolerance, $"ink reaches {inkRight}, past the box width {width}");
    });

    [TestMethod]
    [DataRow(@"\substack{a \\ b}")]
    [DataRow(@"\substack{i < j \\ j < k \\ k < l}")]
    [DataRow(@"\sum_{\substack{i < j \\ j < k}} a_{ij}")]
    public void SubstackRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void SubstackStacksItsLinesInScriptSizeSetSolid() => UiThread.Run(() =>
    {
        Assert.IsTrue(Width(@"\substack{a \\ b}") < Width(@"\begin{matrix}a \\ b\end{matrix}"), "substack lines are script size");
        Assert.IsTrue(TotalHeight(@"\substack{a \\ b}") < TotalHeight(@"\begin{smallmatrix}a \\ b\end{smallmatrix}"),
                      "substack lines should sit closer together than table rows");
    });

    [TestMethod]
    [DataRow(@"\boxed{x}")]
    [DataRow(@"\fbox{x}")]
    [DataRow(@"\boxed{\frac{a}{b}}")]
    public void BoxedRenders(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void AFrameIsWiderAndTallerThanWhatItFrames() => UiThread.Run(() =>
    {
        Assert.IsTrue(Width(@"\boxed{x}") > Width(@"x"));
        Assert.IsTrue(Height(@"\boxed{x}") > Height(@"x"));
    });

    [TestMethod]
    [DataRow(@"\xrightarrow{f}")]
    [DataRow(@"\xleftarrow{f}")]
    [DataRow(@"\xleftrightarrow{f}")]
    [DataRow(@"\xRightarrow{f}")]
    [DataRow(@"\xLeftarrow{f}")]
    [DataRow(@"\xLeftrightarrow{f}")]
    [DataRow(@"\xmapsto{f}")]
    [DataRow(@"\xrightarrow[g]{f}")]
    public void ExtensibleArrowsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void AnExtensibleArrowGrowsToFitItsLabel() => UiThread.Run(() =>
        Assert.IsTrue(Width(@"\xrightarrow{f \circ g \circ h}") > Width(@"\xrightarrow{f}")));

    [TestMethod]
    [DataRow(@"\mathbb{R}")]
    [DataRow(@"\mathbf{x}")]
    [DataRow(@"\mathsf{x}")]
    [DataRow(@"\mathtt{x}")]
    [DataRow(@"\mathfrak{g}")]
    [DataRow(@"\mathscr{L}")]
    [DataRow(@"\textrm{x}")]
    [DataRow(@"\textbf{x}")]
    [DataRow(@"\textit{x}")]
    [DataRow(@"\textsf{x}")]
    [DataRow(@"\texttt{x}")]
    
    public void FontSwitchesRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void ATextFontSwitchKeepsItsSpaces() => UiThread.Run(() =>
        // The argument of a \text* command is text, not maths: the space between the words survives.
        Assert.IsTrue(Width(@"\textbf{a b}") > Width(@"\textbf{ab}")));

    [TestMethod]
    [DataRow(@"\begin{matrix}a & b \\ c & d\end{matrix}")]
    [DataRow(@"\begin{smallmatrix}a & b \\ c & d\end{smallmatrix}")]
    [DataRow(@"\begin{cases}a & x > 0 \\ b & x \leq 0\end{cases}")]
    [DataRow(@"\begin{aligned}a &= b \\ c &= d\end{aligned}")]
    [DataRow(@"\begin{split}a &= b \\ c &= d\end{split}")]
    [DataRow(@"\begin{align*}a &= b \\ c &= d\end{align*}")]
    [DataRow(@"\begin{gather}a \\ b\end{gather}")]
    [DataRow(@"\begin{gather*}a \\ b\end{gather*}")]
    [DataRow(@"\begin{gathered}a \\ b\end{gathered}")]
    public void EnvironmentsRender(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    public void SmallmatrixIsSetInScriptStyle() => UiThread.Run(() =>
        Assert.IsTrue(Width(@"\begin{smallmatrix}a & b\end{smallmatrix}") < Width(@"\begin{matrix}a & b\end{matrix}")));

    [TestMethod]
    [DataRow(@"\implies")]
    [DataRow(@"\impliedby")]
    [DataRow(@"\iff")]
    [DataRow(@"\Longleftarrow")]
    
    
    public void ImplicationArrowsRender(string markup) => UiThread.Run(() => Renders(markup));
}
