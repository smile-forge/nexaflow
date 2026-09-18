using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a sankey diagram through the editor that hosts it: what a node is called is the characters written, so a
/// press puts the caret among them and a keystroke changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("sankey-writing")]
public class SankeyEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source => "sankey-beta\n\nWind,Grid,42\nGrid,Homes,30";

    [TestMethod]
    public void TypingInANodesNameChangesIt() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a sankey diagram's names are written in");

            PressPast(diagram, "Wind");
            Write(rtb, "y");

            StringAssert.Contains(diagram.Source, "Windy,Grid,42", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Windy", "and so does the document");
        }));

    [TestMethod]
    public void ANameGivenACommaIsPutInQuotes() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Homes");
            Write(rtb, ",");

            StringAssert.Contains(diagram.Source, "\"Homes,\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
