using System.Linq;
using System.Text;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A document of many blocks, asked which block an offset or a point is in, and where a block came out — each of which is
/// asked every time the pointer moves, and answered without reading every block (<see cref="ContentEngine.Blocked(int)"/>):
/// so each answer is checked against every block asked in turn.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-long-reading")]
public class LongDocumentTests
{
    /// <summary>Paragraphs, formulas, a list and a quote, over and over, with the blank lines between them that are no block at all.</summary>
    private static string Document()
    {
        var text = new StringBuilder();

        for (var section = 0; section < 60; section++)
            text.Append("## Section ").Append(section).Append("\n\n")
                .Append("Some words with $x^").Append(section).Append("$ in them.\n\n")
                .Append("$$\n\\frac{1}{").Append(section + 1).Append("}\n$$\n\n")
                .Append("- one\n- two\n\n")
                .Append("> quoted\n\n\n");

        return text.ToString();
    }

    private static ContentEngine LaidOut(string document)
    {
        var engine = new ContentEngine();
        var element = new MarkdownElement(document, WpfStyles.Dark, null, engine);

        element.Measure(new Size(600, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));

        return engine;
    }

    [TestMethod]
    public void EveryOffsetIsInTheBlockThatHoldsIt_AndTheOneAfterIsTheNextWritten() => UiThread.Run(() =>
    {
        var document = Document();
        var engine = LaidOut(document);
        var written = engine.ReadRoot.Children.Where(block => !block.Derived && block.Role != Roles.Trivia).ToList();

        for (var offset = 0; offset <= document.Length; offset++)
        {
            var holding = written.FindIndex(block => offset >= block.Start && offset < block.End);

            Assert.AreSame(holding < 0 ? null : written[holding], engine.Blocked(offset), $"the block holding {offset}");
            Assert.AreSame(holding < 0 || holding + 1 >= written.Count ? null : written[holding + 1], engine.BlockedAfter(offset),
                           $"the block after the one holding {offset}");
        }
    });

    [TestMethod]
    public void EveryBlockComesOutWhereItsOwnPieceIs_AndAPointOnItIsOnIt() => UiThread.Run(() =>
    {
        var engine = LaidOut(Document());
        var root = engine.Laid.Root;

        foreach (var block in engine.ReadRoot.Children.Where(block => !block.Derived && block.Role != Roles.Trivia))
        {
            // The block's own piece: the first of the document's that first names something inside the block.
            var own = root.Children.FirstOrDefault(whole => whole.Kind == PortablePieces.Whole
                                                            && whole.SelfAndDescendants().Select(piece => piece.Part).FirstOrDefault(part => part is { Length: > 0 }) is { } named
                                                            && named.Start >= block.Start && named.Start < block.End);

            var where = engine.Where(block);
            if (own.Exists) Assert.AreEqual(own.Bounds, where, $"{block.Kind} at {block.Start} came out as its own piece did");

            var middle = new LayoutPoint(where.X + (where.Width / 2), where.Y + (where.Height / 2));
            Assert.AreSame(block, engine.Blocked(middle), $"a point in the middle of {block.Kind} at {block.Start} is on it");
        }
    });
}
