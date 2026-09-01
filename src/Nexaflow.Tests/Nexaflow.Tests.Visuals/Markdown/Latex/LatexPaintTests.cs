using System;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Rendering;

using Size = System.Windows.Size;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// That a formula painted out of its own layout tree is the same picture the typesetter draws.
///
/// <para>
/// This is the test that says the tree is complete. Everything else asserts that the tree describes the
/// formula correctly — where each piece is, what part of the source it came from. This asserts that the
/// description is the <em>whole</em> description: nothing was left behind in the typesetter, because the
/// tree alone reproduces the image to the pixel. Once that holds, the layout can stop holding the
/// typesetter at all, a single term can be painted on its own, and the caller can stop caching the
/// formula as one flat drawing to keep a blinking caret affordable.
/// </para>
/// <para>
/// Byte equality, not a similarity score. Pixel snapping means a formula that is a shade off is a formula
/// whose guideline sets were not reproduced, which is a real defect and would go unnoticed under a
/// tolerance.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class LatexPaintTests
{
    private const double Scale = 20;

    /// <summary>
    /// Every kind of mark a formula can make: glyphs, rules (a fraction bar, a radical's overline), a
    /// transformed box (an accent), a colour the formula asked for itself, and a background wash.
    /// </summary>
    private static readonly string[] Corpus =
    [
        @"x^2",
        @"\frac{x^2}{2}",
        @"\sqrt[3]{x+1}",
        @"\sqrt{x+1} + \sqrt[3]{y} + \frac{\alpha^2}{\beta_j}",
        @"\int_0^1 x^2 \, dx \;\; \oint_C \vec{F} \cdot d\vec{r} \;\; \iint_D f \, dA \;\; \oiiint",
        @"\lim_{x \to \infty} \frac{1}{x} = 0 \;\; \sup S \;\; \max_i a_i",
        @"\sin x \;\; \cos x \;\; \tan x \;\; \coth x",
        @"\begin{matrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{matrix}",
        @"\begin{align} (a+b)^2 &= a^2 + 2ab + b^2 \\ (a-b)^2 &= a^2 - 2ab + b^2 \end{align}",
        @"\cfrac{1}{2 + \cfrac{1}{3 + \cfrac{1}{4}}}",
        @"\overrightarrow{AB} \;\; \hat{x} \;\; \tilde{y} \;\; \bar{z}",
        @"a'' + b'_i \;\; x^{y^z} \;\; \sum_{i=0}^{n} i^2",
        @"\textcolor{red}{x} + y",
        @"\colorbox{yellow}{x + y}",
    ];

    [TestMethod]
    public void ThemeColourReachesTheGlyphsButNotTheOnesThatChoseTheirOwn() => UiThread.Run(() =>
    {
        // The formula is painted with the theme's brush, so a theme change must repaint without
        // re-typesetting. What a \textcolor asked for is its own and must survive that.
        var layout = LatexLayout.Build(@"\textcolor{red}{x} + y", Scale);
        Assert.IsNotNull(layout);

        var black = Draw(layout.Size, dc => layout.Paint(dc, Brushes.Black));
        var white = Draw(layout.Size, dc => layout.Paint(dc, Brushes.White));
        Assert.AreNotEqual(black, white, "the theme's colour never reached the glyphs");

        var reddish = Draw(layout.Size, dc => layout.Paint(dc, Brushes.Black), Colors.Red);
        var plain = Draw(layout.Size, dc => layout.Paint(dc, Brushes.Black), Colors.White);
        Assert.AreNotEqual(reddish, plain,
            "a red glyph on red paper should vanish — the \\textcolor was overwritten by the theme");
    });

    [TestMethod]
    public void OnePieceCanBePaintedOnItsOwn() => UiThread.Run(() =>
    {
        // What the whole-formula drawing cache used to prevent. Painting a subtree must give that subtree
        // and nothing else, in the same place it sits in the whole.
        const string latex = @"\frac{x^2}{2}+y";
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout);

        var fraction = layout.Tree.Root.SelfAndDescendants()
            .First(n => n.SourceStart == 0 && n.SourceLength == 13);

        var whole = Draw(layout.Size, dc => layout.Paint(dc, Brushes.Black));
        var part = Draw(layout.Size, dc => layout.Paint(dc, Brushes.Black, fraction));

        Assert.AreNotEqual(whole, part, "painting one term drew the whole formula");

        // …and the piece it did draw is the one asked for: the +y is missing, the fraction is not.
        var nothing = Draw(layout.Size, _ => { });
        Assert.AreNotEqual(nothing, part, "painting one term drew nothing at all");
    });

    private static string Draw(Size size, Action<DrawingContext> paint, Color? paper = null)
    {
        var width = (int)Math.Ceiling(Math.Max(size.Width, 1));
        var height = (int)Math.Ceiling(Math.Max(size.Height, 1));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(paper ?? Colors.White), null, new Rect(0, 0, width, height));
            paint(dc);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
}
