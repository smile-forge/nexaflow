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

    [TestMethod]
    [DataRow(@"\textcolor{red}{x}")]
    [DataRow(@"\color{red}{x}")]
    [DataRow(@"\textsc{Abc 123}")]
    [DataRow(@"\bm{\theta}")]
    public void TheCommandsWithNoDrawingYetAreShownAsWritten(string markup) => UiThread.Run(() =>
    {
        // Read, and nothing here draws them yet, so the reader sees the characters they typed with a warning under them
        // rather than nothing at all. Listed so that teaching the builder one fails here, and the row moves to the
        // construct's own test.
        Assert.IsNotNull(Read(markup).Set, $"'{markup}' should still come back as something to show");
        Assert.AreNotEqual(0, Undrawn(markup).Count, $"'{markup}' draws now — move it to a test that says what it draws");
    });
}
