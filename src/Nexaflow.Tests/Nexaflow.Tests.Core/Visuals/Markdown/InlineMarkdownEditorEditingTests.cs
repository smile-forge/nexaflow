using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// Coverage for Word-style editing in <see cref="InlineMarkdownEditor"/> (how the Markdown file
/// editor hosts it, <c>EditOnDoubleClick=true</c>): a single click positions the caret in the
/// rendered document and typing edits the line IN PLACE — no source view, no document rebuild, no
/// scroll jump — with typed text kept literal (markdown syntax escaped) and the block model
/// re-serialized from the paragraph after every edit.
///
/// The historical bug this guards against: native edits were shadowed into the model via
/// rendered-caret offsets, which drift when WPF normalises whitespace — typed text was scattered
/// into neighbouring words ("A font viewer. " became "AAfontnviewer."). The serializer round-trip
/// proof (see <see cref="MarkdownInlineSerializer"/>) makes that class of corruption impossible.
///
/// Interactive desktop only — the editor must be in a shown window for its render pass to run.
/// Run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-inline-editor")]
public class InlineMarkdownEditorEditingTests
{
    // ── Word-style typing ─────────────────────────────────────────────────────

    [TestMethod]
    public void TypingMidParagraph_InsertsExactlyAtCaret_WithoutRebuildingDocument() => UiThread.Run(() =>
    {
        const string original = "A live Process Explorer. An installed-apps manager.";
        const string typed    = "A font viewer. ";
        const string expected = "A live Process Explorer. A font viewer. An installed-apps manager.";

        RunInEditor(original, (editor, rtb) =>
        {
            var docBefore  = rtb.Document;
            var paraBefore = rtb.Document.Blocks.OfType<Paragraph>().First();

            PlaceCaret(rtb, paraBefore, original.IndexOf("An installed", System.StringComparison.Ordinal));
            foreach (var ch in typed) RaiseTextInput(rtb, ch.ToString());

            Assert.AreEqual(expected, editor.Markdown);

            // Word-style means editing in place: same document, same paragraph — no swap that would
            // jump the scroll position or flip the line into tinted source view.
            Assert.AreSame(docBefore, rtb.Document, "Document was rebuilt during Word-style typing.");
            Assert.AreSame(paraBefore, rtb.Document.Blocks.OfType<Paragraph>().First(),
                "Paragraph was replaced during Word-style typing.");
        });
    });

    [TestMethod]
    public void TypedMarkdownSyntax_StaysLiteralText() => UiThread.Run(() =>
    {
        const string original = "Some plain text here.";

        RunInEditor(original, (editor, rtb) =>
        {
            var para = rtb.Document.Blocks.OfType<Paragraph>().First();
            PlaceCaret(rtb, para, "Some ".Length);
            foreach (var ch in "**bold**") RaiseTextInput(rtb, ch.ToString());

            // Typing never creates formatting: the asterisks are escaped so they re-render as the
            // literal characters the user typed.
            Assert.AreEqual(@"Some \*\*bold\*\*plain text here.", editor.Markdown);
        });
    });

    [TestMethod]
    public void TypingNextToBoldText_KeepsSurroundingFormattingIntact() => UiThread.Run(() =>
    {
        const string original = "A **bold** word.";

        RunInEditor(original, (editor, rtb) =>
        {
            var para = rtb.Document.Blocks.OfType<Paragraph>().First();
            PlaceCaret(rtb, para, 0);                       // caret at the very start, before "A"
            foreach (var ch in "Yes, ") RaiseTextInput(rtb, ch.ToString());

            Assert.AreEqual("Yes, A **bold** word.", editor.Markdown);
        });
    });

    [TestMethod]
    public void TypingInMultiLineBlock_FallsBackToSourceEdit_AndStillLandsCorrectly() => UiThread.Run(() =>
    {
        // A soft-wrapped block (single newline, no blank line) is one block the serializer refuses —
        // typing must fall back to source-edit mode and the character must land where the caret was.
        const string original = "line one\nline two";

        RunInEditor(original, (editor, rtb) =>
        {
            rtb.CaretPosition = rtb.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            RaiseTextInput(rtb, "X");

            Assert.AreEqual("Xline one\nline two", editor.Markdown);
        });
    });

    // ── Serializer round-trips (the safety proof behind Word-style sessions) ──

    [TestMethod]
    public void Serializer_RoundTrips_ReversibleConstructs() => UiThread.Run(() =>
    {
        string[] roundTrips =
        [
            "A live Process Explorer. An installed-apps manager.",
            "A **bold** word here.",
            "Some *italic* text.",
            "Nested ***both*** markers.",
            "Inline `code span` text.",
            "A [link](https://example.com/docs) mid-sentence.",
            "Strike ~~this~~ out.",
            "snake_case_names stay literal.",
            "Parens (and such) & ampersands.",
        ];
        foreach (var md in roundTrips)
        {
            var para = FirstParagraph(md);
            Assert.IsTrue(
                MarkdownInlineSerializer.TrySerialize(para, null, escapeLeadingMarker: true, out var rebuilt),
                $"Serialize failed for: {md}");
            Assert.AreEqual(md, rebuilt, $"Round-trip mismatch for: {md}");
        }
    });

    [TestMethod]
    public void Serializer_HeadingInlines_RoundTripAfterPrefix() => UiThread.Run(() =>
    {
        var para = FirstParagraph("## A Section **Title**");
        Assert.IsTrue(MarkdownInlineSerializer.TrySerialize(para, null, escapeLeadingMarker: false, out var rebuilt));
        Assert.AreEqual("A Section **Title**", rebuilt);
    });

    [TestMethod]
    public void Serializer_RefusesOrMismatches_IrreversibleConstructs() => UiThread.Run(() =>
    {
        // These must fail the session-start proof (TrySerialize false, or output != source) so the
        // editor falls back to source-edit mode instead of risking a lossy rewrite.
        string[] notRoundTrippable =
        [
            "H~2~O subscript.",          // sub/superscript spans have no lossless markdown form here
            "Some ==marked== text.",     // highlight span
            "Alt __bold__ delimiters.",  // normalizes to ** — output differs from source
        ];
        foreach (var md in notRoundTrippable)
        {
            var para = FirstParagraph(md);
            bool ok = MarkdownInlineSerializer.TrySerialize(para, null, escapeLeadingMarker: true, out var rebuilt);
            Assert.IsFalse(ok && rebuilt == md, $"Unexpectedly round-tripped: {md}");
        }
    });

    // ── Harness ───────────────────────────────────────────────────────────────
    // Shown-window setup, keystroke synthesis and caret placement live in MarkdownEditorHarness,
    // shared with the block-undo tests.

    private static void RunInEditor(string markdown, System.Action<InlineMarkdownEditor, RichTextBox> test)
        => MarkdownEditorHarness.Run(markdown, test);

    private static void RaiseTextInput(RichTextBox rtb, string text)
        => MarkdownEditorHarness.RaiseTextInput(rtb, text);

    private static void PlaceCaret(RichTextBox rtb, Paragraph para, int textOffset)
        => MarkdownEditorHarness.PlaceCaret(rtb, para, textOffset);

    private static Paragraph FirstParagraph(string markdown)
    {
        var ctx = new MarkdownRenderContext { Palette = MarkdownPalette.Dark };
        var doc = MarkdownFlowDocument.Build(markdown, ctx);
        return doc.Blocks.OfType<Paragraph>().First();
    }
}
