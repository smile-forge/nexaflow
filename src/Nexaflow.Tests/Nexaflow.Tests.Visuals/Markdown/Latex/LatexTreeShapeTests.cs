using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

using Point = System.Windows.Point;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// That the real typesetter yields the shape the rest of the feature assumes, on the lines that have
/// actually broken.
///
/// <para>
/// This replaces a sweep. The same defect kept arriving wearing different clothes — "it selects the whole
/// line", "it jumps to the start", "it flickers as I drag over an arrow" — and the test that caught them
/// pressed at every point of a grid across each rendered line and checked the caret landed nearby:
/// thousands of samples, a tolerance, and a rule of its own that was wrong twice before it settled. Every
/// one of those reports was the pointer resolving to a node somewhere else, and with the tree that is a
/// property of two nodes rather than of a pixel. So it is asserted of the nodes, once each, exactly.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-selection")]
public class LatexTreeShapeTests
{
    private const double Scale = 20;

    /// <summary>Lines from the sample maths pages, chosen for the constructs that have actually broken.</summary>
    private static readonly (string Latex, string What)[] Lines =
    [
        (@"\int_0^1 x^2 \, dx \;\; \oint_C \vec{F} \cdot d\vec{r} \;\; \iint_D f \, dA \;\; \oiiint",
            "integrals and accented letters"),
        (@"\begin{align} (a+b)^2 &= a^2 + 2ab + b^2 \\ (a-b)^2 &= a^2 - 2ab + b^2 \end{align}",
            "an aligned block, whose rows stack"),
        (@"\begin{matrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{matrix}", "a matrix"),
        (@"\cfrac{1}{2 + \cfrac{1}{3 + \cfrac{1}{4}}}", "nested continued fractions"),
        (@"\sin x \;\; \cos x \;\; \tan x \;\; \cot x", "operator names separated by spacing"),
        (@"n \bmod m \;\; a \equiv b \pmod{n} \;\; x \mod y", "commands that build their own body"),
        (@"\lim_{x \to \infty} \frac{1}{x} = 0 \;\; \sup S \;\; \max_i a_i", "limits with subscripts"),
    ];

