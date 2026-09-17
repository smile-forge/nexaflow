using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// How Mermaid writes a character a place cannot hold as itself — as an entity code — and reads it back as the character.
/// </summary>
[TestClass]
[CoversNode("mermaid-block-ast")]
public class MermaidTextTests
{
    [TestMethod]
    public void AnEntityCodeReadsAsTheCharacterItStandsFor()
    {
        foreach (var (written, says) in new[]
                 {
                     ("Say #quot;hi#quot;", "Say \"hi\""),
                     ("Number #35;1", "Number #1"),
                     ("#9829; it", "\u2665 it"),
                     ("Salt #amp; pepper", "Salt & pepper"),
                 })
            Assert.AreEqual(says, MermaidText.Decode(written), written);
    }

    [TestMethod]
    public void WhatStandsForNothingIsLeftAsItWasWritten()
    {
        foreach (var written in new[] { "#nope;", "100#", "# quot;", "#0;", "no codes at all" })
            Assert.AreEqual(written, MermaidText.Decode(written), written);
    }

    [TestMethod]
    public void AQuoteIsWrittenAsItsCode_AndReadsBackAsAQuote()
    {
        const string text = "a \"quoted\" word";

        Assert.AreEqual("a #quot;quoted#quot; word", MermaidText.Quoted(text));
        Assert.AreEqual(text, MermaidText.Decode(MermaidText.Quoted(text)));
    }

    [TestMethod]
    public void ThePunctuationThatClosesWhatSomethingSaysHasACodeOfItsOwn()
    {
        Assert.AreEqual("9:00", MermaidText.Decode("9#colon;00"));
        Assert.AreEqual("a;b", MermaidText.Decode("a#semi;b"));
        Assert.AreEqual("50%", MermaidText.Decode("50#percnt;"));
        Assert.AreEqual("#nothing;", MermaidText.Decode("#nothing;"), "a code that stands for nothing stays as it was written");
    }
}
