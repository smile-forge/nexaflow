using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a requirement diagram through the editor that hosts it: what a field is set to and what a requirement is called are
/// the characters written, so a press puts the caret among them and a keystroke changes them — and renaming one carries to every
/// relation that names it.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("requirement-diagram")]
public class RequirementEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "requirementDiagram\n  requirement test_req {\n    id: 1\n    text: the test text\n  }\n"
        + "  element widget {\n    type: simulation\n  }\n  widget - satisfies -> test_req";

    [TestMethod]
    public void TypingInWhatAFieldIsSetToChangesIt() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a requirement diagram's words are written in where the host takes edits");

            PressPast(diagram, "the test text");
            Write(editor, "s");
            PressPast(diagram, "simulation");
            Write(editor, "s");

            StringAssert.Contains(diagram.Source, "text: the test texts", diagram.Source);
            StringAssert.Contains(diagram.Source, "type: simulations", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "the test texts", "and so does the document");
        }));

    [TestMethod]
    public void RenamingARequirementCarriesToTheRelationThatNamesIt() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "test_req");
            Write(editor, "2");

            StringAssert.Contains(diagram.Source, "requirement test_req2 {", diagram.Source);
            StringAssert.Contains(diagram.Source, "-> test_req2", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));

    [TestMethod]
    public void WhatABareNameCannotHoldIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "widget");
            Write(editor, "-");

            StringAssert.Contains(diagram.Source, "element widget {", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("widget-", StringComparison.Ordinal),
                           "a dash would read as the line of a relation");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
