using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a flowchart through the editor that hosts it: a node's label, the id of a node nothing else writes, what is written
/// on a link and the title of a subgraph are all the characters written, so a press puts the caret among them and a keystroke
/// changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("flowchart-writing")]
public class FlowchartEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "flowchart TD\n  start[\"Store\"] -- feeds --> lone\n  subgraph inner\n    held\n  end";

    [TestMethod]
    public void TypingInALabelAnIdALinksWordsAndASubgraphsTitleChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a flowchart's words are written in");

            PressPast(diagram, "Store");
            Write(rtb, "s");
            PressPast(diagram, "feeds");
            Write(rtb, "!");
            PressPast(diagram, "lone");
            Write(rtb, "r");
            PressPast(diagram, "inner");
            Write(rtb, "s");

            StringAssert.Contains(diagram.Source, "start[\"Stores\"]", diagram.Source);
            StringAssert.Contains(diagram.Source, "-- feeds! -->", diagram.Source);
            StringAssert.Contains(diagram.Source, "loner", diagram.Source);
            StringAssert.Contains(diagram.Source, "subgraph inners", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Stores", "and so does the document");
        }));

    [TestMethod]
    public void WhatABareIdCannotHoldIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "lone");
            Write(rtb, "[");

            StringAssert.Contains(diagram.Source, "lone", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("lone[", StringComparison.Ordinal),
                           "a bracket would open a label rather than name the node");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));

    [TestMethod]
    public void ADashThatWouldStartALinkIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "lone");
            Write(rtb, "-");

            StringAssert.Contains(diagram.Source, "lone", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count, "an id a dash carries on still reads");
        }));

    [TestMethod]
    public void WordsOnALinkArePutInQuotesToHoldWhatWouldCloseItEarly() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "feeds");
            Write(rtb, "-");
            PressPast(diagram, "feeds-");
            Write(rtb, "-");

            StringAssert.Contains(diagram.Source, "\"feeds--\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count, "which is how Mermaid holds those characters there too");
        }));
}
