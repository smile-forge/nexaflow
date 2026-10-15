using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Prose;

/// <summary>
/// A grid table read as Markdig reads one: a cell written over several lines is one cell, a cell written down through the rule
/// under it covers the next row too, and what is written in a cell is a small document of its own — while every character is
/// still where it was typed, the pipes and rules the table's and each slice of a line its cell's.
/// </summary>
[TestClass]
[CoversNode("grid-tables")]
public class MarkdownGridTableTests
{
    private const string OverTwoLines = "+---------+---------+\n| one     | two     |\n| more    | again   |\n+---------+---------+\n";

    private const string DownTwoRows = "+---+---+---+\n| AAAAA | B |\n+ AAAAA +---+\n| AAAAA | C |\n+---+---+---+\n| D | E | F |\n+---+---+---+\n";

    private const string WithAList = "+-------+---+\n| - one | y |\n| - two |   |\n+-------+---+\n";

    private const string WithAHead = "+---+---+\n| a | b |\n+===+===+\n| 1 | 2 |\n+---+---+\n";

    private const string NotAddingUp = "+---+---+---+\n| AAAAA | B |\n+ A +---+ B +\n| A | C | B |\n+---+---+---+\n| DDDDD | E |\n+---+---+---+\n";

    private static readonly string[] Grids = [OverTwoLines, DownTwoRows, WithAList, WithAHead, NotAddingUp];

    [TestMethod]
    public void EveryGridReadsBackAsItWasWritten_EveryPieceWhereItWasRead()
    {
        foreach (var source in Grids)
        {
            var tree = Read(source);

            Assert.AreEqual(source, tree.Print(), source);
            CollectionAssert.AreEqual(new List<string>(), AstOracle.Faults(source, tree).ToList(), source);
        }
    }

    [TestMethod]
    public void ACellWrittenOverTwoLinesHoldsBothOfItsLinesAndNothingOfTheCellBesideIt()
    {
        var cells = Cells(Read(OverTwoLines));

        Assert.AreEqual(2, cells.Count);
        CollectionAssert.AreEqual(new[] { "one", "more" }, Words(cells[0]));
        CollectionAssert.AreEqual(new[] { "two", "again" }, Words(cells[1]));
        Assert.AreEqual(1, cells[0].SelfAndDescendants().Count(piece => piece.Kind == MarkdownKinds.Break), "its lines are parted by a break");
    }

    [TestMethod]
    public void ACellWrittenDownThroughTheRuleUnderItCoversTheNextRowToo()
    {
        var tree = Read(DownTwoRows);
        var rows = tree.SelfAndDescendants().Where(piece => piece.Kind == MarkdownKinds.Row).ToList();
        var first = Cells(rows[0]);

        Assert.AreEqual(3, rows.Count);
        CollectionAssert.AreEqual(new[] { "AAAAA", "AAAAA", "AAAAA" }, Words(first[0]), "its slice of the rule is its own");
        Assert.AreEqual(new MarkdownSpans(2, 2), Spans(first[0]));
        CollectionAssert.AreEqual(new[] { "C" }, Cells(rows[1]).SelectMany(Words).ToArray(), "the row under it holds only what starts in it");
    }

    [TestMethod]
    public void ACellHoldingAListHoldsTheListAndNotTheCellBesideIt()
    {
        var cells = Cells(Read(WithAList));
        var items = cells[0].SelfAndDescendants().Where(piece => piece.Kind == MarkdownKinds.Item).ToList();

        Assert.AreEqual(2, items.Count);
        CollectionAssert.AreEqual(new[] { "one", "two" }, items.SelectMany(Words).ToArray());
        CollectionAssert.AreEqual(new[] { "y" }, Words(cells[1]));
    }

    [TestMethod]
    public void ARuleOfEqualsMakesTheRowsAboveItTheHead()
    {
        var rows = Read(WithAHead).SelfAndDescendants().Where(piece => piece.Kind == MarkdownKinds.Row).ToList();

        CollectionAssert.AreEqual(new[] { MarkdownRoles.Head, Roles.Row }, rows.Select(row => row.Role).ToArray());
    }

    [TestMethod]
    public void AGridWhoseCellsDoNotAddUpIsTheParagraphItsLinesAre()
    {
        var tree = Read(NotAddingUp);

        Assert.AreEqual(0, tree.SelfAndDescendants().Count(piece => piece.Kind == MarkdownKinds.Table));
        Assert.AreEqual(MarkdownKinds.Paragraph, tree.Children.Single(block => block.Role != Roles.Trivia).Kind);
    }

    private static ContentNode Read(string source) => MarkdownParser.Parsing()(source).Tree;

    private static List<ContentNode> Cells(ContentNode tree) => [.. tree.SelfAndDescendants().Where(piece => piece.Kind == MarkdownKinds.Cell)];

    private static string[] Words(ContentNode node) =>
        [.. node.SelfAndDescendants().Where(piece => piece is { IsLeaf: true, Kind: MarkdownKinds.Word }).Select(piece => piece.Text)];

    private static MarkdownSpans? Spans(ContentNode cell) =>
        cell.Children.Where(part => part.IsDerived).SelectMany(part => part.SelfAndDescendants()).Select(part => part.Held).OfType<MarkdownSpans>().FirstOrDefault();
}
