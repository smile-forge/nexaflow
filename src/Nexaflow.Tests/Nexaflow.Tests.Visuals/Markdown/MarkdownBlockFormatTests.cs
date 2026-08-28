using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The text rules behind the Markdown editing mini-toolbar's formatting buttons, asserted on
/// <see cref="MarkdownBlockFormat"/> — the pure block transforms <c>InlineMarkdownEditor</c> applies.
/// Every button is a toggle, so each case asserts both directions and where the caret lands.
/// </summary>
[TestClass]
public class MarkdownBlockFormatTests
{
    // ── Headings (H1 / H2 / H3) ───────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-fmt-headings")]
    public void SetHeading_AddsPrefix_ToAPlainBlock()
    {
        var (text, caret) = MarkdownBlockFormat.SetHeading("A title", 2);

        Assert.AreEqual("## A title", text);
        Assert.AreEqual(text.Length, caret);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-headings")]
    public void SetHeading_SameLevelTwice_StripsTheHeading()
    {
        var (once, _) = MarkdownBlockFormat.SetHeading("A title", 1);
        var (twice, _) = MarkdownBlockFormat.SetHeading(once, 1);

        Assert.AreEqual("# A title", once);
        Assert.AreEqual("A title", twice);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-headings")]
    public void SetHeading_DifferentLevel_ReplacesTheExistingOne()
    {
        var (text, _) = MarkdownBlockFormat.SetHeading("# A title", 3);

        Assert.AreEqual("### A title", text);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-headings")]
    public void SetHeading_OnlyTouchesTheFirstLine()
    {
        var (text, caret) = MarkdownBlockFormat.SetHeading("A title\nbody line\nmore body", 1);

        Assert.AreEqual("# A title\nbody line\nmore body", text);
        Assert.AreEqual("# A title".Length, caret, "Caret should sit at the end of the rewritten first line.");
    }

    // ── Inline markers (bold / italic / strike / code) ────────────────────────

    [TestMethod]
    [CoversNode("markdown-fmt-inline")]
    public void InsertMarkers_WithNoSelection_ParksTheCaretBetweenThem()
    {
        var (text, caret) = MarkdownBlockFormat.InsertMarkers("ab", 1, "**");

        Assert.AreEqual("a****b", text);
        Assert.AreEqual(3, caret);                       // "a**|**b" — ready to type
    }

    [TestMethod]
    [CoversNode("markdown-fmt-inline")]
    public void InsertMarkers_ClampsAnOutOfRangeCaret()
    {
        var (text, caret) = MarkdownBlockFormat.InsertMarkers("ab", 99, "`");

        Assert.AreEqual("ab``", text);
        Assert.AreEqual(3, caret);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-inline")]
    public void WrapSelection_SurroundsTheSelectedText()
    {
        Assert.AreEqual("**bold**", MarkdownBlockFormat.WrapSelection("bold", "**"));
        Assert.AreEqual("*it*", MarkdownBlockFormat.WrapSelection("it", "*"));
        Assert.AreEqual("~~gone~~", MarkdownBlockFormat.WrapSelection("gone", "~~"));
        Assert.AreEqual("`code`", MarkdownBlockFormat.WrapSelection("code", "`"));
    }

    // ── Quote ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-fmt-quote")]
    public void ToggleLinePrefix_QuotesEveryLine()
    {
        var (text, caret) = MarkdownBlockFormat.ToggleLinePrefix("one\ntwo", "> ");

        Assert.AreEqual("> one\n> two", text);
        Assert.AreEqual(text.Length, caret);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-quote")]
    public void ToggleLinePrefix_UnquotesWhenEveryLineIsAlreadyQuoted()
    {
        var (text, _) = MarkdownBlockFormat.ToggleLinePrefix("> one\n> two", "> ");

        Assert.AreEqual("one\ntwo", text);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-quote")]
    public void ToggleLinePrefix_PartiallyQuoted_QuotesTheRestRatherThanStripping()
    {
        var (text, _) = MarkdownBlockFormat.ToggleLinePrefix("> one\ntwo", "> ");

        Assert.AreEqual("> > one\n> two", text);   // nests the already-quoted line rather than half-stripping
    }

    // ── Code fence ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("markdown-fmt-codeblock")]
    public void ToggleCodeFence_WrapsAnUnfencedBlock()
    {
        var (text, caret) = MarkdownBlockFormat.ToggleCodeFence("x = 1");

        Assert.AreEqual("```\nx = 1\n```", text);
        Assert.AreEqual(text.Length, caret);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-codeblock")]
    public void ToggleCodeFence_UnwrapsAFencedBlock()
    {
        var (text, _) = MarkdownBlockFormat.ToggleCodeFence("```\nx = 1\n```");

        Assert.AreEqual("x = 1", text);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-codeblock")]
    public void ToggleCodeFence_RoundTrips()
    {
        const string original = "line one\nline two";

        var (fenced, _) = MarkdownBlockFormat.ToggleCodeFence(original);
        var (back, _) = MarkdownBlockFormat.ToggleCodeFence(fenced);

        Assert.AreEqual(original, back);
    }

    [TestMethod]
    [CoversNode("markdown-fmt-codeblock")]
    public void ToggleCodeFence_KeepsALanguageTagWhenUnwrapping_IsNotAttempted()
    {
        // An info-string fence ("```csharp") is still a fence, so toggling drops the fence lines whole.
        var (text, _) = MarkdownBlockFormat.ToggleCodeFence("```csharp\nvar x = 1;\n```");

        Assert.AreEqual("var x = 1;", text);
    }
}
