using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in an architecture diagram through the editor that hosts it: what is written on a group, under a service, on a
/// service that has only its id, and on an edge are all the characters written, so a press puts the caret among them and a
/// keystroke changes them.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("architecture-writing")]
public class ArchitectureEditingTests : MermaidEditing
{
    /// <inheritdoc/>
    protected override string Source =>
        "architecture-beta\n  group api(cloud)[Edge]\n  service db(database)[Store] in api\n  service plain in api\n"
        + "  db:R -[feeds]- L:plain";

    [TestMethod]
    public void TypingInAGroupAServiceAnIdAndAnEdgesWordsChangesThem() => UiThread.Run(() =>
    {
        foreach (var (words, typed, said) in new[]
                 {
                     ("Edge", "s", "[Edges]"),
                     ("Store", "s", "[Stores]"),
                     ("feeds", "!", "-[feeds!]-"),
                     ("plain", "s", "service plains"),
                 })
        {
            InADocument((editor, diagram) =>
            {
                Assert.IsFalse(diagram.IsReadOnly, "an architecture diagram's words are written in");

                PressPast(diagram, words);
                Assert.AreEqual(words, diagram.SelectedText.Length == 0 ? diagram.Source[(diagram.Caret - words.Length)..diagram.Caret] : diagram.SelectedText,
                                $"pressing past '{words}' puts the caret after it, and instead selected '{diagram.SelectedText}' at {diagram.Caret}");
                Write(editor, typed);

                StringAssert.Contains(diagram.Source, said, diagram.Source);
                Assert.AreEqual(0, diagram.Diagnostics.Count);
                StringAssert.Contains(editor.Markdown, said, "and so does the document");
            });
        }
    });

    [TestMethod]
    public void WhatABareIdCannotHoldIsDroppedRatherThanWritten() => UiThread.Run(() =>
        InADocument((editor, diagram) =>
        {
            PressPast(diagram, "plain");
            Write(editor, "(");

            Assert.IsFalse(diagram.Source.Contains("plain(", StringComparison.Ordinal), "a bracket would open an icon rather than name the service");
            StringAssert.Contains(diagram.Source, "service plain", diagram.Source);
            Assert.AreEqual(0, diagram.Diagnostics.Count);
        }));
}
