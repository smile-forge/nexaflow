using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Lines;
using Nexaflow.Markdown.PlainText;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.PlainText;

/// <summary>
/// Plain text, read: one run of its characters held as written, cut — for the stretches laid — into lines, each its words and the spaces
/// between them and the line end closing it.
/// </summary>
[TestClass]
[CoversNode("md-plain-text")]
public class TextParserTests
{
    [TestMethod]
    public void TextIsReadAsOneRunOfItsCharacters_HeldAsWritten()
    {
        var tree = TextParser.Parse("one two\nthree");

        Assert.AreEqual(TextKinds.Text, tree.Kind);
        Assert.AreEqual(TextParser.Language, ((BlockNode)tree).Language, "a tree of text says so, for what is written in it to be answered as text");
        Assert.AreEqual("one two\nthree", tree.Children.Single(child => child.Role == Roles.Body).Text);
    }

    [TestMethod]
    public void ALineIsItsWordsAndTheSpacesBetweenThem_ItsLineEndAPieceOfItsOwn()
    {
        var lines = Lines("one  two\tthree\r\nfour");

        Assert.AreEqual(2, lines.Count);
        CollectionAssert.AreEqual(new[] { "one", "  ", "two", "\t", "three", "\r\n" }, lines[0].Children.Select(piece => piece.Text).ToArray(),
            "a return before a line feed is the line end's");
        CollectionAssert.AreEqual(new[] { TextKinds.Word, Kinds.Space, TextKinds.Word, Kinds.Space, TextKinds.Word, LineKinds.LineEnd },
            lines[0].Children.Select(piece => piece.Kind).ToArray());
        CollectionAssert.AreEqual(new[] { "four" }, lines[1].Children.Select(piece => piece.Text).ToArray(), "the last line has no line end to hold");
    }

    [TestMethod]
    public void EveryPieceStandsWhereItsCharactersAreWritten()
    {
        const string source = "  lead\nnext word\n";

        foreach (var piece in Lines(source).SelectMany(line => line.Children))
            Assert.AreEqual(piece.Text, source.Substring(piece.Offset!.Value, piece.Text.Length), $"'{piece.Text}' stands somewhere else");
    }

    [TestMethod]
    public void ABlankLineIsALineHoldingOnlyItsLineEnd()
    {
        var lines = Lines("a\n\nb");

        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual(LineKinds.LineEnd, lines[1].Children.Single().Kind);
    }

    [TestMethod]
    public void NothingWrittenIsStillOneLine_ForTheCaretToStandOn()
    {
        var lines = Lines("");

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual(0, lines[0].Children.Count);
    }

    /// <summary>The lines <paramref name="source"/> is cut into.</summary>
    private static List<ContentNode> Lines(string source) =>
        [.. TextParser.InLines(TextParser.Parse(source)).Children.Where(child => child.Kind == TextKinds.Line)];
}
