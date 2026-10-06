using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Renaming a node, which is renaming it everywhere (<c>FlowchartEdits</c>). What a node or a subgraph is called is how every
/// other line says which one it means, so a name typed into in one place is written in all of them, as one edit and one undo.
/// UI category: text is set on an STA thread; no window opens.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("flowchart-writing")]
public class FlowchartRenameTests
{
    [TestMethod]
    public void AndRenamingASubgraphCarriesToWhatJoinsIt() => UiThread.Run(() =>
    {
        // A link joins a subgraph by its name exactly as it joins a node by one.
        const string chart = "```mermaid\nflowchart TB\n  one --> two\n  subgraph one\n    a\n  end\n"
                             + "  subgraph two\n    b\n  end\n```\n";

        var written = Typed(chart, "one", "s");

        StringAssert.Contains(written, "ones --> two", "where the reader typed");
        StringAssert.Contains(written, "subgraph ones", "and the subgraph it joins");
    });

    /// <summary>
    /// <paramref name="markdown"/> with <paramref name="text"/> typed at the end of the run of words <paramref name="says"/> — the
    /// caret put there by a press, as a reader puts it there, because the piece under the pointer is what says which language an
    /// edit is in.
    /// </summary>
    private static string Typed(string markdown, string says, string text)
    {
        var engine = new ContentEngine();
        var element = new MarkdownElement(markdown, StyleFormat.Dark, engine: engine) { IsReadOnly = false };

        element.Measure(new Size(900, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));

        var run = engine.Laid.Root.SelfAndDescendants()
                        .Where(piece => piece.Words?.Glyphs.Text == says)
                        .Select(piece => piece.Part)
                        .OfType<ContentPart>()
                        .First();

        var box = engine.Where(run);

        engine.Input(new ContentPress(new Point(box.Right - 0.5, box.Y + (box.Height / 2))));
        foreach (var letter in text) element.Type(letter);

        return element.Markdown;
    }
}
