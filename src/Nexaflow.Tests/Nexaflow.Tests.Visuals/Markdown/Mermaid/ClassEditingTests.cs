using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a class diagram through the editor that hosts it: a class's label, a member written as it is drawn, what is
/// written on a relation and what a note says are all the characters written, so a press puts the caret among them and a
/// keystroke changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("class-diagram")]
public class ClassEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "classDiagram\n  class Animal[\"The animal\"]\n  Animal <|-- Duck : becomes\n  class Duck {\n    +String beakColor\n  }\n"
        + "  note for Duck \"mind this\"";

    [TestMethod]
    public void TypingInALabelAMemberARelationAndANoteChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a class diagram's words are written in where the host takes edits");

            PressPast(diagram, "The animal");
            Write(rtb, "s");
            PressPast(diagram, "+String beakColor");
            Write(rtb, "s");
            PressPast(diagram, "becomes");
            Write(rtb, " a");
            PressPast(diagram, "mind this");
            Write(rtb, " one");

            StringAssert.Contains(diagram.Source, "class Animal[\"The animals\"]", diagram.Source);
            StringAssert.Contains(diagram.Source, "+String beakColors", diagram.Source);
            StringAssert.Contains(diagram.Source, "Duck : becomes a", diagram.Source);
            StringAssert.Contains(diagram.Source, "note for Duck \"mind this one\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "The animals", "and so does the document");
        }));

    [TestMethod]
    public void WhatABareIdCannotHoldIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Duck");
            Write(rtb, "-");

            StringAssert.Contains(diagram.Source, "Duck", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("Duck-", StringComparison.Ordinal),
                           "a dash would read as the line of a relation");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