    private static LatexTree Build(string latex, string what)
    {
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout, $"expected {what} to typeset: {latex}");
        return layout.Tree;
    }

    [TestMethod]
    public void NoPieceOfLayoutRepeatsAName() => UiThread.Run(() =>
    {
        // The invariant the whole map rests on: each piece of layout names a different part of the source.
        // Where two would name the same — a radical's sign and the node holding the whole root — the inner
        // one gives its name up, because a link two nodes share is a link that has to be interpreted.
        foreach (var (latex, what) in Lines)
        {
            var repeated = Build(latex, what).Root.SelfAndDescendants()
                .Where(n => n.Sits().Length > 0)
                .Where(n => n.Ancestors().Any(a => a.Sits().Start == n.Sits().Start && a.Sits().Length == n.Sits().Length))
                .ToList();

            Assert.AreEqual(0, repeated.Count,
                $"in {what}, layout repeating a name its own ancestor carries: {string.Join("; ", repeated)}");
        }
    });

    [TestMethod]
    public void ANameAlwaysCoversTheNamesInsideIt() => UiThread.Run(() =>
    {
        // Nesting in the layout has to mean nesting in the source, or promotion has nothing to stand on:
        // a selection that grew to a node would come back as a range not containing what was selected.
        // The typesetter breaks this whenever it stamps a construct's span before reading the construct's
        // argument — `\vec{F}` named the four characters of `\vec` while drawing all seven.
        foreach (var (latex, what) in Lines)
        {
            var tree = Build(latex, what);

            foreach (var node in tree.Root.SelfAndDescendants().Where(n => n.Sits().Length > 0))
                foreach (var inside in node.Children.SelectMany(c => c.SelfAndDescendants()).Where(n => n.Sits().Length > 0))
                    Assert.IsTrue(
                        inside.Sits().Start >= node.Sits().Start && inside.Sits().End <= node.Sits().End,
                        $"in {what}, {inside} sits inside {node} but names source outside it — {latex}");
        }
    });

    [TestMethod]
    public void PressingOnSomethingResolvesToThatThing() => UiThread.Run(() =>
    {
        // "It selects the whole line", "it reads the end of the line", "it jumps to the start" were all
        // this: a press resolving to a node other than the one under it. Descent makes it structural —
        // the answer is a node containing the point — so it is asserted once per node instead of sampled.
        foreach (var (latex, what) in Lines)
        {
            var tree = Build(latex, what);

            foreach (var node in tree.Root.Ink())
            {
                var centre = new Point(
                    node.Bounds.X + node.Bounds.Width / 2,
                    node.Bounds.Y + node.Bounds.Height / 2);

                var offset = tree.OffsetAt(centre);
                Assert.IsTrue(offset == node.Sits().Start || offset == node.Sits().End,
                    $"in {what}, pressing the middle of {node} reported offset {offset}, "
                    + $"which is neither of its own edges — {latex}");
            }
        }
    });

    [TestMethod]
    public void SelectingOnePieceNeverTakesTheLine() => UiThread.Run(() =>
    {
        // The symptom as it is met: a small drag flickering out to highlight the entire line. A selection
        // grows only to a node whose every piece is covered, so selecting one piece can only reach the
        // whole line when the line is that one piece.
        foreach (var (latex, what) in Lines)
        {
            var tree = Build(latex, what);

            foreach (var node in tree.Root.Ink())
            {
                var (start, length) = tree.SnapRange(node.Sits().Start, node.Sits().Length);

                Assert.IsFalse(start <= 0 && length >= latex.Length,
                    $"in {what}, selecting {node} took the whole line — {latex}");
                Assert.IsTrue(start <= node.Sits().Start && start + length >= node.Sits().End,
                    $"in {what}, selecting {node} came back as {start}+{length}, which does not contain it — {latex}");
            }
        }
    });

    [TestMethod]
    public void AMatrixIsRowsOfCells() => UiThread.Run(() =>
    {
        // Canvas-style selection reads rows and columns off the tree rather than clustering rectangles
        // into bands, so the tree has to actually have them.
        var tree = Build(@"\begin{matrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{matrix}", "a matrix");

        Assert.AreEqual(9, tree.Root.Ink().Count(), "nine cells");

        var rows = tree.Root.Rows();
        Assert.AreEqual(3, rows.Count, "in three rows of the tree, not three bands of a picture");
        foreach (var row in rows)
            Assert.AreEqual(3, row.SelectMany(n => n.Ink()).Count(), "each holding three cells");
    });

    [TestMethod]
    public void ARealMatrixSelectsLikeASheet() => UiThread.Run(() =>
    {
        // Reported from the app: "if you highlight down it should select down, if you highlight across it
        // should select across". The rules are proved over hand-built grids in ContentSelectionTests; this
        // is the part only the typesetter can answer — that a matrix really does come out of it as rows of
        // cells, so those rules have something to work on.
        const string latex = @"\begin{matrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{matrix}";
        var tree = Build(latex, "a matrix");
        ILayoutNode Cell(string digit) => tree.Root.Ink().Single(n => Text(tree, n) == digit);

        var column = ContentSelection.Between(tree.Root, Cell("2"), Cell("8"));
        Assert.AreEqual(3, column.Ranges.Count, "down the middle column is three cells, three ranges");
        CollectionAssert.AreEqual(
            new[] { "2", "5", "8" },
            column.Ranges.Select(r => latex.Substring(r.Start, r.Length)).ToArray());

        var row = ContentSelection.Between(tree.Root, Cell("4"), Cell("6"));
        Assert.AreEqual(1, row.Ranges.Count, "across a row is contiguous in the source");
        StringAssert.Contains(latex.Substring(row.Ranges[0].Start, row.Ranges[0].Length), "4");
        StringAssert.Contains(latex.Substring(row.Ranges[0].Start, row.Ranges[0].Length), "6");

        var block = ContentSelection.Between(tree.Root, Cell("1"), Cell("5"));
        Assert.AreEqual(2, block.Ranges.Count, "corner to corner is a block: two rows of two");
    });

    [TestMethod]
    [CoversNode("latex-grid-selection")]
    public void ADragInsideOneCellPicksOutWhatItCrossed() => UiThread.Run(() =>
    {
        // Reported from the app: in this matrix the whole of `4b^{2}+3` could be selected and nothing
        // smaller — not the 3 on its own, not the `4b^{2}`. Every drag was read as a block of cells
        // however short it was, and a block of one cell is that cell. A cell is where the grid stops
        // having anything to say: what was dragged over inside one is a run of terms like any other.
        const string latex = @"A = \begin{pmatrix} a & 4b^{2}+3 \\ c^4 & d+3i \end{pmatrix}";
        var tree = Build(latex, "a matrix");
        ILayoutNode At(int offset) => tree.Root.Ink().Single(n => n.Sits().Start == offset);

        var four = latex.IndexOf("4b", StringComparison.Ordinal);
        var two = latex.IndexOf("{2}", StringComparison.Ordinal) + 1;
        var three = latex.IndexOf("+3", StringComparison.Ordinal) + 1;

        var alone = ContentSelection.Between(tree.Root, At(three), At(three));
        Assert.AreEqual(1, alone.Ranges.Count);
        Assert.AreEqual("3", latex.Substring(alone.Ranges[0].Start, alone.Ranges[0].Length));

        var term = ContentSelection.Between(tree.Root, At(four), At(two));
        Assert.AreEqual(1, term.Ranges.Count);
        Assert.AreEqual("4b^{2}", latex.Substring(term.Ranges[0].Start, term.Ranges[0].Length),
            "grown out to the whole script, because half of `^{2}` is not something you can carry");

        // And the cells still select as cells the moment the drag leaves one, which is the behaviour
        // this must not have cost: two rows of two, one range each.
        var block = ContentSelection.Between(
            tree.Root, At(four), At(latex.IndexOf(@"c^4", StringComparison.Ordinal)));
        Assert.AreEqual(2, block.Ranges.Count, "corner to corner is still a block of cells");
    });

    [TestMethod]
    public void AFractionHoldsItsNumeratorAndDenominator() => UiThread.Run(() =>
    {
        // \cfrac nests three deep, which is what made the continued-fraction line such a good bug farm:
        // every level has to be a level.
        var tree = Build(@"\cfrac{1}{2 + \cfrac{1}{3 + \cfrac{1}{4}}}", "nested continued fractions");
        var four = tree.Root.Ink().Single(n => Text(tree, n) == "4");

        Assert.AreEqual(3, four.Ancestors().Count(a => Text(tree, a).StartsWith(@"\cfrac")),
            "the 4 sits inside three fractions");
    });

    private static string Text(LatexTree tree, ILayoutNode node) =>
        node.Sits().Length > 0 ? tree.Latex.Substring(node.Sits().Start, node.Sits().Length) : string.Empty;
}
