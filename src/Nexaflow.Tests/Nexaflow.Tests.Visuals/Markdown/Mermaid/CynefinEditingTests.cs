using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a Cynefin diagram through the editor that hosts it: an item carded in a domain, an item inside the disorder
/// cloud, and a movement's label are the characters written, so a press puts the caret among them and a keystroke changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("cynefin-writing")]
public class CynefinEditingTests : MermaidEditing
{
    private const string Sense =
        "cynefin-beta\n  complex\n    \"Investigate root cause\"\n  confusion\n    \"Unclassified A\"\n  chaotic\n  chaotic --> complex : \"Stabilised\"";

    /// <inheritdoc/>
    protected override string Source => Sense;

    [TestMethod]
    public void TypingInACardedItemOneInTheDisorderAndAMovementsLabelChangesThem() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a Cynefin diagram's words are written in");

            PressPast(diagram, "Investigate root cause");
            Write(editor, "s");
            PressPast(diagram, "Unclassified A");
            Write(editor, "!");
            PressPast(diagram, "Stabilised");
            Write(editor, "?");

            StringAssert.Contains(diagram.Source, "\"Investigate root causes\"", diagram.Source);
            StringAssert.Contains(diagram.Source, "\"Unclassified A!\"", diagram.Source);
            StringAssert.Contains(diagram.Source, "\"Stabilised?\"", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Investigate root causes", "and so does the document");
        }));
}
