using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A reply's prose run together on one line, as the AI bar shows an answer: the words of its paragraphs, headings, items
/// and quotes, their marks off, and none of what is not prose.
/// </summary>
[TestClass]
[CoversNode("aibar-bar-reply")]
public class MarkdownProseTests
{
    [TestMethod]
    public void TheProse_IsRunTogether_WithoutItsMarks()
        => Assert.AreEqual("Title Some bold and a link here. One Two A quote.",
                           MarkdownProse.RunTogether("# Title\n\nSome **bold** and a [link](https://example.com/secret)\nhere.\n\n" +
                                                     "- One\n- Two\n\n> A quote.\n"));

    [TestMethod]
    public void WhatIsNotProse_IsStripped()
        => Assert.AreEqual("Before. After.",
                           MarkdownProse.RunTogether("Before.\n\n```csharp\nvar hidden = 1;\n```\n\n| head | er |\n|---|---|\n| cell | two |\n\n" +
                                                     "![a picture](x.png)\n\n<div>markup</div>\n\n---\n\nAfter.\n"));

    [TestMethod]
    public void NothingWritten_IsNothing()
        => Assert.AreEqual("", MarkdownProse.RunTogether(null));
}
