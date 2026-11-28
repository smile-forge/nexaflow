using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>A block picked out by column: the same columns of every line, written over, taken out and copied a line at a time.</summary>
[TestClass]
[CoversNode("content-column-selection")]
public class ColumnSelectionTests
{
    private const string Text = "alpha one\nbeta\ngamma three\n";

    [TestMethod]
    public void ABlock_PicksOutTheSameColumnsOfEveryLine_AsFarAsAShorterLineReaches()
    {
        var state = ColumnSelection.Between(new EditState(Text, 0), (0, 2), (2, 6), tabWidth: 4);

        Assert.AreEqual("pha \nta\nmma ", state.SelectedText.ReplaceLineEndings("\n"));
        Assert.IsInstanceOfType<ColumnRanges>(state.Selected);
    }

    [TestMethod]
    public void Columns_AreCountedAsTheLineIsSet_ATabReachingItsStop()
    {
        var state = ColumnSelection.Between(new EditState("\tx\n    y\n", 0), (0, 4), (1, 5), tabWidth: 4);

        Assert.AreEqual("x\ny", state.SelectedText.ReplaceLineEndings("\n"), "a tab and four spaces both reach column four");
    }

    [TestMethod]
    public void WritingOverABlock_WritesOnEveryLine_FillingOutAShortLineOnlyWhereItIsWritten()
    {
        var state = ColumnSelection.Between(new EditState(Text, 0), (0, 6), (2, 6), tabWidth: 4);

        var written = state.Write("|");

        Assert.AreEqual("alpha |one\nbeta  |\ngamma |three\n", written.Document.ToString());

        var block = (ColumnRanges)written.Selected!;
        Assert.AreEqual((7, 7), (block.Low, block.High), "typing goes on writing on every line after what was written");
    }

    [TestMethod]
    public void PastingAsManyLinesAsTheBlockHas_PutsALineOnEachLine()
    {
        var state = ColumnSelection.Between(new EditState("a\nb\nc\n", 0), (0, 1), (2, 1), tabWidth: 4);

        Assert.AreEqual("a1\nb2\nc3\n", state.Write("1\n2\n3").Document.ToString());
    }

    [TestMethod]
    public void BackspaceAndDelete_FromABlockOfNoWidth_TakeACharacterOnEveryLineThatHasOne()
    {
        var state = ColumnSelection.Between(new EditState("abc\nde\nfgh\n", 0), (0, 2), (2, 2), tabWidth: 4);

        Assert.AreEqual("ac\nd\nfh\n", state.Backspace().Document.ToString());
        Assert.AreEqual("ab\nde\nfg\n", state.Delete().Document.ToString(), "the short line has nothing after the block to take");
    }

    [TestMethod]
    public void WritingOverABlock_IsOneChangeToTakeBack()
    {
        var document = new EditedDocument(Text);
        var before = ColumnSelection.Between(new EditState(document.Current, 0), (0, 0), (2, 0), tabWidth: 4);
        var after = before.Write("> ");

        document.Wrote(before, after);
        document.Undo(after);

        Assert.AreEqual(Text, document.ToString());
    }

    [TestMethod]
    public void TypingOnInABlock_JoinsWhatWasTyped_SoItIsTakenBackAsOne()
    {
        var document = new EditedDocument(Text);
        var start = ColumnSelection.Between(new EditState(document.Current, 0), (0, 0), (2, 0), tabWidth: 4);
        var once = start.Write("/");
        document.Wrote(start, once);
        var twice = once.Write("/");
        document.Wrote(once, twice);

        Assert.AreEqual("//alpha one\n//beta\n//gamma three\n", document.ToString());

        var back = document.Undo(twice);

        Assert.AreEqual(Text, document.ToString());
        Assert.IsInstanceOfType<ColumnRanges>(back!.Selected, "and the block stands where it was begun");
    }
}
