using System;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// The document as versions: an edit is a change made to one version, and the version it makes knows that change both ways —
/// so what changed between two versions is read, not worked out, wherever one was made from the other.
/// </summary>
[TestClass]
[CoversNode("markdown-long-writing")]
public class ContentDocumentTests
{
    [TestMethod]
    public void AVersionMadeByAChangeKnowsTheChangeBothWays()
    {
        var first = ContentDocument.Of("one two three");
        var second = first.Apply(ContentChange.Write(4, 3, "2"));

        Assert.AreEqual("one 2 three", second.ToString());
        Assert.IsTrue(second.MadeFrom(first));
        Assert.AreNotEqual(first.Version, second.Version);

        var (forward, backward) = second.ChangesFrom(first);
        Assert.AreSame(second.Change, forward, "known from how it was made, not found by comparing");
        Assert.AreEqual("one two three", second.Apply(backward).ToString());
    }

    [TestMethod]
    public void SeveralWritesAreOneChange_InWhateverOrderTheyAreSaid()
    {
        var first = ContentDocument.Of("a b c d");
        var change = new ContentChange([new ContentWrite(6, 1, "D"), new ContentWrite(0, 1, "A"), new ContentWrite(4, 0, "+")], 0);

        var second = first.Apply(change);

        Assert.AreEqual("A b +c D", second.ToString());
        Assert.AreEqual("a b c d", second.Apply(second.Inverse!).ToString(), "the inverse is in the new version's offsets");
    }

    [TestMethod]
    public void AChangeThatWritesNothingDifferentLeavesTheSameVersion()
    {
        var first = ContentDocument.Of("same");

        Assert.AreSame(first, first.Apply(ContentChange.Write(1, 2, "am")));
        Assert.AreSame(first, first.Apply(new ContentChange([], 0)));
    }

    [TestMethod]
    public void WritesThatOverlapAreRefused()
    {
        var first = ContentDocument.Of("abcdef");

        Assert.ThrowsExactly<ArgumentException>(() =>
            first.Apply(new ContentChange([new ContentWrite(1, 3, "x"), new ContentWrite(2, 1, "y")], 0)));
    }

    [TestMethod]
    public void LinesReadOnward_AreEachLineAsItsOwnPlaceSaysIt_AndStopWhenAsked()
    {
        // Long enough to be held as a tree of stretches, with line ends of all three kinds, some across where stretches meet.
        var text = string.Concat(Enumerable.Range(0, 30_000).Select(line => $"line {line}" + (line % 3) switch { 0 => "\n", 1 => "\r\n", _ => "\r" })) + "last";
        var document = ContentDocument.Of(text);

        var read = new List<(int Line, int Start, string Text)>();
        document.LinesFrom(12_345, (line, start, words) =>
        {
            read.Add((line, start, words));
            return line < 20_000;
        });

        Assert.AreEqual(20_001 - 12_345, read.Count, "it went on past where it was asked to stop");
        foreach (var (line, start, words) in read)
        {
            var (lineStart, lineEnd) = document.LineAt(document.OffsetOf(line));
            Assert.AreEqual(lineStart, start, $"line {line} starts elsewhere");
            Assert.AreEqual(document.Slice(lineStart, lineEnd - lineStart).TrimEnd('\r', '\n'), words, $"line {line} reads otherwise");
        }

        var last = (Line: -1, Text: string.Empty);
        document.LinesFrom(document.LineCount - 2, (line, _, words) => { last = (line, words); return true; });
        Assert.AreEqual((document.LineCount - 1, "last"), last, "the line no line end closes is read too");
    }

    [TestMethod]
    public void TwoVersionsMadeApartAreComparedOnce_AsTheOneStretchWhereTheyDiffer()
    {
        var earlier = ContentDocument.Of("The quick brown fox.");
        var later = ContentDocument.Of("The quick red fox.");

        var (forward, backward) = later.ChangesFrom(earlier);

        Assert.IsFalse(later.MadeFrom(earlier));
        Assert.AreEqual(new ContentWrite(10, "brown".Length, "red"), forward.Writes[0]);
        Assert.AreEqual("The quick red fox.", earlier.Apply(forward).ToString());
        Assert.AreEqual("The quick brown fox.", later.Apply(backward).ToString());
    }

    [TestMethod]
    public void TwoVersionsSayingTheSameThingAreEqual_ButNotTheSameVersion()
    {
        var one = ContentDocument.Of("text");
        var other = ContentDocument.Of("text");

        Assert.AreEqual(one, other);
        Assert.AreNotSame(one, other);
        Assert.AreNotEqual(one.Version, other.Version);
    }

    [TestMethod]
    public void EveryEditToAStateIsOneChangeMadeToItsDocument()
    {
        var plain = new EditState("alpha beta gamma", 5);
        var picked = plain.Select([new EditRange(0, 5), new EditRange(11, 5)]);

        (string Edit, EditState Before, EditState After)[] edits =
        [
            ("typing", plain, plain.Write("!")),
            ("typing over two stretches", picked, picked.Write("X")),
            ("inserting with the caret walked back", plain, plain.Insert("{}", 1)),
            ("wrapping two stretches", picked, picked.Wrap("[", "]")),
            ("backspace", plain, plain.Backspace()),
            ("backspace over two stretches", picked, picked.Backspace()),
            ("forward delete", plain, plain.Delete()),
            ("taking out a stretch", plain, plain.Remove(6, 4)),
        ];

        foreach (var (edit, before, after) in edits)
        {
            Assert.IsTrue(after.Document.MadeFrom(before.Document), $"{edit} is one change to the document it was made to");
            Assert.AreEqual(before.Document.ToString(), after.Document.Apply(after.Document.Inverse!).ToString(), $"{edit} is taken back by its inverse");
        }

        Assert.AreEqual("X beta ", picked.Write("X").Document.ToString(), "the first stretch written over, the second taken out");
        Assert.AreEqual("[alpha] beta [gamma]", picked.Wrap("[", "]").Document.ToString());
    }
}
