using System;
using System.Linq;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// Constructs as a palette key writes them: the brackets, the stack, the words, with nothing typed in them yet.
/// <para>
/// Every one is TeX that TeX sets, and every one is what the reader is looking at between pressing the key and typing
/// what goes inside — so each has to set as itself, and not fall back to the characters it was written with.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class UnwrittenConstructsTests
{
    [TestMethod]
    [DataRow(@"\left( \right)")]
    [DataRow(@"\left(\right)")]
    [DataRow(@"\left\langle \right\rangle")]
    [DataRow(@"\left. \right|_{a}")]
    public void AFenceWithNothingInItIsStillItsDelimiters(string markup) => UiThread.Run(() =>
    {
        Renders(markup);
        Assert.IsTrue(Width(markup) > 0, $"'{markup}' drew no delimiter at all");
    });

    [TestMethod]
    [DataRow("'")]
    [DataRow("''")]
    [DataRow("'^2")]
    public void APrimeWithNothingBeforeItIsAPrimeOnAnEmptyBase(string markup) => UiThread.Run(() =>
    {
        // TeX reads ' as ^{\prime}, so with nothing before it the prime stands on an empty base — exactly as {}' does.
        Renders(markup);
        Assert.AreEqual(Width("{}" + markup), Width(markup), 1e-9, $"'{markup}' is not set as {{}}{markup} is");
    });

    [TestMethod]
    [DataRow(@"\substack{}")]
    [DataRow(@"\sum_{\substack{}} x")]
    [DataRow(@"\begin{pmatrix}\end{pmatrix}")]
    public void AGridWithNoLineWrittenInItIsOneEmptyCell(string markup) => UiThread.Run(() => Renders(markup));

    [TestMethod]
    [CoversNode("latex-placeholders")]
    [DataRow(@"\substack{}")]
    [DataRow(@"x + \text{}")]
    public void WhatIsStillToBeWrittenInAStackOrInWordsIsAHole(string markup) => UiThread.Run(() =>
    {
        var laid = Laying.Formula(markup, 20, placeholders: true);

        Assert.IsFalse(laid.ShowsSource, $"'{markup}' fell back to the characters it was written with");
        Assert.AreEqual(1, laid.Holes.Count, $"'{markup}' should show one hole where the writing goes");
    });

    [TestMethod]
    [CoversNode("latex-placeholders")]
    [DataRow(@"\frac{}{x}")]
    [DataRow(@"\tfrac{}{x}")]
    [DataRow(@"x^{}")]
    [DataRow(@"\sqrt[]{x}")]
    public void AHoleIsAnOutlineAtEverySize(string markup) => UiThread.Run(() =>
    {
        // Four hairlines, in a numerator as much as in a script: a line as thick as the box is tall fills it, and a
        // filled box reads as something written.
        var hole = Laying.Formula(markup, 20, placeholders: true).Holes.Single();
        var sides = hole.Marks.ToArray().OfType<RuleMark>().ToList();
        var across = Math.Min(hole.Bounds.Width, hole.Bounds.Height);

        Assert.AreEqual(4, sides.Count, $"the hole in '{markup}' is not four sides");
        foreach (var side in sides)
            Assert.IsTrue(Math.Min(side.Bounds.Width, side.Bounds.Height) < across / 4,
                $"a side of the hole in '{markup}' is {Math.Min(side.Bounds.Width, side.Bounds.Height)} thick across a box {across} wide");
    });
}
