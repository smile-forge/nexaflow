using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Barcode;

/// <summary>
/// A <c>barcode</c> block read into a tree: its fields as every code block's are, and its value spelled out a character at a
/// time, so each character a symbology prints can say which one of the value it is.
/// </summary>
[TestClass]
[CoversNode("barcode-block-syntax")]
public class BarcodeParserTests
{
    [TestMethod]
    public void TheTreePrintsBackAsWhatWasWritten()
    {
        foreach (var source in new[] { "format: EAN13\nvalue: 590123412345\n", "value:\n# a comment\r\nformat: CODE39", "just prose", "" })
            Assert.AreEqual(source, BarcodeParser.Parse(source).Print());
    }

    [TestMethod]
    public void TheValueIsOnePiecePerCharacter_AndNothingElseIs()
    {
        var tree = BarcodeParser.Parse("format: CODE128\nvalue: AB 1");

        var value = tree.SelfAndDescendants().Single(node => node.Role == MatrixRoles.Value && !node.IsLeaf);
        CollectionAssert.AreEqual(new[] { "A", "B", " ", "1" }, value.Children.Select(letter => letter.Text).ToArray());
        Assert.IsTrue(value.Children.All(letter => letter.Kind == BarcodeKinds.Character));

        var format = tree.SelfAndDescendants().Single(node => node.Role == MatrixRoles.Value && node.Text == "CODE128");
        Assert.IsTrue(format.IsLeaf, "a setting is read whole");
    }

    [TestMethod]
    public void AValueWithNothingAfterItsColonHasNothingToSpell() =>
        Assert.IsFalse(BarcodeParser.Parse("value:").SelfAndDescendants().Any(node => node.Kind == BarcodeKinds.Character));

    [TestMethod]
    public void AValueNotYetWrittenIsAHole_OnlyWhereSomebodyIsWriting()
    {
        const string source = "format: CODE128\nvalue:";

        Assert.IsFalse(BarcodeParser.Parse(source).SelfAndDescendants().Any(node => node.Kind == Kinds.Hole), "a page being read wants none");

        var written = BarcodeParser.Parse(source, holes: true);
        var hole = written.SelfAndDescendants().Single(node => node.Kind == Kinds.Hole);

        Assert.AreEqual(source, written.Print(), "and it takes up none of the source");
        Assert.IsTrue(hole.IsDerived);
        Assert.AreEqual(0, BarcodeParser.Parse("format: CODE128\nvalue: X", holes: true).SelfAndDescendants().Count(node => node.Kind == Kinds.Hole),
                        "a value that is written has none");
    }
}
