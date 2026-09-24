using System;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Typing into a document as it is drawn: a press puts the caret among the words and a key writes there — straight into
/// the source at the offset the words were set from, with nothing reconstructed from what was drawn.
///
/// <para>
/// <strong>What these guard against.</strong> Typing into a drawn document once meant working the source back out of the
/// drawing, which drifted wherever the drawing normalised whitespace — "A font viewer. " landed as "AAfontnviewer." — and
/// could rewrite a construct it did not understand into one it did. Every run of words now names the characters it was
/// set from, so an edit lands exactly where the caret is and nothing it did not touch is written again.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-inline-editor")]
public class DocumentTypingTests
{
    [TestMethod]
    public void TypingMidParagraphLandsExactlyAtTheCaret_OnTheSameDocument() => UiThread.Run(() =>
    {
        const string original = "A live Process Explorer. An installed-apps manager.";

        MarkdownEditorHarness.Run(original, editor =>
        {
            var drawn = editor.Shown;

            MarkdownEditorHarness.PlaceCaret(editor, original.IndexOf("An installed", StringComparison.Ordinal));
            MarkdownEditorHarness.Type(editor, "A font viewer. ");

            Assert.AreEqual("A live Process Explorer. A font viewer. An installed-apps manager.", editor.Markdown);
            Assert.AreSame(drawn, editor.Shown, "the same document, written in — nothing swapped under the reader");
        });
    });

    [TestMethod]
    public void WhatIsTypedStaysWhatWasTyped() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Some plain text here.", editor =>
        {
            MarkdownEditorHarness.PlaceCaret(editor, "Some ".Length);
            MarkdownEditorHarness.Type(editor, "**bold**");

            // Typing never makes formatting: the asterisks are written so that they read as the asterisks typed.
            Assert.AreEqual(@"Some \*\*bold\*\*plain text here.", editor.Markdown);
        }));

    [TestMethod]
    public void TypingBesideSomethingSetHeavyLeavesItAsItWas() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("A **bold** word.", editor =>
        {
            MarkdownEditorHarness.PlaceCaret(editor, 0);
            MarkdownEditorHarness.Type(editor, "Yes, ");

            Assert.AreEqual("Yes, A **bold** word.", editor.Markdown);
        }));

    [TestMethod]
    public void TypingInAParagraphOfSeveralLinesLandsWhereTheCaretIs() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("line one\nline two", editor =>
        {
            MarkdownEditorHarness.PlaceCaret(editor, 0);
            MarkdownEditorHarness.Type(editor, "X");

            Assert.AreEqual("Xline one\nline two", editor.Markdown);
        }));

    [TestMethod]
    public void NothingTheCaretDidNotTouchIsWrittenAgain() => UiThread.Run(() =>
    {
        // Constructs with more than one spelling, or none this renderer draws as they were written: a rewrite through
        // anything that understood them would normalise them, and none of them may move.
        string[] kept =
        [
            "H~2~O subscript.",
            "Some ==marked== text.",
            "Alt __bold__ delimiters.",
            "Nested ***both*** markers.",
            "Inline `code span` text.",
            "A [link](https://example.com/docs) mid-sentence.",
            "Strike ~~this~~ out.",
            "snake_case_names stay literal.",
            "Parens (and such) & ampersands.",
        ];

        foreach (var line in kept)
            MarkdownEditorHarness.Run("Start. " + line, editor =>
            {
                MarkdownEditorHarness.PlaceCaret(editor, "Start".Length);
                MarkdownEditorHarness.Type(editor, "ed");

                Assert.AreEqual("Started. " + line, editor.Markdown, line);
            });
    });
}
