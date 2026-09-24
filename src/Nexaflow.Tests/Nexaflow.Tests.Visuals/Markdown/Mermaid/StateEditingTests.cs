using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a state diagram through the editor that hosts it: what is written on a state, the id of a state nothing else writes,
/// what is written on a transition, the name of a composite state and the words of a note are all the characters written, so a
/// press puts the caret among them and a keystroke changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("state-diagram")]
public class StateEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "stateDiagram-v2\n  [*] --> Still\n  Still : Standing there\n  Still --> Moving : pushed\n"
        + "  state Machine {\n    Moving --> Slowing\n  }\n  note right of Slowing : mind this";

    [TestMethod]
    public void TypingInAStateATransitionACompositeAndANoteChangesThem() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a state diagram's words are written in");

            PressPast(diagram, "Standing there");
            Write(editor, "!");
            PressPast(diagram, "pushed");
            Write(editor, " hard");
            PressPast(diagram, "Machine");
            Write(editor, "s");
            PressPast(diagram, "mind this");
            Write(editor, " one");

            StringAssert.Contains(diagram.Source, "Still : Standing there!", diagram.Source);
            StringAssert.Contains(diagram.Source, "Moving : pushed hard", diagram.Source);
            StringAssert.Contains(diagram.Source, "state Machines {", diagram.Source);
            StringAssert.Contains(diagram.Source, "note right of Slowing : mind this one", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Standing there!", "and so does the document");
        }));

    [TestMethod]
    public void WhatABareIdCannotHoldIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "Slowing");
            Write(editor, "-");

            StringAssert.Contains(diagram.Source, "Slowing", diagram.Source);
            Assert.IsFalse(diagram.Source.Contains("Slowing-", StringComparison.Ordinal),
                           "a dash would read as the arrow of a transition");
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
