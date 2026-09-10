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

    // ── The braket package ──────────────────────────────────────────────────

    [TestMethod]
    public void DiracNotationTypesets() => UiThread.Run(() =>
    {
        foreach (var latex in new[] { @"\braket{0|0}", @"\bra{\psi}", @"\ket{\phi}",
                                      @"\Braket{a|b}", @"\Bra{a}", @"\Ket{b}" })
        {
            var layout = LatexBuilder.Build(latex, 16);
            Assert.IsNotNull(layout, latex);
            Assert.AreEqual(0, layout.Trouble.Count, $"{latex} was read without trouble");
            Assert.IsTrue(layout.Size.Width > 0, $"{latex} drew something");
        }
    });

    [TestMethod]
    public void AnEmptyBraOrKetIsAHoleLikeAnyOther() => UiThread.Run(() =>
    {
        // \bra{} is what someone writes on the way to \bra{\psi}, so it gets the same box as every
        // other unwritten argument — visible, aimable, and reported as unfinished.
        var layout = Formula.Lay(@"\bra{}", 16, placeholders: true);

        Assert.IsNotNull(layout);
        Assert.AreEqual(1, layout.Holes.Count, "the bra has a hole in it");
        Assert.AreEqual(1, layout.Trouble.Count, "and says so");
    });

    private static string Text(string latex, Piece node) =>
        latex.Substring(node.Sits().Start, node.Sits().Length);
}
