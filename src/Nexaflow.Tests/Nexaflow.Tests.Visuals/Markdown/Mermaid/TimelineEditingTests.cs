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
/// Writing in a timeline through the editor that hosts it: a period's name, an event written on its line, one written on
/// a line going on from it, and a section's name are the characters written, so a press puts the caret among them and a
/// keystroke changes the timeline.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("timeline-writing")]
public class TimelineEditingTests : MermaidEditing
{
    private const string Social =
        "timeline\n  section Early days\n    2002 : LinkedIn\n         : Friendster\n    2004 : Facebook";

    /// <inheritdoc/>
    protected override string Source => Social;

    [TestMethod]
    public void TypingInASectionAPeriodAndItsEventsChangesThem() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            Assert.IsFalse(diagram.IsReadOnly, "a timeline's words are written in");

            PressPast(diagram, "Early days");
            Write(rtb, "!");
            PressPast(diagram, "LinkedIn");
            Write(rtb, "!");
            PressPast(diagram, "Friendster");
            Write(rtb, "?");

            StringAssert.Contains(diagram.Source, "section Early days!", diagram.Source);
            StringAssert.Contains(diagram.Source, "2002 : LinkedIn!", diagram.Source);
            StringAssert.Contains(diagram.Source, ": Friendster?", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
            StringAssert.Contains(editor.Markdown, "LinkedIn!", "and so does the document");
        }));

    [TestMethod]
    public void AColonTypedIntoAnEventGoesInAsTheEntityCodeForIt() => UiThread.Run(() =>
        InADocument((editor, rtb, diagram) =>
        {
            PressPast(diagram, "Facebook");
            Write(rtb, ":");

            StringAssert.Contains(diagram.Source, "Facebook#colon;", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count, "and the line still reads as one period");
        }));
}
