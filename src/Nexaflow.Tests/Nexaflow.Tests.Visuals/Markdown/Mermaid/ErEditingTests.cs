using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in an ER diagram through the editor that hosts it: what an attribute holds and is called, what a relationship is
/// called and what an entity is called are all the characters written, so a press puts the caret among them and a keystroke
/// changes them — and renaming an entity carries to every relationship that names it.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("er-diagram")]
public class ErEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "erDiagram\n  CUSTOMER ||--o{ ORDER : places\n  CUSTOMER {\n    string email UK\n  }";

    [TestMethod]
    public void TypingInAnAttributeAndInWhatARelationshipIsCalledChangesThem() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "an ER diagram's words are written in where the host takes edits");

            PressPast(diagram, "email");
            Write(editor, "s");
            PressPast(diagram, "places");
            Write(editor, " an order for");

            StringAssert.Contains(diagram.Source, "string emails UK", diagram.Source);
            StringAssert.Contains(diagram.Source, ": places an order for", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "string emails UK", "and so does the document");
        }));

    [TestMethod]
    public void RenamingAnEntityCarriesToEveryRelationshipThatNamesIt() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "CUSTOMER");
            Write(editor, "S");

            StringAssert.Contains(diagram.Source, "CUSTOMERS ||--o{ ORDER", diagram.Source);
            StringAssert.Contains(diagram.Source, "CUSTOMERS {", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));

    [TestMethod]
    public void WhatABareNameCannotHoldIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "ORDER");
            Write(editor, "|");

            StringAssert.Contains(diagram.Source, "o{ ORDER :", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("ORDER|", StringComparison.Ordinal),
                           "a bar would read as how many of it the other has");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
