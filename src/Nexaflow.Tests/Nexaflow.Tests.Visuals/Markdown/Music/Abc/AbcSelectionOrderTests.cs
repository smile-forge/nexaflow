using System.Linq;
using System.Windows;
using System.Windows.Input;
using Nexaflow.Visuals.Text.Markdown;
using System.Windows.Media;
using Nexaflow.Tests.Features.Fixtures;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// What a score says can be selected along, and in which direction.
///
/// <para>
/// Three things are declared: each layer runs the length of the tune, everything sounding at one moment
/// stacks, and a bar contains what it draws. That is enough for a drag to mean what a reader means by it,
/// and — the reason it is worth being exactly this — enough for an animation to be a walk rather than a
/// rewrite. Notes appearing as they are played is the note layer in order; notes, then words, then chords
/// is the layers in turn; a bar at a time with all three is the bars, each with its stacks.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-layout")]
[DoNotParallelize]
public class AbcSelectionOrderTests
{
    /// <summary>Two bars, a chord over the first note, and two verses under the lot.</summary>
    private const string Tune =
        "X:1\nL:1/4\nK:C\n\"Am\"CDEF|GABc|\n"
        + "w: one two three four five six sev-en eight\n"
        + "w: ay bee cee dee ee eff gee aitch\n";

    [TestMethod]
    public void ASyllableIsItsOwnPieceAndNamesItsOwnCharacters() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(Tune, 600, Brushes.Black, 1.0);
        var sung = Of(layout, "syllable");

        Assert.AreEqual(16, sung.Count, "eight notes, two verses");

        // …and each names the characters of the verse, not of the note it is drawn under. Walked in the
        // order the score declared, because tree order interleaves the verses — every note carries its
        // first verse and its second before the next note is reached.
        var written = Verse(sung[0]).Take(3)
            .Select(n => layout.Abc.Substring(n.Sits().Start, n.Sits().Length)).ToList();

        CollectionAssert.AreEqual(new[] { "one", "two", "three" }, written,
                                  "a syllable should name its own word in the w: line");
    });

    [TestMethod]
    public void AChordIsItsOwnPieceAndNamesWhatWasTyped() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(Tune, 600, Brushes.Black, 1.0);
        var chords = Of(layout, "chord");

        Assert.AreEqual(1, chords.Count);
        Assert.AreEqual("\"Am\"", layout.Abc.Substring(chords[0].Sits().Start, chords[0].Sits().Length));
    });

    [TestMethod]
    public void AVerseRunsTheLengthOfTheTuneAndTheBarLineDoesNotStopIt() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(Tune, 600, Brushes.Black, 1.0);
        var first = Of(layout, "syllable")[0];

        // Stepping sideways walks the verse — across the bar line, which is the point.
        var walked = 1;
        for (var at = first.Step(vertical: false, forward: true); at.Exists;
             at = at.Step(vertical: false, forward: true)) walked++;

        Assert.AreEqual(8, walked, "one verse is eight syllables, both bars");
    });

    [TestMethod]
    public void AndDownFromANoteIsWhatSoundsWithIt() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(Tune, 600, Brushes.Black, 1.0);
        var note = Of(layout, "note")[0];

        // The chord is over it, so up is the chord and down is verse one, then verse two.
        Assert.AreEqual("chord", Kind(note.Step(vertical: true, forward: false)));
        Assert.AreEqual("syllable", Kind(note.Step(vertical: true, forward: true)));

        var second = note.Step(vertical: true, forward: true)!.Step(vertical: true, forward: true);
        Assert.AreEqual("syllable", Kind(second));
        Assert.AreEqual("ay", layout.Abc.Substring(second!.Sits().Start, second.Sits().Length));
    });

    [TestMethod]
    public void ASyllableSelectsWithoutTheNoteComingWithIt() => UiThread.Run(() =>
    {
        var layout = AbcLayout.Build(Tune, 600, Brushes.Black, 1.0);
        var sung = Of(layout, "syllable");

        var verse = Verse(sung[0]);
        var swept = ContentSelection.Between(layout.Root, verse[0], verse[2]);

        Assert.AreEqual(1, swept.Ranges.Count, "three words of one verse is one stretch of source");
        Assert.AreEqual("one two three",
                        layout.Abc.Substring(swept.Ranges[0].Start, swept.Ranges[0].Length),
                        "dragging along a verse should give the verse, not the notes above it");
    });

    [TestMethod]
    public void ShiftAndAnArrowSweepsTheSameWayADragDoes() => UiThread.Run(() =>
    {
        var element = Open();
        var block = (IEditableBlock)element;

        // Click the first syllable, then hold Shift and walk right twice.
        element.BeginPointerSelect(Middle(element, "syllable", 0));
        element.EndPointerSelect();

        // Through the seam the host drives for an arrow key, which is the only handler for one.
        Assert.IsTrue(block.MoveCaret(forward: true, extend: true));
        Assert.IsTrue(block.MoveCaret(forward: true, extend: true));

        Assert.AreEqual("one two three", Selected(element),
                        "the anchor should stay put while the far end walks the verse");
    });

    [TestMethod]
    public void AndShiftDownReachesTheWordsUnderTheNote() => UiThread.Run(() =>
    {
        var element = Open();
        var block = (IEditableBlock)element;

        element.BeginPointerSelect(Middle(element, "note", 0));
        element.EndPointerSelect();

        Assert.IsTrue(block.MoveCaretVertically(up: false, extend: true),
                      "a note with words under it has somewhere to go");

        Assert.IsTrue(Selected(element).Contains("one"),
                      $"stepping down from the first note should reach its syllable — got: {Selected(element)}");
    });

    [TestMethod]
    public void AndThereIsNothingPastTheEndOfARun() => UiThread.Run(() =>
    {
        var element = Open();
        var block = (IEditableBlock)element;

        element.BeginPointerSelect(Middle(element, "syllable", 0));
        element.EndPointerSelect();

        // Left from the first syllable of a verse is off the end of the run, so there is nothing to
        // extend and it falls back to moving the caret — one handler, two behaviours, never both at once.
        var before = Selected(element);
        block.MoveCaret(forward: false, extend: true);
        Assert.AreEqual(before, Selected(element), "there was nothing that way to select");
    });

    // ── Driving the element ─────────────────────────────────────────────────

    private static AbcElement Open()
    {
        var element = new AbcElement(Tune, MarkdownPalette.Dark);
        element.Measure(new Size(600, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));
        return element;
    }

    /// <summary>The middle of the nth thing of a kind, in element coordinates.</summary>
    private static Point Middle(AbcElement element, string kind, int nth)
    {
        var node = Of(element.Layout!, kind)[nth];
        return new Point(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));
    }

    private static string Selected(AbcElement element) =>
        string.Join(" | ", element.Selection.Select(r => element.Source.Substring(r.Start, r.Length)));

    /// <summary>One verse, from a syllable in it — the run the score says it is.</summary>
    private static System.Collections.Generic.List<Piece> Verse(Piece from)
    {
        var run = new System.Collections.Generic.List<Piece> { from };
        for (var at = from.Step(vertical: false, forward: true); at.Exists;
             at = at.Step(vertical: false, forward: true)) run.Add(at);
        return run;
    }

    private static System.Collections.Generic.List<Piece> Of(AbcLayout layout, string kind) =>
        [.. layout.Root.SelfAndDescendants().Where(n => n.Kind == kind)];

    private static string? Kind(Piece node) => node.Exists ? node.Kind : null;
}
