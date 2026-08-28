using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// What a piece of a formula is <em>to</em> the construct holding it.
///
/// <para>
/// The question copying cannot do without. Take the <c>3</c> out of <c>\sqrt[3]{x+1}</c> and you have a
/// 3, and you also have "the degree of a root" — and only the second reading lets pasting it onto
/// something else produce a cube root of that something. Neither geometry nor source offsets can answer
/// it: the 3 and the 1 of <c>\frac{3}{1}</c> are a character each, in the same places, meaning entirely
/// different things.
/// </para>
/// <para>
/// So the answer comes off the parse tree, where it is true whether or not anything was ever drawn. The
/// layout tree holds a reference to that tree and nothing more, which is what keeps it about layout.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class LatexRoleTests
{
    private const double Scale = 16;

    private static (LatexTree Tree, ILayoutNode Node) Piece(string latex, string text)
    {
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout, latex);

        var node = layout.Tree.Root.Ink()
            .FirstOrDefault(n => latex.Substring(n.SourceStart, n.SourceLength) == text);
        Assert.IsNotNull(node, $"no piece reading \"{text}\" in {latex}");
        return (layout.Tree, node);
    }

    [TestMethod]
    public void ARootsDegreeKnowsItIsADegree() => UiThread.Run(() =>
    {
        var (tree, three) = Piece(@"\sqrt[3]{x+1}", "3");

        var role = tree.RoleOf(three);
        Assert.IsNotNull(role, "the 3 belongs to something");
        Assert.AreEqual("degree", role.Value.Role);
    });

    [TestMethod]
    public void TheSameCharacterMeansSomethingElseSomewhereElse() => UiThread.Run(() =>
    {
        // The whole point, in one comparison: identical text, identical kind of node, different meaning —
        // and nothing about where it sits on the page or in the source could tell them apart.
        var (roots, degree) = Piece(@"\sqrt[3]{x+1}", "3");
        var (fraction, numerator) = Piece(@"\frac{3}{1}", "3");

        Assert.AreEqual("degree", roots.RoleOf(degree)?.Role);
        Assert.AreEqual("numerator", fraction.RoleOf(numerator)?.Role);
    });

    [TestMethod]
    public void EachPartOfAConstructNamesItsOwnPlace() => UiThread.Run(() =>
    {
        var (tree, denominator) = Piece(@"\frac{a}{b}", "b");
        Assert.AreEqual("denominator", tree.RoleOf(denominator)?.Role);

        var (scripts, exponent) = Piece(@"y^{7}", "7");
        Assert.AreEqual("superscript", scripts.RoleOf(exponent)?.Role);

        var (subs, index) = Piece(@"y_{7}", "7");
        Assert.AreEqual("subscript", subs.RoleOf(index)?.Role);
    });

    [TestMethod]
    public void TheConstructComesBackWithTheRole() => UiThread.Run(() =>
    {
        // A role is no use without the thing it is a role in — pasting the degree somewhere needs to know
        // it was a root, so it can make a root of what it lands on.
        const string latex = @"\sqrt[3]{x+1}";
        var (tree, three) = Piece(latex, "3");

        var role = tree.RoleOf(three);
        Assert.IsNotNull(role);

        var construct = role.Value.Construct;
        Assert.AreEqual(latex, latex.Substring(construct.SourceStart, construct.SourceLength),
            "the whole root, backslash included — what a paste would have to rebuild");
    });

    [TestMethod]
    public void APieceStandingForNothingHasNoRole() => UiThread.Run(() =>
    {
        // A fraction's bar is drawn for the construct rather than by anything in it, so there is no part
        // for it to be. Asking must give nothing rather than an invented answer.
        var layout = LatexLayout.Build(@"\frac{a}{b}", Scale);
        Assert.IsNotNull(layout);

        var bar = layout.Tree.Root.SelfAndDescendants()
            .FirstOrDefault(n => n.Kind == "HorizontalRule");
        Assert.IsNotNull(bar, "the fraction has a bar");
        Assert.IsNull(layout.Tree.RoleOf(bar), "which is the fraction's own drawing, not a part of it");
    });

    [TestMethod]
    public void WhatCouldNotBeReadHasNoRoleEither() => UiThread.Run(() =>
    {
        // Recovered text was shown, not understood. It stands for no structure, so it plays no part in
        // any — and copying it can only ever yield the characters.
        var layout = LatexLayout.Build(@"x + \nosuchcommand", Scale);
        Assert.IsNotNull(layout);

        var guessed = layout.Tree.Root.Ink().Where(layout.Tree.IsGuesswork).ToList();
        Assert.AreNotEqual(0, guessed.Count, "the unreadable part is there to ask about");

        foreach (var piece in guessed)
            Assert.IsNull(layout.Tree.RoleOf(piece),
                $"\"{layout.Tree.Latex.Substring(piece.SourceStart, piece.SourceLength)}\" was shown, not read, "
                + "so it plays no part in anything");
    });
}
