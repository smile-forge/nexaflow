using System;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath.Rendering;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Coverage for <see cref="LatexLayout"/> — the half that needs the typesetter: does WpfMath give back
/// a tree that means what the rest of the feature assumes?
///
/// Deliberately small. Everything you can *ask* about a formula's shape is tested in
/// <see cref="LatexTreeTests"/> against a hand-built tree, with no fonts and no desktop. What is
/// left here is the contract with WpfMath itself — the assumptions that would break silently if a
/// version bump changed how it reports source positions.
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class LatexLayoutTests
{
    private const double Scale = 20;
    private const string Fraction = @"\frac{x^2}{2}+\sqrt{y}";

    private static LatexTree Build(string latex)
    {
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout, $"expected {latex} to typeset");
        return layout.Tree;
    }

    [TestMethod]
    public void ACommandOwnsTheBackslashThatIntroducesIt() => UiThread.Run(() =>
    {
        // WpfMath hands the atom the span of the command's *name* — `alpha`, not `\alpha`. The layout
        // claims the backslash back; without that, backspace would un-render α to `alpha`.
        var tree = Build(@"\alpha + \beta");

        var alpha = tree.Root.Ink().Single(n => n.SourceStart == 0);
        Assert.AreEqual(@"\alpha".Length, alpha.SourceLength);
        CollectionAssert.DoesNotContain(tree.CaretStops.ToList(), 1,
            "and the backslash stops being a caret position of its own");
    });

    [TestMethod]
    public void NestedConstructsKeepTheirOwnSpans() => UiThread.Run(() =>
    {
        // The whole feature rests on this: an exponent and a numerator have to be separately addressable,
        // or there is no such thing as "select the exponent".
        var ink = Build(Fraction).Root.Ink().ToList();

        Assert.IsNotNull(ink.SingleOrDefault(n => n.SourceStart == 6 && n.SourceLength == 1),
            "the numerator's x");
        Assert.IsNotNull(ink.SingleOrDefault(n => n.SourceStart == 8 && n.SourceLength == 1),
            "the exponent's 2");
        Assert.IsNotNull(ink.SingleOrDefault(n => n.SourceStart == 11 && n.SourceLength == 1),
            "the denominator's 2");
    });

    [TestMethod]
    public void TheTreeNestsTheWayTheFormulaReads() => UiThread.Run(() =>
    {
        // Parentage is what the whole rework rests on: it is kept from the render walk rather than
        // guessed afterwards by comparing rectangles, which is how a numerator ever came to be treated as
        // a sibling of the fraction that holds it.
        var tree = Build(Fraction);
        var numerator = tree.Root.Ink().Single(n => n.SourceStart == 6 && n.SourceLength == 1);
        var denominator = tree.Root.Ink().Single(n => n.SourceStart == 11);

        var shared = numerator.Ancestors().First(a => a.Ancestors().Contains(tree.Root) || a == tree.Root);
        Assert.IsNotNull(shared);
        Assert.IsTrue(numerator.Ancestors().Any(a => a.SourceStart == 0 && a.SourceLength == 13),
            "the x sits inside the fraction");
        Assert.IsTrue(denominator.Ancestors().Any(a => a.SourceStart == 0 && a.SourceLength == 13),
            "and so does the 2 below the bar");
        Assert.IsTrue(numerator.Ancestors().Any(a => a.SourceStart == 6 && a.SourceLength == 3),
            "with the script as a level of its own between them");
    });

    [TestMethod]
    public void EachPartOfARootNamesADifferentPartOfTheSource() => UiThread.Run(() =>
    {
        // The shape this rework exists for. A root lays out as a degree, a sign and its contents: the node
        // holding all three names the whole `\sqrt[3]{x+1}`, the degree names the 3, the contents name the
        // x+1, and the sign names nothing — the typesetter hands it the whole span, and it is taken back
        // off it, because the node above already carries that. A link that two nodes share is one that has
        // to be interpreted, and every version of that interpretation has been wrong somewhere.
        const string latex = @"\sqrt[3]{x+1}";
        var tree = Build(latex);
        var named = tree.Root.SelfAndDescendants().Where(n => n.SourceLength > 0).ToList();

        var repeated = named
            .Where(n => n.Ancestors().Any(a => a.SourceStart == n.SourceStart && a.SourceLength == n.SourceLength))
            .ToList();
        Assert.AreEqual(0, repeated.Count,
            "layout repeating a name its own ancestor carries: " + string.Join("; ", repeated));

        Assert.IsTrue(named.Any(n => n.SourceStart == 0 && n.SourceLength == latex.Length), "the root as a whole");
        Assert.IsTrue(tree.Root.Ink().Any(n => latex.Substring(n.SourceStart, n.SourceLength) == "3"), "its degree");
        Assert.IsTrue(tree.Root.Ink().Any(n => latex.Substring(n.SourceStart, n.SourceLength) == "x"), "its contents");
    });

    [TestMethod]
    public void ScriptsAreDrawnRaisedAndSmall() => UiThread.Run(() =>
    {
        // The caret's shape is taken straight from these boxes, so if the typesetter ever stopped
        // distinguishing them the caret would silently go uniform.
        var ink = Build(Fraction).Root.Ink().ToList();
        var exponent = ink.Single(n => n.SourceStart == 8);
        var denominator = ink.Single(n => n.SourceStart == 11);

        Assert.IsTrue(exponent.Bounds.Height < denominator.Bounds.Height);
        Assert.IsTrue(exponent.Bounds.Top < denominator.Bounds.Top);
    });

    [TestMethod]
    public void EveryPieceIsInsideTheReportedSize() => UiThread.Run(() =>
    {
        // Boxes can be laid out above or left of the origin; the layout normalises them, and a caret
        // drawn from a negative coordinate would land outside the control.
        var layout = LatexLayout.Build(Fraction, Scale);
        Assert.IsNotNull(layout);

        foreach (var node in layout.Tree.Root.SelfAndDescendants().Where(n => n.SourceLength > 0))
        {
            Assert.IsTrue(node.Bounds.X >= -0.01 && node.Bounds.Y >= -0.01,
                $"{node} sits outside the control");
            Assert.IsTrue(node.Bounds.Right <= layout.Size.Width + 0.01
                          && node.Bounds.Bottom <= layout.Size.Height + 0.01,
                $"{node} overflows {layout.Size}");
        }
    });

    [TestMethod]
    public void SelectingInsideARootStaysInsideIt() => UiThread.Run(() =>
    {
        // Reported from the app: nothing inside a root could be selected. WpfMath draws the radical sign
        // as one glyph carrying the span of the whole `\sqrt{x+1}`, so a drag inside the root must not be
        // read as having crossed the sign.
        const string root = @"\sqrt{x+1}";
        var (start, length) = Build(root).SnapRange(6, 1);

        Assert.AreEqual("x", root.Substring(start, length));
    });

    [TestMethod]
    public void SpacingMacrosDoNotBecomeSelectable() => UiThread.Run(() =>
    {
        // Reported from the app: this line kept selecting itself entirely. WpfMath gives a `\;`'s boxes
        // source spans into the *macro's own text*, and read as offsets into the formula those land
        // anywhere at all — so a drag jumped between unrelated stretches. Such a box now stays in the tree
        // as part of its parent's layout, naming no source of its own.
        const string trig = @"\sin x \;\; \cos x \;\; \tan x";
        var tree = Build(trig);

        Assert.IsTrue(tree.Root.SelfAndDescendants().All(n => n.SourceEnd() <= trig.Length),
            "a span reaching past the end of the formula came from somewhere else entirely");

        var (start, length) = tree.SnapRange(5, 1);
        Assert.AreEqual("x", trig.Substring(start, length),
            "selecting one argument must not swallow the line");
    });

    [TestMethod]
    public void AMatrixReportsItsCellsAsAGrid() => UiThread.Run(() =>
    {
        // Three rows, three columns, each cell its own glyph — what canvas-style selection needs.
        var tree = Build(@"\begin{matrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{matrix}");
        var cells = tree.Root.Ink().Where(n => n.SourceLength == 1).ToList();

        Assert.AreEqual(9, cells.Count, "nine cells");
        Assert.AreEqual(3, cells.Select(c => Math.Round(c.Bounds.Y)).Distinct().Count(), "in three rows");
        Assert.AreEqual(3, cells.Select(c => Math.Round(c.Bounds.X)).Distinct().Count(), "and three columns");
    });

    /// <summary>
    /// Formulas from the sample pages that between them exercise every construct that has, at some point,
    /// reported a span it had no business reporting.
    /// </summary>
    private static readonly string[] Corpus =
    [
        @"\int_0^1 x^2 \, dx \;\; \oint_C \vec{F} \cdot d\vec{r} \;\; \iint_D f \, dA \;\; \iiiint \;\; \oiiint",
        @"\lim_{x \to \infty} \frac{1}{x} = 0 \;\; \limsup_{n} a_n \;\; \sup S \;\; \max_i a_i \;\; \min_i a_i",
        @"\sin x \;\; \cos x \;\; \tan x \;\; \arcsin x \;\; \sinh x \;\; \coth x",
        @"\begin{matrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{matrix}",
        @"\sqrt{x+1} + \sqrt[3]{y} + \frac{\alpha^2}{\beta_j}",
    ];

    [TestMethod]
    public void EveryNamedOperatorIsAddressable() => UiThread.Run(() =>
    {
        // Reported from the app: \lim, \sup, \max and the integrals could not be selected at all. They are
        // predefined formulas, parsed from their own definition text, so their atoms carried offsets into
        // *that* string and were discarded as belonging to another source.
        foreach (var (latex, command) in new[]
                 {
                     (@"\lim_{n} a_n", @"\lim"), (@"\sup S", @"\sup"), (@"\max_i a_i", @"\max"),
                     (@"\sin x", @"\sin"), (@"\iiiint", @"\iiiint"), (@"x + \iiiint", @"\iiiint"),
                 })
        {
            var tree = Build(latex);
            var at = latex.IndexOf(command, StringComparison.Ordinal);

            Assert.IsTrue(
                tree.Root.SelfAndDescendants().Any(n => n.SourceStart == at && n.SourceLength >= command.Length),
                $"{command} is not addressable in {latex}");
        }
    });

    [TestMethod]
    public void EveryTermOnALineIsSelectableOnItsOwn() => UiThread.Run(() =>
    {
        // Reported from the app, on the sample pages' own lines: whole terms could not be selected —
        // \pmod{n} in the middle of the modular-arithmetic line, and a press in the gap between two trig
        // terms selected from the start of the line instead.
        foreach (var (latex, term) in new[]
                 {
                     (@"n \bmod m \;\; a \equiv b \pmod{n} \;\; x \mod y", @"\pmod{n}"),
                     (@"\lim_{x} \frac{1}{x} \;\; \sup S \;\; \inf S", @"\inf"),
                     (@"\sin x \;\; \cos x \;\; \tan x", @"\cos"),
                 })
        {
            var tree = Build(latex);
            var at = latex.IndexOf(term, StringComparison.Ordinal);

            var (start, length) = tree.SnapRange(at, term.Length);
            StringAssert.StartsWith(latex.Substring(start, length), term,
                $"selecting {term} in {latex} came back as something else");
        }
    });

    [TestMethod]
    public void ASelectionIsAlwaysASliceYouCouldCutOut() => UiThread.Run(() =>
    {
        // Dragging from a fraction's numerator to its denominator crosses `}{`, and the raw offsets give
        // back `1}{x` — braces closing something the selection never opened. Promotion turns it into the
        // fraction instead, because every piece of the fraction's ink is in the drag.
        const string latex = @"\lim_{x} \frac{1}{x} = 0";
        var tree = Build(latex);

        var numerator = latex.IndexOf("1", StringComparison.Ordinal);
        var denominator = latex.IndexOf("}{x", StringComparison.Ordinal) + 2;
        var (start, length) = tree.SnapRange(numerator, denominator - numerator + 1);

        Assert.AreEqual(@"\frac{1}{x}", latex.Substring(start, length));
    });

    private static XamlMath.TexEnvironment Environment() =>
        WpfTeXEnvironment.Create(scale: Scale);

    [TestMethod]
    public void NothingToTypesetIsNoLayout() => UiThread.Run(() =>
        Assert.IsNull(LatexLayout.Build("", Scale)));

    [TestMethod]
    public void WhatWasShownRatherThanReadIsMarkedAsSuch() => UiThread.Run(() =>
    {
        // Low confidence, per node rather than per formula: the recovered characters are shown, so they
        // must be pointable, but they stand for no structure and nothing should promote or copy them as
        // though they did.
        var layout = LatexLayout.Build(@"x + \nosuchcommand", Scale);
        Assert.IsNotNull(layout);

        var guessed = layout.Tree.Root.Ink().Where(layout.Tree.IsGuesswork).ToList();
        Assert.AreNotEqual(0, guessed.Count, "the unreadable part is in the tree and marked");

        var sound = layout.Tree.Root.Ink().Where(n => !layout.Tree.IsGuesswork(n)).ToList();
        Assert.AreNotEqual(0, sound.Count, "and the rest of the formula is not");
    });

    // ── A stretch shown as written ──────────────────────────────────────────

    [TestMethod]
    [CoversNode("latex-shown-as-written")]
    public void ShowingAStretchAsWrittenDoesNotMoveWhatComesBeforeIt() => UiThread.Run(() =>
    {
        // The point of un-rendering one construct is that only that construct changes. Every offset is
        // the source's own either way, so the parts either side keep both their spans and their places.
        const string latex = @"x+\frac{a}{b}+y";
        var at = latex.IndexOf(@"\frac", StringComparison.Ordinal);

        var typeset = LatexLayout.Build(latex, Scale);
        var writing = LatexLayout.Build(latex, Scale, shownAsWritten: new LatexRawZone(at, latex.LastIndexOf('+')));
        Assert.IsNotNull(typeset);
        Assert.IsNotNull(writing);

        var before = typeset.Tree.RangeRects(0, at);
        var stillBefore = writing.Tree.RangeRects(0, at);
        Assert.AreNotEqual(0, before.Count);
        Assert.AreEqual(before.Max(r => r.Right), stillBefore.Max(r => r.Right), 0.5,
            "the x+ in front of it has not moved");
    });
}
