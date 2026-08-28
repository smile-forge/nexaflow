using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Brackets, and what the editor makes of them.
///
/// <para>
/// A delimiter is not a part of the thing it delimits — it is not a place content goes, so it names no
/// role, the same as a fraction's bar. But unlike a bar it is not decoration either: a bracket carries
/// meaning only as a pair, and one without its partner cannot be read at all. So the piece before the
/// caret at the end of a bracketed group is the <em>group</em>, and backspace takes it back to its
/// source rather than deleting a closing bracket and leaving nothing that parses.
/// </para>
/// <para>
/// <c>\Bigl[</c> and <c>\Bigr]</c> are the exception that shows the rule: in LaTeX they really are two
/// independent symbols that merely happen to be sized alike, so each is its own thing and is taken as
/// one.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-fence-slots")]
public class BracketTests
{
    // ── A pair is one thing ─────────────────────────────────────────────────

    [TestMethod]
    public void ThePieceBeforeTheCaretIsTheWholeBracketedGroup() => UiThread.Run(() =>
    {
        foreach (var latex in new[] { @"x + \left[ y \right]", @"x + \left( y \right)",
                                      @"x + \left\{ y \right\}", @"x + \left\langle y \right\rangle" })
        {
            var found = Before(latex, latex.Length);
            Assert.IsNotNull(found, latex);
            StringAssert.StartsWith(Text(latex, found), @"\left", $"{latex} — the group, not its closing bracket");
        }
    });

    [TestMethod]
    public void ASizedDelimiterIsItsOwnThing() => UiThread.Run(() =>
    {
        // \Bigl[ and \Bigr] are two independent symbols in LaTeX, not a pair — so each is one thing,
        // and taking one does not orphan another.
        const string latex = @"x + \Bigl[ y \Bigr]";
        var found = Before(latex, latex.Length);

        Assert.IsNotNull(found);
        Assert.AreEqual(@"\Bigr]", Text(latex, found));
    });

    [TestMethod]
    public void ASelectionNeverTakesHalfAPair() => UiThread.Run(() =>
    {
        // A bracket carries meaning only as a pair — one without its partner cannot be read at all —
        // so picking one out has to mean picking out the group. Anything else could be copied or
        // carried somewhere and would arrive as nothing that parses.
        const string latex = @"x + \left[ y \right]";
        var layout = LatexLayout.Build(latex, 16);
        Assert.IsNotNull(layout);

        var closing = layout.Tree.Root.Ink()
            .FirstOrDefault(n => Text(latex, n).StartsWith(@"\right", System.StringComparison.Ordinal));
        Assert.IsNotNull(closing, "the closing bracket is a piece you can point at");

        var owner = layout.Tree.Owning(closing);
        var picked = ContentSelection.Between(layout.Tree.Root, owner, owner);
        var taken = string.Concat(picked.Ranges.Select(r => latex.Substring(r.Start, r.Length)));

        Assert.AreEqual(@"\left[ y \right]", taken, "the whole group, opener included");
    });

    // ── The braket package ──────────────────────────────────────────────────

    [TestMethod]
    public void DiracNotationTypesets() => UiThread.Run(() =>
    {
        foreach (var latex in new[] { @"\braket{0|0}", @"\bra{\psi}", @"\ket{\phi}",
                                      @"\Braket{a|b}", @"\Bra{a}", @"\Ket{b}" })
        {
            var layout = LatexLayout.Build(latex, 16);
            Assert.IsNotNull(layout, latex);
            Assert.AreEqual(0, layout.Tree.Diagnostics.Count, $"{latex} was read without trouble");
            Assert.IsTrue(layout.Tree.Size.Width > 0, $"{latex} drew something");
        }
    });

    [TestMethod]
    public void AnEmptyBraOrKetIsAHoleLikeAnyOther() => UiThread.Run(() =>
    {
        // \bra{} is what someone writes on the way to \bra{\psi}, so it gets the same box as every
        // other unwritten argument — visible, aimable, and reported as unfinished.
        var layout = LatexLayout.Build(@"\bra{}", 16, placeholders: true);

        Assert.IsNotNull(layout);
        Assert.AreEqual(1, layout.Tree.Placeholders.Count, "the bra has a hole in it");
        Assert.AreEqual(1, layout.Tree.Diagnostics.Count, "and says so");
    });

    [TestMethod]
    public void ABraketIsOneThingToTheEditorToo() => UiThread.Run(() =>
    {
        // It is a fence like any other, so it behaves like one: the whole of it is what stands before
        // the caret, not the ⟩ that happens to end it.
        const string latex = @"x + \braket{0|0}";
        var found = Before(latex, latex.Length);

        Assert.IsNotNull(found);
        Assert.AreEqual(@"\braket{0|0}", Text(latex, found));
    });

    private static ILayoutNode? Before(string latex, int caret)
    {
        var layout = LatexLayout.Build(latex, 16);
        Assert.IsNotNull(layout, latex);
        return layout.Tree.SymbolBefore(caret);
    }

    private static string Text(string latex, ILayoutNode node) =>
        latex.Substring(node.SourceStart, node.SourceLength);
}
