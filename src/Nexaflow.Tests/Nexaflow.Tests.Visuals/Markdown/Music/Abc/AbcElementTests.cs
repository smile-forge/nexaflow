using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// Writing music by typing at it: the keys a reader presses, and what the tune says afterwards.
///
/// <para>
/// Everything here goes through <see cref="IEditableBlock"/>, which is the seam the document drives — so a
/// test that presses a key is testing the same path the editor uses rather than a shortcut into the model.
/// The one thing it cannot test is the host: whether a keystroke actually reaches the block is
/// <c>InlineMarkdownEditor</c>'s, and a journey's.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("abc-editing")]
[DoNotParallelize]
public class AbcElementTests
{
    private const string Tune = "X:1\nL:1/8\nK:C\nCDEF|\n";

    /// <summary>A score, engraved and holding the caret, with the caret put where a test wants it.</summary>
    private static (AbcElement Element, IEditableBlock Block) Open(string abc = Tune, int caret = -1)
    {
        var element = new AbcElement(abc, MarkdownPalette.Dark);
        element.Measure(new Size(500, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));

        var block = (IEditableBlock)element;
        block.TakeCaretArriving(new CaretArrival(BlockExit.After, CaretStep.Character, null));
        if (caret >= 0) block.SelectRange(caret, 0);

        return (element, block);
    }

    [TestMethod]
    public void TypingALetterAddsANoteInTheOctaveTheLastOneWasIn() => UiThread.Run(() =>
    {
        var (element, block) = Open("X:1\nK:C\nc'd'|\n");
        block.SelectRange(0, 0);

        // The caret lands at the end of the tune, which is where somebody typing would be.
        block.Type('E');

        Assert.IsTrue(element.Source.Contains("e'"), $"got: {element.Source}");
    });

    [TestMethod]
    public void PageUpMovesTheNoteBeforeTheCaretAnOctave() => UiThread.Run(() =>
    {
        var (element, block) = Open();
        Select(block, "F");

        Assert.IsTrue(block.HandleKey(Key.PageUp, ModifierKeys.None));
        Assert.AreEqual("X:1\nL:1/8\nK:C\nCDEf|\n", element.Source);

        Assert.IsTrue(block.HandleKey(Key.PageDown, ModifierKeys.None));
        Assert.AreEqual(Tune, element.Source, "and back again");
    });

    [TestMethod]
    public void HashSharpensAndUnderscoreFlattens() => UiThread.Run(() =>
    {
        var (element, block) = Open();
        Select(block, "D");

        block.Type('#');
        Assert.AreEqual("X:1\nL:1/8\nK:C\nC^DEF|\n", element.Source);

        block.Type('_');
        Assert.AreEqual("X:1\nL:1/8\nK:C\nC=DEF|\n", element.Source, "one semitone down from a sharp is a natural");
    });

    [TestMethod]
    public void PlusLengthensAndMinusShortens() => UiThread.Run(() =>
    {
        var (element, block) = Open();
        Select(block, "E");

        block.Type('+');
        Assert.AreEqual("X:1\nL:1/8\nK:C\nCDE2F|\n", element.Source);

        block.Type('-');
        block.Type('-');
        Assert.AreEqual("X:1\nL:1/8\nK:C\nCDE/F|\n", element.Source);
    });

    [TestMethod]
    public void AGestureAppliesToTheWholeSelection() => UiThread.Run(() =>
    {
        var (element, block) = Open();

        // Everything after the header — all four notes and the bar line.
        var from = Tune.IndexOf("CDEF", StringComparison.Ordinal);
        block.SelectRange(from, 4);

        Assert.IsTrue(block.HandleKey(Key.PageUp, ModifierKeys.None));
        Assert.AreEqual("X:1\nL:1/8\nK:C\ncdef|\n", element.Source);
    });

    [TestMethod]
    public void TheDocumentIsToldWhenTheTuneChanges() => UiThread.Run(() =>
    {
        var (_, block) = Open();
        Select(block, "C");

        var told = 0;
        block.SourceChanged += (_, _) => told++;

        block.Type('#');
        Assert.AreEqual(1, told);
    });

    [TestMethod]
    public void AndTheCaretWalksInAndOutOfTheTune() => UiThread.Run(() =>
    {
        var (_, block) = Open();

        var left = 0;
        block.Exited += (_, _) => left++;

        // In at the front, then forward until it runs out the back.
        block.TakeCaretArriving(new CaretArrival(BlockExit.Before, CaretStep.Character, null));
        for (var i = 0; i < 200 && left == 0; i++) block.MoveCaret(forward: true, extend: false);

        Assert.AreEqual(1, left, "it left exactly once, off the end it was walking toward");
    });

    [TestMethod]
    public void WithNothingSelectedAGestureActsOnTheNoteTheCaretHasJustPassed() => UiThread.Run(() =>
    {
        // The half of the rule that makes the keys usable: a reader who has just typed a note and wants it
        // an octave up should not have to select it first.
        var (element, block) = Open();
        block.TakeCaretArriving(new CaretArrival(BlockExit.After, CaretStep.Character, null));

        Assert.IsTrue(block.HandleKey(Key.PageUp, ModifierKeys.None));
        Assert.AreEqual("X:1\nL:1/8\nK:C\nCDEf|\n", element.Source, "the last note, which is where the caret was");
    });

    [TestMethod]
    public void ItOffersARibbonOfTheSameGestures() => UiThread.Run(() =>
    {
        var (_, block) = Open();

        var ribbon = block.BuildRibbon();

        Assert.IsInstanceOfType<AbcRibbon>(ribbon);
    });

    [TestMethod]
    public void AndNothingItDoesLeavesATuneThatWillNotReadBack() => UiThread.Run(() =>
    {
        // Every gesture prints a tree and hands the source on. The one thing none of them may do is print
        // something the parser will not give back unchanged, because that source is what is stored.
        foreach (var key in new[] { Key.PageUp, Key.PageDown })
            foreach (var typed in new[] { '#', '_', '+', '-' })
            {
                var (element, block) = Open();
                Select(block, "D");

                block.HandleKey(key, ModifierKeys.None);
                block.Type(typed);

                var source = element.Source;
                Assert.AreEqual(source,
                    Nexaflow.Markdown.Music.Abc.AbcParser.Parse(source).Print(),
                    $"{key} then '{typed}'");
            }
    });

    /// <summary>Selects the last occurrence of <paramref name="written"/> — one note, usually.</summary>
    private static void Select(IEditableBlock block, string written)
    {
        var at = block.Source.LastIndexOf(written, StringComparison.Ordinal);
        Assert.IsTrue(at >= 0, written);
        block.SelectRange(at, written.Length);
    }
}
