using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>Writing in a sequence diagram where it is drawn: what a message says, a participant's name, and a note.</summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("sequence-diagram")]
public class SequenceEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "sequenceDiagram\n  participant Alice\n  Alice->>John: Hello\n  Note over Alice,John: they meet";

    [TestMethod]
    public void TypingInWhatAMessageSaysAndInANoteChangesThem() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a sequence diagram's words are written in where the host takes edits");

            PressPast(diagram, "Hello");
            Write(editor, " John");
            PressPast(diagram, "they meet");
            Write(editor, " again");

            StringAssert.Contains(diagram.Source, ": Hello John", diagram.Source);
            StringAssert.Contains(diagram.Source, ": they meet again", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, ": Hello John", "and so does the document");
        }));

    [TestMethod]
    public void RenamingAParticipantCarriesToEveryLineThatNamesIt() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "Alice");
            Write(editor, "a");

            StringAssert.Contains(diagram.Source, "participant Alicea", diagram.Source);
            StringAssert.Contains(diagram.Source, "Alicea->>John", diagram.Source);
            StringAssert.Contains(diagram.Source, "Note over Alicea,John", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));

    [TestMethod]
    public void WhatWouldMakeANameAnArrowIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "John");
            Write(editor, ">");

            StringAssert.Contains(diagram.Source, "Alice->>John:", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("John>", StringComparison.Ordinal),
                           "an angle bracket would read as part of a message's own characters");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));

    [TestMethod]
    public void AHyphenGoesIntoANameBecauseItDoesNotEndOne() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "John");
            Write(editor, "-Doe");

            StringAssert.Contains(diagram.Source, "Alice->>John-Doe:", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
