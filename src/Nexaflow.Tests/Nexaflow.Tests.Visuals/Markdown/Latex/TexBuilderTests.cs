using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexaflow.Markdown.Latex;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Parsers;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// What <see cref="TexFormulaBuilder"/> can set from a reading.
///
/// <para>
/// A construct the builder has no drawing for is set as the characters it was written with and reported, so a
/// formula only comes back empty when nothing in it builds at all. These say that everything the builder claims
/// to know builds, and that what nothing knows still comes back as something to show.
/// </para>
///
/// Needs an STA thread for the fonts. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class TexBuilderTests
{
    private const double Scale = 16;

    /// <summary>What the builder is expected to manage today. It grows as constructs are taught to it.</summary>
    private static readonly string[] Known =
    [
        "a",
        "a+b",
        "a + b",
        "x^2",
        "x_i",
        "x^2_i",
        "x^{2}",
        @"\alpha",
        @"\alpha + \beta",
        "{a}",
        "{a+b}",
        @"\frac{a}{b}",
        @"\frac{a+b}{c}",
        @"\frac{1}{1 + \frac{1}{x}}",
        @"\sqrt{x}",
        @"\sqrt{x+1}",
        @"\frac{\sqrt{a}}{b}",
        "2x^{2} + 3",
        @"\alpha^{\beta}",
        @"\left( a \right)",
        @"\left[ \frac{a}{b} \right]",
        @"\left\{ x \right\}",
        @"\overline{x}",
        @"\underline{x+1}",
        @"\vec{a}",
        @"\dot{q}^{2}",
        @"\tilde{X}(t)",
        @"\sum_{i=0}^{n} i",
        @"\int_{0}^{1} x",
        @"\sum x",
        @"\left( a \right)^{2}",
        @"\left( \frac{a}{b} \right)^{2}",

        // Tables. The rows and cells are nodes of the reading, so the grid is read rather than worked
        // out from where the cells were drawn — which is the whole reason the table gestures wanted this.
        @"\begin{matrix} a & b \\ c & d \end{matrix}",
        @"\begin{pmatrix} a & b \\ c & d \end{pmatrix}",
        @"\begin{bmatrix} \alpha & \beta \\ \gamma & \delta \end{bmatrix}",
        @"\begin{Bmatrix} a \end{Bmatrix}",
        @"\begin{vmatrix} a & b \end{vmatrix}",
        @"\begin{Vmatrix} a & b \end{Vmatrix}",
        @"\begin{smallmatrix} a & b \end{smallmatrix}",
        @"\begin{cases} x & y \\ z & w \end{cases}",
        @"\begin{pmatrix} \frac{a}{b} & \sqrt{c} \\ x^{2} & \alpha \end{pmatrix}",
        @"\begin{matrix} a & b \\ c \end{matrix}",              // ragged: squared off with holes
        @"\begin{matrix} a & \\ & d \end{matrix}",              // cells with nothing written in them
        @"\begin{matrix} a \\ b \\ \end{matrix}",               // a \\ ends its row; it opens no other
        @"\begin{align} a &= b \\ c &= d \end{align}",
        @"\begin{gathered} a \\ b \end{gathered}",
        @"\begin{array}{cc} a & b \\ c & d \end{array}",
        @"\begin{array}{c|c} a & b \end{array}",
        @"\left[ \begin{matrix} a & b \end{matrix} \right]",

        @"\sqrt[3]{x}",
        @"\sqrt[n]{x+1}",

        // A style is a property of the letters, not an atom round them.
        @"\mathrm{abc}",
        @"A\mathrm{abc}B",              // a style nests here; written first in a row it is parked
        @"\mathbf{x} + \mathit{y}",
        @"\frac{\mathrm{d}y}{\mathrm{d}x}",
        @"\mathcal{L}^{2}",

        // Marks. These carry more weight than the rest of this list, because the corpus cannot check
        // them at all: it holds 22,653 formulas writing a prime as `^{\prime}` and not one written as
        // `'`. So this is the only place the two readings are held against each other for a mark, and
        // it is deliberately more than a couple of shapes.
        "f'",
        "f''",
        "f'''",
        @"\alpha'",
        @"\frac{f'}{g'}",
        @"x'_{i}",                      // the subscript lands on the x, not on the prime before it
        @"x''_{i}",
        @"x'''_{i}",
        @"y'^{2}",
        @"y'^{2}_{n}",
        "f'(x)",
        "f'g'",
        "{f'}",                         // braced, so the mark is inside a group of its own
        "{f}'",                         // and braced the other way, so the group is what wears it
        @"\sqrt{f'}",
        @"\sum f'",
        @"\left( f' \right)",
        @"\frac{\alpha''}{\beta'}",
        @"\begin{matrix} f' & g'' \end{matrix}",
        @"\prime",           // the symbol, which is a different thing entirely

        // A script on a construct. These agree everywhere except between delimiters, which is where the
        // decline now sits — it used to sit here, and cost every one of these its coverage for nothing.
        @"\overline{J}^{a}",
        @"\overline{{J}}^{a}",
        @"\underline{x}^{2}",
        @"\overline{f}'",
        @"\frac{f}{g}_{i}",
        @"\frac{f}{g}_{i} h",

        "a~b",               // a tie
        @"\mathrm{~mod~}",   // and a tie inside a style, which is how a paper spaces an operator name
        @"X \mathrm{~mod~} 2",
        @"\mathrm{Im~} z",

        // The empty group: a place for the next thing to attach to, and the tensor-index idiom that
        // half the physics in the corpus is written with.
        "{}",
        @"T^{\alpha}{}_{\alpha}",

        // Carbon-14, and how a prefix is usually written. Not the builder's prefix branch at all: `{}`
        // carries a script like anything else, so these are ordinary suffix scripts on an empty box
        // followed by the base — TeX's own construction, and why the empty group had to come first.
        @"{}^{14}_{6}\mathrm{C}",
        @"{}^{3}He",

        // And the branch itself: a script written after something that cannot carry one belongs to what
        // comes next, space beside it or not. Reviewed 2026-08-28 — identical rendering, ours is the tree.
        @"x~^{2}y",
        @"F_{\rho} ~ ^{\nu} G",
        @"\int C ~ _{\wedge} dT",

        // Something set above or below something else. The roles say which is which, because the order
        // does not: \overset and \underset both write the annotation first.
        @"\stackrel{\rm def}{=}",
        @"\overset{a}{b}",
        @"\underset{a}{b}",
        @"A \stackrel{f}{\longrightarrow} B",

        // And the rest of the commands the table can build from arguments it is handed.
        @"\binom{n}{k}",
        @"\dbinom{n}{k} + \tbinom{a}{b}",
        @"\phantom{x} y",
        @"\overrightarrow{AB}",
        @"\boldsymbol{\alpha}",
        @"\vdots",
        @"\ddots",
        @"\underbrace{a+b}",
        @"\overbrace{x y}",

        // A brace wearing its label — the n belongs to the brace and is set centred beneath it, which
        // the reading has as a script around the whole command until the builder puts them together.
        @"\underbrace{a+b}_{n}",
        @"\overbrace{x+y}^{m}",
        @"\underbrace{1+\cdots+1}_{k}",
        @"\underbrace{a}^{b}",          // labelled on the side it does not label: an ordinary script

        // A script with nothing at all before it. It stands alone, drawn where it was written, on a box
        // of no width — the typesetter's own parser refuses these outright.
        @"^{(4)}R_{\mu}",
        @"{_a b c}",
        @"^{*}F",
        @"\mathrm{\quad ~}",            // a tie beside an asked-for space, inside a style
        @"\fbox{a}",
        @"\mathop{\rm tr}",

        // Commands whose whole effect belongs to a page this formula does not have. They draw nothing,
        // so they make no atom — but the reading keeps them, argument and all.
        @"E = mc^2 \tag{1}",
        @"a = b \nonumber",
        @"x \label{eq:one} + y",
        @"a \not= b",
        @"x \not\in S",
        @"\not\approx",
        @"A^{a}{}_{\mu} X_{a}",
        @"\int_{}^{} x",
        @"R_{ab} = R_{acb}{}^{c}",

        // Space that was asked for rather than typed. TeX's own spacing comes from atom classes and is
        // not written down; these are, so they build like any other command.
        @"a\,b",
        @"a\;b",
        @"a\!b",
        @"a\:b",
        @"a\quad b",
        @"a\qquad b",
        @"a\ b",                        // the control space: asked for, so built
        @"6 4 \ ,",                     // how a paper spaces a formula off from its punctuation

        // Sized delimiters. Not a fence: each one stands on its own, which is why the second of these is
        // good LaTeX and the third — a bracket opened at one size and closed at another — is too.
        @"\big( x \big)",
        @"\bigl( x",
        @"\bigl( x \Biggr]",
        @"\Big\{ a \Big\}",
        @"\biggl\| v \biggr\|",
        @"\big| x \big|",

        // The whole of the fences sample the typesetting baseline covers. It used to decline on the
        // \big family and go to the parser; now it builds, so whether that moved any ink is a question
        // worth asking here rather than inferring from a hash that also counts the containers.
        @"\left( a \right) + \left[ b \right] + \left\{ c \right\} + \left| d \right|
          + \left\langle e \right\rangle + \bigl( h \bigr) + \Bigl[ i \Bigr]
          + \biggl\{ j \biggr\} + \Biggl| k \Biggr|",

        // Macros whose expansion is several atoms rather than one — three dots, a slash laid over an
        // equals. The reader wrote one token and it draws as a little assembly, which is the case that
        // used to be declined: those atoms were parsed from the definition and carry offsets into it.
        @"a \cdots b",
        @"a \ldots b",
        @"x \neq y",
        @"a \longrightarrow b",
        @"\hbar \omega",

        // Switches, which take the rest of the group they stand in rather than an argument. So the
        // scope is a fact about the run, and nothing says where it ends except the closing brace.
        @"{\cal L}",
        @"{\cal L M}",
        @"A {\cal L} B",
        @"{\bf x} + y",
        @"{\it a}",
        @"\displaystyle \sum_{i=0}^{n} i",
        @"{\displaystyle \frac{a}{b}}",
        @"\textstyle \frac{a}{b}",
        @"\frac{{\cal A}}{{\cal B}}",
        @"\begin{matrix} {\bf a} & b \end{matrix}",

        // A symbol wearing a script, between delimiters. What is declined there is a script on something
        // a command *built*, and the difference between the two is worth twelve thousand formulas.
        @"\left( \sum_{i} \right)",
        @"\left( \int_{0}^{1} x \right)",
        @"\left( \alpha^{2} \right)",
    ];

    [TestMethod]
    public void EverythingItClaimsToKnowItCanBuild() => UiThread.Run(() =>
    {
        foreach (var latex in Known)
            Typesetting.Typeset.Renders(latex);
    });

    [TestMethod]
    public void AndEverythingElseItBuildsSomethingFor() => UiThread.Run(() =>
    {
        // A command *nothing* knows has no better rendering to defer to, so this must answer for it: it shows what was
        // written and reports it, which is what a reader needs.
        foreach (var latex in new[] { @"\notacommand{x}",
                                      @"\alhpa + \beta",
                                      @"\bbox[red]{a}",      // nothing knows this one either
                                      @"\hline",           // nor this: it is a rule between rows
                                      @"x + \nosuchthing" })
        {
            Assert.IsNotNull(Typesetting.Typeset.Laid(latex).Tree, $"nothing was laid for {latex}");
            Assert.IsTrue(Typesetting.Typeset.Undrawn(latex).Count + Typesetting.Typeset.Unreadable(latex).Count > 0,
                          $"{latex} was shown as written and not reported");
        }
    });

    /// <summary>
    /// Every macro draws as something, rather than as its own name in plain letters.
    ///
    /// <para>
    /// The hole this exists to close. <c>TexMacroTableTests</c> checks that a macro resolves and that
    /// the source still prints back, and both of those passed for <c>\hbar</c> while it was drawing the
    /// characters <c>\hbar</c> on the page in 4,343 corpus formulas. It resolved to
    /// <c>\bar{}\mspace{-9mu}h</c>, which is right, and nothing could <em>build</em> <c>\mspace</c>,
    /// which was not visible from the parse tree at all.
    /// </para>
    /// <para>
    /// It stayed invisible for a second reason worth remembering: the formula declined and the engine's
    /// own parser drew it instead, so the corpus sweep — which measures what comes out of
    /// <see cref="LatexLayout.Build"/> — reported no change. It was measuring the fallback. The moment
    /// the fallback went, so did the rendering.
    /// </para>
    /// </summary>
    [TestMethod]
    public void EveryMacroDrawsAsSomethingRatherThanAsItsOwnName() => UiThread.Run(() =>
    {
        var unbuilt = new List<string>();

        foreach (var name in TexMacros.All.Keys)
        {
            var layout = LatexBuilder.Lay(name, 16);

            if (layout is null) { unbuilt.Add($"{name} draws nothing at all"); continue; }

            // A warning here is the builder saying it had no drawing for something and set the
            // characters instead — which for a macro means the definition names something this cannot
            // build, and the reader sees the definition rather than the symbol.
            foreach (var trouble in layout.Trouble)
                unbuilt.Add($"{name}: {trouble.Message}");
        }

        Assert.AreEqual(0, unbuilt.Count, string.Join("\n", unbuilt));
    });
}
