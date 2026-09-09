using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// Where a tie lives in the tree.
///
/// <para>
/// A curve is the one thing an engraver cannot draw as it goes: it joins two notes and is not known
/// until both have landed, and over the end of a line it is not one curve but two. So it is built around
/// the notes afterwards, and it sits at whatever level can span what it joins.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcCurveTests
{
    [TestMethod]
    public void ATieInsideABarGathersTheTwoNotesItJoins() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build("X:1\nL:1/4\nK:C\nA- A B c |\n", 700, Brushes.Black, 1.0);

        // Written as two groups, so the two notes are siblings in the bar. Written as one — `A-A` —
        // they would be siblings inside that written group instead and the tie would gather there: the
        // same rule reaching a smaller answer, which is what "whatever the two ends turn out to be"
        // means.
        var set = Only(layout, "tie-set");

        Assert.AreEqual("measure", Kind(set.Parent), "a tie inside a bar belongs to the bar");
        CollectionAssert.AreEquivalent(
            new[] { "note", "note", "tie" }, set.Children.Select(Kind).ToArray(),
            "the set holds " + string.Join(",", set.Children.Select(Kind)));
    });

    [TestMethod]
    public void ATieAcrossABarLineBelongsToTheLineInstead() => UiThread.Run(() =>
    {
        // Gathering these two would mean lifting a note out of each bar, and a bar that no longer holds
        // its own notes can never be selected as one. The line is the smallest thing that reaches across
        // a bar line, so that is where the tie goes.
        var layout = AbcBuilder.Build("X:1\nL:1/4\nK:C\nA B-|B c |\n", 700, Brushes.Black, 1.0);

        var set = Only(layout, "tie-set");

        Assert.AreEqual("system", Kind(set.Parent), "a tie across a bar line belongs to the line");
        CollectionAssert.AreEquivalent(
            new[] { "tie" }, set.Children.Select(Kind).ToArray(),
            "the set holds " + string.Join(",", set.Children.Select(Kind)));
    });

    [TestMethod]
    public void AndEveryBarStillHoldsItsOwnNotes() => UiThread.Run(() =>
    {
        // The invariant the choice above protects, asserted rather than described. Regrouping moves
        // nodes, and a note moved out of its bar is a bar that can no longer be selected whole.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var layout = AbcBuilder.Build(abc, 700, Brushes.Black, 1.0);

            foreach (var note in layout.Root.SelfAndDescendants().Where(n => Kind(n) is "note" or "rest"))
                Assert.IsTrue(note.Ancestors().Any(a => Kind(a) == "measure"),
                              $"{what}: a {Kind(note)} is in no bar");
        }
    });

    [TestMethod]
    public void ASlurOverABeamedGroupGathersTheGroupNotItsNotes() => UiThread.Run(() =>
    {
        // "The two notes, note-sets or beam groups the curve connects" — whatever the two ends turn out
        // to be at the level they share, which for a note slurred to a beamed run is the run.
        var layout = AbcBuilder.Build("X:1\nL:1/8\nK:C\nA2(A2 GFGA) |\n", 700, Brushes.Black, 1.0);

        var set = Only(layout, "slur-set");

        Assert.IsTrue(set.Children.Any(c => Kind(c) == "beam"),
                      "the set holds " + string.Join(",", set.Children.Select(Kind)));
    });

    [TestMethod]
    public void NothingIsDrawnTwiceOrLeftWithTwoParents() => UiThread.Run(() =>
    {
        // Regrouping re-parents, and the failure mode of that is a node reachable down two paths — which
        // paints it twice and measures it twice, both invisibly.
        foreach (var (what, abc) in AbcConstructs.Everything)
        {
            var layout = AbcBuilder.Build(abc, 700, Brushes.Black, 1.0);
            var seen = new HashSet<Piece>();

            foreach (var node in layout.Root.SelfAndDescendants())
            {
                Assert.IsTrue(seen.Add(node), $"{what}: a {Kind(node)} is reachable twice");

                foreach (var child in node.Children)
                    Assert.AreEqual(node, child.Parent,
                                   $"{what}: a {Kind(child)} is held by a {Kind(node)} that is not its parent");
            }
        }
    });

    private static Piece Only(Laid layout, string kind)
    {
        var found = layout.Root.SelfAndDescendants().Where(n => Kind(n) == kind).ToList();
        Assert.AreEqual(1, found.Count, $"expected one {kind}, found {found.Count}");
        return found[0];
    }

    private static string Kind(Piece node) => node.Exists ? node.Kind : "nothing";
}
