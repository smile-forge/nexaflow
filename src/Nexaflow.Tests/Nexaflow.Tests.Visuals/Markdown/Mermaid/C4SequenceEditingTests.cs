using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>Writing in a C4 sequence where it is drawn: a label on a card, what a relationship says, and a name.</summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("c4-sequence")]
public class C4SequenceEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "C4Sequence\nPerson(customer, \"Banking Customer\")\nContainer(spa, \"Single-Page App\", \"Angular\")\n"
        + "participant Store\nRel(customer, spa, \"Submits credentials\", \"HTTPS\")\nRel(spa, Store, \"Saves\")";

    [TestMethod]
    public void TypingInALabelAndInWhatARelationshipSaysChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a C4 sequence's words are written in where the host takes edits");

            PressPast(diagram, "Banking Customer");
            Write(rtb, "s");
            PressPast(diagram, "Submits credentials");
            Write(rtb, " to it");

            StringAssert.Contains(diagram.Source, "\"Banking Customers\"", diagram.Source);
            StringAssert.Contains(diagram.Source, "\"Submits credentials to it\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "\"Banking Customers\"", "and so does the document");
        }));

    /// <summary>
    /// A C4 element's own name is never drawn — its label is — so it is renamed from the source rather than from the drawing.
    /// A participant written as a sequence diagram's own is drawn as its name, and renaming that carries to the macros naming it.
    /// </summary>
    [TestMethod]
    public void RenamingAParticipantCarriesToTheMacroThatNamesIt() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Store");
            Write(rtb, "s");

            StringAssert.Contains(diagram.Source, "participant Stores", diagram.Source);
            StringAssert.Contains(diagram.Source, "Rel(spa, Stores,", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));

    [TestMethod]
    public void WhatWouldCloseAMacrosBracketsIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Store");
            Write(rtb, ")");

            StringAssert.Contains(diagram.Source, "Rel(spa, Store,", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("Store)", StringComparison.Ordinal),
                           "a bracket would close the brackets a macro names it between");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
