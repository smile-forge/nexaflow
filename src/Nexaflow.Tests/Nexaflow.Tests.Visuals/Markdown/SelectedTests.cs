using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What is picked out is said to the page, whatever it is — words, a note in a tune, a box in a flowchart — through the one
/// routed event, with what each thing picked out stands for and the language it is written in.
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("markdown-text")]
public class SelectedTests
{
    private const string Chart =
        """
        ```mermaid
        ---
        config:
          nexaflow:
            defaultExpansion: 1
        ---
        graph TD
          root["Root"] --> child["Child"]
          child --> hidden["Hidden"]
        ```

        """;

    [TestMethod]
    public void PickingOutWordsSaysWhichWords() => UiThread.Run(() =>
    {
        var (surface, told) = Shown("Hello brave world\n");

        surface.Shown.Select(6, 5);

        var pick = told.Last().Picked.Single();
        Assert.AreEqual("brave", pick.Text);
        Assert.AreEqual((6, 5), (pick.Start, pick.Length));
        Assert.IsNull(pick.Language, "words written in the document itself are markdown's");
    });

    [TestMethod]
    public void PickingOutABoxInAFlowchartSaysWhichNode() => UiThread.Run(() =>
    {
        var (surface, told) = Shown(Chart);

        var box = surface.Shown.Laid.Root.SelfAndDescendants().First(piece => piece.Acts?.Select?.Target == "child");
        var middle = new Point(box.Bounds.X + (box.Bounds.Width / 2), box.Bounds.Y + (box.Bounds.Height / 2));

        surface.Shown.BeginPointerSelect(middle, ModifierKeys.Control);
        surface.Shown.EndPointerSelect();

        var pick = told.Last().Picked.Single(one => one.Id is not null);
        Assert.AreEqual("child", pick.Id, "the node the drawing calls it");
        Assert.AreEqual("mermaid", pick.Language);
    });

    [TestMethod]
    public void PickingOutANoteSaysItIsWrittenInTheTunesLanguage() => UiThread.Run(() =>
    {
        const string tune = "```abc\nX:1\nL:1/4\nK:C\nCDEF|\n```\n";
        var (surface, told) = Shown(tune);

        surface.Shown.Select(tune.IndexOf("CDEF", System.StringComparison.Ordinal), 1);

        var pick = told.Last().Picked.First();
        Assert.AreEqual("abc", pick.Language);
        Assert.IsNotNull(pick.Part, "and what it stands for in the tune as read");
    });

    [TestMethod]
    public void LettingGoOfWhatWasPickedOutIsSaidToo() => UiThread.Run(() =>
    {
        var (surface, told) = Shown("Hello brave world\n");

        surface.Shown.Select(6, 5);
        surface.Shown.ClearSelection();

        Assert.IsTrue(told.Last().IsEmpty, "a page showing detail beside the document hears that nothing is chosen");
    });

    /// <summary><paramref name="markdown"/> on a surface, laid out, and every Selected it raises, in order.</summary>
    private static (MarkdownSurface Surface, List<ContentSelectionChange> Told) Shown(string markdown)
    {
        var surface = new MarkdownSurface { IsReadOnly = false };
        var told = new List<ContentSelectionChange>();

        surface.Selected += (_, e) => told.Add(e.Change);
        surface.Markdown = markdown;

        surface.Measure(new Size(900, 900));
        surface.Arrange(new Rect(0, 0, 900, 900));
        surface.UpdateLayout();

        return (surface, told);
    }
}
