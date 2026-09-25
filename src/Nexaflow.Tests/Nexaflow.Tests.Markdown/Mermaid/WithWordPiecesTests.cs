using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// What a diagram's words are made of (<see cref="WithWordPieces"/>): a run that is only its own characters has nothing held
/// for it, and one with a line break, an entity code or a binding in it is the pieces it is, each where it was written.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class WithWordPiecesTests
{
    /// <summary>The block read and worked over, and the run of words written as <paramref name="written"/> in it.</summary>
    private static (ContentPart Words, MermaidWords Held) Read(string source, string written)
    {
        var tree = new WithWordPieces().Run(MermaidParser.Read(source));
        var words = ContentPart.Of(tree).SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Text == written);

        return (words, MermaidWords.Held(tree));
    }

    [TestMethod]
    public void WordsThatAreOnlyTheirOwnCharactersHoldNothing()
    {
        var (words, held) = Read("graph TD\n  a[Plain words here] --> b\n", "Plain words here");

        Assert.IsNull(held.Of(words.Node));
        Assert.AreSame(MermaidWords.None, held, "a diagram with nothing to say about its words has nothing hung on it");
    }

    [TestMethod]
    public void ALineBreakAndAnEntityCodeArePiecesOfTheirOwn_EachWhereItWasWritten()
    {
        const string source = "graph TD\n  a[\"One<br/>Two #35;3\"] --> b\n";
        var (words, held) = Read(source, "One<br/>Two #35;3");
        var pieces = held.Of(words.Node)!;

        CollectionAssert.AreEqual(new[] { "One", "<br/>", "Two ", "#35;", "3" }, pieces.Select(piece => piece.Written).ToArray());
        CollectionAssert.AreEqual(new[] { "One", "", "Two ", "#", "3" }, pieces.Select(piece => piece.Says).ToArray());
        CollectionAssert.AreEqual(new[] { false, true, false, false, false }, pieces.Select(piece => piece.Breaks).ToArray());
        CollectionAssert.AreEqual(new[] { true, false, true, false, true }, pieces.Select(piece => piece.AsWritten).ToArray());

        // Each stands where it was written, worked out from the words it is a piece of.
        foreach (var piece in pieces)
        {
            var stands = piece.In(words);
            Assert.AreEqual(piece.Written, source.Substring(stands.Start, stands.Length), piece.Written);
        }
    }

    [TestMethod]
    public void EveryWayOfWritingALineBreakBreaksIt()
    {
        foreach (var mark in new[] { "<br>", "<br/>", "<br />", "<BR>", @"\n" })
        {
            var written = $"Up{mark}Down";
            var (words, held) = Read($"graph TD\n  a[\"{written}\"] --> b\n", written);

            Assert.AreEqual(1, held.Of(words.Node)!.Count(piece => piece.Breaks), mark);
        }
    }

    [TestMethod]
    public void ACodeThatStandsForNothingIsTheCharactersItIs()
    {
        var (words, held) = Read("graph TD\n  a[\"Keep #nothing; here\"] --> b\n", "Keep #nothing; here");
        var pieces = held.Of(words.Node);

        Assert.IsTrue(pieces is null || pieces.All(piece => piece.AsWritten), "nothing it stands for, so nothing to read it as");
    }
}
