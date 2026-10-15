using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// Code written inside a container: a fence in a quote, a fence in a list item, indented code in a quote. What the language is
/// handed is its own lines, without the marks the container put in front of each — and what it
/// reads is given those marks back where they stand, so every piece still names the characters it was written as.
/// </summary>
[TestClass]
[CoversNode("markdown-text")]
public class MarkdownCodeLinesTests
{
    private const string Quoted = "> ```mermaid\n> pie\n>   \"a\" : 1\n> ```\n";

    private const string Itemised = "- ```js\n  let x;\n  more();\n  ```\n";

    private const string Indented = ">     code1\n>       code2\n\n    top\n";

    private static readonly string[] Documents = [Quoted, Itemised, Indented];

    [TestMethod]
    public void EveryDocumentReadsBackAsItWasWritten_EveryPieceWhereItWasRead()
    {
        foreach (var source in Documents)
        {
            var tree = Read(source);

            Assert.AreEqual(source, tree.Print(), source);
            CollectionAssert.AreEqual(new List<string>(), AstOracle.Faults(source, tree).ToList(), source);
        }
    }

    [TestMethod]
    public void AFenceInAQuoteHandsItsLanguageTheLinesWithoutTheQuotesMarks()
    {
        Assert.AreEqual("pie\n  \"a\" : 1", Own(Read(Quoted)));
    }

    [TestMethod]
    public void AFenceInAnItemHandsItsLanguageTheLinesWithoutTheItemsIndentation()
    {
        Assert.AreEqual("let x;\nmore();", Own(Read(Itemised)));
    }

    [TestMethod]
    public void IndentedCodeIsItsLinesWithoutTheMarksOrTheFourColumnsThatMadeItCode()
    {
        var code = Read(Indented).SelfAndDescendants().Where(node => node.Kind == MarkdownKinds.Code).ToList();

        CollectionAssert.AreEqual(new[] { "code1\n  code2", "top" }, code.Select(Written).ToArray());
    }

    [TestMethod]
    public void WhatTheLanguageReadIsGivenTheMarksBack_EachPieceStandingWhereItsCharactersAre()
    {
        var holder = Holder(Quoted);
        var body = holder.Part(Roles.Body)!;

        // Read as a language might: one piece running over both lines, so the mark between them has to cut it.
        var read = ContentNode.Branch(Kinds.Sequence, [ContentNode.Leaf(MarkdownKinds.Word, ContentNested.Own(body.Node), offset: 0)], offset: 0);
        var spliced = ContentPart.Of(ContentNested.Spliced(read, body.Node), body.Start);

        var (start, length) = ContentNested.Own(body);
        Assert.AreEqual(Quoted.Substring(start, length), spliced.Print());

        foreach (var piece in spliced.SelfAndDescendants().Where(part => part.Children.Count == 0))
            Assert.AreEqual(piece.Text, Quoted.Substring(piece.Start, piece.Length), $"'{piece.Text}' stands where it was written");

        var pieces = spliced.SelfAndDescendants().Where(part => part.Children.Count == 0).Select(part => part.Text).ToArray();
        CollectionAssert.AreEqual(new[] { "> ", "pie\n", "> ", "  \"a\" : 1" }, pieces, string.Join("|", pieces));
    }

    [TestMethod]
    public void APlaceInTheDocumentIsItsPlaceAmongTheLanguagesOwnCharacters_AndBack()
    {
        var body = Holder(Quoted).Part(Roles.Body)!;
        var second = Quoted.IndexOf("  \"a\"", System.StringComparison.Ordinal);

        Assert.AreEqual("pie\n".Length, ContentNested.OwnAt(body, second));
        Assert.AreEqual(second, ContentNested.DocumentAt(body, "pie\n".Length));
    }

    private static ContentNode Read(string source) => MarkdownParser.Parsing()(source).Tree;

    private static ContentPart Holder(string source) =>
        ContentPart.Of(Read(source)).SelfAndDescendants().First(part => ContentNested.Language(part) is not null && part.Part(Roles.Body) is not null);

    private static string Own(ContentNode tree) =>
        ContentNested.Own(tree.SelfAndDescendants().First(node => ContentNested.Language(node) is not null).Part(Roles.Body)!);

    private static string Written(ContentNode code) =>
        string.Concat(code.Part(Roles.Body)!.Leaves().Where(leaf => leaf.Role != Roles.Trivia).Select(leaf => leaf.Text));
}
