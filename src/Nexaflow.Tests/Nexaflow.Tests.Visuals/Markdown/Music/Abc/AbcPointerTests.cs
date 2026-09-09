using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Generic;
using System;

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
        var layout = AbcBuilder.Build(Sung, 900, Brushes.Black, 1.0);
        var notes = Every(layout, "note");

        Assert.IsTrue(notes.Count >= 8, $"only {notes.Count} notes — the tune did not engrave");

        foreach (var note in notes)
        {
            var hit = layout.Root.PieceAt(Middle(note)).Selectable();

            Assert.AreEqual(note, hit,
                $"a press on the note at {note.Bounds.X:F0} came back as a {Kind(hit)}");
        }
    });

    [TestMethod]
    public void SweepingAlongTheNotesNeverLandsOnTheGroupTheyAreIn() => UiThread.Run(() =>
    {
        // Reported from the app: "selecting a single note is difficult with the group selecting flickering
        // in and out as the mouse drags across". A beamed group's rectangle covers the notes it joins, so a
        // point in the gap between two heads was nought away from the group and a little way from either
        // note — and the group won. Every gap the pointer crossed flipped the selection from a note to six.
        //
        // Swept a pixel at a time along the row the heads sit on, which is the gesture that failed.
        var layout = AbcBuilder.Build("X:1\nL:1/8\nK:G\nGABc dedB|dedB dedB|\n", 900, Brushes.Black, 1.0);
        var notes = Every(layout, "note");

        Assert.IsTrue(notes.Count >= 8, $"only {notes.Count} notes — the tune did not engrave");

        var row = notes[0].Bounds.Y + (notes[0].Bounds.Height / 2);
        var trouble = new List<string>();

        for (var x = notes[0].Bounds.X; x <= notes[^1].Bounds.Right; x += 1)
        {
            var at = layout.Root.PieceAt(new Point(x, row)).Selectable();
            if (!at.Exists) { trouble.Add($"{x:F0}: nothing"); continue; }

            // Anything holding a selectable piece of its own is a group, and a group is something a reader
            // gets by covering it rather than by aiming between its members.
            if (at.SelfAndDescendants().Skip(1).Any(n => n.Part is { Length: > 0 }))
                trouble.Add($"{x:F0}: {Kind(at)}");
        }

        Assert.AreEqual(0, trouble.Count,
            "the sweep landed on a group at " + string.Join(", ", trouble.Take(6)));
    });

    [TestMethod]
    public void AndPressingWhatANoteIsDrawnFromMeansTheNoteToo() => UiThread.Run(() =>
    {
        // A head, a stem, a ledger line and a dot are how a note is drawn, not anything anybody typed.
        // Each is a node so that a press lands on it and climbs — which is the same rule that resolves a
        // staff line, said once rather than per piece.
        var layout = AbcBuilder.Build(Sung, 900, Brushes.Black, 1.0);

        foreach (var note in Every(layout, "note"))
            foreach (var piece in note.Children)
            {
                Assert.IsNull(piece.Part, $"a {Kind(piece)} names a piece of the tune; nobody wrote one");

                Assert.AreEqual(note, layout.Root.PieceAt(Middle(piece)).Selectable(),
                    $"a press on a {Kind(piece)} did not come back as the note it draws");
            }
    });

    [TestMethod]
    public void APressPutsTheCaretDownWhereItLanded() => UiThread.Run(() =>
    {
        // Asked of what the caret is *for* rather than of a private field. With nothing selected, a note
        // gesture acts on the note the caret is in — so sharpening after a press on the third note says
        // both that a caret exists and that it landed there. Before a press put one down the caret sat at
        // nought whatever the reader did, and this sharpened nothing at all.
        //
        // Pressed right of the head's centre, which is the half that means "after this one". Left of it
        // puts the caret in front of the note and the gesture then reaches the note before, exactly as
        // backspace does in a line of text.
        var element = new AbcElement("X:1\nL:1/4\nK:C\nA B c d |\n", MarkdownPalette.Light);
        element.Measure(new Size(700, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

        var third = element.Layout!.Root.SelfAndDescendants()
            .Where(n => n.Kind == "note")
            .ElementAt(2);

        element.BeginPointerSelect(new Point(third.Bounds.Right - 1, third.Bounds.Y + (third.Bounds.Height / 2)));
        element.EndPointerSelect();
        element.ClearSelection();

        element.Type('#');

        StringAssert.Contains(element.Source, "^c", "the sharp went somewhere else: " + element.Source);
    });

    [TestMethod]
    public void APressLandsTheCaretBesideWhateverItIsNearest() => UiThread.Run(() =>
    {
        // Reported from the app: "you cannot really insert the caret in a blank spot in the score or where
        // you might want it, it seems to put it at the start of that block rather than where you clicked".
        // A press on nothing takes the nearest ink, and the nearest ink was whichever container's rectangle
        // happened to cover the gap — a bar, or the beamed group — whose source begins at its first note.
        // So every press between notes gave the same answer.
        const string Tune = "X:1\nL:1/4\nK:C\nA B c d | e f g a |\n";

        var layout = AbcBuilder.Build(Tune, 900, Brushes.Black, 1.0);
        var notes = Every(layout, "note");
        var row = notes[0].Bounds.Y + (notes[0].Bounds.Height / 2);

        // Pressed in the gap just after each note: the caret belongs to that note, not to the bar.
        for (var at = 0; at < notes.Count - 1; at++)
        {
            var gap = new Point(notes[at].Bounds.Right + 3, row);
            var offset = layout.Root.OffsetAt(gap);

            Assert.AreEqual(notes[at].Sits().End, offset,
                $"a press just past the note at {notes[at].Sits().Start} landed at {offset}"
                + $" ({Tune[Math.Max(0, offset - 1)]}|{Tune[Math.Min(Tune.Length - 1, offset)]})");
        }

        // …and past the end of the music, which is where a reader aims to add to it. Not the last note
            // necessarily — the bar line after it is a real thing and is genuinely nearer — but past it, and
            // nowhere near the start of the block, which is the answer that was being given.
            var beyond = layout.Root.OffsetAt(new Point(notes[^1].Bounds.Right + 40, row));

            Assert.IsTrue(beyond >= notes[^1].Sits().End,
                $"a press past the last note landed at {beyond}, before the note ending at {notes[^1].Sits().End}");
    });

    [TestMethod]
    public void ADragAlongTheNotesTakesTheNotesAndNothingAbleToBeAboveOrBelowThem() => UiThread.Run(() =>
    {
        var layout = AbcBuilder.Build(Sung, 900, Brushes.Black, 1.0);
        var notes = Every(layout, "note");

        var swept = ContentSelection.Between(layout.Root, notes[0], notes[3]);

        CollectionAssert.AreEquivalent(
            notes.Take(4).ToList(), swept.Pieces.ToList(),
            "a drag along the note layer came back as " + string.Join(",", swept.Pieces.Select(Kind)));

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
        var layout = AbcBuilder.Build(Sung, 900, Brushes.Black, 1.0);
        var (notes, sung) = (Every(layout, "note"), Every(layout, "syllable"));

        var block = ContentSelection.Between(layout.Root, notes[0], sung[2]).Pieces.ToList();

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

        var layout = AbcBuilder.Build(TwoParts, 900, Brushes.Black, 1.0);
        var sections = Every(layout, "section");

        Assert.AreEqual(2, sections.Count,
            "a repeat and a double bar are two sections, not " + sections.Count);

        Assert.IsTrue(sections[0].Sits().End <= sections[1].Sits().Start,
            "the sections overlap in the source");

        foreach (var section in sections)
            Assert.IsTrue(section.Ink().Any(n => Kind(n) == "note"),
                "a section holds no notes, so it is a section of nothing");
    });

    private static System.Collections.Generic.List<Piece> Every(Laid layout, string kind) =>
        [.. layout.Root.SelfAndDescendants().Where(n => Kind(n) == kind)];

    private static Point Middle(Piece node) =>
        new(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));

    private static string Kind(Piece node) => node.Exists ? node.Kind : "nothing";
}
