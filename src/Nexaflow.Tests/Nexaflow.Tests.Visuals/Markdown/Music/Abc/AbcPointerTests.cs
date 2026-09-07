using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// What pointing at a tune means: the thing under the pointer, and what a drag from one to another
/// covers.
///
/// <para>
/// The tune carries a chord symbol over some notes and a word under most of them, because that is what
/// broke it. A note holding those as children stopped being a leaf, and the descent that finds what a
/// press landed on only considers leaves — so it walked straight past the note, fell back to the nearest
/// ink, and the bar won the tie because it came first. Half the notes in a tune could not be clicked, and
/// which half depended on whether anything was sung on them.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcPointerTests
{
    private const string Sung =
        "X: 1\nT: the Auld Grey Cat\nM: C|\nL: 1/8\nK: EDorian\n"
        + "\"Em\"e2e2 E3F | GFGA BABc |\n"
        + "w: one two three four five six seven eight\n";

    [TestMethod]
    public void PressingANoteMeansTheNoteAndNotTheBarItIsIn() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(Sung, 900, Brushes.Black, 1.0);
        var notes = Every(layout, "note");

        Assert.IsTrue(notes.Count >= 8, $"only {notes.Count} notes — the tune did not engrave");

        foreach (var note in notes)
        {
            var hit = layout.Root.NodeAt(Middle(note))?.Selectable();

            Assert.AreEqual(note, hit,
                $"a press on the note at {note.Bounds.X:F0} came back as a {Kind(hit)}");
        }
    });

    [TestMethod]
    public void AndPressingWhatANoteIsDrawnFromMeansTheNoteToo() => UiThread.Run(() =>
    {
        // A head, a stem, a ledger line and a dot are how a note is drawn, not anything anybody typed.
        // Each is a node so that a press lands on it and climbs — which is the same rule that resolves a
        // staff line, said once rather than per piece.
        var layout = AbcLayout.Build(Sung, 900, Brushes.Black, 1.0);

        foreach (var note in Every(layout, "note"))
            foreach (var piece in note.Children)
            {
                Assert.IsNull(piece.Part, $"a {Kind(piece)} names a piece of the tune; nobody wrote one");

                Assert.AreEqual(note, layout.Root.NodeAt(Middle(piece))?.Selectable(),
                    $"a press on a {Kind(piece)} did not come back as the note it draws");
            }
    });

    [TestMethod]
    public void ADragAlongTheNotesTakesTheNotesAndNothingAbleToBeAboveOrBelowThem() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(Sung, 900, Brushes.Black, 1.0);
        var notes = Every(layout, "note");

        var swept = ContentSelection.Between(layout.Root, notes[0], notes[3]);

        CollectionAssert.AreEquivalent(
            notes.Take(4).ToList(), swept.Nodes.ToList(),
            "a drag along the note layer came back as " + string.Join(",", swept.Nodes.Select(Kind)));

        // …and the source it stands for skips whatever lies between them that nobody dragged over. The
        // chord is written before the first note and the words are a line below; one span from the first
        // note to the last would have swallowed both.
        foreach (var other in Every(layout, "chord").Concat(Every(layout, "syllable")))
            Assert.IsFalse(
                swept.Ranges.Any(r => other.Sits().Start >= r.Start && other.Sits().End <= r.Start + r.Length),
                $"the drag swept up a {Kind(other)} at {other.Sits().Start} that it never crossed");
    });

    [TestMethod]
    public void AndADragDownFromANoteToAWordTakesTheBlockBetweenThem() => UiThread.Run(() =>
    {
        // The other axis, and the reason the two are declared separately: down from a note is what sounds
        // with it, and the block between two of those is a rectangle of the score — the same gesture a
        // matrix answers, from the same code, because the builder said what lines up.
        var layout = AbcLayout.Build(Sung, 900, Brushes.Black, 1.0);
        var (notes, sung) = (Every(layout, "note"), Every(layout, "syllable"));

        var block = ContentSelection.Between(layout.Root, notes[0], sung[2]).Nodes.ToList();

        CollectionAssert.AreEquivalent(
            notes.Take(3).Concat(sung.Take(3)).ToList(), block,
            "a drag from a note down to a word came back as " + string.Join(",", block.Select(Kind)));
    });

    [TestMethod]
    public void TheSectionsOfALineAreThingsOfTheirOwn() => UiThread.Run(() =>
    {
        // A repeat is where the music turns, so the bars either side of one are different sections — the
        // unit a reader means by "the A part", and the level a selection would otherwise skip on its way
        // from a bar to a whole line.
        const string TwoParts = "X:1\nK:G\n|:GABc dedB:|\"Em\"c2ec B2dB||\n";

        var layout = AbcLayout.Build(TwoParts, 900, Brushes.Black, 1.0);
        var sections = Every(layout, "section");

        Assert.AreEqual(2, sections.Count,
            "a repeat and a double bar are two sections, not " + sections.Count);

        Assert.IsTrue(sections[0].Sits().End <= sections[1].Sits().Start,
            "the sections overlap in the source");

        foreach (var section in sections)
            Assert.IsTrue(section.Ink().Any(n => Kind(n) == "note"),
                "a section holds no notes, so it is a section of nothing");
    });

    private static System.Collections.Generic.List<ILayoutNode> Every(AbcLayout layout, string kind) =>
        [.. layout.Root.SelfAndDescendants().Where(n => Kind(n) == kind)];

    private static Point Middle(ILayoutNode node) =>
        new(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));

    private static string Kind(ILayoutNode? node) => (node as LayoutNode)?.Kind ?? "nothing";
}
