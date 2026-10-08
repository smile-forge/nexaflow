using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Music.Abc;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Music.Abc;

namespace Nexaflow.Tests.Visuals.Markdown.Music.Abc;

/// <summary>
/// What a key pressed in a tune comes to, written the way the engine writes it.
///
/// <para>
/// The handler's whole job is to name the stretches of source a key changes and let the engine write them, so
/// what is worth asserting is the tune that comes of a keystroke. Every press here goes the way a real one
/// does: ABC says what the key means, what it answers is put through ABC's own spelling where it is words as
/// the reader means them, and the engine makes the source of it.
/// </para>
/// <para>
/// Which is also the one test that the tree a gesture is told about is the tree the stages worked over. A key
/// signature reaches a note through a stage, so flattening an F in G major can only write a natural if the
/// handler was given the staged tune — on a bare parse it would write a flat, and the reader would hear the
/// key sharpen it straight back.
/// </para>
/// </summary>
[TestClass]
[CoversNode("abc-editing")]
public class AbcEditsTests
{
    private const string Tune = "X:1\nL:1/8\nK:G\nGABc dedB|c2A2|\n";

    /// <summary>
    /// What ABC makes of <paramref name="kind"/> pressed at <paramref name="caret"/> in <paramref name="abc"/>, or null
    /// where it says nothing and the key is the engine's. The tune is read and worked over as the engine reads it, and
    /// the part the key landed in is the innermost one holding the caret, as the layout would have given it.
    /// </summary>
    private static ContentChange? Answered(string abc, int caret, EditKind kind, string text = "")
    {
        var state = new EditState(abc, caret);
        var tune = ContentPart.Of(AbcPipeline.Of().Run(AbcParser.Parse(abc)));

        var at = tune.SelfAndDescendants()
            .Where(part => !part.Derived && part.Length > 0 && part.Start <= caret && part.End >= caret)
            .OrderBy(part => part.Length)
            .FirstOrDefault() ?? tune;

        return AbcEdits.Instance.Edit(new ContentEdit(kind, text, new Landing(state, Laid.Nothing, -1), default, at, tune));
    }

    /// <summary>
    /// And the tune that comes of it — the change put through ABC's spelling where it holds words as the reader means
    /// them, then written, which is what <c>ContentEngine</c> does with every answer a language gives.
    /// </summary>
    private static string Pressed(string abc, int caret, EditKind kind, string text = "")
    {
        if (Answered(abc, caret, kind, text) is not { } change) return abc;

        var spelled = change.Writes.Any(write => write.Meant) ? Transpiles.By<AbcParser>()(change) : change;

        return spelled is null ? abc : ContentEngine.Made(new EditState(abc, caret), spelled).Source;
    }

    // ── A key changing the note the caret stands after ──────────────────────

    [TestMethod]
    public void PageUpMovesTheNoteBeforeTheCaretAnOctave()
    {
        Assert.AreEqual("X:1\nL:1/8\nK:G\ngABc dedB|c2A2|\n", Pressed(Tune, 15, EditKind.Raising),
                        "the G moved up, and every other character of the tune is where it was");
    }

    [TestMethod]
    public void AndPageDownMovesItBack()
    {
        Assert.AreEqual(Tune, Pressed("X:1\nL:1/8\nK:G\ngABc dedB|c2A2|\n", 15, EditKind.Lowering));
    }

    [TestMethod]
    public void AHashSharpensIt()
    {
        Assert.AreEqual("X:1\nL:1/8\nK:C\n^CDE|\n", Pressed("X:1\nL:1/8\nK:C\nCDE|\n", 15, EditKind.Typing, "#"));
    }

    [TestMethod]
    public void AndAnUnderscoreFlatteningAnFInGMajorWritesANatural()
    {
        // The assertion that the handler is told the staged tune and not a bare parse: F sounds sharp here
        // because the key says so, which only a stage knows, and a semitone down from F sharp is F natural.
        Assert.AreEqual("X:1\nL:1/8\nK:G\n=FGA|\n", Pressed("X:1\nL:1/8\nK:G\nFGA|\n", 15, EditKind.Typing, "_"));
    }

    [TestMethod]
    public void APlusLengthensItAndAMinusShortensIt()
    {
        Assert.AreEqual("X:1\nL:1/8\nK:C\nA2|\n", Pressed("X:1\nL:1/8\nK:C\nA|\n", 15, EditKind.Typing, "+"));
        Assert.AreEqual("X:1\nL:1/8\nK:C\nA/|\n", Pressed("X:1\nL:1/8\nK:C\nA|\n", 15, EditKind.Typing, "-"));
    }

    [TestMethod]
    public void AndAGestureNamesTheNoteItChangedAndNothingAroundIt()
    {
        var change = Answered(Tune, 15, EditKind.Raising)!;
        var write = change.Writes.Single();

        Assert.AreEqual(14, write.Start, "where the G stands in the document");
        Assert.AreEqual(1, write.Length, "one character — not the tune handed back to be reprinted");
        Assert.AreEqual("g", write.Text);
    }

    // ── A key writing a new note ─────────────────────────────────────────────

    [TestMethod]
    public void ALetterTypedInTheStaffWritesThatNoteInTheOctaveOfTheOneBeforeIt()
    {
        Assert.AreEqual("X:1\nL:1/8\nK:G\nGABce dedB|c2A2|\n", Pressed(Tune, 18, EditKind.Typing, "E"),
                        "typed after a lower-case c, the new note is spelled lower-case too");
    }

    [TestMethod]
    public void AndSpaceWritesAPauseOfAWholeNoteAgainstTheTunesOwnUnit()
    {
        Assert.AreEqual("X:1\nL:1/8\nK:G\nGABcz8 dedB|c2A2|\n", Pressed(Tune, 18, EditKind.Settling, " "),
                        "eight eighths is a whole note");
    }

    // ── Where a tune is text, the key is the engine's ────────────────────────

    [TestMethod]
    public void AKeyPressedInAFieldIsNotAGesture()
    {
        // A G typed in a title is the letter G, and a plus under the staff is a plus. Saying nothing is how
        // those reach the engine, which writes into a run of words as a run of words.
        Assert.IsNull(Answered("X:1\nT:A tune\nK:G\nGA|\n", 10, EditKind.Typing, "G"), "inside T:A tune");
        Assert.IsNull(Answered("X:1\nK:G\nGA|\nw:la la\n", 18, EditKind.Typing, "+"), "inside w:la la");
    }
}
