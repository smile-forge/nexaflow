using System.Linq;
using Nexaflow.Tests.Fixtures;

using static Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting.Typeset;

namespace Nexaflow.Tests.Visuals.Markdown.Latex.Typesetting;

/// <summary>
/// What becomes of markup that cannot be taken at face value.
/// <para>
/// Every case here was once an exception that abandoned the formula and left the reader looking at nothing. Nothing
/// throws now: a stretch that cannot be read is carried through and marked, and what is shown is the characters that
/// were typed with a squiggle under them.
/// </para>
/// <para>
/// It comes back in one of two ways, and which is not a detail: a command nobody has heard of was never read at all,
/// and one that was read but has no drawing is set as its own characters. The reader is told which by the colour of
/// the squiggle, so these say which too.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class UnreadableMarkupTests
{
    // ── read, and nothing to draw ────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\left x\right)")]         // x is not a delimiter
    [DataRow(@"\sum_ ")]                 // no subscript
    [DataRow(@"\color{red}")]            // a colour, and nothing to colour with it
    public void WhatCannotBeDrawnIsSetAsTheCharactersWritten(string markup) => UiThread.Run(() =>
    {
        Assert.AreNotEqual(0, Undrawn(markup).Count);
        Assert.AreEqual(0, Unreadable(markup).Count);
    });

    // ── begun and not finished ───────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\sqrt", @"\sqrt has no radicand")]
    [DataRow(@"\frac{}", @"\frac has no denominator")]
    [DataRow(@"\binom{}", @"\binom has no denominator")]
    [DataRow(@"\color", @"\color has no argument")]
    [DataRow(@"\left{", "this { is never closed")]              // \left takes a delimiter; { opens a group
    [DataRow(@"\left{2+2\right\}", "this { is never closed")]    // and nothing ever closes it
    [DataRow(@"\sqrt{x^2+1", "this { is never closed")]
    [DataRow(@"x^{2", "this { is never closed")]
    public void AConstructBegunAndNotFinishedSaysWhichPartIsMissing(string markup, string reason) => UiThread.Run(() =>
        // Read is not the same as finished. The reader recovers — a group runs to the end of what there is, a command
        // takes the arguments that are there — so the formula still prints back exactly and still draws. The only
        // trace is what is absent, so the reading has to say so itself.
        CollectionAssert.Contains(Reasons(markup).ToList(), reason));

    // ── never read at all ────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\left\x\right)", @"\x")]
    [DataRow(@"\left\", @"\")]
    public void ACommandNobodyHasHeardOfIsMarkedAsUnread(string markup, string unknown) => UiThread.Run(() =>
        // The other colour. There is no drawing to decline because there is no command: the name itself is what
        // could not be made sense of, and the mark goes on exactly that.
        CollectionAssert.AreEqual(new[] { unknown }, Unreadable(markup).ToList()));

    // ── colours ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(@"\color [nonexistent123] {red} x")]
    [DataRow(@"\colorbox [nonexistent123] {red} x")]
    [DataRow(@"\color {reddit} x")]
    [DataRow(@"\colorbox {reddit} x")]
    [DataRow(@"\color [gray] {x} x")]
    [DataRow(@"\color [gray] {1.01} x")]
    [DataRow(@"\color [argb] {2, 0.5, 0.5, 0.5} x")]
    [DataRow(@"\color [argb] {x, 0.5, 0.5, 0.5} x")]
    [DataRow(@"\color [ARGB] {256, 128, 128, 128} x")]
    [DataRow(@"\color [ARGB] {x, 128, 128, 128} x")]
    [DataRow(@"\color [cmyk] {2, 0.5, 0.5, 0.5, 0.1} x")]
    [DataRow(@"\color [cmyk] {x, 0.5, 0.5, 0.5, 0.1} x")]
    [DataRow(@"\color [HTML] {wwwwwwww} x")]
    public void AColourThatCannotBeReadCostsTheCommandAndNotTheFormula(string markup) => UiThread.Run(() =>
    {
        // A colour model nobody has heard of, a colour nobody has heard of, and numbers outside what the model takes.
        // All the same answer: the command is set as its own characters and what it was going to colour is still set
        // as maths.
        var marked = Undrawn(markup);
        Assert.AreNotEqual(0, marked.Count);
        foreach (var stretch in marked)
            StringAssert.StartsWith(stretch, @"\color");
    });
}
