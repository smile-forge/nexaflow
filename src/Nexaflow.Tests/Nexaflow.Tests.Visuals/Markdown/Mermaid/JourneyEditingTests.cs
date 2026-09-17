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
/// Writing in a user journey through the editor that hosts it: what a task says, a section's name and an actor's name in
/// the legend are the characters written, so a press puts the caret among them and a keystroke changes the journey.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("journey-writing")]
public class JourneyEditingTests : MermaidEditing
{
    private const string Working =
        "journey\n  section Go to work\n    Make tea: 5: Me\n    Do work: 1: Me, Cat";

    /// <inheritdoc/>
    protected override string Source => Working;

    [TestMethod]
    public void TypingInATaskASectionAndAnActorInTheLegendChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a journey's words are written in");

            PressPast(diagram, "Go to work");
            Write(rtb, "!");
            PressPast(diagram, "Make tea");
            Write(rtb, "?");

            StringAssert.Contains(diagram.Source, "section Go to work!", diagram.Source);
            StringAssert.Contains(diagram.Source, "Make tea?: 5: Me", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "Make tea?", "and so does the document");
        }));

    [TestMethod]
    public void AColonTypedIntoWhatATaskSaysGoesInAsTheEntityCodeForIt() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Do work");
            Write(rtb, ":");

            StringAssert.Contains(diagram.Source, "Do work#colon;: 1: Me, Cat", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count, "and the line still reads as one task");
        }));
}
