using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a block diagram through the editor that hosts it: what is written on a block, the id of a block nothing else
/// writes, and what is written on a link are all the characters written, so a press puts the caret among them and a keystroke
/// changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("block-writing")]
public class BlockEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "block-beta\n  columns 3\n  db((\"Store\")) space store\n  db -- \"feeds\" --> store\n  lone";

    [TestMethod]
    public void TypingInALabelAnIdAndALinksWordsChangesThem() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a block diagram's words are written in");

            PressPast(diagram, "Store");
            Write(editor, "s");
            PressPast(diagram, "feeds");
            Write(editor, "!");
            PressPast(diagram, "lone");
            Write(editor, "r");

            StringAssert.Contains(diagram.Source, "db((\"Stores\"))", diagram.Source);
            StringAssert.Contains(diagram.Source, "-- \"feeds!\" -->", diagram.Source);
            StringAssert.Contains(diagram.Source, "loner", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Stores", "and so does the document");
        }));

    [TestMethod]
    public void WhatABareIdCannotHoldIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "lone");
            Write(editor, "[");

            StringAssert.Contains(diagram.Source, "lone", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("lone[", StringComparison.Ordinal), "a bracket would open a label rather than name the block");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
